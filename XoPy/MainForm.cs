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
        QueueListView.Columns.Add("Estado", 120);
        QueueListView.Columns.Add("Progreso", 90);
        QueueListView.View = View.Details;
        QueueListView.FullRowSelect = true;
        QueueListView.MultiSelect = true;
        QueueListView.GridLines = true;
        QueueListView.HideSelection = false;
        QueueListView.ColumnClick += QueueListView_ColumnClick;
        QueueListView.KeyDown += QueueListView_KeyDown;
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
        var destination = DestinationTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(destination)) { ShowError("Elige una carpeta de destino."); return; }
        try { Directory.CreateDirectory(destination); }
        catch (Exception ex) { ShowError($"No se pudo crear el destino: {ex.Message}"); return; }

        var pending = _jobs.Where(j => j.State is TransferState.Pending or TransferState.Cancelled or TransferState.Failed).ToList();
        if (pending.Count == 0) { StatusLabel.Text = "No hay transferencias pendientes."; return; }
        foreach (var job in pending)
        {
            job.DestinationPath = destination;
            job.Operation = OperationComboBox.SelectedIndex == 1 ? OperationType.Move : OperationType.Copy;
            job.State = TransferState.Pending;
            job.ErrorMessage = null;
        }
        _isPaused = false;
        PauseButton.Text = "Pausar";
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
        if (!_engine.IsRunning) return;
        _engine.Cancel();
        StatusLabel.Text = "Cancelando… Se conservará el progreso parcial para reanudar.";
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
        var selectedJobs = QueueListView.SelectedItems.Cast<ListViewItem>().Select(item => item.Tag as TransferJob).Where(job => job is not null).ToHashSet();
        if (selectedJobs.Count == 0) { StatusLabel.Text = "Selecciona uno o más elementos para quitar."; return; }
        _jobs.RemoveAll(job => selectedJobs.Contains(job));
        RefreshQueue();
        StatusLabel.Text = $"Se quitaron {selectedJobs.Count} elemento(s) de la cola.";
    }

    private void ClearCompletedButton_Click(object? sender, EventArgs e)
    {
        if (_engine.IsRunning) return;
        var removed = _jobs.RemoveAll(job => job.State == TransferState.Completed);
        RefreshQueue();
        StatusLabel.Text = removed == 0 ? "No hay transferencias finalizadas para quitar." : $"Se quitaron {removed} transferencia(s) finalizada(s).";
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
        StatusLabel.Text = retried == 0 ? "No hay transferencias fallidas o canceladas." : $"{retried} transferencia(s) lista(s) para reintentar.";
    }

    private void QueueListView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Delete || _engine.IsRunning) return;
        RemoveSelectedButton_Click(sender, EventArgs.Empty);
        e.Handled = true;
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
            foreach (var job in jobs.Where(job => !string.IsNullOrWhiteSpace(job.SourcePath)))
            {
                if (job.State is TransferState.Running or TransferState.Paused)
                {
                    job.State = TransferState.Pending;
                    job.ErrorMessage = null;
                }
                _jobs.Add(job);
            }
            RefreshQueue();
            StatusLabel.Text = $"Cola cargada: {_jobs.Count} elemento(s).";
        }
        catch (Exception ex) { ShowError($"No se pudo cargar la cola: {ex.Message}"); }
    }

    private void AddJob(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))) return;
        var fullPath = Path.GetFullPath(sourcePath);
        if (_jobs.Any(job => string.Equals(Path.GetFullPath(job.SourcePath), fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            StatusLabel.Text = $"Ya está en la cola: {Path.GetFileName(fullPath)}";
            return;
        }
        var job = new TransferJob
        {
            SourcePath = fullPath,
            DestinationPath = DestinationTextBox.Text,
            Operation = OperationComboBox.SelectedIndex == 1 ? OperationType.Move : OperationType.Copy,
            State = TransferState.Pending,
            Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath))
        };
        try
        {
            job.SizeBytes = File.Exists(fullPath) ? new FileInfo(fullPath).Length : Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories).Sum(GetLengthSafely);
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

    private void QueueListView_DragDrop(object? sender, DragEventArgs e)
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
                var job = _jobs.FirstOrDefault(item => item.Id == e.JobId);
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
                var failed = _jobs.Count(job => job.State == TransferState.Failed);
                var cancelled = _jobs.Count(job => job.State == TransferState.Cancelled);
                StatusLabel.Text = failed > 0 ? $"Finalizada con {failed} error(es)" : cancelled > 0 ? $"Cancelada; {cancelled} transferencia(s) pendiente(s)" : "Transferencia finalizada";
                RefreshQueue();
                UpdateTrayProgress();
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
