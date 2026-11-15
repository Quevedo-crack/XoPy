using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XoPy;

public enum OperationType { Copy, Move }
public enum TransferState { Pending, Running, Paused, Completed, Failed, Cancelled }
public enum ConflictResolution { Rename, Overwrite, Skip }

public class TransferJob
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
    private sealed record PartialMetadata(string SourcePath, string TargetPath, long SourceLength, long SourceLastWriteUtcTicks);

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

    private async Task RunAsync(List<TransferJob> jobs, bool verify, ConflictResolution conflictResolution, CancellationToken token)
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
                    await ProcessJobAsync(job, verify, conflictResolution, token);
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
                foreach (var job in jobs.Where(j => j.State == TransferState.Pending))
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
        var sourceIsDirectory = Directory.Exists(job.SourcePath);
        if (!sourceIsDirectory && !File.Exists(job.SourcePath)) throw new FileNotFoundException("No se encontró el origen.", job.SourcePath);

        var sourceFullPath = Path.GetFullPath(job.SourcePath);
        var destinationFullPath = Path.GetFullPath(job.DestinationPath);
        if (sourceIsDirectory && IsSameOrChildPath(destinationFullPath, sourceFullPath))
            throw new IOException("El destino de una carpeta no puede ser la misma carpeta de origen ni estar dentro de ella.");

        var files = sourceIsDirectory
            ? Directory.EnumerateFiles(sourceFullPath, "*", SearchOption.AllDirectories).ToArray()
            : new[] { sourceFullPath };
        Directory.CreateDirectory(destinationFullPath);
        var completedBytes = 0L;

        foreach (var source in files)
        {
            token.ThrowIfCancellationRequested();
            await WaitIfPausedAsync(token);
            var sourceInfo = new FileInfo(source);
            var relative = sourceIsDirectory ? Path.GetRelativePath(sourceFullPath, source) : Path.GetFileName(source);
            var target = Path.Combine(destinationFullPath, relative);
            if (PathsEqual(source, target))
                throw new IOException("El archivo de destino coincide con el origen; se canceló para evitar sobrescribirlo o borrarlo.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await CopyOneFileAsync(source, target, job, verify, resolution, completedBytes, token);
            completedBytes += sourceInfo.Length;
            job.CurrentBytes = completedBytes;
            job.ProgressPercent = job.SizeBytes <= 0 ? 100 : Math.Min(100, completedBytes * 100d / job.SizeBytes);
            Report(job);
        }

        if (sourceIsDirectory && job.Operation == OperationType.Move && !token.IsCancellationRequested)
        {
            foreach (var directory in Directory.EnumerateDirectories(sourceFullPath, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
            {
                try { if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); } catch { }
            }
            try { if (!Directory.EnumerateFileSystemEntries(sourceFullPath).Any()) Directory.Delete(sourceFullPath); } catch { }
        }
    }

    private async Task CopyOneFileAsync(string source, string requestedTarget, TransferJob job, bool verify, ConflictResolution resolution, long completedBytes, CancellationToken token)
    {
        var target = requestedTarget;
        var targetIsFile = File.Exists(target);
        var targetIsDirectory = Directory.Exists(target);
        if (targetIsFile || targetIsDirectory)
        {
            if (resolution == ConflictResolution.Skip) return;
            if (resolution == ConflictResolution.Rename) target = GetNonConflictingPath(target);
            else if (targetIsDirectory) throw new IOException($"No se puede sobrescribir el destino porque es una carpeta: {target}");
        }

        if (PathsEqual(source, target))
            throw new IOException("El archivo de destino coincide con el origen; se canceló para evitar sobrescribirlo o borrarlo.");

        var sourceInfo = new FileInfo(source);
        var partial = GetPartialPath(source, target);
        var metadataPath = partial + ".json";
        var metadata = new PartialMetadata(Path.GetFullPath(source), Path.GetFullPath(target), sourceInfo.Length, sourceInfo.LastWriteTimeUtc.Ticks);
        var offset = GetValidPartialLength(partial, metadataPath, metadata);
        var preservePartial = false;

        try
        {
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata));
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                input.Seek(offset, SeekOrigin.Begin);
                var buffer = new byte[1024 * 1024];
                int read;
                long fileBytes = offset;
                job.CurrentBytes = completedBytes + offset;
                job.ProgressPercent = job.SizeBytes <= 0 ? 0 : Math.Min(100, job.CurrentBytes * 100d / job.SizeBytes);
                Report(job);
                while ((read = await input.ReadAsync(buffer.AsMemory(), token)) > 0)
                {
                    await WaitIfPausedAsync(token);
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    fileBytes += read;
                    job.CurrentBytes = completedBytes + fileBytes;
                    job.ProgressPercent = job.SizeBytes <= 0 ? 0 : Math.Min(100, job.CurrentBytes * 100d / job.SizeBytes);
                    Report(job);
                }
                await output.FlushAsync(token);
            }

            File.SetLastWriteTime(partial, sourceInfo.LastWriteTime);
            File.SetCreationTime(partial, sourceInfo.CreationTime);
            if (verify && !await FilesMatchAsync(source, partial, token)) throw new IOException($"La verificación SHA-256 falló: {sourceInfo.Name}");

            // File.Move without overwrite for Rename mode also protects against a race
            // where another process creates the candidate after we selected its name.
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

    private static long GetValidPartialLength(string partial, string metadataPath, PartialMetadata expected)
    {
        try
        {
            if (!File.Exists(partial) || !File.Exists(metadataPath))
            {
                TryDelete(partial);
                TryDelete(metadataPath);
                return 0;
            }

            var saved = JsonSerializer.Deserialize<PartialMetadata>(File.ReadAllText(metadataPath));
            var info = new FileInfo(partial);
            var matches = saved is not null
                && string.Equals(saved.SourcePath, expected.SourcePath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(saved.TargetPath, expected.TargetPath, StringComparison.OrdinalIgnoreCase)
                && saved.SourceLength == expected.SourceLength
                && saved.SourceLastWriteUtcTicks == expected.SourceLastWriteUtcTicks
                && info.Length <= expected.SourceLength;
            if (matches) return info.Length;
        }
        catch { }

        TryDelete(partial);
        TryDelete(metadataPath);
        return 0;
    }

    private static string GetPartialPath(string source, string target)
    {
        var identity = Encoding.UTF8.GetBytes(Path.GetFullPath(source) + "\0" + Path.GetFullPath(target));
        var hash = Convert.ToHexString(SHA256.HashData(identity));
        return Path.Combine(Path.GetDirectoryName(target)!, $".xopy-{hash}.partial");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static bool IsSameOrChildPath(string candidate, string parent)
    {
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        return normalizedCandidate.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(normalizedParent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string first, string second) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase);

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

    private async Task WaitIfPausedAsync(CancellationToken token)
    {
        while (_paused)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(150, token);
        }
    }

    private void Report(TransferJob job) => ProgressChanged?.Invoke(this, new TransferProgressEventArgs(job.Id, job.State, job.ProgressPercent, job.CurrentBytes));
}
