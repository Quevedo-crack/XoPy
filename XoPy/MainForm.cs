using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace XoPy;

public partial class MainForm : Form
{
    private sealed record HistoryEntry(DateTime FinishedAt, string Result, int JobCount, long BytesCopied, TimeSpan Elapsed, double AverageBytesPerSecond);
    private sealed record AppSettings(string Destination, int Operation, int Conflict, bool Verify, int SpeedLimitMiB);

    private readonly List<TransferJob> _jobs = new();
    private readonly CopyEngine _engine = new();
    private readonly OpenFileDialog _openFilesDialog = new() { Multiselect = true, Filter = "Todos los archivos (*.*)|*.*" };
    private readonly FolderBrowserDialog _folderDialog = new();
    private readonly FolderBrowserDialog _destinationDialog = new();
    private readonly SaveFileDialog _saveQueueDialog = new() { Filter = "Archivos JSON (*.json)|*.json", DefaultExt = "json", FileName = "xopy-queue.json" };
    private readonly OpenFileDialog _loadQueueDialog = new() { Filter = "Archivos JSON (*.json)|*.json", DefaultExt = "json" };
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly System.Windows.Forms.Timer _metricsTimer = new() { Interval = 500 };
    private readonly Stopwatch _transferClock = new();
    private readonly string _appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XoPy");
    private List<TransferJob> _runJobs = new();
    private bool _isPaused;
    private int _sortColumn = -1;
    private bool _sortAscending = true;
    private int _speedLimitMiB;
    private long _lastSampleBytes;
    private DateTime _lastSampleAt = DateTime.UtcNow;
    private double _currentBytesPerSecond;

    public MainForm()
    {
        InitializeComponent();
        QueueListView.Columns.Add("Transferencia", 210);
        QueueListView.Columns.Add("Origen", 300);
        QueueListView.Columns.Add("Destino", 300);
        QueueListView.Columns.Add("Tamaño", 100);
        QueueListView.Columns.Add("Estado", 150);
        QueueListView.Columns.Add("Progreso", 100);
        QueueListView.ColumnClick += QueueListView_ColumnClick;
        QueueListView.KeyDown += QueueListView_KeyDown;
        QueueListView.AllowDrop = true;
        QueueListView.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        QueueListView.DragDrop += QueueListView_DragDrop;
        DragEnter += MainForm_DragEnter;
        DragDrop += MainForm_DragDrop;

        DestinationTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "XoPy_Transfers");
        OperationComboBox.Items.AddRange(new object[] { "Copiar", "Mover" });
        OperationComboBox.SelectedIndex = 0;
        ConflictComboBox.Items.AddRange(new object[] { "Cambiar nombre", "Sobrescribir", "Omitir" });
        ConflictComboBox.SelectedIndex = 0;
        LoadSettings();

        _engine.ProgressChanged += Engine_ProgressChanged;
        _engine.Completed += Engine_Completed;
        _metricsTimer.Tick += (_, _) => UpdateMetrics();
        _metricsTimer.Start();
        TrayIcon.Icon = SystemIcons.Application;
        TrayIcon.Text = "XoPy - Listo";
        TrayIcon.ContextMenuStrip = _trayMenu;
        TrayIcon.DoubleClick += (_, _) => RestoreFromTray();
        _trayMenu.Items.Add("Abrir XoPy", null, (_, _) => RestoreFromTray());
        _trayMenu.Items.Add("Pausar / reanudar", null, PauseButton_Click);
        _trayMenu.Items.Add("Historial de transferencias", null, ShowHistoryDialog);
        _trayMenu.Items.Add("Salir", null, (_, _) => Close());

        var queueMenu = new ContextMenuStrip();
        queueMenu.Items.Add("Historial de transferencias", null, ShowHistoryDialog);
        queueMenu.Items.Add("Opciones", null, ShowOptionsDialog);
        QueueListView.ContextMenuStrip = queueMenu;
        RefreshQueue();
    }

    private void AddFilesButton_Click(object? sender, EventArgs e)
    {
        if (_openFilesDialog.ShowDialog(this) != DialogResult.OK) return;
        foreach (var file in _openFilesDialog.FileNames) AddJob(file);
        RefreshQueue();
    }

    private void AddFolderButton_Click(object? sender, EventArgs e)
    {
        if (_folderDialog.ShowDialog(this) != DialogResult.OK) return;
        AddJob(_folderDialog.SelectedPath);
        RefreshQueue();
    }

    private void BrowseDestinationButton_Click(object? sender, EventArgs e)
    {
        if (Directory.Exists(DestinationTextBox.Text)) _destinationDialog.SelectedPath = DestinationTextBox.Text;
        if (_destinationDialog.ShowDialog(this) == DialogResult.OK) DestinationTextBox.Text = _destinationDialog.SelectedPath;
    }

    private void ShowOptionsDialog(object? sender, EventArgs e)
    {
        using var dialog = new OptionsDialog(DestinationTextBox.Text, OperationComboBox.SelectedIndex, ConflictComboBox.SelectedIndex, VerifyCheckBox.Checked, _speedLimitMiB);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        DestinationTextBox.Text = dialog.Destination;
        OperationComboBox.SelectedIndex = dialog.Operation;
        ConflictComboBox.SelectedIndex = dialog.Conflict;
        VerifyCheckBox.Checked = dialog.Verify;
        _speedLimitMiB = dialog.SpeedLimitMiB;
        SaveSettings();
        StatusLabel.Text = "Opciones guardadas; todas las funciones son gratuitas.";
    }

    private void StartButton_Click(object? sender, EventArgs e)
    {
        if (_engine.IsRunning) return;
        var destination = DestinationTextBox.Text.Trim();
        if (destination.Length == 0) { ShowError("Elige una carpeta de destino."); return; }
        var pending = _jobs.Where(job => job.State is TransferState.Pending or TransferState.Cancelled or TransferState.Failed).ToList();
        if (pending.Count == 0) { StatusLabel.Text = "No hay transferencias pendientes."; return; }
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
        _runJobs = pending;
        _isPaused = false;
        _lastSampleBytes = 0;
        _currentBytesPerSecond = 0;
        _lastSampleAt = DateTime.UtcNow;
        _transferClock.Restart();
        PauseButton.Text = "Ⅱ  Pausar";
        _engine.Start(pending, VerifyCheckBox.Checked, GetConflictResolution(), _speedLimitMiB * 1024L * 1024L);
        StatusLabel.Text = "Transferencia iniciada";
        RefreshQueue();
    }

    private ConflictResolution GetConflictResolution() => ConflictComboBox.SelectedIndex switch
    {
        1 => ConflictResolution.Overwrite,
        2 => ConflictResolution.Skip,
        _ => ConflictResolution.Rename
    };

    private void PauseButton_Click(object? sender, EventArgs e)
    {
        if (!_engine.IsRunning) return;
        _isPaused = !_isPaused;
        _engine.SetPaused(_isPaused);
        PauseButton.Text = _isPaused ? "▶  Reanudar" : "Ⅱ  Pausar";
        if (_isPaused) _transferClock.Stop(); else _transferClock.Start();
        UpdateMetrics();
    }

    private void StopButton_Click(object? sender, EventArgs e)
    {
        if (!_engine.IsRunning) return;
        _engine.Cancel();
        StatusLabel.Text = "Cancelando… se conservará el progreso parcial.";
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
        QueueListView.Items[target].Selected = true;
        QueueListView.Items[target].Focused = true;
    }

    private void RemoveSelectedButton_Click(object? sender, EventArgs e)
    {
        if (_engine.IsRunning) return;
        var selected = QueueListView.SelectedItems.Cast<ListViewItem>().Select(item => item.Tag as TransferJob).Where(job => job is not null).ToHashSet();
        if (selected.Count == 0) { StatusLabel.Text = "Selecciona elementos para quitarlos de la cola."; return; }
        _jobs.RemoveAll(job => selected.Contains(job));
        RefreshQueue();
        StatusLabel.Text = $"Se quitaron {selected.Count} elemento(s).";
    }

    private void ClearCompletedButton_Click(object? sender, EventArgs e)
    {
        if (_engine.IsRunning) return;
        var count = _jobs.RemoveAll(job => job.State == TransferState.Completed);
        RefreshQueue();
        StatusLabel.Text = count == 0 ? "No hay transferencias finalizadas." : $"Se quitaron {count} transferencia(s) finalizada(s).";
    }

    private void RetryFailedButton_Click(object? sender, EventArgs e)
    {
        if (_engine.IsRunning) return;
        var retried = 0;
        foreach (var job in _jobs.Where(job => job.State is TransferState.Failed or TransferState.Cancelled))
        {
            job.State = TransferState.Pending;
            job.ErrorMessage = null;
            retried++;
        }
        RefreshQueue();
        StatusLabel.Text = retried == 0 ? "No hay trabajos fallidos o cancelados." : $"{retried} trabajo(s) listos para reintentar.";
    }

    private void QueueListView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete && !_engine.IsRunning)
        {
            RemoveSelectedButton_Click(sender, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.F6) ShowHistoryDialog(sender, EventArgs.Empty);
    }

    private void SaveQueueButton_Click(object? sender, EventArgs e)
    {
        if (_saveQueueDialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(_saveQueueDialog.FileName, JsonSerializer.Serialize(_jobs, new JsonSerializerOptions { WriteIndented = true }));
            StatusLabel.Text = "Cola guardada.";
        }
        catch (Exception ex) { ShowError($"No se pudo guardar la cola: {ex.Message}"); }
    }

    private void LoadQueueButton_Click(object? sender, EventArgs e)
    {
        if (_engine.IsRunning) { StatusLabel.Text = "Detén la transferencia antes de cargar una cola."; return; }
        if (_loadQueueDialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var loaded = JsonSerializer.Deserialize<List<TransferJob>>(File.ReadAllText(_loadQueueDialog.FileName));
            if (loaded is null) { ShowError("El archivo no contiene una cola válida."); return; }
            _jobs.Clear();
            foreach (var job in loaded.Where(job => !string.IsNullOrWhiteSpace(job.SourcePath)))
            {
                if (job.State is TransferState.Running or TransferState.Paused) job.State = TransferState.Pending;
                _jobs.Add(job);
            }
            RefreshQueue();
            StatusLabel.Text = $"Cola cargada: {_jobs.Count} elemento(s).";
        }
        catch (Exception ex) { ShowError($"No se pudo cargar la cola: {ex.Message}"); }
    }

    private void AddJob(string sourcePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))) return;
            var fullPath = Path.GetFullPath(sourcePath);
            if (_jobs.Any(job => PathsEqual(job.SourcePath, fullPath))) return;
            var job = new TransferJob
            {
                SourcePath = fullPath,
                DestinationPath = DestinationTextBox.Text,
                Operation = OperationComboBox.SelectedIndex == 1 ? OperationType.Move : OperationType.Copy,
                State = TransferState.Pending,
                Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath))
            };
            job.SizeBytes = File.Exists(fullPath) ? new FileInfo(fullPath).Length : Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories).Sum(GetLengthSafely);
            _jobs.Add(job);
        }
        catch (Exception ex) { StatusLabel.Text = $"No se pudo añadir '{Path.GetFileName(sourcePath)}': {ex.Message}"; }
    }

    private static long GetLengthSafely(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static bool PathsEqual(string first, string second) => string.Equals(Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private void QueueListView_ColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (_engine.IsRunning) return;
        if (_sortColumn == e.Column) _sortAscending = !_sortAscending;
        else { _sortColumn = e.Column; _sortAscending = true; }
        Func<TransferJob, object> key = e.Column switch
        {
            0 => job => job.Name,
            1 => job => job.SourcePath,
            2 => job => job.DestinationPath,
            3 => job => job.SizeBytes,
            4 => job => job.State.ToString(),
            _ => job => job.ProgressPercent
        };
        var sorted = _sortAscending ? _jobs.OrderBy(key).ToList() : _jobs.OrderByDescending(key).ToList();
        _jobs.Clear();
        _jobs.AddRange(sorted);
        RefreshQueue();
    }

    private void QueueListView_DragDrop(object? sender, DragEventArgs e) => AddDroppedPaths(e);
    private void MainForm_DragEnter(object? sender, DragEventArgs e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    private void MainForm_DragDrop(object? sender, DragEventArgs e) => AddDroppedPaths(e);

    private void AddDroppedPaths(DragEventArgs e)
    {
        if (_engine.IsRunning || e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var path in paths) AddJob(path);
        RefreshQueue();
    }

    private void RefreshQueue()
    {
        var selectedIds = QueueListView.Items.Cast<ListViewItem>().Where(item => item.Selected).Select(item => (item.Tag as TransferJob)?.Id).Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        QueueListView.BeginUpdate();
        QueueListView.Items.Clear();
        foreach (var job in _jobs)
        {
            var item = new ListViewItem(job.Name) { Tag = job, ToolTipText = job.ErrorMessage ?? job.SourcePath };
            item.SubItems.Add(job.SourcePath);
            item.SubItems.Add(job.DestinationPath);
            item.SubItems.Add(FormatBytes(job.SizeBytes));
            item.SubItems.Add(job.ErrorMessage is null ? job.State.ToString() : $"{job.State}: {job.ErrorMessage}");
            item.SubItems.Add($"{job.ProgressPercent:0.##}%");
            item.Selected = selectedIds.Contains(job.Id);
            QueueListView.Items.Add(item);
        }
        QueueListView.EndUpdate();
        UpdateProgressControls();
    }

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
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                var job = _jobs.FirstOrDefault(item => item.Id == e.JobId);
                if (job is null) return;
                job.State = e.State;
                job.ProgressPercent = e.ProgressPercent;
                job.CurrentBytes = e.CurrentBytes;
                RefreshQueue();
                UpdateMetrics();
            });
        }
        catch (InvalidOperationException) { }
    }

    private void UpdateMetrics()
    {
        if (_runJobs.Count == 0) return;
        var now = DateTime.UtcNow;
        var copied = _runJobs.Sum(job => Math.Min(job.CurrentBytes, job.SizeBytes));
        var interval = (now - _lastSampleAt).TotalSeconds;
        if (interval >= 0.4)
        {
            _currentBytesPerSecond = Math.Max(0, copied - _lastSampleBytes) / interval;
            _lastSampleBytes = copied;
            _lastSampleAt = now;
        }
        var elapsedSeconds = _transferClock.Elapsed.TotalSeconds;
        var average = elapsedSeconds > 0 ? copied / elapsedSeconds : 0;
        var total = _runJobs.Sum(job => job.SizeBytes);
        var remaining = Math.Max(0, total - copied);
        var etaSeconds = average > 0 ? remaining / average : double.PositiveInfinity;
        var eta = double.IsFinite(etaSeconds) ? $"{Math.Ceiling(etaSeconds / 60):0} min" : "calculando…";
        var state = _isPaused ? "Pausado" : _engine.IsRunning ? "Copiando" : "Listo";
        StatusLabel.Text = $"{state} | Velocidad actual: {FormatBytes(_currentBytesPerSecond)}/s | Media: {FormatBytes(average)}/s | Transcurrido: {_transferClock.Elapsed:hh\\:mm\\:ss} | Restante: {eta}";
        UpdateTrayProgress();
    }

    private void Engine_Completed(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                _isPaused = false;
                _transferClock.Stop();
                PauseButton.Text = "Ⅱ  Pausar";
                var failed = _runJobs.Count(job => job.State == TransferState.Failed);
                var cancelled = _runJobs.Count(job => job.State == TransferState.Cancelled);
                var result = failed > 0 ? $"Finalizado con {failed} error(es)." : cancelled > 0 ? $"Cancelado: {cancelled} trabajo(s)." : "Transferencia finalizada.";
                SaveHistory(result);
                RefreshQueue();
                UpdateMetrics();
            });
        }
        catch (InvalidOperationException) { }
    }

    private void UpdateProgressControls()
    {
        var total = _jobs.Sum(job => job.SizeBytes);
        var copied = _jobs.Sum(job => Math.Min(job.CurrentBytes, job.SizeBytes));
        OverallProgressBar.Value = total <= 0 ? (_jobs.Count > 0 && _jobs.All(job => job.State == TransferState.Completed) ? 100 : 0) : (int)Math.Clamp(copied * 100d / total, 0, 100);
    }

    private void UpdateTrayProgress()
    {
        var state = _engine.IsRunning ? (_isPaused ? "Pausado" : "Copiando") : "Listo";
        TrayIcon.Text = $"XoPy - {state}: {OverallProgressBar.Value}%";
    }

    private string HistoryFile => Path.Combine(_appData, "history.json");
    private string SettingsFile => Path.Combine(_appData, "settings.json");

    private void SaveHistory(string result)
    {
        try
        {
            Directory.CreateDirectory(_appData);
            var entries = File.Exists(HistoryFile) ? JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(HistoryFile)) ?? new() : new();
            var bytes = _runJobs.Sum(job => Math.Min(job.CurrentBytes, job.SizeBytes));
            var average = _transferClock.Elapsed.TotalSeconds > 0 ? bytes / _transferClock.Elapsed.TotalSeconds : 0;
            entries.Insert(0, new HistoryEntry(DateTime.Now, result, _runJobs.Count, bytes, _transferClock.Elapsed, average));
            File.WriteAllText(HistoryFile, JsonSerializer.Serialize(entries.Take(200).ToList(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void ShowHistoryDialog(object? sender, EventArgs e)
    {
        try
        {
            var entries = File.Exists(HistoryFile) ? JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(HistoryFile)) ?? new() : new();
            var lines = entries.Take(50).Select(entry => $"{entry.FinishedAt:yyyy-MM-dd HH:mm:ss} | {entry.Result} | {entry.JobCount} trabajo(s) | {FormatBytes(entry.BytesCopied)} | {entry.Elapsed:hh\\:mm\\:ss} | media {FormatBytes(entry.AverageBytesPerSecond)}/s");
            MessageBox.Show(this, entries.Count == 0 ? "Aún no hay transferencias en el historial." : string.Join(Environment.NewLine, lines), "Historial de XoPy", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(_appData);
            var settings = new AppSettings(DestinationTextBox.Text, OperationComboBox.SelectedIndex, ConflictComboBox.SelectedIndex, VerifyCheckBox.Checked, _speedLimitMiB);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState != FormWindowState.Minimized) return;
        Hide();
        ShowInTaskbar = false;
        TrayIcon.ShowBalloonTip(1000, "XoPy", "XoPy sigue ejecutándose en segundo plano.", ToolTipIcon.Info);
    }

    private void ShowError(string message)
    {
        StatusLabel.Text = message;
        MessageBox.Show(this, message, "XoPy", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _metricsTimer.Stop();
            _metricsTimer.Dispose();
            TrayIcon.Visible = false;
            _trayMenu.Dispose();
            components?.Dispose();
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
    private readonly NumericUpDown _speed = new() { Minimum = 0, Maximum = 1048576, DecimalPlaces = 0, ThousandsSeparator = true, Dock = DockStyle.Left, Width = 150 };
    public string Destination => _destination.Text;
    public int Operation => _operation.SelectedIndex;
    public int Conflict => _conflict.SelectedIndex;
    public bool Verify => _verify.Checked;
    public int SpeedLimitMiB => (int)_speed.Value;

    public OptionsDialog(string destination, int operation, int conflict, bool verify, int speedLimitMiB)
    {
        Text = "Opciones de XoPy — todas gratuitas";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 285);
        _destination.Text = destination;
        _operation.Items.AddRange(new object[] { "Copiar", "Mover" });
        _operation.SelectedIndex = Math.Clamp(operation, 0, 1);
        _conflict.Items.AddRange(new object[] { "Cambiar nombre", "Sobrescribir", "Omitir" });
        _conflict.SelectedIndex = Math.Clamp(conflict, 0, 2);
        _verify.Checked = verify;
        _speed.Value = Math.Clamp(speedLimitMiB, 0, (int)_speed.Maximum);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "Carpeta destino", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_destination, 1, 0);
        layout.Controls.Add(new Label { Text = "Operación", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        layout.Controls.Add(_operation, 1, 1);
        layout.Controls.Add(new Label { Text = "Conflictos", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(_conflict, 1, 2);
        layout.Controls.Add(_verify, 1, 3);
        layout.Controls.Add(new Label { Text = "Límite MiB/s (0 = sin límite)", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        layout.Controls.Add(_speed, 1, 4);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "Aceptar", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 5);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
