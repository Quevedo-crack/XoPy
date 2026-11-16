using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XoPy;

public enum OperationType { Copy, Move }
public enum TransferState { Pending, Running, Paused, Completed, Failed, Cancelled }
public enum ConflictResolution { Rename, Overwrite, Skip }

public sealed class TransferJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public Guid? GroupId { get; set; }
    public bool IsFolderGroup { get; set; }
    public bool IsExpanded { get; set; }
    public bool IsDirectoryEntry { get; set; }
    public OperationType Operation { get; set; }
    public TransferState State { get; set; }
    public long SizeBytes { get; set; }
    public long CurrentBytes { get; set; }
    public double ProgressPercent { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class TransferProgressEventArgs(Guid jobId, TransferState state, double progressPercent, long currentBytes) : EventArgs
{
    public Guid JobId { get; } = jobId;
    public TransferState State { get; } = state;
    public double ProgressPercent { get; } = progressPercent;
    public long CurrentBytes { get; } = currentBytes;
}

public sealed class CopyEngine
{
    private sealed record PartialMetadata(string SourcePath, string TargetPath, long Length, long LastWriteUtcTicks);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _targetLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, long> _lastReportedAt = new();
    private CancellationTokenSource? _cancellation;
    private volatile bool _paused;
    private int _isRunning;
    private long _speedLimitBytesPerSecond;
    private int _maxDegreeOfParallelism = 4;
    private int _bufferSizeBytes = 1024 * 1024;

    public bool IsRunning => Volatile.Read(ref _isRunning) != 0;
    public event EventHandler<TransferProgressEventArgs>? ProgressChanged;
    public event EventHandler? Completed;

    public void Start(
        IReadOnlyCollection<TransferJob> jobs,
        bool verify,
        ConflictResolution conflictResolution,
        long speedLimitBytesPerSecond = 0,
        int maxDegreeOfParallelism = 4,
        int bufferSizeBytes = 1024 * 1024)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0) return;

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        _paused = false;
        Interlocked.Exchange(ref _speedLimitBytesPerSecond, Math.Max(0, speedLimitBytesPerSecond));
        _maxDegreeOfParallelism = Math.Clamp(maxDegreeOfParallelism, 1, 16);
        _bufferSizeBytes = Math.Clamp(bufferSizeBytes, 64 * 1024, 8 * 1024 * 1024);
        _ = RunAsync(jobs.ToList(), verify, conflictResolution, _cancellation.Token);
    }

    public void SetPaused(bool paused) => _paused = paused;
    public void Cancel() => _cancellation?.Cancel();

    private async Task RunAsync(List<TransferJob> jobs, bool verify, ConflictResolution resolution, CancellationToken token)
    {
        try
        {
            await Parallel.ForEachAsync(jobs, new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxDegreeOfParallelism,
                CancellationToken = token
            }, async (job, cancellationToken) =>
            {
                job.State = TransferState.Running;
                job.CurrentBytes = 0;
                job.ProgressPercent = 0;
                job.ErrorMessage = null;
                Report(job, force: true);
                try
                {
                    await ProcessJobAsync(job, verify, resolution, cancellationToken);
                    job.State = cancellationToken.IsCancellationRequested ? TransferState.Cancelled : TransferState.Completed;
                    if (job.State == TransferState.Completed)
                    {
                        job.CurrentBytes = job.SizeBytes;
                        job.ProgressPercent = 100;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    job.State = TransferState.Cancelled;
                }
                catch (Exception ex)
                {
                    job.State = TransferState.Failed;
                    job.ErrorMessage = ex.Message;
                }
                Report(job, force: true);
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (token.IsCancellationRequested)
            {
                foreach (var job in jobs.Where(job => job.State == TransferState.Pending))
                {
                    job.State = TransferState.Cancelled;
                    Report(job, force: true);
                }
            }
            Volatile.Write(ref _isRunning, 0);
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ProcessJobAsync(TransferJob job, bool verify, ConflictResolution resolution, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(job.SourcePath) || string.IsNullOrWhiteSpace(job.DestinationPath))
            throw new IOException("El origen o el destino están vacíos.");

        if (job.IsDirectoryEntry)
        {
            if (!Directory.Exists(job.SourcePath)) throw new DirectoryNotFoundException($"No se encontró la carpeta: {job.SourcePath}");
            var directoryTarget = ResolveTargetPath(job, isDirectory: true);
            Directory.CreateDirectory(directoryTarget);
            return;
        }

        if (Directory.Exists(job.SourcePath))
        {
            await ProcessLegacyDirectoryJobAsync(job, verify, resolution, token);
            return;
        }
        if (!File.Exists(job.SourcePath)) throw new FileNotFoundException("No se encontró el origen.", job.SourcePath);

        var target = ResolveTargetPath(job, isDirectory: false);
        if (PathsEqual(job.SourcePath, target))
            throw new IOException("El destino coincide con el origen; se canceló para proteger el archivo.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await CopyOneFileAsync(job.SourcePath, target, job, verify, resolution, 0, token);
    }

    private async Task ProcessLegacyDirectoryJobAsync(TransferJob job, bool verify, ConflictResolution resolution, CancellationToken token)
    {
        var sourceRoot = Path.GetFullPath(job.SourcePath);
        var destinationBase = Path.GetFullPath(job.DestinationPath);
        var destinationRoot = Path.Combine(destinationBase, Path.GetFileName(Path.TrimEndingDirectorySeparator(sourceRoot)));
        if (IsSameOrChild(destinationRoot, sourceRoot))
            throw new IOException("El destino no puede ser la misma carpeta de origen ni estar dentro de ella.");

        var directories = Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories).ToArray();
        foreach (var directory in directories)
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, directory)));
        }
        Directory.CreateDirectory(destinationRoot);

        var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToArray();
        long completedBytes = 0;
        foreach (var source in files)
        {
            token.ThrowIfCancellationRequested();
            await WaitIfPausedAsync(job, token);
            var info = new FileInfo(source);
            var relative = Path.GetRelativePath(sourceRoot, source);
            var target = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await CopyOneFileAsync(source, target, job, verify, resolution, completedBytes, token);
            completedBytes = SaturatingAdd(completedBytes, info.Length);
            job.CurrentBytes = completedBytes;
            job.ProgressPercent = job.SizeBytes <= 0 ? 100 : Math.Min(100, completedBytes * 100d / job.SizeBytes);
            Report(job);
        }

        if (job.Operation == OperationType.Move && !token.IsCancellationRequested)
        {
            foreach (var directory in directories.OrderByDescending(path => path.Length))
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            if (!Directory.EnumerateFileSystemEntries(sourceRoot).Any()) Directory.Delete(sourceRoot);
        }
    }

    private static string ResolveTargetPath(TransferJob job, bool isDirectory)
    {
        var destinationBase = Path.GetFullPath(job.DestinationPath);
        if (job.GroupId is Guid)
        {
            var relative = NormalizeRelativePath(job.RelativePath);
            var source = Path.GetFullPath(job.SourcePath);
            var sourceRoot = source;
            var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            var parentCount = isDirectory ? segments.Length : segments.Length;
            for (var i = 0; i < parentCount; i++)
            {
                sourceRoot = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceRoot))
                    ?? throw new IOException("La ruta relativa no coincide con la ruta de origen.");
            }

            var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(sourceRoot));
            if (string.IsNullOrWhiteSpace(folderName))
                throw new IOException("No se pudo determinar la carpeta raíz del elemento.");
            var groupedRoot = Path.Combine(destinationBase, folderName);
            return CombineUnderRoot(groupedRoot, relative);
        }

        var standaloneRelative = string.IsNullOrWhiteSpace(job.RelativePath)
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(job.SourcePath))
            : NormalizeRelativePath(job.RelativePath);
        return CombineUnderRoot(destinationBase, standaloneRelative);
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new IOException("La ruta relativa del elemento no es válida.");

        var segments = relativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
            throw new IOException("La ruta relativa del elemento no es segura.");
        return Path.Combine(segments);
    }

    private static string CombineUnderRoot(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullTarget = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var rootPrefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) || fullRoot.EndsWith(Path.AltDirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        if (!fullTarget.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("La ruta de destino sale de la carpeta de destino seleccionada.");
        return fullTarget;
    }

    private async Task CopyOneFileAsync(string source, string requestedTarget, TransferJob job, bool verify, ConflictResolution resolution, long completedBytes, CancellationToken token)
    {
        var targetLock = _targetLocks.GetOrAdd(Path.GetFullPath(requestedTarget), _ => new SemaphoreSlim(1, 1));
        await targetLock.WaitAsync(token);
        try
        {
            var target = requestedTarget;
            if (File.Exists(target) || Directory.Exists(target))
            {
                if (resolution == ConflictResolution.Skip) return;
                if (resolution == ConflictResolution.Rename) target = GetNonConflictingPath(target);
                else if (Directory.Exists(target)) throw new IOException($"No se puede sobrescribir una carpeta: {target}");
            }
            if (PathsEqual(source, target)) throw new IOException("El destino coincide con el origen; se canceló para proteger el archivo.");

            var sourceInfo = new FileInfo(source);
            var partial = GetPartialPath(source, target);
            var metadataPath = partial + ".json";
            var expected = new PartialMetadata(Path.GetFullPath(source), Path.GetFullPath(target), sourceInfo.Length, sourceInfo.LastWriteTimeUtc.Ticks);
            var offset = GetValidPartialLength(partial, metadataPath, expected);
            var preservePartial = false;
            try
            {
                File.WriteAllText(metadataPath, JsonSerializer.Serialize(expected));
                await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, _bufferSizeBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var output = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None, _bufferSizeBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    input.Seek(offset, SeekOrigin.Begin);
                    var buffer = new byte[_bufferSizeBytes];
                    long fileBytes = offset;
                    long bytesWrittenThisRun = 0;
                    var throttleClock = Stopwatch.StartNew();
                    job.CurrentBytes = completedBytes + offset;
                    UpdateProgress(job);
                    int read;
                    while ((read = await input.ReadAsync(buffer.AsMemory(), token)) > 0)
                    {
                        await WaitIfPausedAsync(job, token);
                        await ThrottleAsync(bytesWrittenThisRun, token, throttleClock);
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        bytesWrittenThisRun += read;
                        fileBytes += read;
                        job.CurrentBytes = completedBytes + fileBytes;
                        UpdateProgress(job);
                    }
                    await output.FlushAsync(token);
                }

                File.SetLastWriteTime(partial, sourceInfo.LastWriteTime);
                File.SetCreationTime(partial, sourceInfo.CreationTime);
                if (verify && !await FilesMatchAsync(source, partial, token))
                    throw new IOException($"La verificación SHA-256 falló: {sourceInfo.Name}");
                File.Move(partial, target, resolution == ConflictResolution.Overwrite);
                TryDelete(metadataPath);
                if (job.Operation == OperationType.Move) File.Delete(source);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                preservePartial = true;
                throw;
            }
            finally
            {
                if (!preservePartial)
                {
                    TryDelete(partial);
                    TryDelete(metadataPath);
                }
            }
        }
        finally { targetLock.Release(); }
    }

    private async Task ThrottleAsync(long bytesWritten, CancellationToken token, Stopwatch clock)
    {
        var limit = Interlocked.Read(ref _speedLimitBytesPerSecond);
        if (limit <= 0) return;
        var requiredSeconds = bytesWritten / (double)limit;
        var delay = requiredSeconds - clock.Elapsed.TotalSeconds;
        if (delay > 0.01) await Task.Delay(TimeSpan.FromSeconds(Math.Min(delay, 2)), token);
    }

    private static long GetValidPartialLength(string partial, string metadataPath, PartialMetadata expected)
    {
        try
        {
            if (!File.Exists(partial) || !File.Exists(metadataPath)) throw new IOException();
            var saved = JsonSerializer.Deserialize<PartialMetadata>(File.ReadAllText(metadataPath));
            var partialInfo = new FileInfo(partial);
            if (saved is not null
                && string.Equals(saved.SourcePath, expected.SourcePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(saved.TargetPath, expected.TargetPath, StringComparison.OrdinalIgnoreCase)
                && saved.Length == expected.Length
                && saved.LastWriteUtcTicks == expected.LastWriteUtcTicks
                && partialInfo.Length <= expected.Length)
                return partialInfo.Length;
        }
        catch { }
        TryDelete(partial);
        TryDelete(metadataPath);
        return 0;
    }

    private static string GetPartialPath(string source, string target)
    {
        var identity = Encoding.UTF8.GetBytes(Path.GetFullPath(source) + "\0" + Path.GetFullPath(target));
        return Path.Combine(Path.GetDirectoryName(target)!, $".xopy-{Convert.ToHexString(SHA256.HashData(identity))}.partial");
    }

    private static bool IsSameOrChild(string path, string parent)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return candidate.Equals(root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string first, string second) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);

    private static string GetNonConflictingPath(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 1; ; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    private static async Task<bool> FilesMatchAsync(string source, string destination, CancellationToken token)
    {
        await using var a = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var b = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hashA = await SHA256.HashDataAsync(a, token);
        var hashB = await SHA256.HashDataAsync(b, token);
        return CryptographicOperations.FixedTimeEquals(hashA, hashB);
    }

    private async Task WaitIfPausedAsync(TransferJob job, CancellationToken token)
    {
        if (_paused && job.State != TransferState.Paused)
        {
            job.State = TransferState.Paused;
            Report(job, force: true);
        }
        while (_paused)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(150, token);
        }
        if (job.State == TransferState.Paused)
        {
            job.State = TransferState.Running;
            Report(job, force: true);
        }
    }

    private void UpdateProgress(TransferJob job)
    {
        job.ProgressPercent = job.SizeBytes <= 0 ? 0 : Math.Min(100, job.CurrentBytes * 100d / job.SizeBytes);
        Report(job);
    }

    private void Report(TransferJob job, bool force = false)
    {
        var now = Stopwatch.GetTimestamp();
        if (!force)
        {
            var previous = _lastReportedAt.GetOrAdd(job.Id, 0);
            if (Stopwatch.GetElapsedTime(previous, now) < TimeSpan.FromMilliseconds(250)) return;
        }
        _lastReportedAt[job.Id] = now;
        ProgressChanged?.Invoke(this, new TransferProgressEventArgs(job.Id, job.State, job.ProgressPercent, job.CurrentBytes));
    }

    private static long SaturatingAdd(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
