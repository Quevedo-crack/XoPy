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
    private CancellationTokenSource? _cancellation;
    private volatile bool _paused;
    private int _isRunning;

    public bool IsRunning => Volatile.Read(ref _isRunning) != 0;
    public event EventHandler<TransferProgressEventArgs>? ProgressChanged;
    public event EventHandler? Completed;

    public void Start(IReadOnlyCollection<TransferJob> jobs, bool verify, ConflictResolution conflictResolution)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0) return;
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        _paused = false;
        _ = RunAsync(jobs.ToList(), verify, conflictResolution, _cancellation.Token);
    }

    public void SetPaused(bool paused) => _paused = paused;
    public void Cancel() => _cancellation?.Cancel();

    private async Task RunAsync(List<TransferJob> jobs, bool verify, ConflictResolution resolution, CancellationToken token)
    {
        try
        {
            foreach (var job in jobs)
            {
                if (token.IsCancellationRequested) break;
                job.State = TransferState.Running;
                job.CurrentBytes = 0;
                job.ProgressPercent = 0;
                job.ErrorMessage = null;
                Report(job);
                try
                {
                    await ProcessJobAsync(job, verify, resolution, token);
                    if (token.IsCancellationRequested) job.State = TransferState.Cancelled;
                    else
                    {
                        job.State = TransferState.Completed;
                        job.CurrentBytes = job.SizeBytes;
                        job.ProgressPercent = 100;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    job.State = TransferState.Cancelled;
                    Report(job);
                    break;
                }
                catch (Exception ex)
                {
                    job.State = TransferState.Failed;
                    job.ErrorMessage = ex.Message;
                }
                Report(job);
            }

            if (token.IsCancellationRequested)
            {
                foreach (var job in jobs.Where(job => job.State == TransferState.Pending))
                {
                    job.State = TransferState.Cancelled;
                    Report(job);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ProcessJobAsync(TransferJob job, bool verify, ConflictResolution resolution, CancellationToken token)
    {
        var isDirectory = Directory.Exists(job.SourcePath);
        if (!isDirectory && !File.Exists(job.SourcePath)) throw new FileNotFoundException("No se encontró el origen.", job.SourcePath);

        var sourceRoot = Path.GetFullPath(job.SourcePath);
        var destinationRoot = Path.GetFullPath(job.DestinationPath);
        if (isDirectory && IsSameOrChild(destinationRoot, sourceRoot))
            throw new IOException("El destino no puede ser la misma carpeta de origen ni estar dentro de ella.");

        string[] files = isDirectory
            ? Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToArray()
            : [sourceRoot];
        if (isDirectory)
        {
            foreach (var sourceDirectory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                var relativeDirectory = Path.GetRelativePath(sourceRoot, sourceDirectory);
                Directory.CreateDirectory(Path.Combine(destinationRoot, relativeDirectory));
            }
        }
        Directory.CreateDirectory(destinationRoot);

        long completedBytes = 0;
        foreach (var source in files)
        {
            token.ThrowIfCancellationRequested();
            await WaitIfPausedAsync(job, token);
            var info = new FileInfo(source);
            var relative = isDirectory ? Path.GetRelativePath(sourceRoot, source) : Path.GetFileName(source);
            var target = Path.Combine(destinationRoot, relative);
            if (PathsEqual(source, target)) throw new IOException("El destino coincide con el origen; se canceló para proteger el archivo.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var copied = await CopyOneFileAsync(source, target, job, verify, resolution, completedBytes, token);
            completedBytes += info.Length;
            job.CurrentBytes = completedBytes;
            job.ProgressPercent = job.SizeBytes <= 0 ? 100 : Math.Min(100, completedBytes * 100d / job.SizeBytes);
            Report(job);
            if (!copied) continue;
        }

        if (isDirectory && job.Operation == OperationType.Move && !token.IsCancellationRequested)
        {
            foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories).OrderByDescending(path => path.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            if (!Directory.EnumerateFileSystemEntries(sourceRoot).Any()) Directory.Delete(sourceRoot);
        }
    }

    private async Task<bool> CopyOneFileAsync(string source, string requestedTarget, TransferJob job, bool verify, ConflictResolution resolution, long completedBytes, CancellationToken token)
    {
        var target = requestedTarget;
        if (File.Exists(target) || Directory.Exists(target))
        {
            if (resolution == ConflictResolution.Skip) return false;
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
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                input.Seek(offset, SeekOrigin.Begin);
                var buffer = new byte[1024 * 1024];
                long fileBytes = offset;
                job.CurrentBytes = completedBytes + offset;
                UpdateProgress(job);
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(), token)) > 0)
                {
                    await WaitIfPausedAsync(job, token);
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    fileBytes += read;
                    job.CurrentBytes = completedBytes + fileBytes;
                    UpdateProgress(job);
                }
                await output.FlushAsync(token);
            }

            File.SetLastWriteTime(partial, sourceInfo.LastWriteTime);
            File.SetCreationTime(partial, sourceInfo.CreationTime);
            if (verify && !await FilesMatchAsync(source, partial, token)) throw new IOException($"La verificación SHA-256 falló: {sourceInfo.Name}");
            File.Move(partial, target, resolution == ConflictResolution.Overwrite);
            TryDelete(metadataPath);
            if (job.Operation == OperationType.Move) File.Delete(source);
            return true;
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
            Report(job);
        }
        while (_paused)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(150, token);
        }
        if (job.State == TransferState.Paused)
        {
            job.State = TransferState.Running;
            Report(job);
        }
    }

    private void UpdateProgress(TransferJob job)
    {
        job.ProgressPercent = job.SizeBytes <= 0 ? 0 : Math.Min(100, job.CurrentBytes * 100d / job.SizeBytes);
        Report(job);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private void Report(TransferJob job) => ProgressChanged?.Invoke(this, new TransferProgressEventArgs(job.Id, job.State, job.ProgressPercent, job.CurrentBytes));
}
