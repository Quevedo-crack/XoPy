using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XoPy;

public partial class MainForm : Form
{
    private readonly List<TransferJob> _jobs = new();
    private readonly CopyEngine _engine;
    private bool _isPaused;
    private readonly OpenFileDialog _openFilesDialog = new();
    private readonly FolderBrowserDialog _folderDialog = new();
    private readonly SaveFileDialog _saveQueueDialog = new();
    private readonly OpenFileDialog _loadQueueDialog = new();

    public MainForm()
    {
        InitializeComponent();

        _engine = new CopyEngine();
        _engine.ProgressChanged += Engine_ProgressChanged;
        _engine.Completed += Engine_Completed;

        QueueListView.Columns.Add("Name", 250);
        QueueListView.Columns.Add("Origen", 220);
        QueueListView.Columns.Add("Destino", 220);
        QueueListView.Columns.Add("Tamaño", 90);
        QueueListView.Columns.Add("Estado", 90);
        QueueListView.Columns.Add("Progreso", 100);
        QueueListView.View = View.Details;
        QueueListView.FullRowSelect = true;
        QueueListView.GridLines = true;

        _openFilesDialog.Multiselect = true;
        _openFilesDialog.Filter = "Todos los archivos (*.*)|*.*";

        _saveQueueDialog.Filter = "Archivos JSON (*.json)|*.json";
        _saveQueueDialog.DefaultExt = "json";
        _saveQueueDialog.FileName = "xopy-queue.json";

        _loadQueueDialog.Filter = "Archivos JSON (*.json)|*.json";
        _loadQueueDialog.DefaultExt = "json";

        StatusLabel.Text = "Listo";
        OverallProgressBar.Minimum = 0;
        OverallProgressBar.Maximum = 100;
        OverallProgressBar.Value = 0;
    }

    private void AddFilesButton_Click(object sender, EventArgs e)
    {
        if (_openFilesDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        foreach (var file in _openFilesDialog.FileNames)
        {
            AddJob(file, GetDefaultDestination(), OperationType.Copy);
        }

        RefreshQueue();
    }

    private void AddFolderButton_Click(object sender, EventArgs e)
    {
        if (_folderDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        AddJob(_folderDialog.SelectedPath, GetDefaultDestination(), OperationType.Copy);
        RefreshQueue();
    }

    private void StartButton_Click(object sender, EventArgs e)
    {
        if (_engine.IsRunning)
        {
            StatusLabel.Text = "La copia ya está en ejecución.";
            return;
        }

        if (_jobs.Count == 0)
        {
            StatusLabel.Text = "No hay trabajos en la cola.";
            return;
        }

        _isPaused = false;
        _engine.Start(_jobs);
        StatusLabel.Text = "Transferencia iniciada";
    }

    private void PauseButton_Click(object sender, EventArgs e)
    {
        if (!_engine.IsRunning)
        {
            return;
        }

        _isPaused = !_isPaused;
        _engine.SetPaused(_isPaused);
        StatusLabel.Text = _isPaused ? "Transferencia pausada" : "Transferencia reanudada";
    }

    private void StopButton_Click(object sender, EventArgs e)
    {
        _engine.Cancel();
        StatusLabel.Text = "Transferencia cancelada";
    }

    private void MoveUpButton_Click(object sender, EventArgs e)
    {
        if (QueueListView.SelectedIndices.Count != 1)
        {
            return;
        }

        var index = QueueListView.SelectedIndices[0];
        if (index <= 0)
        {
            return;
        }

        var item = _jobs[index];
        _jobs.RemoveAt(index);
        _jobs.Insert(index - 1, item);
        RefreshQueue();
    }

    private void MoveDownButton_Click(object sender, EventArgs e)
    {
        if (QueueListView.SelectedIndices.Count != 1)
        {
            return;
        }

        var index = QueueListView.SelectedIndices[0];
        if (index >= _jobs.Count - 1)
        {
            return;
        }

        var item = _jobs[index];
        _jobs.RemoveAt(index);
        _jobs.Insert(index + 1, item);
        RefreshQueue();
    }

    private void SaveQueueButton_Click(object sender, EventArgs e)
    {
        if (_saveQueueDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var json = JsonSerializer.Serialize(_jobs, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_saveQueueDialog.FileName, json);
        StatusLabel.Text = "Cola guardada";
    }

    private void LoadQueueButton_Click(object sender, EventArgs e)
    {
        if (_loadQueueDialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (!File.Exists(_loadQueueDialog.FileName))
        {
            return;
        }

        var json = File.ReadAllText(_loadQueueDialog.FileName);
        var jobs = JsonSerializer.Deserialize<List<TransferJob>>(json);

        if (jobs is null)
        {
            return;
        }

        _jobs.Clear();
        _jobs.AddRange(jobs);
        RefreshQueue();
        StatusLabel.Text = "Cola cargada";
    }

    private void AddJob(string sourcePath, string destinationPath, OperationType operation)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        var job = new TransferJob
        {
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            Operation = operation,
            State = TransferState.Pending,
            Name = Path.GetFileName(sourcePath)
        };

        if (Directory.Exists(sourcePath))
        {
            job.SizeBytes = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories).Sum(FileInfoExtensions.GetLengthFast);
        }
        else if (File.Exists(sourcePath))
        {
            job.SizeBytes = new FileInfo(sourcePath).Length;
        }

        _jobs.Add(job);
    }

    private string GetDefaultDestination()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var target = Path.Combine(desktop, "XoPy_Transfers");
        Directory.CreateDirectory(target);
        return target;
    }

    private void RefreshQueue()
    {
        QueueListView.Items.Clear();

        foreach (var job in _jobs)
        {
            var item = new ListViewItem(job.Name)
            {
                Tag = job
            };

            item.SubItems.Add(job.SourcePath);
            item.SubItems.Add(job.DestinationPath);
            item.SubItems.Add(FormatBytes(job.SizeBytes));
            item.SubItems.Add(job.State.ToString());
            item.SubItems.Add(job.ProgressPercent.ToString("0.##") + "%");
            QueueListView.Items.Add(item);
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int i = 0;

        while (value >= 1024 && i < suffixes.Length - 1)
        {
            value /= 1024;
            i++;
        }

        return $"{value:0.##} {suffixes[i]}";
    }

    private void Engine_ProgressChanged(object? sender, TransferProgressEventArgs e)
    {
        var job = _jobs.FirstOrDefault(x => x.Id == e.JobId);
        if (job is null)
        {
            return;
        }

        job.State = e.State;
        job.ProgressPercent = e.ProgressPercent;
        job.CurrentBytes = e.CurrentBytes;

        UpdateProgressControls();
        RefreshQueue();
    }

    private void Engine_Completed(object? sender, EventArgs e)
    {
        StatusLabel.Text = "Transferencia finalizada";
        OverallProgressBar.Value = 100;
        RefreshQueue();
    }

    private void UpdateProgressControls()
    {
        var total = _jobs.Count > 0 ? _jobs.Sum(x => x.SizeBytes) : 0L;
        var transferred = _jobs.Sum(x => x.CurrentBytes);

        if (total > 0)
        {
            var value = (int)((transferred * 100d) / total);
            OverallProgressBar.Value = Math.Clamp(value, 0, 100);
        }
        else
        {
            OverallProgressBar.Value = 0;
        }
    }
}

internal static class FileInfoExtensions
{
    public static long GetLengthFast(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }
}
