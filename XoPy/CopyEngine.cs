using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace XoPy;

public sealed record ConflictPrompt(string SourcePath, string TargetPath, long SourceSize, long TargetSize, DateTime SourceModified, DateTime TargetModified);

public enum ConflictChoice { Overwrite, Skip, Rename, OverwriteAll, SkipAll, Cancel }

public sealed record ErrorPrompt(string FilePath, string Message, int Attempt);

public enum ErrorChoice { Retry, Skip, SkipAll, Abort }

public sealed class EngineOptions
{
    public string Destination { get; set; } = string.Empty;
    public ConflictPolicy Conflict { get; set; } = ConflictPolicy.Ask;
    public ErrorPolicy OnError { get; set; } = ErrorPolicy.Retry;
    public VerifyAlgorithm Verify { get; set; } = VerifyAlgorithm.None;
    public bool VerifyAfterCopy { get; set; }
    public int RetryCount { get; set; } = 3;
    public int Parallelism { get; set; } = 2;
    public int BufferSize { get; set; } = 1024 * 1024;
    public long SpeedLimitBytesPerSecond { get; set; }
    public bool PreserveTimestamps { get; set; } = true;
    public bool UseRecycleBin { get; set; } = true;
}

public static class FileLog
{
    private static readonly object Gate = new();
    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XoPy", "transfer.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var directory = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(directory);
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > 2 * 1024 * 1024) info.Delete();
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { }
    }
}

public sealed class SkipRequestedException : Exception
{
    public SkipRequestedException() : base("El usuario saltó este archivo.") { }
}

public sealed class CopyEngine
{
    private CancellationTokenSource? _cts;
    private volatile bool _paused;
    private volatile bool _skipCurrent;
    private int _running;
    private long _doneBytes;
    private long _totalBytes;
    private long _errors;
    private readonly Stopwatch _clock = new();
    private ConflictChoice? _stickyConflict;
    private ErrorChoice? _stickyError;

    public event EventHandler? Finished;
    public Func<ConflictPrompt, ConflictChoice>? ConflictResolver { get; set; }
    public Func<ErrorPrompt, ErrorChoice>? ErrorResolver { get; set; }

    public bool IsRunning => Volatile.Read(ref _running) != 0;
    public bool IsPaused => _paused;
    public long TotalBytes => Interlocked.Read(ref _totalBytes);
    public long DoneBytes => Interlocked.Read(ref _doneBytes);
    public long Errors => Interlocked.Read(ref _errors);
    public TimeSpan Elapsed => _clock.Elapsed;

    public double SpeedBytesPerSecond
    {
        get
        {
            var seconds = _clock.Elapsed.TotalSeconds;
            return seconds > 0.4 ? Math.Max(0, DoneBytes) / seconds : 0;
        }
    }

    public bool Start(IEnumerable<TransferItem> items, EngineOptions options)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;
        var list = items.ToList();
        if (list.Count == 0)
        {
            Volatile.Write(ref _running, 0);
            return false;
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _paused = false;
        _skipCurrent = false;
        _stickyConflict = null;
        _stickyError = null;
        options.Parallelism = Math.Clamp(options.Parallelism, 1, 16);
        options.BufferSize = Math.Clamp(options.BufferSize, 64 * 1024, 8 * 1024 * 1024);

        Interlocked.Exchange(ref _doneBytes, 0);
        Interlocked.Exchange(ref _errors, 0);
        Interlocked.Exchange(ref _totalBytes, list.Sum(item => Math.Max(0, item.SizeBytes)));
        _clock.Restart();
        FileLog.Write($"Inicio: {list.Count} elementos, {options.Destination}, verificar={options.VerifyAfterCopy} ({options.Verify})");
        _ = Task.Run(() => RunAsync(list, options, _cts.Token));
        return true;
    }

    public void SetPaused(bool paused) => _paused = paused;

    /// <summary>Omite el archivo que se está copiando en este momento.</summary>
    public void SkipCurrent() => _skipCurrent = true;

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
                item.HashSource = null;
                item.HashTarget = null;
                try
                {
                    await ProcessAsync(item, options, token).ConfigureAwait(false);
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
                    item.Error = Describe(ex);
                    Interlocked.Increment(ref _errors);
                    FileLog.Write($"ERROR {item.SourcePath}: {ex}");
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
            FileLog.Write("ERROR general: " + ex);
        }
        finally
        {
            _clock.Stop();
            Volatile.Write(ref _running, 0);
            FileLog.Write($"Fin: {DoneBytes} bytes en {Elapsed.TotalSeconds:0.0}s, errores={Errors}");
            try { Finished?.Invoke(this, EventArgs.Empty); } catch { }
        }
    }

    private async Task ProcessAsync(TransferItem item, EngineOptions options, CancellationToken token)
    {
        if (item.Operation == OperationType.Delete)
        {
            await DeleteAsync(item, options, token).ConfigureAwait(false);
            return;
        }

        if (item.Kind == ItemKind.DirectoryEntry)
        {
            var directory = string.IsNullOrWhiteSpace(item.DestinationPath)
                ? Path.Combine(options.Destination, item.RelativePath)
                : item.DestinationPath;
            Directory.CreateDirectory(directory);
            item.TransferredBytes = 0;
            return;
        }

        if (item.Operation == OperationType.Test)
        {
            await TestAsync(item, options, token).ConfigureAwait(false);
            return;
        }

        await TransferFileAsync(item, options, token).ConfigureAwait(false);
    }

    private async Task TransferFileAsync(TransferItem item, EngineOptions options, CancellationToken token)
    {
        var source = Path.GetFullPath(item.SourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("No se encontró el archivo: " + source, source);

        var destination = string.IsNullOrWhiteSpace(item.DestinationPath)
            ? Path.Combine(options.Destination, Path.GetFileName(source))
            : item.DestinationPath;
        item.DestinationPath = destination;

        var directory = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(directory)) throw new IOException("Ruta de destino no válida: " + destination);
        Directory.CreateDirectory(directory);

        var target = ResolveTarget(source, destination, options, token);
        if (target is null)
        {
            item.State = ItemState.Skipped;
            item.TransferredBytes = item.SizeBytes;
            return;
        }

        await CopyWithRetryAsync(source, target, item, options, token).ConfigureAwait(false);
        item.TransferredBytes = item.SizeBytes;

        if (item.Operation == OperationType.Move)
        {
            try
            {
                DeleteFileForcibly(source);
            }
            catch (Exception ex)
            {
                item.Error = "Copiado, pero no se pudo borrar el origen: " + Describe(ex);
                Interlocked.Increment(ref _errors);
                FileLog.Write($"No se pudo borrar el origen {source}: {ex}");
            }
        }
    }

    private async Task CopyWithRetryAsync(string source, string target, TransferItem item, EngineOptions options, CancellationToken token)
    {
        var attempt = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await CopyCoreAsync(source, target, item, options, token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (SkipRequestedException)
            {
                item.State = ItemState.Skipped;
                item.Error = null;
                item.TransferredBytes = item.SizeBytes;
                FileLog.Write($"Archivo omitido por el usuario: {source}");
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                FileLog.Write($"Intento {attempt} fallido para {source} -> {target}: {ex.Message}");
                var choice = DecideError(source, ex, attempt, options);
                if (choice == ErrorChoice.Retry)
                {
                    if (attempt <= Math.Max(1, options.RetryCount))
                    {
                        await Task.Delay(250 * attempt, token).ConfigureAwait(false);
                        continue;
                    }
                    choice = ErrorChoice.Skip;
                }
                if (choice == ErrorChoice.Abort)
                {
                    item.Error = Describe(ex);
                    _cts?.Cancel();
                    throw;
                }
                item.Error = Describe(ex);
                item.State = ItemState.Failed;
                return;
            }
        }
    }

    private async Task CopyCoreAsync(string source, string target, TransferItem item, EngineOptions options, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(target)!;
        var name = Path.GetFileName(target);
        var temp = Path.Combine(directory, "." + name + ".xopy-part");

        // Primero intentamos escribir en un temporal para no dejar el destino a medias.
        // Si no se puede crear (permisos, rutas largas, unidades de red), escribimos directo.
        bool tempCreated = false;
        try
        {
            tempCreated = true;
            await WriteFileAsync(source, temp, item, options, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            TryDeleteFile(temp);
            tempCreated = false;
            item.TransferredBytes = 0;
            FileLog.Write($"Temporal no disponible para {target}: {ex.Message}. Se copia directamente.");
        }

        if (!tempCreated)
        {
            await WriteFileAsync(source, target, item, options, token).ConfigureAwait(false);
            await FinishFileAsync(source, target, item, options, token).ConfigureAwait(false);
            return;
        }

        try
        {
            await FinishFileAsync(source, temp, item, options, token).ConfigureAwait(false);
            if (File.Exists(target)) ClearReadOnly(target);
            File.Move(temp, target, true);
            ApplyMetadata(source, target, options);
        }
        finally
        {
            TryDeleteFile(temp);
        }
    }

    private async Task FinishFileAsync(string source, string writtenPath, TransferItem item, EngineOptions options, CancellationToken token)
    {
        if (options.VerifyAfterCopy && options.Verify != VerifyAlgorithm.None)
        {
            item.State = ItemState.Verifying;
            try
            {
                var sourceHash = await Hashing.ComputeAsync(options.Verify, source, token).ConfigureAwait(false);
                var targetHash = await Hashing.ComputeAsync(options.Verify, writtenPath, token).ConfigureAwait(false);
                item.HashSource = Hashing.ToText(options.Verify, sourceHash);
                item.HashTarget = Hashing.ToText(options.Verify, targetHash);
                if (!Hashing.Equal(sourceHash, targetHash))
                    throw new IOException($"{Format.Algorithm(options.Verify)} no coincide al verificar la copia.");
            }
            finally
            {
                item.State = ItemState.Running;
            }
        }

        if (options.PreserveTimestamps)
        {
            try
            {
                var info = new FileInfo(source);
                File.SetLastWriteTimeUtc(writtenPath, info.LastWriteTimeUtc);
                File.SetCreationTimeUtc(writtenPath, info.CreationTimeUtc);
            }
            catch { }
        }
    }

    private void ApplyMetadata(string source, string target, EngineOptions options)
    {
        try
        {
            var attributes = File.GetAttributes(source) & ~(FileAttributes.ReparsePoint | FileAttributes.Directory | FileAttributes.ReadOnly);
            File.SetAttributes(target, attributes);
        }
        catch { }
    }

    private async Task WriteFileAsync(string source, string destination, TransferItem item, EngineOptions options, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, options.BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, options.BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[options.BufferSize];
        var throttle = Stopwatch.StartNew();
        long copied = 0;

        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (_paused) await WaitWhilePausedAsync(token).ConfigureAwait(false);
            if (_skipCurrent)
            {
                _skipCurrent = false;
                throw new SkipRequestedException();
            }

            var read = await input.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (read == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);

            copied += read;
            item.TransferredBytes = copied;
            item.CurrentFile = source;
            Interlocked.Add(ref _doneBytes, read);
            await ThrottleAsync(copied, throttle, options, token).ConfigureAwait(false);
        }

        await output.FlushAsync(token).ConfigureAwait(false);
    }

    private async Task TestAsync(TransferItem item, EngineOptions options, CancellationToken token)
    {
        var source = Path.GetFullPath(item.SourcePath);
        var algorithm = options.Verify == VerifyAlgorithm.None ? VerifyAlgorithm.Sha256 : options.Verify;
        var target = string.IsNullOrWhiteSpace(item.DestinationPath)
            ? Path.Combine(options.Destination, Path.GetFileName(source))
            : item.DestinationPath;
        item.DestinationPath = target;

        if (!File.Exists(source)) throw new FileNotFoundException("No se encontró el archivo: " + source, source);
        if (!File.Exists(target)) throw new FileNotFoundException("No se encontró la copia de destino: " + target, target);

        item.State = ItemState.Verifying;
        var sourceHash = await Hashing.ComputeAsync(algorithm, source, token).ConfigureAwait(false);
        var targetHash = await Hashing.ComputeAsync(algorithm, target, token).ConfigureAwait(false);
        item.HashSource = Hashing.ToText(algorithm, sourceHash);
        item.HashTarget = Hashing.ToText(algorithm, targetHash);
        if (!Hashing.Equal(sourceHash, targetHash))
            throw new IOException($"{Format.Algorithm(algorithm)} no coincide con el original.");

        Interlocked.Add(ref _doneBytes, Math.Max(0, item.SizeBytes));
        item.TransferredBytes = item.SizeBytes;
        item.State = ItemState.Running;
    }

    private async Task DeleteAsync(TransferItem item, EngineOptions options, CancellationToken token)
    {
        var source = Path.GetFullPath(item.SourcePath);
        var isDirectory = Directory.Exists(source);
        if (!isDirectory && !File.Exists(source))
            throw new FileNotFoundException("No se encontró el elemento: " + source, source);

        if (options.UseRecycleBin)
        {
            if (!ShellFile.Recycle(source))
                throw new IOException("Windows no pudo enviar el elemento a la Papelera de reciclaje.");
            item.TransferredBytes = item.SizeBytes;
            item.State = ItemState.Completed;
            Interlocked.Add(ref _doneBytes, Math.Max(0, item.SizeBytes));
            return;
        }

        if (!isDirectory)
        {
            DeleteFileForcibly(source);
            item.TransferredBytes = item.SizeBytes;
            Interlocked.Add(ref _doneBytes, Math.Max(0, item.SizeBytes));
            return;
        }

        foreach (var file in EnumerateFiles(source))
        {
            token.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(token).ConfigureAwait(false);
            var length = SafeLength(file);
            DeleteFileForcibly(file);
            item.TransferredBytes += length;
            Interlocked.Add(ref _doneBytes, length);
        }
        foreach (var directory in EnumerateDirectories(source).Reverse())
        {
            try { Directory.Delete(directory, false); } catch { }
        }
        Directory.Delete(source, true);
        item.TransferredBytes = Math.Max(item.TransferredBytes, item.SizeBytes);
    }

    private string? ResolveTarget(string source, string target, EngineOptions options, CancellationToken token)
    {
        if (Directory.Exists(target) && File.Exists(source))
            throw new IOException("Ya existe una carpeta con ese nombre: " + target);
        if (!File.Exists(target))
        {
            if (Directory.Exists(target)) throw new IOException("Ya existe una carpeta con ese nombre: " + target);
            return target;
        }

        var decision = DecideConflict(new FileInfo(source), new FileInfo(target), options);
        switch (decision)
        {
            case ConflictChoice.Overwrite:
                ClearReadOnly(target);
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
            case ConflictPolicy.Overwrite: return ConflictChoice.Overwrite;
            case ConflictPolicy.Skip: return ConflictChoice.Skip;
            case ConflictPolicy.Rename: return ConflictChoice.Rename;
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

    private ErrorChoice DecideError(string file, Exception ex, int attempt, EngineOptions options)
    {
        switch (options.OnError)
        {
            case ErrorPolicy.Retry: return ErrorChoice.Retry;
            case ErrorPolicy.Skip: return ErrorChoice.Skip;
            case ErrorPolicy.Abort: return ErrorChoice.Abort;
            default:
                if (_stickyError == ErrorChoice.SkipAll) return ErrorChoice.Skip;
                var resolver = ErrorResolver;
                if (resolver is null) return ErrorChoice.Skip;
                var choice = resolver(new ErrorPrompt(file, Describe(ex), attempt));
                if (choice == ErrorChoice.SkipAll) { _stickyError = ErrorChoice.SkipAll; return ErrorChoice.Skip; }
                return choice;
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

    public static void DeleteFileForcibly(string path)
    {
        if (File.Exists(path))
        {
            ClearReadOnly(path);
            File.Delete(path);
        }
    }

    private static void ClearReadOnly(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }
        catch { }
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

    public static string Describe(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
            return "Acceso denegado: comprueba los permisos del origen y del destino, que el archivo no esté abierto en otro programa y que el destino no sea una carpeta protegida (Windows, Program Files) ni la raíz de un disco.";
        if (ex is PathTooLongException)
            return "La ruta es demasiado larga (más de 260 caracteres). Acorta los nombres de carpeta.";
        if (ex is DirectoryNotFoundException)
            return "No se encontró la carpeta de destino.";
        if (ex is IOException io && (io.HResult & 0xFFFF) == 32)
            return "El archivo está siendo usado por otro programa.";
        return ex.Message;
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
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
