using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace XoPy;

public partial class MainForm : Form
{
    private sealed record HistoryEntry(DateTime FinishedAt, string Result, int FileCount, long BytesCopied, TimeSpan Elapsed, double AverageBytesPerSecond);
    private sealed record AppSettings(string Destination, int Operation, int Conflict, bool Verify, int SpeedLimitMiB, int ParallelJobs, int BufferKiB);

    private readonly List<TransferJob> _jobs = new();
    private readonly CopyEngine _engine = new();
    private readonly OpenFileDialog _filesDialog = new() { Multiselect = true, Filter = "Todos los archivos (*.*)|*.*" };
    private readonly FolderBrowserDialog _folderDialog = new();
    private readonly FolderBrowserDialog _destinationDialog = new();
    private readonly SaveFileDialog _saveQueueDialog = new() { Filter = "Colas XoPy (*.json)|*.json", DefaultExt = "json", FileName = "xopy-queue.json" };
    private readonly OpenFileDialog _loadQueueDialog = new() { Filter = "Colas XoPy (*.json)|*.json", DefaultExt = "json" };
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 350 };
    private readonly Stopwatch _clock = new();
    private readonly string _appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XoPy");
    private readonly Dictionary<Guid, TransferProgressEventArgs> _latestProgress = new();
    private List<TransferJob> _activeJobs = new();
    private bool _paused;
    private int _sortColumn = -1;
    private bool _sortAscending = true;
    private int _parallelJobs = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
    private int _bufferKiB = 1024;
    private int _speedLimitMiB;
    private long _lastSampleBytes;
    private DateTime _lastSampleAt = DateTime.UtcNow;
    private double _currentSpeed;

    private string SettingsFile => Path.Combine(_appData, "settings.json");
    private string HistoryFile => Path.Combine(_appData, "history.json");

    public MainForm()
    {
        InitializeComponent();
        QueueListView.Columns.Add("Nombre", 240);
        QueueListView.Columns.Add("Origen", 300);
        QueueListView.Columns.Add("Destino relativo", 240);
        QueueListView.Columns.Add("Tamaño", 100);
        QueueListView.Columns.Add("Estado", 150);
        QueueListView.Columns.Add("Progreso", 100);
        QueueListView.ColumnClick += QueueListView_ColumnClick;
        QueueListView.KeyDown += QueueListView_KeyDown;
        QueueListView.DoubleClick += QueueListView_DoubleClick;
        QueueListView.AllowDrop = true;
        QueueListView.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        QueueListView.DragDrop += (_, e) => AddDroppedPaths(e);
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => AddDroppedPaths(e);

        OperationComboBox.Items.AddRange(new object[] { "Copiar", "Mover" });
        OperationComboBox.SelectedIndex = 0;
        ConflictComboBox.Items.AddRange(new object[] { "Cambiar nombre", "Sobrescribir", "Omitir" });
        ConflictComboBox.SelectedIndex = 0;
        DestinationTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "XoPy_Transfers");
        LoadSettings();

        _engine.ProgressChanged += Engine_ProgressChanged;
        _engine.Completed += Engine_Completed;
        _uiTimer.Tick += (_, _) => FlushProgressAndUpdateMetrics();
        _uiTimer.Start();
        TrayIcon.Icon = SystemIcons.Application;
        TrayIcon.Text = "XoPy - Listo";
        TrayIcon.ContextMenuStrip = _trayMenu;
        TrayIcon.DoubleClick += (_, _) => RestoreFromTray();
        _trayMenu.Items.Add("Abrir XoPy", null, (_, _) => RestoreFromTray());
        _trayMenu.Items.Add("Pausar / reanudar", null, PauseButton_Click);
        _trayMenu.Items.Add("Historial", null, ShowHistoryDialog);
        _trayMenu.Items.Add("Salir", null, (_, _) => Close());

        RefreshQueue();
    }

    private void AddFilesButton_Click(object? sender, EventArgs e)
    {
        if (_filesDialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var file in _filesDialog.FileNames) AddSingleFile(file);
        RefreshQueue();
    }

    private void AddFolderButton_Click(object? sender, EventArgs e)
    {
        if (_folderDialog.ShowDialog(this) != DialogResult.OK) return;
        AddFolderTree(_folderDialog.SelectedPath);
        RefreshQueue();
    }

    private void AddFolderTree(string folderPath)
    {
        try
        {
            var root = Path.GetFullPath(folderPath);
            if (_jobs.Any(j => j.IsFolderGroup && PathsEqual(j.SourcePath, root))) return;
            var groupId = Guid.NewGuid();
            var group = new TransferJob
            {
                Id = groupId,
                Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(root)),
                SourcePath = root,
                DestinationPath = DestinationTextBox.Text,
                RelativePath = string.Empty,
                IsFolderGroup = true,
                IsExpanded = true,
                State = TransferState.Pending
            };
            _jobs.Add(group);
            foreach (var file in EnumerateFilesWithoutReparsePoints(root))
            {
                var info = new FileInfo(file);
                var relative = Path.GetRelativePath(root, file);
                var job = new TransferJob
                {
                    Name = Path.GetFileName(file),
                    SourcePath = file,
                    DestinationPath = DestinationTextBox.Text,
                    RelativePath = relative,
                    GroupId = groupId,
                    State = TransferState.Pending,
                    SizeBytes = info.Length
                };
                _jobs.Add(job);
                group.SizeBytes = SaturatingAdd(group.SizeBytes, job.SizeBytes);
            }
            // Queue empty subdirectories as visible, independently manageable directory entries.
            foreach (var dir in EnumerateDirectoriesWithoutReparsePoints(root).Where(d => !Directory.EnumerateFileSystemEntries(d).Any()))
            {
                var relative = Path.GetRelativePath(root, dir);
                _jobs.Add(new TransferJob
                {
                    Name = Path.GetFileName(dir), SourcePath = dir, DestinationPath = DestinationTextBox.Text,
                    RelativePath = relative, GroupId = groupId, IsDirectoryEntry = true, State = TransferState.Pending
                });
            }
        }
        catch (Exception ex) { StatusLabel.Text = $"No se pudo añadir la carpeta: {ex.Message}"; }
    }

    private static IEnumerable<string> EnumerateFilesWithoutReparsePoints(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(directory))
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) yield return file;
            foreach (var child in Directory.EnumerateDirectories(directory))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesWithoutReparsePoints(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                yield return child;
                pending.Push(child);
            }
        }
    }

    private void AddSingleFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var full = Path.GetFullPath(path);
            if (_jobs.Any(j => !j.IsFolderGroup && !j.IsDirectoryEntry && PathsEqual(j.SourcePath, full))) return;
            _jobs.Add(new TransferJob
            {
                Name = Path.GetFileName(full), SourcePath = full, DestinationPath = DestinationTextBox.Text,
                RelativePath = Path.GetFileName(full), State = TransferState.Pending, SizeBytes = new FileInfo(full).Length
            });
        }
        catch (Exception ex) { StatusLabel.Text = $"No se pudo añadir el archivo: {ex.Message}"; }
    }

    private void BrowseDestinationButton_Click(object? sender, EventArgs e)
    {
        if (Directory.Exists(DestinationTextBox.Text)) _destinationDialog.SelectedPath = DestinationTextBox.Text;
        if (_destinationDialog.ShowDialog(this) == DialogResult.OK) DestinationTextBox.Text = _destinationDialog.SelectedPath;
    }

    private void ShowOptionsDialog(object? sender, EventArgs e)
    {
        using var dialog = new OptionsDialog(DestinationTextBox.Text, OperationComboBox.SelectedIndex, ConflictComboBox.SelectedIndex, VerifyCheckBox.Checked, _speedLimitMiB, _parallelJobs, _bufferKiB);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        DestinationTextBox.Text = dialog.Destination;
        OperationComboBox.SelectedIndex = dialog.Operation;
        ConflictComboBox.SelectedIndex = dialog.Conflict;
        VerifyCheckBox.Checked = dialog.Verify;
        _speedLimitMiB = dialog.SpeedLimitMiB;
        _parallelJobs = dialog.ParallelJobs;
        _bufferKiB = dialog.BufferKiB;
        SaveSettings();
        StatusLabel.Text = "Preferencias guardadas.";
    }

    private void StartButton_Click(object? sender, EventArgs e)
    {
        if (_engine.IsRunning) return;
        var destination = DestinationTextBox.Text.Trim();
        if (destination.Length == 0) { ShowError("Elige una carpeta de destino."); return; }
        var pending = _jobs.Where(IsRunnable).Where(j => j.State is TransferState.Pending or TransferState.Cancelled or TransferState.Failed).ToList();
        if (pending.Count == 0) { StatusLabel.Text = "No hay archivos pendientes."; return; }
        try { Directory.CreateDirectory(destination); }
        catch (Exception ex) { ShowError($"No se pudo crear el destino: {ex.Message}"); return; }
        foreach (var job in pending)
        {
            job.DestinationPath = destination;
            job.Operation = OperationComboBox.SelectedIndex == 1 ? OperationType.Move : OperationType.Copy;
            job.State = TransferState.Pending;
            job.CurrentBytes = 0;
            job.ProgressPercent = 0;
            job.ErrorMessage = null;
        }
        _activeJobs = pending;
        _paused = false;
        _lastSampleBytes = 0;
        _currentSpeed = 0;
        _lastSampleAt = DateTime.UtcNow;
        _clock.Restart();
        PauseButton.Text = "Ⅱ  Pausar";
        _engine.Start(pending, VerifyCheckBox.Checked, GetConflictResolution(), _speedLimitMiB * 1024L * 1024L, _parallelJobs, _bufferKiB * 1024);
        StatusLabel.Text = $"Iniciando hasta {_parallelJobs} trabajos simultáneos…";
        RefreshQueue();
    }

    private static bool IsRunnable(TransferJob job) => !job.IsFolderGroup;

    private ConflictResolution GetConflictResolution() => ConflictComboBox.SelectedIndex switch
    {
        1 => ConflictResolution.Overwrite,
        2 => ConflictResolution.Skip,
        _ => ConflictResolution.Rename
    };

    private void PauseButton_Click(object? sender, EventArgs e)
    {
        if (!_engine.IsRunning) return;
        _paused = !_paused;
        _engine.SetPaused(_paused);
        PauseButton.Text = _paused ? "▶  Reanudar" : "Ⅱ  Pausar";
        if (_paused) _clock.Stop(); else _clock.Start();
        UpdateMetrics();
    }

    private void StopButton_Click(object? sender, EventArgs e)
    {
        if (_engine.IsRunning) { _engine.Cancel(); StatusLabel.Text = "Cancelando… se conservarán los parciales recuperables."; }
    }

    private void MoveUpButton_Click(object? sender, EventArgs e) => MoveSelected(-1);
    private void MoveDownButton_Click(object? sender, EventArgs e) => MoveSelected(1);

    private void MoveSelected(int direction)
    {
        if (_engine.IsRunning || QueueListView.SelectedIndices.Count != 1) return;
        var index = QueueListView.SelectedIndices[0];
        var target = index + direction;
        if (target < 0 || target >= _jobs.Count) return;
        (_jobs[index], _jobs[target]) = (_jobs[target], _jobs[index]);
        RefreshQueue();
        if (target < QueueListView.Items.Count) QueueListView.Items[target].Selected = true;
    }

    private void RemoveSelectedButton_Click(object? sender, EventArgs e)
    {
        if (_engine.IsRunning) { StatusLabel.Text = "Detén la transferencia antes de modificar la cola."; return; }
        var selected = QueueListView.SelectedItems.Cast<ListViewItem>().Select(item => item.Tag as TransferJob).Where(j => j is not null).ToList();
        if (selected.Count == 0) { StatusLabel.Text = "Selecciona uno o varios elementos."; return; }
        var removeIds = selected.Select(j => j!.Id).ToHashSet();
        foreach (var group in selected.Where(j => j!.IsFolderGroup).Cast<TransferJob>())
            foreach (var child in _jobs.Where(j => j.GroupId == group.Id)) removeIds.Add(child.Id);
        _jobs.RemoveAll(j => removeIds.Contains(j.Id));
        RefreshQueue();
        StatusLabel.Text = $"Se quitaron {removeIds.Count} elemento(s) de la cola.";
    }

    private void ClearCompletedButton_Click(object? sender, EventArgs e)
    {
        if (_engine.IsRunning) return;
        var doneIds = _jobs.Where(j => !j.IsFolderGroup && j.State == TransferState.Completed).Select(j => j.Id).ToHashSet();
        _jobs.RemoveAll(j => doneIds.Contains(j.Id));
        _jobs.RemoveAll(j => j.IsFolderGroup && !_jobs.Any(child => child.GroupId == j.Id));
        RefreshQueue();
        StatusLabel.Text = $"Se limpiaron {doneIds.Count} archivo(s) completado(s).";
    }

    private void RetryFailedButton_Click(object? sender, EventArgs e)
    {
        if (_engine.IsRunning) return;
        var count = 0;
        foreach (var job in _jobs.Where(j => !j.IsFolderGroup && j.State is TransferState.Failed or TransferState.Cancelled))
        {
            job.State = TransferState.Pending;
            job.ErrorMessage = null;
            count++;
        }
        RefreshQueue();
        StatusLabel.Text = count == 0 ? "No hay archivos fallidos." : $"{count} archivo(s) listos para reintentar.";
    }

    private void QueueListView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete) { RemoveSelectedButton_Click(sender, EventArgs.Empty); e.Handled = true; }
        if (e.KeyCode == Keys.F6) ShowHistoryDialog(sender, EventArgs.Empty);
    }

    private void QueueListView_DoubleClick(object? sender, EventArgs e)
    {
        if (QueueListView.SelectedItems.Count != 1 || QueueListView.SelectedItems[0].Tag is not TransferJob { IsFolderGroup: true } group) return;
        group.IsExpanded = !group.IsExpanded;
        RefreshQueue();
    }

    private void SaveQueueButton_Click(object? sender, EventArgs e)
    {
        if (_saveQueueDialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(_saveQueueDialog.FileName, JsonSerializer.Serialize(_jobs, new JsonSerializerOptions { WriteIndented = true })); StatusLabel.Text = "Cola guardada."; }
        catch (Exception ex) { ShowError($"No se pudo guardar la cola: {ex.Message}"); }
    }

    private void LoadQueueButton_Click(object? sender, EventArgs e)
    {
        if (_engine.IsRunning) { StatusLabel.Text = "Detén las transferencias antes de cargar una cola."; return; }
        if (_loadQueueDialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<List<TransferJob>>(File.ReadAllText(_loadQueueDialog.FileName));
            if (loaded is null) { ShowError("El archivo no contiene una cola válida."); return; }
            _jobs.Clear();
            foreach (var job in loaded)
            {
                if (job.State is TransferState.Running or TransferState.Paused) job.State = TransferState.Pending;
                _jobs.Add(job);
            }
            RefreshQueue();
            StatusLabel.Text = $"Cola cargada: {_jobs.Count} elemento(s).";
        }
        catch (Exception ex) { ShowError($"No se pudo cargar la cola: {ex.Message}"); }
    }

    private void AddDroppedPaths(DragEventArgs e)
    {
        if (_engine.IsRunning || e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var path in paths)
        {
            if (Directory.Exists(path)) AddFolderTree(path);
            else AddSingleFile(path);
        }
        RefreshQueue();
    }

    private void QueueListView_ColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (_engine.IsRunning || e.Column == 0) return;
        if (_sortColumn == e.Column) _sortAscending = !_sortAscending; else { _sortColumn = e.Column; _sortAscending = true; }
        Func<TransferJob, object> key = e.Column switch
        {
            1 => j => j.SourcePath,
            2 => j => j.RelativePath,
            3 => j => j.SizeBytes,
            4 => j => j.State.ToString(),
            _ => j => j.ProgressPercent
        };
        var sorted = _sortAscending ? _jobs.OrderBy(key).ToList() : _jobs.OrderByDescending(key).ToList();
        _jobs.Clear(); _jobs.AddRange(sorted); RefreshQueue();
    }

    private void RefreshQueue()
    {
        if (QueueListView.IsDisposed) return;
        var selected = QueueListView.Items.Cast<ListViewItem>().Where(i => i.Selected).Select(i => (i.Tag as TransferJob)?.Id).Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        QueueListView.BeginUpdate();
        QueueListView.Items.Clear();
        var hiddenGroups = new HashSet<Guid>();
        foreach (var job in _jobs)
        {
            if (job.IsFolderGroup)
            {
                var children = _jobs.Where(c => c.GroupId == job.Id).ToList();
                job.SizeBytes = children.Sum(c => c.SizeBytes);
                job.CurrentBytes = children.Sum(c => Math.Min(c.CurrentBytes, c.SizeBytes));
                job.ProgressPercent = job.SizeBytes == 0 ? 100 : Math.Clamp(job.CurrentBytes * 100d / job.SizeBytes, 0, 100);
                job.State = AggregateState(children);
                if (!job.IsExpanded) hiddenGroups.Add(job.Id);
                AddQueueItem(job, (job.IsExpanded ? "▾  " : "▸  ") + job.Name, 0, selected);
                continue;
            }
            if (job.GroupId is Guid parent && (hiddenGroups.Contains(parent) || !_jobs.FirstOrDefault(x => x.Id == parent)!.IsExpanded)) continue;
            AddQueueItem(job, "    " + (job.IsDirectoryEntry ? "▣  " : "📄  ") + job.Name, job.GroupId.HasValue ? 1 : 0, selected);
        }
        QueueListView.EndUpdate();
        UpdateProgressControls();
        UpdateCounters();
    }

    private void AddQueueItem(TransferJob job, string displayName, int indentation, HashSet<Guid> selected)
    {
        var item = new ListViewItem(displayName) { Tag = job, ToolTipText = job.ErrorMessage ?? job.SourcePath };
        item.SubItems.Add(job.SourcePath);
        item.SubItems.Add(job.RelativePath);
        item.SubItems.Add(FormatBytes(job.SizeBytes));
        item.SubItems.Add(job.ErrorMessage is null ? job.State.ToString() : $"{job.State}: {job.ErrorMessage}");
        item.SubItems.Add($"{job.ProgressPercent:0.##}%");
        item.Selected = selected.Contains(job.Id);
        QueueListView.Items.Add(item);
    }

    private static TransferState AggregateState(List<TransferJob> children)
    {
        if (children.Count == 0) return TransferState.Completed;
        if (children.Any(x => x.State == TransferState.Running)) return TransferState.Running;
        if (children.Any(x => x.State == TransferState.Paused)) return TransferState.Paused;
        if (children.Any(x => x.State == TransferState.Failed)) return TransferState.Failed;
        if (children.All(x => x.State == TransferState.Completed)) return TransferState.Completed;
        if (children.Any(x => x.State == TransferState.Cancelled)) return TransferState.Cancelled;
        return TransferState.Pending;
    }

    private void UpdateCounters()
    {
        var files = _jobs.Where(j => !j.IsFolderGroup).ToList();
        var pending = files.Count(j => j.State == TransferState.Pending);
        var active = files.Count(j => j.State is TransferState.Running or TransferState.Paused);
        var complete = files.Count(j => j.State == TransferState.Completed);
        var failed = files.Count(j => j.State == TransferState.Failed);
        QueueCountLabel.Text = pending.ToString(); ActiveCountLabel.Text = active.ToString();
        CompletedCountLabel.Text = complete.ToString(); FailedCountLabel.Text = failed.ToString();
        QueueSummaryLabel.Text = pending.ToString(); ActiveSummaryLabel.Text = active.ToString();
        CompletedSummaryLabel.Text = complete.ToString(); FailedSummaryLabel.Text = failed.ToString();
    }

    private static long SaturatingAdd(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;

    private static string FormatBytes(double bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    private void Engine_ProgressChanged(object? sender, TransferProgressEventArgs e)
    {
        if (IsDisposed) return;
        lock (_latestProgress) _latestProgress[e.JobId] = e;
    }

    private void FlushProgressAndUpdateMetrics()
    {
        if (IsDisposed) return;
        List<TransferProgressEventArgs> snapshot;
        lock (_latestProgress) { snapshot = _latestProgress.Values.ToList(); _latestProgress.Clear(); }
        foreach (var progress in snapshot)
        {
            var job = _jobs.FirstOrDefault(x => x.Id == progress.JobId);
            if (job is null) continue;
            job.State = progress.State;
            job.ProgressPercent = progress.ProgressPercent;
            job.CurrentBytes = progress.CurrentBytes;
        }
        if (snapshot.Count > 0) RefreshQueue();
        UpdateMetrics();
    }

    private void UpdateMetrics()
    {
        if (_activeJobs.Count == 0) return;
        var now = DateTime.UtcNow;
        var bytes = _activeJobs.Sum(j => Math.Min(j.CurrentBytes, j.SizeBytes));
        var seconds = (now - _lastSampleAt).TotalSeconds;
        if (seconds >= .5)
        {
            _currentSpeed = Math.Max(0, bytes - _lastSampleBytes) / seconds;
            _lastSampleBytes = bytes;
            _lastSampleAt = now;
        }
        var average = _clock.Elapsed.TotalSeconds > 0 ? bytes / _clock.Elapsed.TotalSeconds : 0;
        var remaining = Math.Max(0, _activeJobs.Sum(j => j.SizeBytes) - bytes);
        var etaSeconds = average > 0 ? remaining / average : double.PositiveInfinity;
        var eta = double.IsFinite(etaSeconds) ? $"{Math.Ceiling(etaSeconds / 60):0} min" : "calculando…";
        var state = _paused ? "Pausado" : _engine.IsRunning ? "Copiando" : "Listo";
        StatusLabel.Text = $"{state} | {FormatBytes(_currentSpeed)}/s | media {FormatBytes(average)}/s | {_clock.Elapsed:hh\\:mm\\:ss} transcurridos | restan {eta}";
        UpdateTrayProgress();
    }

    private void Engine_Completed(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                _paused = false;
                _clock.Stop();
                PauseButton.Text = "Ⅱ  Pausar";
                var failed = _activeJobs.Count(j => j.State == TransferState.Failed);
                var cancelled = _activeJobs.Count(j => j.State == TransferState.Cancelled);
                var outcome = failed > 0 ? $"Finalizado con {failed} error(es)" : cancelled > 0 ? $"Cancelado ({cancelled})" : "Transferencia finalizada";
                SaveHistory(outcome);
                foreach (var group in _jobs.Where(j => j.IsFolderGroup))
                {
                    if (OperationComboBox.SelectedIndex == 1 && _jobs.Where(j => j.GroupId == group.Id).All(j => j.State == TransferState.Completed))
                        DeleteEmptySourceDirectories(group.SourcePath);
                }
                RefreshQueue();
                UpdateMetrics();
            });
        }
        catch (InvalidOperationException) { }
    }

    private static void DeleteEmptySourceDirectories(string root)
    {
        try
        {
            foreach (var directory in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
        }
        catch { }
    }

    private void UpdateProgressControls()
    {
        var work = _jobs.Where(j => !j.IsFolderGroup).ToList();
        var total = work.Sum(j => j.SizeBytes);
        var copied = work.Sum(j => Math.Min(j.CurrentBytes, j.SizeBytes));
        OverallProgressBar.Value = total <= 0 ? (work.Count > 0 && work.All(j => j.State == TransferState.Completed) ? 100 : 0) : (int)Math.Clamp(copied * 100d / total, 0, 100);
    }

    private void UpdateTrayProgress()
    {
        var state = _engine.IsRunning ? (_paused ? "Pausado" : "Copiando") : "Listo";
        TrayIcon.Text = $"XoPy - {state}: {OverallProgressBar.Value}%";
    }

    private void SaveHistory(string outcome)
    {
        try
        {
            Directory.CreateDirectory(_appData);
            var history = File.Exists(HistoryFile) ? JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(HistoryFile)) ?? new() : new();
            var copied = _activeJobs.Sum(j => Math.Min(j.CurrentBytes, j.SizeBytes));
            var avg = _clock.Elapsed.TotalSeconds > 0 ? copied / _clock.Elapsed.TotalSeconds : 0;
            history.Insert(0, new HistoryEntry(DateTime.Now, outcome, _activeJobs.Count, copied, _clock.Elapsed, avg));
            File.WriteAllText(HistoryFile, JsonSerializer.Serialize(history.Take(200), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void ShowHistoryDialog(object? sender, EventArgs e)
    {
        try
        {
            var history = File.Exists(HistoryFile) ? JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(HistoryFile)) ?? new() : new();
            var text = string.Join(Environment.NewLine, history.Take(40).Select(h => $"{h.FinishedAt:yyyy-MM-dd HH:mm:ss} | {h.Result} | {h.FileCount} archivos | {FormatBytes(h.BytesCopied)} | {h.Elapsed:hh\\:mm\\:ss} | {FormatBytes(h.AverageBytesPerSecond)}/s"));
            MessageBox.Show(this, string.IsNullOrEmpty(text) ? "Aún no hay transferencias en el historial." : text, "Historial de XoPy", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError($"No se pudo leer el historial: {ex.Message}"); }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return;
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile));
            if (settings is null) return;
            if (!string.IsNullOrWhiteSpace(settings.Destination)) DestinationTextBox.Text = settings.Destination;
            OperationComboBox.SelectedIndex = Math.Clamp(settings.Operation, 0, 1);
            ConflictComboBox.SelectedIndex = Math.Clamp(settings.Conflict, 0, 2);
            VerifyCheckBox.Checked = settings.Verify;
            _speedLimitMiB = Math.Clamp(settings.SpeedLimitMiB, 0, 1024 * 1024);
            _parallelJobs = Math.Clamp(settings.ParallelJobs, 1, 16);
            _bufferKiB = Math.Clamp(settings.BufferKiB, 64, 8192);
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(_appData);
            var data = new AppSettings(DestinationTextBox.Text, OperationComboBox.SelectedIndex, ConflictComboBox.SelectedIndex, VerifyCheckBox.Checked, _speedLimitMiB, _parallelJobs, _bufferKiB);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void RestoreFromTray() { Show(); WindowState = FormWindowState.Normal; ShowInTaskbar = true; Activate(); }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState != FormWindowState.Minimized) return;
        Hide(); ShowInTaskbar = false;
        TrayIcon.ShowBalloonTip(1000, "XoPy", "XoPy sigue ejecutándose en segundo plano.", ToolTipIcon.Info);
    }

    private void ShowError(string message)
    {
        StatusLabel.Text = message;
        MessageBox.Show(this, message, "XoPy", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static bool PathsEqual(string a, string b) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _uiTimer.Stop(); _uiTimer.Dispose();
            TrayIcon.Visible = false; _trayMenu.Dispose(); components?.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class OptionsDialog : Form
{
    private readonly TextBox _destination = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _operation = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _conflict = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly CheckBox _verify = new() { Text = "Verificar copias con SHA-256", AutoSize = true };
    private readonly NumericUpDown _speed = new() { Minimum = 0, Maximum = 1048576, ThousandsSeparator = true, Dock = DockStyle.Left, Width = 150 };
    private readonly NumericUpDown _parallel = new() { Minimum = 1, Maximum = 16, Dock = DockStyle.Left, Width = 100 };
    private readonly NumericUpDown _buffer = new() { Minimum = 64, Maximum = 8192, Increment = 64, Dock = DockStyle.Left, Width = 100 };
    public string Destination => _destination.Text;
    public int Operation => _operation.SelectedIndex;
    public int Conflict => _conflict.SelectedIndex;
    public bool Verify => _verify.Checked;
    public int SpeedLimitMiB => (int)_speed.Value;
    public int ParallelJobs => (int)_parallel.Value;
    public int BufferKiB => (int)_buffer.Value;

    public OptionsDialog(string destination, int operation, int conflict, bool verify, int speedLimitMiB, int parallelJobs, int bufferKiB)
    {
        Text = "Preferencias de XoPy";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(560, 390);
        _destination.Text = destination;
        _operation.Items.AddRange(new object[] { "Copiar", "Mover" }); _operation.SelectedIndex = Math.Clamp(operation, 0, 1);
        _conflict.Items.AddRange(new object[] { "Cambiar nombre", "Sobrescribir", "Omitir" }); _conflict.SelectedIndex = Math.Clamp(conflict, 0, 2);
        _verify.Checked = verify;
        _speed.Value = Math.Clamp(speedLimitMiB, 0, (int)_speed.Maximum);
        _parallel.Value = Math.Clamp(parallelJobs, 1, (int)_parallel.Maximum);
        _buffer.Value = Math.Clamp(bufferKiB, 64, (int)_buffer.Maximum);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddOption(layout, 0, "Carpeta destino", _destination);
        AddOption(layout, 1, "Operación", _operation);
        AddOption(layout, 2, "Si el destino existe", _conflict);
        AddOption(layout, 3, "Verificación", _verify);
        AddOption(layout, 4, "Límite MiB/s (0 = ilimitado)", _speed);
        AddOption(layout, 5, "Archivos simultáneos (1–16)", _parallel);
        AddOption(layout, 6, "Buffer por archivo (KiB)", _buffer);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "Guardar", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 7); layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout); AcceptButton = ok; CancelButton = cancel;
    }

    private static void AddOption(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(control, 1, row);
    }
}
