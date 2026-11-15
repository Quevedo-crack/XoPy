using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace XoPy;

public partial class MainForm : Form
{
    private readonly List<TransferJob> _jobs = new();
    private readonly CopyEngine _engine = new();
    private readonly OpenFileDialog _openFilesDialog = new() { Multiselect = true, Filter = "Todos los archivos (*.*)|*.*" };
    private readonly FolderBrowserDialog _folderDialog = new();
    private readonly FolderBrowserDialog _destinationDialog = new();
    private readonly SaveFileDialog _saveQueueDialog = new() { Filter = "Archivos JSON (*.json)|*.json", DefaultExt = "json", FileName = "xopy-queue.json" };
    private readonly OpenFileDialog _loadQueueDialog = new() { Filter = "Archivos JSON (*.json)|*.json", DefaultExt = "json" };
    private readonly ContextMenuStrip _trayMenu = new();
    private bool _isPaused;
    private int _sortColumn = -1;
    private bool _sortAscending = true;

    public MainForm()
    {
        InitializeComponent();
        QueueListView.Columns.Add("Nombre", 180);
        QueueListView.Columns.Add("Origen", 240);
        QueueListView.Columns.Add("Destino", 240);
        QueueListView.Columns.Add("Tamaño", 90);
        QueueListView.Columns.Add("Estado", 100);
        QueueListView.Columns.Add("Progreso", 90);
        QueueListView.View = View.Details;
        QueueListView.FullRowSelect = true;
        QueueListView.GridLines = true;
        QueueListView.ColumnClick += QueueListView_ColumnClick;
        QueueListView.AllowDrop = true;
        QueueListView.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        QueueListView.DragDrop += QueueListView_DragDrop;

        DestinationTextBox.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "XoPy_Transfers");
        OperationComboBox.Items.AddRange(new object[] { "Copiar", "Mover" });
        OperationComboBox.SelectedIndex = 0;
        ConflictComboBox.Items.AddRange(new object[] { "Cambiar nombre", "Sobrescribir", "Omitir" });
        ConflictComboBox.SelectedIndex = 0;

        _engine.ProgressChanged += Engine_ProgressChanged;
        _engine.Completed += Engine_Completed;
        TrayIcon.Icon = SystemIcons.Application;
        TrayIcon.Text = "XoPy - Listo";
        TrayIcon.ContextMenuStrip = _trayMenu;
        TrayIcon.DoubleClick += (_, _) => RestoreFromTray();
        _trayMenu.Items.Add("Abrir XoPy", null, (_, _) => RestoreFromTray());
        _trayMenu.Items.Add("Pausar / reanudar", null, (_, _) => PauseButton_Click(this, EventArgs.Empty));
        _trayMenu.Items.Add("Salir", null, (_, _) => Close());
        StatusLabel.Text = "Listo";
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

    private void StartButton_Click(object? sender, EventArgs e)
    {
        if (_engine.IsRunning) return;
        if (!Directory.Exists(DestinationTextBox.Text))
        {
            try { Directory.CreateDirectory(DestinationTextBox.Text); }
            catch (Exception ex) { ShowError($"No se pudo crear el destino: {ex.Message}"); return; }
        }

        var pending = _jobs.Where(j => j.State is TransferState.Pending or TransferState.Cancelled or TransferState.Failed).ToList();
        if (pending.Count == 0) { StatusLabel.Text = "No hay transferencias pendientes."; return; }
        foreach (var job in pending)
        {
            job.DestinationPath = DestinationTextBox.Text;
            job.Operation = OperationComboBox.SelectedIndex == 1 ? OperationType.Move : OperationType.Copy;
            job.State = TransferState.Pending;
            job.CurrentBytes = 0;
            job.ProgressPercent = 0;
            job.ErrorMessage = null;
        }
        _isPaused = false;
        _engine.Start(pending, VerifyCheckBox.Checked, GetConflictResolution());
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
        PauseButton.Text = _isPaused ? "Reanudar" : "Pausar";
        StatusLabel.Text = _isPaused ? "Transferencia pausada" : "Transferencia reanudada";
        UpdateTrayProgress();
    }

    private void StopButton_Click(object? sender, EventArgs e)
    {
        _engine.Cancel();
        StatusLabel.Text = "Cancelando…";
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
    }

    private void SaveQueueButton_Click(object? sender, EventArgs e)
    {
        if (_saveQueueDialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(_saveQueueDialog.FileName, JsonSerializer.Serialize(_jobs, new JsonSerializerOptions { WriteIndented = true }));
            StatusLabel.Text = "Cola guardada";
        }
        catch (Exception ex) { ShowError($"No se pudo guardar la cola: {ex.Message}"); }
    }

    private void LoadQueueButton_Click(object? sender, EventArgs e)
    {
        if (_engine.IsRunning) { StatusLabel.Text = "Detén la transferencia antes de cargar una cola."; return; }
        if (_loadQueueDialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var jobs = JsonSerializer.Deserialize<List<TransferJob>>(File.ReadAllText(_loadQueueDialog.FileName));
            if (jobs is null) { ShowError("El archivo de cola no contiene una lista válida."); return; }
            _jobs.Clear();
            _jobs.AddRange(jobs.Where(j => !string.IsNullOrWhiteSpace(j.SourcePath)).Select(j =>
            {
                // A paused state is only meaningful while the current process is running.
                // After loading, make it runnable again just like an interrupted job.
                if (j.State is TransferState.Running or TransferState.Paused) j.State = TransferState.Pending;
                return j;
            }));
            RefreshQueue();
            StatusLabel.Text = "Cola cargada";
        }
        catch (Exception ex) { ShowError($"No se pudo cargar la cola: {ex.Message}"); }
    }

    private void AddJob(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))) return;
        var job = new TransferJob
        {
            SourcePath = sourcePath,
            DestinationPath = DestinationTextBox.Text,
            Operation = OperationComboBox.SelectedIndex == 1 ? OperationType.Move : OperationType.Copy,
            State = TransferState.Pending,
            Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(sourcePath))
        };
        try
        {
            job.SizeBytes = File.Exists(sourcePath) ? new FileInfo(sourcePath).Length : Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories).Sum(GetLengthSafely);
        }
        catch (Exception ex) { job.ErrorMessage = ex.Message; }
        _jobs.Add(job);
    }

    private static long GetLengthSafely(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private void QueueListView_ColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (_engine.IsRunning) return;
        if (_sortColumn == e.Column) _sortAscending = !_sortAscending;
        else { _sortColumn = e.Column; _sortAscending = true; }
        Func<TransferJob, object> key = e.Column switch
        {
            0 => j => j.Name,
            1 => j => j.SourcePath,
            2 => j => j.DestinationPath,
            3 => j => j.SizeBytes,
            4 => j => j.State.ToString(),
            _ => j => j.ProgressPercent
        };
        var sorted = _sortAscending ? _jobs.OrderBy(key).ToList() : _jobs.OrderByDescending(key).ToList();
        _jobs.Clear();
        _jobs.AddRange(sorted);
        RefreshQueue();
    }

    private void QueueListView_DragDrop(object? sender, DragEventArgs e)
    {
        if (_engine.IsRunning || e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var path in paths) AddJob(path);
        RefreshQueue();
    }

    private void RefreshQueue()
    {
        QueueListView.BeginUpdate();
        QueueListView.Items.Clear();
        foreach (var job in _jobs)
        {
            var item = new ListViewItem(job.Name) { Tag = job };
            item.SubItems.Add(job.SourcePath);
            item.SubItems.Add(job.DestinationPath);
            item.SubItems.Add(FormatBytes(job.SizeBytes));
            item.SubItems.Add(job.ErrorMessage is null ? job.State.ToString() : $"{job.State}: {job.ErrorMessage}");
            item.SubItems.Add($"{job.ProgressPercent:0.##}%");
            QueueListView.Items.Add(item);
        }
        QueueListView.EndUpdate();
        UpdateProgressControls();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = Math.Max(0, bytes);
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
                var job = _jobs.FirstOrDefault(x => x.Id == e.JobId);
                if (job is null) return;
                job.State = e.State;
                job.ProgressPercent = e.ProgressPercent;
                job.CurrentBytes = e.CurrentBytes;
                RefreshQueue();
                UpdateTrayProgress();
            });
        }
        catch (InvalidOperationException) { }
    }

    private void Engine_Completed(object? sender, EventArgs e)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                _isPaused = false;
                PauseButton.Text = "Pausar";
                var failed = _jobs.Count(j => j.State == TransferState.Failed);
                StatusLabel.Text = failed == 0 ? "Transferencia finalizada" : $"Finalizada con {failed} error(es)";
                RefreshQueue();
                UpdateTrayProgress();
            });
        }
        catch (InvalidOperationException) { }
    }

    private void UpdateProgressControls()
    {
        var total = _jobs.Sum(j => j.SizeBytes);
        var copied = _jobs.Sum(j => Math.Min(j.CurrentBytes, j.SizeBytes));
        OverallProgressBar.Value = total <= 0 ? (_jobs.Count > 0 && _jobs.All(j => j.State == TransferState.Completed) ? 100 : 0) : (int)Math.Clamp(copied * 100d / total, 0, 100);
    }

    private void UpdateTrayProgress()
    {
        var percent = OverallProgressBar.Value;
        var state = _engine.IsRunning ? (_isPaused ? "Pausado" : "Copiando") : "Listo";
        TrayIcon.Text = $"XoPy - {state}: {percent}%";
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
        if (WindowState == FormWindowState.Minimized)
        {
            Hide();
            ShowInTaskbar = false;
            TrayIcon.ShowBalloonTip(1000, "XoPy", "XoPy sigue ejecutándose en segundo plano.", ToolTipIcon.Info);
        }
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
            TrayIcon.Visible = false;
            _trayMenu.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }
}
