using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace XoPy;

public partial class MainForm
{
    private readonly System.Windows.Forms.Timer _trayProgressTimer = new() { Interval = 500 };
    private Icon? _ownedTrayProgressIcon;
    private string FavoritesFile => Path.Combine(_appData, "favorites.json");

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        AddFolderButton.Click -= AddFolderButton_Click;
        AddFolderButton.Click += AddFolderButton_EnhancedClick;
        InstallQueueTools();
        _trayProgressTimer.Tick += (_, _) => RenderTrayProgressIcon();
        _trayProgressTimer.Start();
    }

    private void AddFolderButton_EnhancedClick(object? sender, EventArgs e)
    {
        if (_folderDialog.ShowDialog(this) != DialogResult.OK) return;
        AddFolderTreeWithEntries(_folderDialog.SelectedPath);
        RefreshQueue();
    }

    private void AddFolderTreeWithEntries(string folderPath)
    {
        try
        {
            var root = Path.GetFullPath(folderPath);
            if (_jobs.Any(job => job.IsFolderGroup && PathsEqual(job.SourcePath, root))) return;

            var groupId = Guid.NewGuid();
            _jobs.Add(new TransferJob
            {
                Id = groupId,
                Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(root)),
                SourcePath = root,
                DestinationPath = DestinationTextBox.Text,
                IsFolderGroup = true,
                IsExpanded = true,
                State = TransferState.Pending
            });

            var entries = new List<(string Path, bool IsDirectory)>();
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(root);
            while (pendingDirectories.Count > 0)
            {
                var directory = pendingDirectories.Pop();
                foreach (var childDirectory in Directory.EnumerateDirectories(directory).OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    if ((File.GetAttributes(childDirectory) & FileAttributes.ReparsePoint) != 0) continue;
                    entries.Add((childDirectory, true));
                    pendingDirectories.Push(childDirectory);
                }
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                        entries.Add((file, false));
                }
            }

            foreach (var entry in entries.OrderBy(item => Path.GetRelativePath(root, item.Path), StringComparer.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(root, entry.Path);
                var isDirectory = entry.IsDirectory;
                var size = isDirectory ? 0 : new FileInfo(entry.Path).Length;
                _jobs.Add(new TransferJob
                {
                    Name = relative,
                    SourcePath = entry.Path,
                    DestinationPath = DestinationTextBox.Text,
                    RelativePath = relative,
                    GroupId = groupId,
                    IsDirectoryEntry = isDirectory,
                    State = TransferState.Pending,
                    SizeBytes = size
                });
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"No se pudo añadir la carpeta: {ex.Message}";
        }
    }

    private void InstallQueueTools()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exportar informe CSV…", null, (_, _) => ExportQueueReport(html: false));
        menu.Items.Add("Exportar informe HTML…", null, (_, _) => ExportQueueReport(html: true));
        menu.Items.Add("Añadir elemento seleccionado a favoritos", null, (_, _) => AddSelectedToFavorites());
        var favorites = new ToolStripMenuItem("Añadir favorito a la cola");
        favorites.DropDownOpening += (_, _) => PopulateFavoritesMenu(favorites);
        menu.Items.Add(favorites);
        QueueListView.ContextMenuStrip = menu;
    }

    private void PopulateFavoritesMenu(ToolStripMenuItem menu)
    {
        menu.DropDownItems.Clear();
        foreach (var favorite in ReadFavorites())
        {
            var item = new ToolStripMenuItem(Path.GetFileName(Path.TrimEndingDirectorySeparator(favorite)) is { Length: > 0 } name ? name : favorite)
            {
                ToolTipText = favorite
            };
            item.Click += (_, _) =>
            {
                if (Directory.Exists(favorite)) AddFolderTreeWithEntries(favorite);
                else if (File.Exists(favorite)) AddSingleFile(favorite);
                else
                {
                    MessageBox.Show(this, $"Ya no se encuentra esta ruta:\n{favorite}", "Favoritos de XoPy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                RefreshQueue();
            };
            menu.DropDownItems.Add(item);
        }
        if (menu.DropDownItems.Count == 0) menu.DropDownItems.Add("No hay favoritos", null, null).Enabled = false;
    }

    private void AddSelectedToFavorites()
    {
        if (QueueListView.SelectedItems.Count == 0 || QueueListView.SelectedItems[0].Tag is not TransferJob job) return;
        var path = job.IsFolderGroup ? job.SourcePath : job.SourcePath;
        var favorites = ReadFavorites();
        if (!favorites.Contains(path, StringComparer.OrdinalIgnoreCase)) favorites.Add(path);
        try
        {
            Directory.CreateDirectory(_appData);
            File.WriteAllText(FavoritesFile, JsonSerializer.Serialize(favorites, new JsonSerializerOptions { WriteIndented = true }));
            StatusLabel.Text = "Ruta guardada en favoritos.";
        }
        catch (Exception ex) { ShowError($"No se pudo guardar el favorito: {ex.Message}"); }
    }

    private List<string> ReadFavorites()
    {
        try
        {
            if (!File.Exists(FavoritesFile)) return new List<string>();
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FavoritesFile)) ?? new List<string>();
        }
        catch { return new List<string>(); }
    }

    private void ExportQueueReport(bool html)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = html ? "Informe HTML (*.html)|*.html" : "Informe CSV (*.csv)|*.csv",
            DefaultExt = html ? "html" : "csv",
            FileName = html ? "xopy-report.html" : "xopy-report.csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var jobs = _jobs.Where(job => !job.IsFolderGroup).ToList();
            var content = html ? BuildHtmlReport(jobs) : BuildCsvReport(jobs);
            File.WriteAllText(dialog.FileName, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            StatusLabel.Text = "Informe exportado correctamente.";
        }
        catch (Exception ex) { ShowError($"No se pudo exportar el informe: {ex.Message}"); }
    }

    private static string BuildCsvReport(IEnumerable<TransferJob> jobs)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Nombre,Origen,Destino,Estado,Tamaño (bytes),Progreso,Error");
        foreach (var job in jobs)
        {
            var target = GetReportTarget(job);
            builder.AppendLine(string.Join(",", new[]
            {
                Csv(job.Name), Csv(job.SourcePath), Csv(target), Csv(job.State.ToString()),
                Csv(job.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Csv(job.ProgressPercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%"),
                Csv(job.ErrorMessage ?? string.Empty)
            }));
        }
        return builder.ToString();
    }

    private static string BuildHtmlReport(IEnumerable<TransferJob> jobs)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>Informe de XoPy</title>");
        builder.AppendLine("<style>body{font:14px Segoe UI,Arial;margin:2rem;color:#222}table{border-collapse:collapse;width:100%}th,td{border:1px solid #ccc;padding:.5rem;text-align:left}th{background:#eee}td{overflow-wrap:anywhere}</style></head><body>");
        builder.AppendLine($"<h1>Informe de transferencias XoPy</h1><p>Generado: {WebUtility.HtmlEncode(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))}</p><table><thead><tr><th>Nombre</th><th>Origen</th><th>Destino</th><th>Estado</th><th>Tamaño</th><th>Progreso</th><th>Error</th></tr></thead><tbody>");
        foreach (var job in jobs)
        {
            builder.Append("<tr>");
            foreach (var value in new[] { job.Name, job.SourcePath, GetReportTarget(job), job.State.ToString(), FormatBytes(job.SizeBytes), $"{job.ProgressPercent:0.##}%", job.ErrorMessage ?? string.Empty })
                builder.Append("<td>").Append(WebUtility.HtmlEncode(value)).Append("</td>");
            builder.AppendLine("</tr>");
        }
        builder.AppendLine("</tbody></table></body></html>");
        return builder.ToString();
    }

    private static string GetReportTarget(TransferJob job) =>
        string.IsNullOrWhiteSpace(job.RelativePath) ? job.DestinationPath : Path.Combine(job.DestinationPath, job.RelativePath);

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private void RenderTrayProgressIcon()
    {
        if (IsDisposed || TrayIcon is null) return;
        var percent = Math.Clamp(OverallProgressBar.Value, 0, 100);
        try
        {
            using var bitmap = new Bitmap(32, 32);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var background = new SolidBrush(Color.FromArgb(29, 32, 37)))
            using (var track = new Pen(Color.FromArgb(70, 78, 88), 4))
            using (var progress = new Pen(Color.FromArgb(35, 136, 226), 4))
            using (var textBrush = new SolidBrush(Color.White))
            using (var font = new Font("Segoe UI", 8, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                graphics.FillEllipse(background, 1, 1, 30, 30);
                graphics.DrawArc(track, 3, 3, 26, 26, -90, 360);
                if (percent > 0) graphics.DrawArc(progress, 3, 3, 26, 26, -90, 360f * percent / 100f);
                var label = percent.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var bounds = graphics.MeasureString(label, font);
                graphics.DrawString(label, font, textBrush, (32 - bounds.Width) / 2, (32 - bounds.Height) / 2);
            }

            var handle = bitmap.GetHicon();
            using var nativeIcon = Icon.FromHandle(handle);
            var nextIcon = (Icon)nativeIcon.Clone();
            DestroyIcon(handle);
            var previousIcon = _ownedTrayProgressIcon;
            _ownedTrayProgressIcon = nextIcon;
            TrayIcon.Icon = nextIcon;
            previousIcon?.Dispose();
            var state = _engine.IsRunning ? (_paused ? "Pausado" : "Copiando") : "Listo";
            TrayIcon.Text = $"XoPy - {state}: {percent}%";
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or ObjectDisposedException) { }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayProgressTimer.Stop();
            _trayProgressTimer.Dispose();
            TrayIcon.Icon = SystemIcons.Application;
            _ownedTrayProgressIcon?.Dispose();
            _ownedTrayProgressIcon = null;
        }
        base.Dispose(disposing);
    }
}
