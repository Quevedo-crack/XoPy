using System.Collections.Concurrent;
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
    public List<string> Destinations { get; set; } = new();
    public bool CopyLockedFiles { get; set; }
    public bool ResumePartials { get; set; } = true;
    public int SecureDeletePasses { get; set; }
    public ConflictPolicy Conflict { get; set; } = ConflictPolicy.Ask;
    public ErrorPolicy OnError { get; set; } = ErrorPolicy.Retry;
    public VerifyAlgorithm Verify { get; set; } = VerifyAlgorithm.None;
    public bool VerifyAfterCopy { get; set; }
    public bool ComputeHashes { get; set; }
    public CopyEngineKind Engine { get; set; } = CopyEngineKind.Native;
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
    private readonly ConcurrentDictionary<Guid, ManualResetEventSlim> _itemPauseEvents = new();
    private readonly object _parallelismLock = new();
    private TaskCompletionSource _parallelismChanged = NewSignal();
    private EngineOptions? _activeOptions;
    private int _parallelism = 1;
    private volatile bool _skipCurrent;
    private int _running;
    private long _doneBytes;
    private long _totalBytes;
    private long _pausedTicks;
    private long _pauseStarted;
    private long _errors;
    private readonly Stopwatch _clock = new();
    private ConflictChoice? _stickyConflict;
    private ErrorChoice? _stickyError;

    public event EventHandler? Finished;
    public Func<ConflictPrompt, ConflictChoice>? ConflictResolver { get; set; }
    public Func<ErrorPrompt, ErrorChoice>? ErrorResolver { get; set; }

    public bool IsRunning => Volatile.Read(ref _running) != 0;
    public bool IsPaused => _paused;
    public int Parallelism => Volatile.Read(ref _parallelism);

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void SetParallelism(int value)
    {
        value = Math.Clamp(value, 1, 16);
        lock (_parallelismLock)
        {
            if (_parallelism == value) return;
            Volatile.Write(ref _parallelism, value);
            if (_activeOptions is not null) _activeOptions.Parallelism = value;
            var signal = _parallelismChanged;
            _parallelismChanged = NewSignal();
            signal.TrySetResult();
        }
    }

    private (int Limit, Task Changed) ConcurrencySnapshot()
    {
        lock (_parallelismLock) return (_parallelism, _parallelismChanged.Task);
    }

    public void PauseItem(TransferItem item)
    {
        if (!IsRunning || item.Kind != ItemKind.File || item.State is ItemState.Completed or ItemState.Failed or ItemState.Skipped or ItemState.Cancelled) return;
        try { _itemPauseEvents.GetOrAdd(item.Id, _ => new ManualResetEventSlim(true)).Reset(); }
        catch (ObjectDisposedException) { return; }
        item.State = ItemState.Paused;
    }

    public void ResumeItem(TransferItem item)
    {
        try { if (_itemPauseEvents.TryGetValue(item.Id, out var gate)) gate.Set(); }
        catch (ObjectDisposedException) { }
        if (item.State == ItemState.Paused) item.State = ItemState.Running;
    }

    public bool IsItemPaused(TransferItem item)
    {
        try { return _itemPauseEvents.TryGetValue(item.Id, out var gate) && !gate.IsSet; }
        catch (ObjectDisposedException) { return false; }
    }
    public long TotalBytes => Interlocked.Read(ref _totalBytes);
    public long DoneBytes => Interlocked.Read(ref _doneBytes);
    public long Errors => Interlocked.Read(ref _errors);
    public TimeSpan Elapsed
    {
        get
        {
            var paused = Interlocked.Read(ref _pausedTicks);
            if (_paused)
            {
                var started = Interlocked.Read(ref _pauseStarted);
                if (started > 0) paused += _clock.Elapsed.Ticks - started;
            }
            var elapsed = _clock.Elapsed.Ticks - paused;
            return elapsed > 0 ? TimeSpan.FromTicks(elapsed) : TimeSpan.Zero;
        }
    }

    /// <summary>Tiempo transcurrido desde que comenzó la transferencia, incluyendo el tiempo en pausa.</summary>
    public TimeSpan ElapsedIncludingPauses => _clock.Elapsed;

    public double SpeedBytesPerSecond
    {
        get
        {
            if (_paused) return 0;
            var seconds = Elapsed.TotalSeconds;
            return seconds > 0.4 ? Math.Max(0, DoneBytes) / seconds : 0;
        }
    }

    /// <summary>Tiempo estimado que queda según la velocidad actual (TimeSpan.Zero si no se puede calcular).</summary>
    public TimeSpan Remaining
    {
        get
        {
            var speed = SpeedBytesPerSecond;
            var left = TotalBytes - DoneBytes;
            if (speed <= 1 || left <= 0) return TimeSpan.Zero;
            return TimeSpan.FromSeconds(left / speed);
        }
    }

    public bool Start(IEnumerable<TransferItem> items, EngineOptions options)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return false;
        // Una lista puede reintentarse cuando ya se transfirió entera (o después de mover: el
        // origen ya no existe). Antes se volvía a procesar todo y cada archivo acababa en
        // «Error» al 0 %. Se descartan los ya completados y los que no tienen origen.
        var requested = items.ToList();
        var list = new List<TransferItem>(requested.Count);
        var stale = 0;
        foreach (var item in requested)
        {
            if (item.State == ItemState.Completed) continue;
            if (!HasSource(item))
            {
                item.State = ItemState.Failed;
                item.Error = item.Kind == ItemKind.Folder
                    ? "La carpeta de origen ya no existe (puede haberse movido antes)."
                    : "El archivo de origen ya no existe (puede haberse movido antes).";
                item.TransferredBytes = 0;
                stale++;
                continue;
            }
            list.Add(item);
        }
        if (stale > 0)
            FileLog.Write($"Aviso: {stale} elemento(s) sin origen en disco; se marcan como Error sin reintentarlos.");

        if (list.Count == 0)
        {
            Volatile.Write(ref _running, 0);
            return false;
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _paused = false;
        Interlocked.Exchange(ref _pausedTicks, 0);
        Interlocked.Exchange(ref _pauseStarted, 0);
        _skipCurrent = false;
        _stickyConflict = null;
        _stickyError = null;
        options.Parallelism = Math.Clamp(options.Parallelism, 1, 16);
        lock (_parallelismLock)
        {
            _activeOptions = options;
            Volatile.Write(ref _parallelism, options.Parallelism);
        }
        foreach (var pair in _itemPauseEvents.ToArray())
        {
            pair.Value.Dispose();
            _itemPauseEvents.TryRemove(pair.Key, out _);
        }
        options.BufferSize = Math.Clamp(options.BufferSize, 64 * 1024, 8 * 1024 * 1024);
        options.Destinations = DestinationList(options);
        if (options.CopyLockedFiles) Privileges.EnableBackupPrivileges();

        var destinations = Math.Max(1, options.Destinations.Count);
        Interlocked.Exchange(ref _doneBytes, 0);
        Interlocked.Exchange(ref _errors, 0);
        Interlocked.Exchange(ref _totalBytes, list.Sum(item => Math.Max(0, item.SizeBytes)) * destinations);
        _clock.Restart();
        FileLog.Write($"Inicio: {list.Count} elementos, destinos={string.Join(" | ", options.Destinations)}, verificar={options.VerifyAfterCopy} ({options.Verify}), reanudar={options.ResumePartials}");
        _ = Task.Run(() => RunAsync(list, options, _cts.Token));
        return true;
    }

    public void SetPaused(bool paused)
    {
        if (paused == _paused) return;
        if (paused)
        {
            Interlocked.Exchange(ref _pauseStarted, _clock.Elapsed.Ticks);
        }
        else
        {
            var started = Interlocked.Exchange(ref _pauseStarted, 0);
            if (started > 0) Interlocked.Add(ref _pausedTicks, _clock.Elapsed.Ticks - started);
        }
        _paused = paused;
    }

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
            var active = new List<Task>();
            var next = 0;
            while (next < items.Count || active.Count > 0)
            {
                if (token.IsCancellationRequested)
                    for (; next < items.Count; next++) items[next].State = ItemState.Cancelled;

                var snapshot = ConcurrencySnapshot();
                while (next < items.Count && active.Count < snapshot.Limit && !token.IsCancellationRequested)
                {
                    var item = items[next++];
                    active.Add(ProcessItemAsync(item, options, token));
                }

                if (active.Count == 0) continue;
                if (next < items.Count && active.Count >= snapshot.Limit)
                    await Task.WhenAny(active.Append(snapshot.Changed)).ConfigureAwait(false);
                else
                    await Task.WhenAny(active).ConfigureAwait(false);
                active.RemoveAll(task => task.IsCompleted);
            }
        }
        catch (Exception ex)
        {
            FileLog.Write("ERROR general: " + ex);
        }
        finally
        {
            if (token.IsCancellationRequested)
                foreach (var item in items.Where(i => i.State is ItemState.Pending or ItemState.Running or ItemState.Paused or ItemState.Verifying))
                    item.State = ItemState.Cancelled;
            foreach (var pair in _itemPauseEvents.ToArray())
            {
                pair.Value.Dispose();
                _itemPauseEvents.TryRemove(pair.Key, out _);
            }
            _clock.Stop();
            Volatile.Write(ref _running, 0);
            lock (_parallelismLock) _activeOptions = null;
            if (items.Any(item => item.Operation == OperationType.Move) && Errors == 0)
                CleanupMovedFolders(items);
            FileLog.Write($"Fin: {DoneBytes} bytes en {Elapsed.TotalSeconds:0.0}s, errores={Errors}");
            try { Finished?.Invoke(this, EventArgs.Empty); } catch { }
        }
    }

    private async Task ProcessItemAsync(TransferItem item, EngineOptions options, CancellationToken token)
    {
        try
        {
            await WaitWhilePausedAsync(item, token).ConfigureAwait(false);
            item.State = ItemState.Running;
            item.Error = null;
            item.TransferredBytes = 0;
            item.CurrentFile = null;
            item.HashSource = null;
            item.HashTarget = null;
            await ProcessAsync(item, options, token).ConfigureAwait(false);
            if (token.IsCancellationRequested) item.State = ItemState.Cancelled;
            else if (item.State is ItemState.Running or ItemState.Paused or ItemState.Verifying)
            {
                item.State = string.IsNullOrEmpty(item.Error) ? ItemState.Completed : ItemState.Failed;
                if (item.State == ItemState.Completed) item.TransferredBytes = Math.Max(item.TransferredBytes, item.SizeBytes);
            }
        }
        catch (OperationCanceledException) { item.State = ItemState.Cancelled; }
        catch (SkipRequestedException) { item.State = ItemState.Skipped; }
        catch (Exception ex)
        {
            item.State = ItemState.Failed;
            item.Error = Describe(ex);
            Interlocked.Increment(ref _errors);
            FileLog.Write($"ERROR {item.SourcePath}: {ex}");
        }
    }

    /// <summary>Indica si el origen del elemento sigue existiendo en disco.</summary>
    private static bool HasSource(TransferItem item)
    {
        var path = item.SourcePath;
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (item.Kind == ItemKind.File) return File.Exists(path);
        // Carpeta o entrada de directorio: basta con que exista la carpeta. Si existe, sus
        // hijos se comprueban uno a uno al copiarse.
        return Directory.Exists(path);
    }

    /// <summary>Tras mover, borra las carpetas de origen que quedaron vacías (incluidas las raíz).</summary>
    private static void CleanupMovedFolders(IEnumerable<TransferItem> items)
    {
        foreach (var root in items
                     .Where(item => item.Kind == ItemKind.Folder && item.Operation == OperationType.Move)
                     .Select(item => item.SourcePath)
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                             .OrderByDescending(path => path.Length))
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory, false);
                    }
                    catch { }
                }
                if (!Directory.EnumerateFileSystemEntries(root).Any())
                {
                    Directory.Delete(root, false);
                    FileLog.Write("Carpeta de origen eliminada tras mover: " + root);
                }
            }
            catch (Exception ex)
            {
                FileLog.Write($"No se pudo limpiar la carpeta de origen {root}: {ex.Message}");
            }
        }
    }

    private static List<string> DestinationList(EngineOptions options)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.Destination)) list.Add(options.Destination);
        if (options.Destinations is not null) list.AddRange(options.Destinations);
        return list.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
            var destinations = DestinationList(options).Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
            if (destinations.Count == 0) throw new IOException("No hay ninguna carpeta de destino.");
            foreach (var destination in destinations)
            {
                var directory = Path.Combine(destination, item.RelativePath);
                Directory.CreateDirectory(directory);
            }
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

        var destinations = DestinationList(options);
        if (destinations.Count == 0 && !string.IsNullOrWhiteSpace(item.DestinationPath))
        {
            var folder = Path.GetDirectoryName(item.DestinationPath);
            if (!string.IsNullOrEmpty(folder)) destinations.Add(folder);
        }
        if (destinations.Count == 0) throw new IOException("No hay ninguna carpeta de destino.");

        var relative = string.IsNullOrWhiteSpace(item.RelativePath) ? Path.GetFileName(source) : item.RelativePath;
        var copied = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var destination in destinations)
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, relative);
            var directory = Path.GetDirectoryName(target);
            if (string.IsNullOrEmpty(directory)) throw new IOException("Ruta de destino no válida: " + target);
            Directory.CreateDirectory(directory);

            item.TransferredBytes = 0;
            var resolved = ResolveTarget(source, target, options, token);
            if (resolved is null)
            {
                skipped++;
                Interlocked.Add(ref _doneBytes, Math.Max(0, item.SizeBytes));
                continue;
            }

            await CopyWithRetryAsync(source, resolved, item, options, token).ConfigureAwait(false);
            // Un fallo en UN destino no debe impedir copiar a los demás: antes, si el
            // archivo ya existía y la política era «omitir», se abandonaban los otros
            // destinos y la segunda copia nunca se escribía.
            if (item.State is ItemState.Failed)
            {
                failed++;
                continue;
            }
            if (item.State is ItemState.Skipped)
            {
                skipped++;
                continue;
            }
            item.DestinationPath = resolved;
            copied++;
        }

        if (copied == 0 && skipped > 0)
        {
            item.State = ItemState.Skipped;
            item.TransferredBytes = item.SizeBytes;
            return;
        }
        if (copied == 0 && failed > 0) return;   // el estado de error ya lo puso el motor

        item.TransferredBytes = item.SizeBytes;
        // Al menos un destino se copió: el elemento no debe quedar marcado como fallido.
        if (item.State is ItemState.Failed) item.State = ItemState.Completed;

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
                if (item.TransferredBytes > 0)
                {
                    Interlocked.Add(ref _doneBytes, -item.TransferredBytes);
                    item.TransferredBytes = 0;
                }
                FileLog.Write($"Intento {attempt} fallido para {source} -> {target}: {ex.Message}");
                // Windows Defender, el Explorador o un reproductor pueden bloquear el archivo unos segundos.
                var win32 = ex.HResult & 0xFFFF;
                var transient = ex is UnauthorizedAccessException || win32 is 5 or 32 or 33;
                if (transient && attempt <= 2)
                {
                    await Task.Delay(350 * attempt, token).ConfigureAwait(false);
                    continue;
                }
                var choice = DecideError(source, ex, attempt, options);
                if (choice == ErrorChoice.Retry)
                {
                    if (attempt <= Math.Max(1, options.RetryCount))
                    {
                        await Task.Delay(250 * attempt, token).ConfigureAwait(false);
                        continue;
                    }

                    // Incluso con reintento automático, no abandonar el archivo en silencio:
                    // agotados los reintentos, pedir al usuario la decisión final.
                    choice = DecideError(source, ex, attempt, options, forcePrompt: true);
                    if (choice == ErrorChoice.Retry)
                    {
                        attempt = 0;
                        continue;
                    }
                }
                if (choice == ErrorChoice.Abort)
                {
                    item.Error = Describe(ex);
                    _cts?.Cancel();
                    throw;
                }
                item.Error = Describe(ex);
                item.State = choice is ErrorChoice.Skip or ErrorChoice.SkipAll ? ItemState.Skipped : ItemState.Failed;
                if (choice == ErrorChoice.SkipAll) _stickyError = ErrorChoice.SkipAll;
                return;
            }
        }
    }

    private async Task CopyCoreAsync(string source, string target, TransferItem item, EngineOptions options, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(target)!;
        var name = Path.GetFileName(target);
        var temp = Path.Combine(directory, "." + name + ".xopy-part");
        long sourceLength;
        try { sourceLength = new FileInfo(source).Length; } catch { sourceLength = 0; }

        long existing = 0;
        if (options.ResumePartials && File.Exists(temp))
        {
            try { existing = new FileInfo(temp).Length; } catch { existing = 0; }
            if (existing > sourceLength)
            {
                TryDeleteFile(temp);
                existing = 0;
            }
        }

        // Copia ya terminada en una ejecución anterior: solo hay que verificar y colocar.
        if (existing > 0 && existing == sourceLength)
        {
            item.TransferredBytes = sourceLength;
            await FinishFileAsync(source, temp, item, options, token).ConfigureAwait(false);
            MoveWithRetry(temp, target);
            ApplyMetadata(source, target, options);
            FileLog.Write($"Reanudada (completa) la copia parcial de {target}");
            return;
        }

        // Robocopy: el motor más rápido con muchos archivos, redes y rutas largas.
        if (options.Engine == CopyEngineKind.Robocopy && existing == 0)
        {
            try
            {
                await RobocopyAsync(source, target, item, options, token).ConfigureAwait(false);
                item.TransferredBytes = item.SizeBytes;
                await FinishFileAsync(source, target, item, options, token).ConfigureAwait(false);
                ApplyMetadata(source, target, options);
                FileLog.Write($"Copia con Robocopy: {source} -> {target}");
                return;
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(target);
                throw;
            }
            catch (SkipRequestedException)
            {
                TryDeleteFile(target);
                throw;
            }
            catch (Exception ex)
            {
                if (item.TransferredBytes > 0)
                {
                    Interlocked.Add(ref _doneBytes, -item.TransferredBytes);
                    item.TransferredBytes = 0;
                }
                FileLog.Write($"Robocopy falló para {source} ({ex.Message}); se usa el motor nativo.");
            }
        }

        // Motor nativo de Windows (CopyFileEx): mucho más rápido y conserva atributos y fechas.
        if (options.Engine == CopyEngineKind.Native && existing == 0 && !options.VerifyAfterCopy && !options.ComputeHashes)
        {
            try
            {
                await NativeCopyAsync(source, target, item, options, token).ConfigureAwait(false);
                item.TransferredBytes = item.SizeBytes;
                FileLog.Write($"Copia nativa: {source} -> {target}");
                return;
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(target);
                throw;
            }
            catch (SkipRequestedException)
            {
                TryDeleteFile(target);
                throw;
            }
            catch (Exception ex)
            {
                if (item.TransferredBytes > 0)
                {
                    Interlocked.Add(ref _doneBytes, -item.TransferredBytes);
                    item.TransferredBytes = 0;
                }
                FileLog.Write($"El motor nativo falló para {source} ({ex.Message}); se usa la copia administrada.");
            }
        }

        var keepTemp = false;
        try
        {
            if (existing > 0)
            {
                item.TransferredBytes = existing;
                FileLog.Write($"Reanudando {temp} desde {existing} de {sourceLength} bytes");
                await WriteFileAsync(source, temp, item, options, token, existing, true).ConfigureAwait(false);
            }
            else
            {
                await WriteFileAsync(source, temp, item, options, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            keepTemp = true;
            throw;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            if (existing > 0)
            {
                // No mezclamos una copia parcial con una escritura directa.
                keepTemp = true;
                throw;
            }

            // Si no se puede crear el temporal (permisos, rutas largas, unidades de red), copiamos directo.
            TryDeleteFile(temp);
            item.TransferredBytes = 0;
            FileLog.Write($"Temporal no disponible para {target}: {ex.Message}. Se copia directamente.");
            await WriteFileAsync(source, target, item, options, token).ConfigureAwait(false);
            await FinishFileAsync(source, target, item, options, token).ConfigureAwait(false);
            return;
        }

        try
        {
            await FinishFileAsync(source, temp, item, options, token).ConfigureAwait(false);
            MoveWithRetry(temp, target);
            ApplyMetadata(source, target, options);
        }
        catch
        {
            keepTemp = true;
            throw;
        }
        finally
        {
            if (!keepTemp) TryDeleteFile(temp);
        }
    }

    private async Task FinishFileAsync(string source, string writtenPath, TransferItem item, EngineOptions options, CancellationToken token)
    {
        if ((options.VerifyAfterCopy || options.ComputeHashes) && options.Verify != VerifyAlgorithm.None)
        {
            item.State = ItemState.Verifying;
            try
            {
                var sourceHash = await Hashing.ComputeAsync(options.Verify, source, token).ConfigureAwait(false);
                var targetHash = await Hashing.ComputeAsync(options.Verify, writtenPath, token).ConfigureAwait(false);
                item.HashSource = Hashing.ToText(options.Verify, sourceHash);
                item.HashTarget = Hashing.ToText(options.Verify, targetHash);
                if (options.VerifyAfterCopy && !Hashing.Equal(sourceHash, targetHash))
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

    private async Task WriteFileAsync(string source, string destination, TransferItem item, EngineOptions options,
        CancellationToken token, long offset = 0, bool append = false)
    {
        await using var input = OpenSourceRead(source, options, options.BufferSize);
        if (offset > 0) input.Seek(offset, SeekOrigin.Begin);
        await using var output = new FileStream(destination, append ? FileMode.Append : FileMode.Create, FileAccess.Write,
            FileShare.None, options.BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[options.BufferSize];
        var throttle = Stopwatch.StartNew();
        long copied = offset;

        // Tras una pausa se reinicia la cuenta del límite de velocidad: si se medía desde el
        // principio, al reanudar tocaba «esperar» todo el tiempo pausado y avanzaba lentísimo.
        var baseline = offset;
        var paused = false;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var pauseNow = _paused || IsItemPaused(item);
            if (paused && !pauseNow)
            {
                throttle.Restart();
                baseline = copied;
            }
            paused = pauseNow;
            await WaitWhilePausedAsync(item, token).ConfigureAwait(false);
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
            await ThrottleAsync(copied - baseline, throttle, options, token).ConfigureAwait(false);
        }

        await output.FlushAsync(token).ConfigureAwait(false);
    }

    /// <summary>Abre el origen permitiendo que otro programa lo tenga abierto; si está bloqueado, usa la API de copia de seguridad.</summary>
    /// <summary>Copia un archivo con Robocopy (rápido con muchos archivos, redes y rutas largas).</summary>
    [DllImport("ntdll.dll")]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll")]
    private static extern int NtResumeProcess(IntPtr processHandle);

    private async Task RobocopyAsync(string source, string target, TransferItem item, EngineOptions options, CancellationToken token)
    {
        var sourceDirectory = Path.GetDirectoryName(source);
        var targetDirectory = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(sourceDirectory) || string.IsNullOrEmpty(targetDirectory))
            throw new IOException("Ruta no válida para Robocopy.");
        var sourceName = Path.GetFileName(source);
        if (!string.Equals(sourceName, Path.GetFileName(target), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Robocopy no puede cambiar el nombre del archivo; se usa otro motor.");

        Directory.CreateDirectory(targetDirectory);
        ClearReadOnly(target);
        var length = new FileInfo(source).Length;

        var arguments = new StringBuilder()
            .Append('"').Append(sourceDirectory).Append("\" \"").Append(targetDirectory).Append("\" \"")
            .Append(sourceName).Append('"')
            .Append(" /COPY:DAT /DCOPY:T /IS /IT /R:").Append(Math.Max(0, options.RetryCount))
            .Append(" /W:1 /NJH /NJS /NP /NDL /NFL /NS /NC /XJ")
            .ToString();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("robocopy", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };
        if (!process.Start()) throw new IOException("No se pudo iniciar Robocopy.");

        var reported = 0L;
        var suspended = false;
        try
        {
            while (!process.HasExited)
            {
                token.ThrowIfCancellationRequested();
                var shouldPause = _paused || IsItemPaused(item);
                if (shouldPause && !suspended)
                {
                    suspended = NtSuspendProcess(process.Handle) == 0;
                    if (!suspended) await WaitWhilePausedAsync(item, token).ConfigureAwait(false);
                }
                else if (!shouldPause && suspended)
                {
                    NtResumeProcess(process.Handle);
                    suspended = false;
                }

                if (_skipCurrent)
                {
                    _skipCurrent = false;
                    try { process.Kill(true); } catch { }
                    throw new SkipRequestedException();
                }

                long copied = 0;
                try
                {
                    var info = new FileInfo(target);
                    if (info.Exists) copied = Math.Min(info.Length, length);
                }
                catch { }

                if (copied > reported)
                {
                    Interlocked.Add(ref _doneBytes, copied - reported);
                    reported = copied;
                    item.TransferredBytes = copied;
                }
                await Task.Delay(120, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            if (suspended) { try { NtResumeProcess(process.Handle); } catch { } }
            if (token.IsCancellationRequested) { try { process.Kill(true); } catch { } }
        }

        var code = process.ExitCode;
        if (code >= 8) throw new IOException($"Robocopy terminó con el código {code}.");

        if (reported < length)
        {
            Interlocked.Add(ref _doneBytes, length - reported);
            item.TransferredBytes = length;
        }
    }

    private async Task NativeCopyAsync(string source, string target, TransferItem item, EngineOptions options, CancellationToken token)
    {
        await Task.Run(() =>
        {
            ClearReadOnly(target);
            var transferred = 0L;
            // El reloj del límite de velocidad se reinicia al reanudar: antes se medía desde
            // el inicio de la copia, así que tras una pausa el retardo salía enorme y la
            // reanudación avanzaba «superlento».
            var clock = Stopwatch.StartNew();
            var wasPaused = false;
            var routine = new NativeMethods.CopyProgressRoutine((_, totalTransferred, _, _, _, _, _, _, _) =>
            {
                token.ThrowIfCancellationRequested();
                var pausedNow = _paused || IsItemPaused(item);
                if (wasPaused && !pausedNow)
                {
                    // Al reanudar se descarta el tiempo en pausa del cálculo del límite.
                    clock.Restart();
                    transferred = 0;
                }
                wasPaused = pausedNow;
                WaitWhilePaused(item, token);
                if (_skipCurrent)
                {
                    _skipCurrent = false;
                    return NativeMethods.CopyProgressResult.Cancel;
                }

                var delta = totalTransferred - transferred;
                if (delta > 0)
                {
                    transferred = totalTransferred;
                    Interlocked.Add(ref _doneBytes, delta);
                    item.TransferredBytes = transferred;
                }

                if (options.SpeedLimitBytesPerSecond > 0)
                {
                    var expected = transferred * 1000d / options.SpeedLimitBytesPerSecond;
                    var actual = clock.Elapsed.TotalMilliseconds;
                    if (expected > actual) Thread.Sleep((int)Math.Min(200, expected - actual));
                }
                return NativeMethods.CopyProgressResult.Continue;
            });

            if (!NativeMethods.CopyFileExW(source, target, routine, IntPtr.Zero, IntPtr.Zero, 0))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == 1235) throw new SkipRequestedException();
                throw new IOException("CopyFileEx falló: " + new System.ComponentModel.Win32Exception(error).Message,
                    unchecked((int)0x80070000) | error);
            }
        }, token).ConfigureAwait(false);
    }

    /// <summary>Windows puede bloquear el archivo recién escrito mientras lo analiza el antivirus.</summary>
    private static void MoveWithRetry(string temp, string target)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(target)) ClearReadOnly(target);
                File.Move(temp, target, true);
                return;
            }
            catch (Exception ex) when (attempt < 6 && ex is IOException or UnauthorizedAccessException)
            {
                FileLog.Write($"Reintento {attempt + 1} al mover {temp}: {ex.Message}");
                Thread.Sleep(150 * (attempt + 1));
            }
        }
    }

    private static Stream OpenSourceRead(string path, EngineOptions options, int bufferSize)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                    bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex;
                if (options.CopyLockedFiles)
                {
                    var backup = TryOpenWithBackupSemantics(path, bufferSize);
                    if (backup is not null) return backup;
                }
                Thread.Sleep(120 * (attempt + 1));
            }
        }
        throw last ?? new IOException("No se pudo abrir el archivo: " + path);
    }

    private static Stream? TryOpenWithBackupSemantics(string path, int bufferSize)
    {
        var handle = NativeMethods.CreateFileW(path, NativeMethods.GenericRead, NativeMethods.FileShareAll,
            IntPtr.Zero, NativeMethods.OpenExisting, NativeMethods.FileFlagBackupSemantics | NativeMethods.FileFlagSequentialScan,
            IntPtr.Zero);
        if (handle.IsInvalid) return null;
        FileLog.Write("Abierto con permisos de copia de seguridad: " + path);
        return new FileStream(handle, FileAccess.Read, bufferSize);
    }

    private async Task TestAsync(TransferItem item, EngineOptions options, CancellationToken token)
    {
        var source = Path.GetFullPath(item.SourcePath);
        var algorithm = options.Verify == VerifyAlgorithm.None ? VerifyAlgorithm.Sha256 : options.Verify;
        if (!File.Exists(source)) throw new FileNotFoundException("No se encontró el archivo: " + source, source);

        var destinations = DestinationList(options).Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        if (destinations.Count == 0 && !string.IsNullOrWhiteSpace(item.DestinationPath))
        {
            var folder = Path.GetDirectoryName(item.DestinationPath);
            if (!string.IsNullOrEmpty(folder)) destinations.Add(folder);
        }
        var relative = string.IsNullOrWhiteSpace(item.RelativePath) ? Path.GetFileName(source) : item.RelativePath;

        item.State = ItemState.Verifying;
        var sourceHash = await Hashing.ComputeAsync(algorithm, source, token).ConfigureAwait(false);
        item.HashSource = Hashing.ToText(algorithm, sourceHash);

        var checkedTargets = 0;
        foreach (var destination in destinations)
        {
            var target = Path.Combine(destination, relative);
            if (!File.Exists(target))
                throw new FileNotFoundException("No se encontró la copia de destino: " + target, target);

            var targetHash = await Hashing.ComputeAsync(algorithm, target, token).ConfigureAwait(false);
            item.HashTarget = Hashing.ToText(algorithm, targetHash);
            item.DestinationPath = target;
            if (!Hashing.Equal(sourceHash, targetHash))
                throw new IOException($"{Format.Algorithm(algorithm)} no coincide con el original en {target}.");
            checkedTargets++;
        }

        if (checkedTargets == 0) throw new IOException("No hay destinos que verificar.");
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
            if (options.SecureDeletePasses > 0) SecureWipe(source, options.SecureDeletePasses);
            DeleteFileForcibly(source);
            item.TransferredBytes = item.SizeBytes;
            Interlocked.Add(ref _doneBytes, Math.Max(0, item.SizeBytes));
            return;
        }

        foreach (var file in EnumerateFiles(source))
        {
            token.ThrowIfCancellationRequested();
            await WaitWhilePausedAsync(item, token).ConfigureAwait(false);
            var length = SafeLength(file);
            if (options.SecureDeletePasses > 0) SecureWipe(file, options.SecureDeletePasses);
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
        // Si la ruta de origen y destino es idéntica, nunca sobrescribimos el origen:
        // preguntamos si se omite o si se crea una copia con nombre único.
        if (string.Equals(source.FullName, target.FullName, StringComparison.OrdinalIgnoreCase))
        {
            var samePathPrompt = ConflictResolver;
            if (samePathPrompt is null) return ConflictChoice.Skip;
            var samePathChoice = samePathPrompt(new ConflictPrompt(source.FullName, target.FullName,
                source.Length, target.Length, source.LastWriteTime, target.LastWriteTime));
            return samePathChoice switch
            {
                ConflictChoice.Rename => ConflictChoice.Rename,
                ConflictChoice.Cancel => ConflictChoice.Cancel,
                _ => ConflictChoice.Skip
            };
        }

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

    private ErrorChoice DecideError(string file, Exception ex, int attempt, EngineOptions options, bool forcePrompt = false)
    {
        if (!forcePrompt)
        {
            switch (options.OnError)
            {
                case ErrorPolicy.Retry: return ErrorChoice.Retry;
                case ErrorPolicy.Skip: return ErrorChoice.Skip;
                case ErrorPolicy.Abort: return ErrorChoice.Abort;
            }
        }

        if (_stickyError == ErrorChoice.SkipAll) return ErrorChoice.Skip;
        var resolver = ErrorResolver;
        if (resolver is null) return ErrorChoice.Skip;
        var choice = resolver(new ErrorPrompt(file, Describe(ex), attempt));
        if (choice == ErrorChoice.SkipAll) { _stickyError = ErrorChoice.SkipAll; return ErrorChoice.Skip; }
        return choice;
    }

    private async Task ThrottleAsync(long copied, Stopwatch clock, EngineOptions options, CancellationToken token)
    {
        if (options.SpeedLimitBytesPerSecond <= 0) return;
        var share = Math.Max(1, options.SpeedLimitBytesPerSecond / Math.Max(1, options.Parallelism));
        var expected = copied / (double)share;
        var delay = expected - clock.Elapsed.TotalSeconds;
        if (delay > 0.02) await Task.Delay(TimeSpan.FromSeconds(Math.Min(delay, 1)), token).ConfigureAwait(false);
    }

    private async Task WaitWhilePausedAsync(TransferItem item, CancellationToken token)
    {
        while (_paused || IsItemPaused(item))
        {
            if (IsItemPaused(item)) item.State = ItemState.Paused;
            token.ThrowIfCancellationRequested();
            await Task.Delay(40, token).ConfigureAwait(false);
        }
        if (item.State == ItemState.Paused) item.State = ItemState.Running;
    }

    private void WaitWhilePaused(TransferItem item, CancellationToken token)
    {
        while (_paused || IsItemPaused(item))
        {
            if (IsItemPaused(item)) item.State = ItemState.Paused;
            token.ThrowIfCancellationRequested();
            Thread.Sleep(40);
        }
        if (item.State == ItemState.Paused) item.State = ItemState.Running;
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
        var code = ex.HResult & 0xFFFF;
        if (ex is PathTooLongException)
            return "La ruta es demasiado larga (más de 260 caracteres). Acorta los nombres de carpeta.";
        if (ex is DirectoryNotFoundException)
            return "No se encontró la carpeta de origen o de destino.";
        if (ex is UnauthorizedAccessException || code == 5)
            return "Acceso denegado (error 5). Se ha reintentado automáticamente: cierra el archivo en otros programas, " +
                   "comprueba que el destino no sea una carpeta protegida (Windows, Program Files) y que la unidad no esté llena ni en solo lectura. " +
                   "Si el archivo está bloqueado, activa «Copiar archivos bloqueados» en Opciones.";
        if (code is 32 or 33)
            return "El archivo está siendo usado por otro programa (error " + code + ") y no se ha podido abrir. " +
                   "Ciérralo y vuelve a intentarlo: XoPy reintenta automáticamente varias veces.";
        if (code == 112)
            return "No hay espacio suficiente en el disco de destino (error 112).";
        if (code == 1235)
            return "La copia se canceló.";
        if (code == 87 || code == 123)
            return "La ruta de destino no es válida. Revisa si el destino es la raíz de un disco o una unidad desconectada.";
        return ex.Message + (code != 0 ? $" (error {code})" : string.Empty);
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>Sobrescribe el contenido antes de borrar (borrado seguro).</summary>
    private static void SecureWipe(string path, int passes)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0) return;
            ClearReadOnly(path);
            var length = info.Length;
            var buffer = new byte[1024 * 1024];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, buffer.Length);
            for (var pass = 0; pass < Math.Max(1, passes); pass++)
            {
                stream.Position = 0;
                var remaining = length;
                while (remaining > 0)
                {
                    var chunk = (int)Math.Min(buffer.Length, remaining);
                    Random.Shared.NextBytes(buffer.AsSpan(0, chunk));
                    stream.Write(buffer, 0, chunk);
                    remaining -= chunk;
                }
                stream.Flush(true);
            }
            FileLog.Write($"Borrado seguro ({passes} pasada(s)): {path}");
        }
        catch (Exception ex)
        {
            FileLog.Write("No se pudo sobrescribir " + path + ": " + ex.Message);
        }
    }
}

internal static class NativeMethods
{
    public const uint GenericRead = 0x80000000;
    public const uint FileShareAll = 0x00000007;
    public const uint OpenExisting = 3;
    public const uint FileFlagBackupSemantics = 0x02000000;
    public const uint FileFlagSequentialScan = 0x08000000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess,
        uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LookupPrivilegeValueW(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges,
        ref TokenPrivileges newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    public enum CopyProgressResult : uint
    {
        Continue = 0,
        Cancel = 1,
        Stop = 2,
        Quiet = 3
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate CopyProgressResult CopyProgressRoutine(long totalFileSize, long totalBytesTransferred,
        long streamSize, long streamBytesTransferred, uint streamNumber, CopyProgressResult callbackReason,
        IntPtr sourceFile, IntPtr destinationFile, IntPtr data);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CopyFileExW(string existingFileName, string newFileName, CopyProgressRoutine progressRoutine,
        IntPtr data, IntPtr cancel, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public Luid Luid;
        public uint Attributes;
    }
}

/// <summary>Activa privilegios de copia de seguridad para poder leer archivos protegidos o en uso.</summary>
public static class Privileges
{
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x0002;

    public static void EnableBackupPrivileges()
    {
        Enable("SeBackupPrivilege");
        Enable("SeRestorePrivilege");
    }

    private static void Enable(string name)
    {
        var token = IntPtr.Zero;
        try
        {
            if (!NativeMethods.OpenProcessToken(NativeMethods.GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out token))
                return;
            if (!NativeMethods.LookupPrivilegeValueW(null, name, out var luid)) return;
            var privileges = new NativeMethods.TokenPrivileges
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SePrivilegeEnabled
            };
            NativeMethods.AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
        finally
        {
            if (token != IntPtr.Zero) NativeMethods.CloseHandle(token);
        }
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
