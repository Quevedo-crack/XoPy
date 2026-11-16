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
    public bool IsDirectoryEntry { get; set; }
    public bool IsExpanded { get; set; } = true;
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
    private readonly ConcurrentDictionary<Guid, long> _lastReported = new();
    private CancellationTokenSource? _cancellation;
    private volatile bool _paused;
    private int _running;
    private int _bufferSize = 1024 * 1024;
    private int _parallelism = 3;
    private long _speedLimitBytesPerSecond;

    public bool IsRunning => Volatile.Read(ref _running) != 0;
    public event EventHandler<TransferProgressEventArgs>? ProgressChanged;
    public event EventHandler? Completed;

    public void Start(IReadOnlyCollection<TransferJob> jobs, bool verify, ConflictResolution resolution, long speedLimitBytesPerSecond = 0, int parallelism = 3, int bufferSize = 1024 * 1024)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        _paused = false;
        _parallelism = Math.Clamp(parallelism, 1, 16);
        _bufferSize = Math.Clamp(bufferSize, 64 * 1024, 8 * 1024 * 1024);
        Interlocked.Exchange(ref _speedLimitBytesPerSecond, Math.Max(0, speedLimitBytesPerSecond));
        _ = RunAsync(jobs.ToList(), verify, resolution, _cancellation.Token);
    }

    public void SetPaused(bool paused) => _paused = paused;
    public void Cancel() => _cancellation?.Cancel();

    private async Task RunAsync(List<TransferJob> jobs, bool verify, ConflictResolution resolution, CancellationToken token)
    {
        try
        {
            await Parallel.ForEachAsync(jobs, new ParallelOptions { MaxDegreeOfParallelism = _parallelism, CancellationToken = token }, async (job, ct) =>
            {
                job.State = TransferState.Running; job.CurrentBytes = 0; job.ProgressPercent = 0; job.ErrorMessage = null;
                Report(job, true);
                try
                {
                    await ProcessJobAsync(job, verify, resolution, ct);
                    if (ct.IsCancellationRequested) job.State = TransferState.Cancelled;
                    else { job.State = TransferState.Completed; job.CurrentBytes = job.SizeBytes; job.ProgressPercent = 100; }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { job.State = TransferState.Cancelled; }
                catch (Exception ex) { job.State = TransferState.Failed; job.ErrorMessage = ex.Message; }
                Report(job, true);
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (token.IsCancellationRequested)
                foreach (var job in jobs.Where(j => j.State == TransferState.Pending)) { job.State = TransferState.Cancelled; Report(job, true); }
            Volatile.Write(ref _running, 0);
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ProcessJobAsync(TransferJob job, bool verify, ConflictResolution resolution, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (job.IsDirectoryEntry)
        {
            var root = Path.GetFullPath(job.SourcePath);
            var parent = job.GroupId.HasValue ? jobsafeParent(job) : string.Empty;
            _ = parent;
            var destinationRoot = GetDirectoryDestination(job);
            Directory.CreateDirectory(destinationRoot);
            return;
        }

        var isDirectory = Directory.Exists(job.SourcePath);
        if (!isDirectory && !File.Exists(job.SourcePath)) throw new FileNotFoundException("No se encontró el origen.", job.SourcePath);
        var source = Path.GetFullPath(job.SourcePath);
        if (isDirectory)
        {
            var destination = Path.GetFullPath(job.DestinationPath);
            if (IsSameOrChild(destination, source)) throw new IOException("El destino no puede estar dentro de la carpeta de origen.");
            var files = EnumerateFiles(source).ToArray();
            var rootDestination = Path.Combine(destination, Path.GetFileName(Path.TrimEndingDirectorySeparator(source)));
            Directory.CreateDirectory(rootDestination);
            foreach (var directory in EnumerateDirectories(source))
                Directory.CreateDirectory(Path.Combine(rootDestination, Path.GetRelativePath(source, directory)));
            long completed = 0;
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                var info = new FileInfo(file);
                var target = Path.Combine(rootDestination, Path.GetRelativePath(source, file));
                await CopyOneFileAsync(file, target, job, verify, resolution, completed, token);
                completed += info.Length;
            }
            return;
        }

        var targetPath = string.IsNullOrWhiteSpace(job.RelativePath)
            ? Path.Combine(job.DestinationPath, Path.GetFileName(source))
            : Path.Combine(job.DestinationPath, job.RelativePath);
        var destinationRootPath = GetDirectoryDestination(job);
        if (job.GroupId.HasValue)
        {
            var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(job.DestinationPath == string.Empty ? Path.GetDirectoryName(source)! : FindGroupSourceRoot(job)));
            destinationRootPath = Path.Combine(job.DestinationPath, folderName);
            targetPath = Path.Combine(destinationRootPath, job.RelativePath);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        await CopyOneFileAsync(source, targetPath, job, verify, resolution, 0, token);
    }

    // Group directory path is encoded in RelativePath's destination prefix by the GUI's parent group.
    private static string FindGroupSourceRoot(TransferJob job)
    {
        var source = Path.GetFullPath(job.SourcePath);
        var parts = job.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = source;
        for (var i = 0; i < parts.Length; i++) root = Path.GetDirectoryName(root)!;
        return root;
    }

    private static string jobsafeParent(TransferJob job) => Path.GetDirectoryName(job.SourcePath) ?? string.Empty;
    private static string GetDirectoryDestination(TransferJob job) => Path.Combine(job.DestinationPath, Path.GetFileName(Path.TrimEndingDirectorySeparator(jobsafeParent(job))));

    private async Task CopyOneFileAsync(string source, string requestedTarget, TransferJob job, bool verify, ConflictResolution resolution, long alreadyDone, CancellationToken token)
    {
        var gate = _targetLocks.GetOrAdd(Path.GetFullPath(requestedTarget), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            var target = requestedTarget;
            if (File.Exists(target) || Directory.Exists(target))
            {
                if (resolution == ConflictResolution.Skip) { job.CurrentBytes = job.SizeBytes; job.ProgressPercent = 100; return; }
                if (resolution == ConflictResolution.Rename) target = GetNonConflictingPath(target);
                else if (Directory.Exists(target)) throw new IOException($"No se puede sobrescribir una carpeta: {target}");
            }
            var info = new FileInfo(source);
            var partial = GetPartialPath(source, target);
            var metadataPath = partial + ".json";
            var metadata = new PartialMetadata(Path.GetFullPath(source), Path.GetFullPath(target), info.Length, info.LastWriteTimeUtc.Ticks);
            var offset = GetValidPartialLength(partial, metadataPath, metadata);
            var keep = false;
            try
            {
                File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata));
                await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, _bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var output = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None, _bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    input.Seek(offset, SeekOrigin.Begin);
                    var buffer = new byte[_bufferSize];
                    long copied = offset;
                    long thisRun = 0;
                    var throttleClock = Stopwatch.StartNew();
                    job.CurrentBytes = alreadyDone + offset;
                    int read;
                    while ((read = await input.ReadAsync(buffer.AsMemory(), token)) > 0)
                    {
                        await WaitIfPausedAsync(job, token);
                        await ThrottleAsync(thisRun, read, throttleClock, token);
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        copied += read; thisRun += read;
                        job.CurrentBytes = alreadyDone + copied;
                        job.ProgressPercent = job.SizeBytes == 0 ? 0 : Math.Min(99.9, job.CurrentBytes * 100d / job.SizeBytes);
                        Report(job);
                    }
                    await output.FlushAsync(token);
                }
                File.SetLastWriteTime(partial, info.LastWriteTime);
                if (verify && !await FilesMatchAsync(source, partial, token)) throw new IOException($"La verificación SHA-256 falló: {info.Name}");
                File.Move(partial, target, resolution == ConflictResolution.Overwrite);
                TryDelete(metadataPath);
                if (job.Operation == OperationType.Move) File.Delete(source);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { keep = true; throw; }
            finally { if (!keep) { TryDelete(partial); TryDelete(metadataPath); } }
        }
        finally { gate.Release(); }
    }

    private async Task ThrottleAsync(long written, int next, Stopwatch clock, CancellationToken token)
    {
        var globalLimit = Interlocked.Read(ref _speedLimitBytesPerSecond);
        if (globalLimit <= 0) return;
        var perJobLimit = Math.Max(1, globalLimit / _parallelism);
        var delay = (written + next) / (double)perJobLimit - clock.Elapsed.TotalSeconds;
        if (delay > .01) await Task.Delay(TimeSpan.FromSeconds(Math.Min(delay, 2)), token);
    }

    private async Task WaitIfPausedAsync(TransferJob job, CancellationToken token)
    {
        if (_paused && job.State != TransferState.Paused) { job.State = TransferState.Paused; Report(job, true); }
        while (_paused) { token.ThrowIfCancellationRequested(); await Task.Delay(150, token); }
        if (job.State == TransferState.Paused) { job.State = TransferState.Running; Report(job, true); }
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var dirs = new Stack<string>(); dirs.Push(root);
        while (dirs.Count > 0)
        {
            var dir = dirs.Pop();
            foreach (var file in Directory.EnumerateFiles(dir)) if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) yield return file;
            foreach (var child in Directory.EnumerateDirectories(dir)) if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) dirs.Push(child);
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string root)
    {
        var dirs = new Stack<string>(); dirs.Push(root);
        while (dirs.Count > 0)
        {
            var dir = dirs.Pop();
            foreach (var child in Directory.EnumerateDirectories(dir))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) { yield return child; dirs.Push(child); }
        }
    }

    private static long GetValidPartialLength(string part, string metaPath, PartialMetadata expected)
    {
        try
        {
            var saved = JsonSerializer.Deserialize<PartialMetadata>(File.ReadAllText(metaPath));
            var info = new FileInfo(part);
            if (saved is not null && File.Exists(part) && saved.SourcePath.Equals(expected.SourcePath, StringComparison.OrdinalIgnoreCase)
                && saved.TargetPath.Equals(expected.TargetPath, StringComparison.OrdinalIgnoreCase) && saved.Length == expected.Length
                && saved.LastWriteUtcTicks == expected.LastWriteUtcTicks && info.Length <= expected.Length) return info.Length;
        }
        catch { }
        TryDelete(part); TryDelete(metaPath); return 0;
    }

    private static string GetPartialPath(string source, string target)
    {
        var identity = Encoding.UTF8.GetBytes(Path.GetFullPath(source) + "\0" + Path.GetFullPath(target));
        return Path.Combine(Path.GetDirectoryName(target)!, $".xopy-{Convert.ToHexString(SHA256.HashData(identity))}.partial");
    }

    private static string GetNonConflictingPath(string path)
    {
        var dir = Path.GetDirectoryName(path)!; var name = Path.GetFileNameWithoutExtension(path); var ext = Path.GetExtension(path);
        for (var i = 1; ; i++) { var candidate = Path.Combine(dir, $"{name} ({i}){ext}"); if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate; }
    }

    private static bool IsSameOrChild(string path, string parent)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return candidate.Equals(root, StringComparison.OrdinalIgnoreCase) || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> FilesMatchAsync(string source, string target, CancellationToken token)
    {
        await using var a = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, _bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var b = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read, _bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(a, token), await SHA256.HashDataAsync(b, token));
    }

    private void Report(TransferJob job, bool force = false)
    {
        var now = Stopwatch.GetTimestamp();
        if (!force && _lastReported.TryGetValue(job.Id, out var previous) && Stopwatch.GetElapsedTime(previous, now) < TimeSpan.FromMilliseconds(250)) return;
        _lastReported[job.Id] = now;
        ProgressChanged?.Invoke(this, new TransferProgressEventArgs(job.Id, job.State, job.ProgressPercent, job.CurrentBytes));
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
