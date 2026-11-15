using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XoPy;

public enum OperationType
{
    Copy,
    Move
}

public enum TransferState
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

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

public class TransferProgressEventArgs : EventArgs
{
    public Guid JobId { get; }
    public TransferState State { get; }
    public double ProgressPercent { get; }
    public long CurrentBytes { get; }

    public TransferProgressEventArgs(Guid jobId, TransferState state, double progressPercent, long currentBytes)
    {
        JobId = jobId;
        State = state;
        ProgressPercent = progressPercent;
        CurrentBytes = currentBytes;
    }
}

public class CopyEngine
{
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Task? _workerTask;
    private bool _paused;

    public bool IsRunning { get; private set; }

    public event EventHandler<TransferProgressEventArgs>? ProgressChanged;
    public event EventHandler? Completed;

    public void Start(IEnumerable<TransferJob> jobs)
    {
        if (IsRunning)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _workerTask = Task.Run(() => RunAsync(jobs, _cts.Token));
        IsRunning = true;
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
    }

    public void Cancel()
    {
        if (_cts is not null)
        {
            _cts.Cancel();
        }

        IsRunning = false;
    }

    private async Task RunAsync(IEnumerable<TransferJob> jobs, CancellationToken token)
    {
        try
        {
            foreach (var job in jobs)
            {
                if (token.IsCancellationRequested)
                {
                    job.State = TransferState.Cancelled;
                    break;
                }

                job.State = TransferState.Running;
                await ProcessJobAsync(job, token).ConfigureAwait(false);

                if (token.IsCancellationRequested)
                {
                    job.State = TransferState.Cancelled;
                    break;
                }

                job.State = TransferState.Completed;
                job.ProgressPercent = 100;
            }
        }
        catch (Exception ex)
        {
            foreach (var job in jobs)
            {
                job.State = TransferState.Failed;
                job.ErrorMessage = ex.Message;
            }
        }
        finally
        {
            IsRunning = false;
            Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ProcessJobAsync(TransferJob job, CancellationToken token)
    {
        if (Directory.Exists(job.SourcePath))
        {
            var files = Directory.GetFiles(job.SourcePath, "*", SearchOption.AllDirectories);
            var total = files.Length;
            var processed = 0L;

            foreach (var file in files)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                while (_paused)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    await Task.Delay(150, token).ConfigureAwait(false);
                }

                var relativePath = Path.GetRelativePath(job.SourcePath, file);
                var targetFile = Path.Combine(job.DestinationPath, relativePath);

                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                await CopyFileAsync(file, targetFile, job.Operation, token).ConfigureAwait(false);

                processed++;
                job.CurrentBytes = processed;
                job.ProgressPercent = total > 0 ? (processed * 100d / total) : 100;
                ProgressChanged?.Invoke(this, new TransferProgressEventArgs(job.Id, job.State, job.ProgressPercent, job.CurrentBytes));
            }
        }
        else if (File.Exists(job.SourcePath))
        {
            var sourceInfo = new FileInfo(job.SourcePath);
            var targetFile = Path.Combine(job.DestinationPath, Path.GetFileName(job.SourcePath));
            Directory.CreateDirectory(job.DestinationPath);

            await CopyFileAsync(job.SourcePath, targetFile, job.Operation, token, sourceInfo.Length).ConfigureAwait(false);

            job.CurrentBytes = sourceInfo.Length;
            job.ProgressPercent = 100;
            ProgressChanged?.Invoke(this, new TransferProgressEventArgs(job.Id, job.State, 100, sourceInfo.Length));
        }
    }

    private async Task CopyFileAsync(string sourcePath, string targetPath, OperationType operation, CancellationToken token, long? totalBytes = null)
    {
        try
        {
            if (File.Exists(targetPath) && operation == OperationType.Copy)
            {
                var destInfo = new FileInfo(targetPath);
                if (destInfo.Exists && destInfo.Length == new FileInfo(sourcePath).Length)
                {
                    var sourceHash = await ComputeHashAsync(sourcePath, token).ConfigureAwait(false);
                    var destHash = await ComputeHashAsync(targetPath, token).ConfigureAwait(false);

                    if (string.Equals(sourceHash, destHash, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }

            await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await using var targetStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[1024 * 1024];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false)) > 0)
            {
                if (token.IsCancellationRequested)
                {
                    throw new OperationCanceledException(token);
                }

                while (_paused)
                {
                    if (token.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(token);
                    }

                    await Task.Delay(150, token).ConfigureAwait(false);
                }

                await targetStream.WriteAsync(buffer.AsMemory(0, bytesRead), token).ConfigureAwait(false);
                totalRead += bytesRead;

                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    var progress = (double)totalRead / totalBytes.Value * 100d;
                    ProgressChanged?.Invoke(this, new TransferProgressEventArgs(Guid.Empty, TransferState.Running, progress, totalRead));
                }
            }

            if (operation == OperationType.Move)
            {
                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }
            }

            File.SetLastWriteTime(targetPath, File.GetLastWriteTime(sourcePath));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw;
        }
    }

    private static async Task<string> ComputeHashAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, token).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
