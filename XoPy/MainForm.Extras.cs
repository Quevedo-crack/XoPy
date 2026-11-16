using System.Drawing.Drawing2D;
using System.Net;
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
        QueueListView.ColumnClick -= QueueListView_ColumnClick;
        QueueListView.ColumnClick += QueueListView_EnhancedColumnClick;
        MoveUpButton.Click -= MoveUpButton_Click;
        MoveDownButton.Click -= MoveDownButton_Click;
        MoveUpButton.Click += (_, _) => MoveSelectedWithinGroup(-1);
        MoveDownButton.Click += (_, _) => MoveSelectedWithinGroup(1);
        QueueListView.DragDrop += (_, e) => EnhanceDroppedFolderEntries(e);
        InstallQueueTools();
        InstallFavoritesPage();
        LoadSavedQueueOnStartup();
        SaveSettings();
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
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) entries.Add((file, false));
            }

            foreach (var entry in entries.OrderBy(item => Path.GetRelativePath(root, item.Path), StringComparer.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(root, entry.Path);
                _jobs.Add(new TransferJob
                {
                    Name = relative,
                    SourcePath = entry.Path,
                    DestinationPath = DestinationTextBox.Text,
                    RelativePath = relative,
                    GroupId = groupId,
                    IsDirectoryEntry = entry.IsDirectory,
                    State = TransferState.Pending,
                    SizeBytes = entry.IsDirectory ? 0 : new FileInfo(entry.Path).Length
                });
            }
        }
        catch (Exception ex) { StatusLabel.Text = $"No se pudo añadir la carpeta: {ex.Message}"; }
    }

    private void EnhanceDroppedFolderEntries(DragEventArgs e)
    {
        if (_engine.IsRunning || e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var rootPath in paths.Where(Directory.Exists))
        {
            var root = Path.GetFullPath(rootPath);
            var group = _jobs.FirstOrDefault(job => job.IsFolderGroup && PathsEqual(job.SourcePath, root));
            if (group is null) continue;
            foreach (var child in _jobs.Where(job => job.GroupId == group.Id))
                child.Name = child.RelativePath;

            foreach (var directory in EnumerateDirectoriesWithoutReparsePoints(root))
            {
                var relative = Path.GetRelativePath(root, directory);
                if (_jobs.Any(job => job.GroupId == group.Id && job.IsDirectoryEntry && PathsEqual(job.SourcePath, directory))) continue;
                _jobs.Add(new TransferJob
                {
                    Name = relative, SourcePath = directory, DestinationPath = DestinationTextBox.Text,
                    RelativePath = relative, GroupId = group.Id, IsDirectoryEntry = true, State = TransferState.Pending
                });
            }
        }
        RefreshQueue();
    }

    private void InstallQueueTools()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exportar informe CSV…", null, (_, _) => ExportQueueReport(html: false));
        menu.Items.Add("Exportar informe HTML…", null, (_, _) => ExportQueueReport(html: true));
        menu.Items.Add("Guardar como favorito", null, (_, _) => AddSelectedToFavorites());
        var favorites = new ToolStripMenuItem("Añadir favorito a la cola");
        favorites.DropDownOpening += (_, _) => PopulateFavoritesMenu(favorites);
        menu.Items.Add(favorites);
        QueueListView.ContextMenuStrip = menu;
    }

    private void InstallFavoritesPage()
    {
        if (WorkspaceTabs.TabPages.Cast<TabPage>().Any(page => page.Text == "Favoritos")) return;
        var page = new TabPage("Favoritos") { BackColor = Color.FromArgb(29, 32, 37), Padding = new Padding(12) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = page.BackColor };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "Carpetas y archivos favoritos · clic para añadir a la cola",
            AutoSize = true,
            ForeColor = Color.Gainsboro,
            Padding = new Padding(8)
        }, 0, 0);
        var favoritesList = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(24, 26, 30),
            ForeColor = Color.Gainsboro,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false
        };
        favoritesList.DoubleClick += (_, _) =>
        {
            if (favoritesList.SelectedItem is not string path) return;
            AddFavoritePath(path);
        };
        favoritesList.ContextMenuStrip = new ContextMenuStrip();
        favoritesList.ContextMenuStrip.Items.Add("Quitar favorito", null, (_, _) =>
        {
            if (favoritesList.SelectedItem is not string path) return;
            var favorites = ReadFavorites();
            favorites.RemoveAll(value => string.Equals(value, path, StringComparison.OrdinalIgnoreCase));
            WriteFavorites(favorites);
            RefreshFavoritesList(favoritesList);
        });
        page.Tag = favoritesList;
        layout.Controls.Add(favoritesList, 0, 1);
        page.Controls.Add(layout);
        WorkspaceTabs.TabPages.Add(page);
        RefreshFavoritesList(favoritesList);
    }

    private void RefreshFavoritesList(ListBox list)
    {
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var path in ReadFavorites()) list.Items.Add(path);
        list.EndUpdate();
    }

    private void AddFavoritePath(string path)
    {
        if (Directory.Exists(path)) AddFolderTreeWithEntries(path);
        else if (File.Exists(path)) AddSingleFile(path);
        else
        {
            MessageBox.Show(this, $"Ya no se encuentra esta ruta:\n{path}", "Favoritos de XoPy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        RefreshQueue();
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
            item.Click += (_, _) => AddFavoritePath(favorite);
            menu.DropDownItems.Add(item);
        }
        if (menu.DropDownItems.Count == 0)
        {
            var empty = new ToolStripMenuItem("No hay favoritos") { Enabled = false };
            menu.DropDownItems.Add(empty);
        }
    }

    private void AddSelectedToFavorites()
    {
        if (QueueListView.SelectedItems.Count == 0 || QueueListView.SelectedItems[0].Tag is not TransferJob job) return;
        var favorites = ReadFavorites();
        if (!favorites.Contains(job.SourcePath, StringComparer.OrdinalIgnoreCase)) favorites.Add(job.SourcePath);
        WriteFavorites(favorites);
        if (WorkspaceTabs.TabPages.Cast<TabPage>().FirstOrDefault(page => page.Text == "Favoritos")?.Tag is ListBox list)
            RefreshFavoritesList(list);
        StatusLabel.Text = "Ruta guardada en favoritos.";
    }

    private List<string> ReadFavorites()
    {
        try
        {
            return File.Exists(FavoritesFile)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FavoritesFile)) ?? new List<string>()
                : new List<string>();
        }
        catch { return new List<string>(); }
    }

    private void WriteFavorites(List<string> favorites)
    {
        try
        {
            Directory.CreateDirectory(_appData);
            File.WriteAllText(FavoritesFile, JsonSerializer.Serialize(favorites, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { ShowError($"No se pudieron guardar los favoritos: {ex.Message}"); }
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
            File.WriteAllText(dialog.FileName, html ? BuildHtmlReport(jobs) : BuildCsvReport(jobs), new UTF8Encoding(true));
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
            var target = string.IsNullOrWhiteSpace(job.RelativePath) ? job.DestinationPath : Path.Combine(job.DestinationPath, job.RelativePath);
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
        builder.AppendLine("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>Informe de XoPy</title><style>body{font:14px Segoe UI,Arial;margin:2rem;color:#222}table{border-collapse:collapse;width:100%}th,td{border:1px solid #ccc;padding:.5rem;text-align:left}th{background:#eee}td{overflow-wrap:anywhere}</style></head><body>");
        builder.AppendLine($"<h1>Informe de transferencias XoPy</h1><p>Generado: {WebUtility.HtmlEncode(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))}</p><table><thead><tr><th>Nombre</th><th>Origen</th><th>Destino</th><th>Estado</th><th>Tamaño</th><th>Progreso</th><th>Error</th></tr></thead><tbody>");
        foreach (var job in jobs)
        {
            var target = string.IsNullOrWhiteSpace(job.RelativePath) ? job.DestinationPath : Path.Combine(job.DestinationPath, job.RelativePath);
            builder.Append("<tr>");
            foreach (var value in new[] { job.Name, job.SourcePath, target, job.State.ToString(), FormatBytes(job.SizeBytes), $"{job.ProgressPercent:0.##}%", job.ErrorMessage ?? string.Empty })
                builder.Append("<td>").Append(WebUtility.HtmlEncode(value)).Append("</td>");
            builder.AppendLine("</tr>");
        }
        builder.AppendLine("</tbody></table></body></html>");
        return builder.ToString();
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private void QueueListView_EnhancedColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (_engine.IsRunning) return;
        if (_sortColumn == e.Column) _sortAscending = !_sortAscending;
        else { _sortColumn = e.Column; _sortAscending = true; }

        Func<TransferJob, object> key = e.Column switch
        {
            0 => job => job.Name,
            1 => job => job.SourcePath,
            2 => job => job.DestinationPath + "\\" + job.RelativePath,
            3 => job => job.SizeBytes,
            4 => job => job.State.ToString(),
            _ => job => job.ProgressPercent
        };
        var roots = _jobs.Where(job => job.IsFolderGroup).ToList();
        var singles = _jobs.Where(job => !job.IsFolderGroup && job.GroupId is null).ToList();
        var ordered = new List<TransferJob>();
        foreach (var root in Order(roots, key))
        {
            ordered.Add(root);
            var children = _jobs.Where(job => job.GroupId == root.Id).ToList();
            ordered.AddRange(Order(children, key));
        }
        ordered.AddRange(Order(singles, key));
        _jobs.Clear();
        _jobs.AddRange(ordered);
        RefreshQueue();
    }

    private IEnumerable<TransferJob> Order(IEnumerable<TransferJob> items, Func<TransferJob, object> key) =>
        _sortAscending ? items.OrderBy(key) : items.OrderByDescending(key);

    private void MoveSelectedWithinGroup(int direction)
    {
        if (_engine.IsRunning || QueueListView.SelectedItems.Count != 1 || QueueListView.SelectedItems[0].Tag is not TransferJob selected) return;
        if (selected.IsFolderGroup)
        {
            var groups = _jobs.Where(job => job.IsFolderGroup).ToList();
            MoveWithinList(groups, selected, direction);
            RebuildGroupedQueue(groups);
        }
        else
        {
            var peers = selected.GroupId is Guid groupId
                ? _jobs.Where(job => job.GroupId == groupId).ToList()
                : _jobs.Where(job => !job.IsFolderGroup && job.GroupId is null).ToList();
            MoveWithinList(peers, selected, direction);
            if (selected.GroupId is Guid parentId)
            {
                var group = _jobs.FirstOrDefault(job => job.Id == parentId);
                var allChildren = _jobs.Where(job => job.GroupId == parentId).ToList();
                var reordered = peers.Concat(allChildren.Except(peers)).ToList();
                if (group is not null) ReplaceChildren(parentId, reordered);
            }
            else
            {
                var roots = _jobs.Where(job => job.IsFolderGroup).ToList();
                RebuildGroupedQueue(roots, peers);
            }
        }
        RefreshQueue();
        var visible = QueueListView.Items.Cast<ListViewItem>().FirstOrDefault(item => item.Tag is TransferJob job && job.Id == selected.Id);
        if (visible is not null) visible.Selected = true;
    }

    private static void MoveWithinList(List<TransferJob> items, TransferJob selected, int direction)
    {
        var index = items.FindIndex(job => job.Id == selected.Id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= items.Count) return;
        (items[index], items[target]) = (items[target], items[index]);
    }

    private void ReplaceChildren(Guid groupId, List<TransferJob> ordered)
    {
        var firstChildIndex = _jobs.FindIndex(job => job.GroupId == groupId);
        if (firstChildIndex < 0) return;
        _jobs.RemoveAll(job => job.GroupId == groupId);
        _jobs.InsertRange(Math.Min(firstChildIndex, _jobs.Count), ordered);
    }

    private void RebuildGroupedQueue(List<TransferJob> groups, List<TransferJob>? singles = null)
    {
        var roots = groups.ToList();
        var standalone = singles ?? _jobs.Where(job => !job.IsFolderGroup && job.GroupId is null).ToList();
        var rebuilt = new List<TransferJob>();
        foreach (var group in roots)
        {
            rebuilt.Add(group);
            rebuilt.AddRange(_jobs.Where(job => job.GroupId == group.Id));
        }
        rebuilt.AddRange(standalone);
        _jobs.Clear();
        _jobs.AddRange(rebuilt);
    }

    private void LoadSavedQueueOnStartup()
    {
        var path = Path.Combine(_appData, "resume-queue.json");
        try
        {
            if (!File.Exists(path)) return;
            var loaded = JsonSerializer.Deserialize<List<TransferJob>>(File.ReadAllText(path));
            if (loaded is null || loaded.Count == 0) return;
            _jobs.Clear();
            foreach (var job in loaded)
            {
                if (job.State is TransferState.Running or TransferState.Paused) job.State = TransferState.Pending;
                if (job.IsFolderGroup) job.State = TransferState.Pending;
                _jobs.Add(job);
            }
            RefreshQueue();
            StatusLabel.Text = "Cola anterior restaurada; puedes reanudar la transferencia.";
        }
        catch (Exception ex) { StatusLabel.Text = $"No se pudo restaurar la cola guardada: {ex.Message}"; }
    }

    private void SaveResumeQueue()
    {
        try
        {
            Directory.CreateDirectory(_appData);
            var path = Path.Combine(_appData, "resume-queue.json");
            var snapshot = _jobs.Select(job =>
            {
                var copy = JsonSerializer.Deserialize<TransferJob>(JsonSerializer.Serialize(job))!;
                if (copy.State is TransferState.Running or TransferState.Paused) copy.State = TransferState.Pending;
                return copy;
            }).ToList();
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        catch { }
    }

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
                var size = graphics.MeasureString(label, font);
                graphics.DrawString(label, font, textBrush, (32 - size.Width) / 2, (32 - size.Height) / 2);
            }
            var handle = bitmap.GetHicon();
            using var nativeIcon = Icon.FromHandle(handle);
            var nextIcon = (Icon)nativeIcon.Clone();
            DestroyIcon(handle);
            var previous = _ownedTrayProgressIcon;
            _ownedTrayProgressIcon = nextIcon;
            TrayIcon.Icon = nextIcon;
            previous?.Dispose();
            var state = _engine.IsRunning ? (_paused ? "Pausado" : "Copiando") : "Listo";
            TrayIcon.Text = $"XoPy - {state}: {percent}%";
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or ObjectDisposedException) { }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_engine.IsRunning)
        {
            _engine.Cancel();
            SaveResumeQueue();
        }
        else SaveResumeQueue();
        SaveSettings();
        base.OnFormClosing(e);
    }
}
