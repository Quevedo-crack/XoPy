using System.Diagnostics;
using System.Runtime.InteropServices;

namespace XoPy;

public sealed record ConflictPrompt(string SourcePath, string TargetPath, long SourceSize, long TargetSize, DateTime SourceModified, DateTime TargetModified);

public enum ConflictChoice { Overwrite, Skip, Rename, OverwriteAll, SkipAll, Cancel }

public sealed record ErrorPrompt(string FilePath, string Message, int Attempt);

public enum ErrorChoice { Retry, Skip, SkipAll, Abort }

public sealed class EngineOptions
{
    public string Destination { get; set; } = string.Empty;
    public ConflictPolicy Conflict { get; set; } = ConflictPolicy.Ask;
    public ErrorPolicy OnError { get; set; } = ErrorPolicy.Ask;
    public VerifyAlgorithm Verify { get; set; } = VerifyAlgorithm.None;
    public bool VerifyAfterCopy { get; set; }
    public int RetryCount { get; set; } = 3;
    public int Parallelism { get; set; } = 2;
    public int BufferSize { get; set; } = 1024 * 1024;
    public long SpeedLimitBytesPerSecond { get; set; }
    public bool PreserveTimestamps { get; set; } = true;
    public bool UseRecycleBin { get; set; } = true;
}

public sealed class CopyEngine
{
    private CancellationTokenSource? _cts;
    private volatile bool _paused;
    private int _running;
    private long _doneBytes;
    private long _totalBytes;
    private readonly Stopwatch _clock = new();
    private ConflictChoice? _stickyConflict;
    private ErrorChoice? _stickyError;

    public event EventHandler? Finished;
    public Func<ConflictPrompt, ConflictChoice>? ConflictResolver { get; set; }
    public Func<ErrorPrompt, ErrorChoice>? ErrorResolver { get; set; }
    public Action<string>? Log { get; set; }

    public bool IsRunning => Volatile.Read(ref _running) != 0;
    public bool IsPaused => _paused;
    public long TotalBytes => Interlocked.Read(ref _totalBytes);
    public long DoneBytes => Interlocked.Read(ref _doneBytes);
    public TimeSpan Elapsed => _clock.Elapsed;
    public double SpeedBytesPerSecond
    {
        get
        {
            var seconds = _clock.Elapsed.TotalSeconds;
            return seconds > 0.4 ? DoneBytes / seconds : 0;
        }
    }

    public bool Start(IEnumerable<TransferItem> items, EngineOptions options)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;
        var list = items.ToList();
        if (list.Count == 0) { Volatile.Write(ref _running, 0); return false; }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _paused = false;
        _stickyConflict = null;
        _stickyError = null;
        options.Parallelism = Math.Clamp(options.Parallelism, 1, 16);
        options.BufferSize = Math.Clamp(options.BufferSize, 64 * 1024, 8 * 1024 * 1024);
        Interlocked.Exchange(ref _doneBytes, 0);
        Interlocked.Exchange(ref _totalBytes, list.Sum(item => Math.Max(0, item.SizeBytes)));
        _clock.Restart();
        _ = Task.Run(() => RunAsync(list, options, _cts.Token));
        return true;
    }

    public void SetPaused(bool paused) => _paused = paused;

    public void Cancel()
    {
        try { _cts?.Cancel(); } catch { }
    }

    private async Task RunAsync(List<TransferItem> items, EngineOptions options, CancellationToken token)
    {
        try
        {
            await Parallel.ForEachAsync(items, new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Parallelism,
                CancellationToken = token
            }, async (item, _) =>
            {
                token.ThrowIfCancellationRequested();
                item.State = ItemState.Running;
                item.Error = null;
                item.TransferredBytes = 0;
                item.CurrentFile = null;
                try
                {
                    await ProcessItemAsync(item, options, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) item.State = ItemState.Cancelled;
                    else if (item.State is ItemState.Running or ItemState.Paused or ItemState.Verifying)
                    {
                        item.State = string.IsNullOrEmpty(item.Error) ? ItemState.Completed : ItemState.Failed;
                        if (item.State == ItemState.Completed) item.TransferredBytes = Math.Max(item.TransferredBytes, item.SizeBytes);
                    }
                }
                catch (OperationCanceledException)
                {
                    item.State = ItemState.Cancelled;
                }
                catch (Exception ex)
                {
                    item.State = ItemState.Failed;
                    item.Error = ex.Message;
                    Log?.Invoke($"{item.Name}: {ex.Message}");
                }
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            foreach (var item in items.Where(i => i.State is ItemState.Pending or ItemState.Running or ItemState.Paused or ItemState.Verifying))
                item.State = ItemState.Cancelled;
        }
        catch (Exception ex)
        {
            Log?.Invoke(ex.Message);
        }
        finally
        {
            _clock.Stop();
            Volatile.Write(ref _running, 0);
            try { Finished?.Invoke(this, EventArgs.Empty); } catch { }
        }
    }

    private async Task ProcessItemAsync(TransferItem item, EngineOptions options, CancellationToken token)
    {
        var source = Path.GetFullPath(item.SourcePath);

        switch (item.Operation)
        {
            case OperationType.Delete:
                await DeleteAsync(item, options, token).ConfigureAwait(false);
                return;
            case OperationType.Test:
                await TestAsync(item, options, token).ConfigureAwait(false);
                return;
        }

        if (!Directory.Exists(source) && !File.Exists(source))
            throw new FileNotFoundException("No se encontró el origen: " + source, source);

        var target = string.IsNullOrWhiteSpace(item.DestinationPath)
            ? Path.Combine(options.Destination, Path.GetFileName(Path.TrimEndingDirectorySeparator(source)))
            : item.DestinationPath;
        item.DestinationPath = target;

        if (item.IsFolder)
        {
            if (IsSameOrChild(target, source))
                throw new IOException("El destino no puede estar dentro de la carpeta de origen.");
            if (File.Exists(target))
                throw new IOException("Ya existe un archivo con el nombre de la carpeta de destino: " + target);

            Directory.CreateDirectory(target);
            foreach (var directory in EnumerateDirectories(source))
            {
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
            }

            long done = 0;
            var errors = 0;
            foreach (var file in EnumerateFiles(source))
            {
                token.ThrowIfCancellationRequested();
                await WaitWhilePausedAsync(token).ConfigureAwait(false);
                item.CurrentFile = file;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(target, Path.GetRelativePath(source, file)))!);
                var destinationFile = Path.Combine(target, Path.GetRelativePath(source, file));
                var finalTarget = ResolveTarget(file, destinationFile, options, token);
                var copied = finalTarget is not null &&
                             await CopyFileWithPolicyAsync(file, finalTarget, item, options, done, token).ConfigureAwait(false);
                var length = SafeLength(file);
                done += length;
                item.TransferredBytes = done;
                if (!copied) errors++;
                else if (item.Operation == OperationType.Move) TryDeleteFile(file);
            }

            item.CurrentFile = null;
            if (errors > 0) item.Error ??= $"{errors} archivo(s) no se pudieron transferir";
            else if (item.Operation == OperationType.Move) TryDeleteFolder(source);
            return;
        }

        var finalTargetPath = ResolveTarget(source, target, options, token);
        if (finalTargetPath is null)
        {
            item.State = ItemState.Skipped;
            item.TransferredBytes = item.SizeBytes;
            return;
        }

        var success = await CopyFileWithPolicyAsync(source, finalTargetPath, item, options, 0, token).ConfigureAwait(false);
        if (!success) return;
        item.TransferredBytes = item.SizeBytes;
        if (item.Operation == OperationType.Move) TryDeleteFile(source);
    }

    private string? ResolveTarget(string source, string target, EngineOptions options, CancellationToken token)
    {
        if (Directory.Exists(target) && File.Exists(source))
            throw new IOException("Ya existe una carpeta con ese nombre: " + target);
        if (!File.Exists(target))
        {
            if (Directory.Exists(target))
                throw new IOException("Ya existe una carpeta con ese nombre: " + target);
            return target;
        }

        var sourceInfo = new FileInfo(source);
        var targetInfo = new FileInfo(target);
        var decision = DecideConflict(sourceInfo, targetInfo, options);

        switch (decision)
        {
            case ConflictChoice.Overwrite:
                return target;
            case ConflictChoice.Skip:
                return null;
            case ConflictChoice.Cancel:
                _cts?.Cancel();
                throw new OperationCanceledException(token);
            default:
                return GetUniquePath(target);
        }
    }

    private ConflictChoice DecideConflict(FileInfo source, FileInfo target, EngineOptions options)
    {
        if (_stickyConflict.HasValue) return _stickyConflict.Value;

        switch (options.Conflict)
        {
            case ConflictPolicy.Overwrite:
                return ConflictChoice.Overwrite;
            case ConflictPolicy.Skip:
                return ConflictChoice.Skip;
            case ConflictPolicy.Rename:
                return ConflictChoice.Rename;
            case ConflictPolicy.OverwriteOlder:
                return source.LastWriteTimeUtc > target.LastWriteTimeUtc ? ConflictChoice.Overwrite : ConflictChoice.Skip;
            case ConflictPolicy.OverwriteSmaller:
                return source.Length < target.Length ? ConflictChoice.Overwrite : ConflictChoice.Skip;
            default:
                var resolver = ConflictResolver;
                if (resolver is null) return ConflictChoice.Rename;
                var choice = resolver(new ConflictPrompt(source.FullName, target.FullName, source.Length, target.Length,
                    source.LastWriteTime, target.LastWriteTime));
                if (choice == ConflictChoice.OverwriteAll) { _stickyConflict = ConflictChoice.Overwrite; return ConflictChoice.Overwrite; }
                if (choice == ConflictChoice.SkipAll) { _stickyConflict = ConflictChoice.Skip; return ConflictChoice.Skip; }
                return choice;
        }
    }

    private async Task<bool> CopyFileWithPolicyAsync(string source, string target, TransferItem item, EngineOptions options,
        long baseDone, CancellationToken token)
    {
        var attempt = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await CopyFileAsync(source, target, item, options, baseDone, token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                attempt++;
                var choice = DecideError(source, ex, attempt, options);
                if (choice == ErrorChoice.Retry)
                {
                    if (attempt <= Math.Max(1, options.RetryCount))
                    {
                        await Task.Delay(150 * attempt, token).ConfigureAwait(false);
                        continue;
                    }
                    choice = ErrorChoice.Skip;
                }
                if (choice == ErrorChoice.Abort)
                {
                    item.Error = ex.Message;
                    _cts?.Cancel();
                    throw;
                }
                item.Error = ex.Message;
                return false;
            }
        }
    }

    private ErrorChoice DecideError(string file, Exception ex, int attempt, EngineOptions options)
    {
        switch (options.OnError)
        {
            case ErrorPolicy.Retry:
                return ErrorChoice.Retry;
            case ErrorPolicy.Skip:
                return ErrorChoice.Skip;
            case ErrorPolicy.Abort:
                return ErrorChoice.Abort;
            default:
                if (_stickyError == ErrorChoice.SkipAll) return ErrorChoice.Skip;
                var resolver = ErrorResolver;
                if (resolver is null) return ErrorChoice.Skip;
                var choice = resolver(new ErrorPrompt(file, ex.Message, attempt));
                if (choice == ErrorChoice.SkipAll) { _stickyError = ErrorChoice.SkipAll; return ErrorChoice.Skip; }
                return choice;
        }
    }

    private async Task CopyFileAsync(string source, string target, TransferItem item, EngineOptions options, long baseDone,
        CancellationToken token)
    {
        var info = new FileInfo(source);
        var directory = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(directory)) throw new IOException("Ruta de destino no válida: " + target);
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(directory, "." + Path.GetFileName(target) + ".xopy-part");
        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, options.BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, options.BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[options.BufferSize];
                var throttle = Stopwatch.StartNew();
                long copied = 0;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (_paused) await WaitWhilePausedAsync(token).ConfigureAwait(false);

                    var read = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);

                    copied += read;
                    item.TransferredBytes = baseDone + copied;
                    item.CurrentFile = source;
                    Interlocked.Add(ref _doneBytes, read);
                    await ThrottleAsync(copied, throttle, options, token).ConfigureAwait(false);
                }
                await output.FlushAsync(token).ConfigureAwait(false);
            }

            if (options.VerifyAfterCopy && options.Verify != VerifyAlgorithm.None)
            {
                item.State = ItemState.Verifying;
                try
                {
                    await CompareAsync(source, temp, options.Verify, token).ConfigureAwait(false);
                }
                finally
                {
                    item.State = ItemState.Running;
                }
            }

            if (File.Exists(target))
            {
                try { File.SetAttributes(target, FileAttributes.Normal); } catch { }
            }
            File.Move(temp, target, true);

            if (options.PreserveTimestamps)
            {
                try
                {
                    File.SetLastWriteTimeUtc(target, info.LastWriteTimeUtc);
                    File.SetCreationTimeUtc(target, info.CreationTimeUtc);
                }
                catch { }
            }
            try
            {
                var attributes = info.Attributes & ~(FileAttributes.ReparsePoint | FileAttributes.Directory);
                File.SetAttributes(target, attributes);
            }
            catch { }
        }
        finally
        {
            TryDeleteFile(temp);
        }
    }

    private async Task ThrottleAsync(long copied, Stopwatch clock, EngineOptions options, CancellationToken token)
    {
        if (options.SpeedLimitBytesPerSecond <= 0) return;
        var share = Math.Max(1, options.SpeedLimitBytesPerSecond / Math.Max(1, options.Parallelism));
        var expected = copied / (double)share;
        var delay = expected - clock.Elapsed.TotalSeconds;
        if (delay > 0.02) await Task.Delay(TimeSpan.FromSeconds(Math.Min(delay, 1)), token).ConfigureAwait(false);
    }

    private async Task CompareAsync(string source, string target, VerifyAlgorithm algorithm, CancellationToken token)
    {
        if (!File.Exists(target)) throw new FileNotFoundException("No existe el archivo de destino: " + target, target);
        var first = await Hashing.ComputeAsync(algorithm, source, token).ConfigureAwait(false);
        var second = await Hashing.ComputeAsync(algorithm, target, token).ConfigureAwait(false);
        if (!Hashing.Equal(first, second))
            throw new IOException($"{Format.Algorithm(algorithm)} no coincide: {Path.GetFileName(source)}");
    }

    private async Task TestAsync(TransferItem item, EngineOptions options, CancellationToken token)
    {
        var source = Path.GetFullPath(item.SourcePath);
        var algorithm = options.Verify == VerifyAlgorithm.None ? VerifyAlgorithm.Sha256 : options.Verify;
        var target = string.IsNullOrWhiteSpace(item.DestinationPath)
            ? Path.Combine(options.Destination, Path.GetFileName(Path.TrimEndingDirectorySeparator(source)))
            : item.DestinationPath;
        item.DestinationPath = target;

        if (item.IsFolder)
        {
            if (!Directory.Exists(target))
                throw new DirectoryNotFoundException("No se encontró la copia de destino: " + target);

            long done = 0;
            var errors = 0;
            foreach (var file in EnumerateFiles(source))
            {
                token.ThrowIfCancellationRequested();
                await WaitWhilePausedAsync(token).ConfigureAwait(false);
                item.CurrentFile = file;
                var other = Path.Combine(target, Path.GetRelativePath(source, file));
                try
                {
                    await CompareAsync(file, other, algorithm, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { errors++; item.Error = ex.Message; }

                var length = SafeLength(file);
                done += length;
                item.TransferredBytes = done;
                Interlocked.Add(ref _doneBytes, length);
            }
            item.CurrentFile = null;
            if (errors > 0) item.Error ??= $"{errors} archivo(s) no coinciden";
            return;
        }

        if (!File.Exists(source)) throw new FileNotFoundException("No se encontró el archivo: " + source, source);
        item.State = ItemState.Verifying;
        await CompareAsync(source, target, algorithm, token).ConfigureAwait(false);
        item.TransferredBytes = item.SizeBytes;
        Interlocked.Add(ref _doneBytes, Math.Max(0, item.SizeBytes));
        item.State = ItemState.Running;
    }

    private async Task DeleteAsync(TransferItem item, EngineOptions options, CancellationToken token)
    {
        var source = Path.GetFullPath(item.SourcePath);
        if (!File.Exists(source) && !Directory.Exists(source))
            throw new FileNotFoundException("No se encontró el elemento: " + source, source);

        if (options.UseRecycleBin)
        {
            if (!ShellFile.Recycle(source))
                throw new IOException("Windows no pudo enviar el elemento a la Papelera de reciclaje.");
            item.TransferredBytes = item.SizeBytes;
            item.State = ItemState.Completed;
            return;
        }

        if (File.Exists(source))
        {
            File.Delete(source);
            item.TransferredBytes = item.SizeBytes;
            Interlocked.Add(ref _doneBytes, Math.Max(0, item.SizeBytes));
            item.State = ItemState.Completed;
            return;
        }

        foreach (var file in EnumerateFiles(source))
        {
            token.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(token).ConfigureAwait(false);
            var length = SafeLength(file);
            File.Delete(file);
            Interlocked.Add(ref _doneBytes, length);
            item.TransferredBytes += length;
        }
        try { Directory.Delete(source, true); } catch { }
        item.State = ItemState.Completed;
        item.TransferredBytes = Math.Max(item.TransferredBytes, item.SizeBytes);
    }

    private async Task WaitWhilePausedAsync(CancellationToken token)
    {
        while (_paused)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(120, token).ConfigureAwait(false);
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var directory = stack.Pop();
            string[] files;
            string[] children;
            try
            {
                files = Directory.GetFiles(directory);
                children = Directory.GetDirectories(directory);
            }
            catch { continue; }

            foreach (var file in files)
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(file); } catch { continue; }
                if ((attributes & FileAttributes.ReparsePoint) == 0) yield return file;
            }
            foreach (var child in children)
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(child); } catch { continue; }
                if ((attributes & FileAttributes.ReparsePoint) == 0) stack.Push(child);
            }
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var directory = stack.Pop();
            string[] children;
            try { children = Directory.GetDirectories(directory); } catch { continue; }
            foreach (var child in children)
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(child); } catch { continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                yield return child;
                stack.Push(child);
            }
        }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private static string GetUniquePath(string path)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        return path;
    }

    public static bool IsSameOrChild(string candidate, string parent)
    {
        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            return path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteFolder(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }
}

public static class ShellFile
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT operation);

    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    public static bool Recycle(string path)
    {
        try
        {
            var operation = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = path + "\0",
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
            };
            return SHFileOperation(ref operation) == 0;
        }
        catch
        {
            return false;
        }
    }
}
