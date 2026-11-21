using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace XoPy;

public sealed partial class MainForm
{
    private readonly List<Task> _pendingScans = new();

    // ---------------------------------------------------------------- cola

    private void AddFiles()
    {
        if (_engine.IsRunning) { SetStatus("Detén la transferencia antes de añadir archivos."); return; }
        if (_openFiles.ShowDialog(this) != DialogResult.OK) return;
        _ = AddPathsAsync(_openFiles.FileNames);
    }

    private void AddFolder()
    {
        if (_engine.IsRunning) { SetStatus("Detén la transferencia antes de añadir carpetas."); return; }
        if (_pickFolder.ShowDialog(this) != DialogResult.OK) return;
        _ = AddPathsAsync(new[] { _pickFolder.SelectedPath });
    }

    private void DropPaths(DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) _ = AddPathsAsync(paths);
    }

    private async Task AddPathsAsync(IEnumerable<string> paths)
    {
        try
        {
            var scans = new List<Task>();
            var files = new List<TransferItem>();

            foreach (var raw in paths)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var path = Path.GetFullPath(raw.Trim().Trim('"').TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (path.Length == 0) continue;
                    if (_items.Any(existing => string.Equals(existing.SourcePath, path, StringComparison.OrdinalIgnoreCase))) continue;

                    if (File.Exists(path))
                    {
                        var info = new FileInfo(path);
                        files.Add(new TransferItem
                        {
                            Kind = ItemKind.File,
                            Depth = 0,
                            SourcePath = path,
                            RelativePath = info.Name,
                            SizeBytes = info.Length,
                            SizeKnown = true,
                            State = ItemState.Pending
                        });
                    }
                    else if (Directory.Exists(path))
                    {
                        var folder = new TransferItem
                        {
                            Kind = ItemKind.Folder,
                            Depth = 0,
                            SourcePath = path,
                            RelativePath = Path.GetFileName(path),
                            SizeKnown = false,
                            State = ItemState.Measuring,
                            IsExpanded = true
                        };
                        _items.Add(folder);
                        var scan = ScanAndApplyAsync(folder);
                        scans.Add(scan);
                        _pendingScans.Add(scan);
                        _ = scan.ContinueWith(_ =>
                        {
                            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => _pendingScans.Remove(scan))); }
                            catch { }
                        });
                    }
                }
                catch (Exception ex)
                {
                    FileLog.Write("No se pudo añadir " + raw + ": " + ex.Message);
                }
            }

            if (files.Count > 0)
            {
                // Los archivos sueltos van antes que las carpetas pendientes de analizar.
                var firstFolder = _items.FindIndex(item => item.Kind == ItemKind.Folder);
                if (firstFolder < 0) _items.AddRange(files);
                else _items.InsertRange(firstFolder, files);
            }

            if (files.Count > 0 || scans.Count > 0)
            {
                RebuildRows();
                UpdateFooter();
                UpdateButtons();
                SelectTab(0);
                SetStatus(scans.Count > 0
                    ? $"Analizando {scans.Count} carpeta(s)…"
                    : $"{files.Count} archivo(s) añadidos a la lista.");
            }

            if (scans.Count > 0)
            {
                await Task.WhenAll(scans);
                RebuildRows();
                UpdateFooter();
                UpdateButtons();
                var fileCount = _items.Count(item => item.Kind == ItemKind.File);
                var fileBytes = _items.Where(item => item.Kind == ItemKind.File).Sum(item => Math.Max(0, item.SizeBytes));
                SetStatus($"{_items.Count} elemento(s) en la lista · {fileCount} archivo(s) · {Format.Bytes(fileBytes)}");
            }

            UpdateJobs();
        }
        catch (Exception ex)
        {
            FileLog.Write("Error añadiendo elementos: " + ex);
            SetStatus("No se pudieron añadir todos los elementos.");
        }
    }

    private async Task ScanAndApplyAsync(TransferItem folder)
    {
        ScanResult result;
        try
        {
            var path = folder.SourcePath;
            result = await Task.Run(() => ScanFolderCore(path));
        }
        catch (Exception ex)
        {
            result = new ScanResult();
            result.Complete = false;
            result.Error = ex.Message;
        }

        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => ApplyScan(folder, result))); }
            catch { }
            return;
        }
        ApplyScan(folder, result);
    }

    private sealed class ScanNode
    {
        public string Path = string.Empty;
        public string Name = string.Empty;
        public string Relative = string.Empty;
        public long Size;
        public bool IsDirectory;
        public readonly List<ScanNode> Children = new();
    }

    private sealed class ScanResult
    {
        public ScanNode? Root;
        public long Bytes;
        public int Files;
        public bool Complete = true;
        public string? Error;
    }

    private static ScanResult ScanFolderCore(string root)
    {
        var result = new ScanResult();
        var rootName = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        if (string.IsNullOrEmpty(rootName)) rootName = root;

        var rootNode = new ScanNode { Path = root, Name = rootName, Relative = rootName, IsDirectory = true };
        var stack = new Stack<(ScanNode Node, string Directory)>();
        stack.Push((rootNode, root));

        while (stack.Count > 0)
        {
            var (node, directory) = stack.Pop();
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(directory);
                directories = Directory.GetDirectories(directory);
            }
            catch (Exception ex)
            {
                result.Complete = false;
                result.Error ??= ex.Message;
                continue;
            }

            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    node.Children.Add(new ScanNode
                    {
                        Path = info.FullName,
                        Name = info.Name,
                        Relative = Path.Combine(node.Relative, info.Name),
                        Size = info.Length,
                        IsDirectory = false
                    });
                    result.Bytes += info.Length;
                    result.Files++;
                }
                catch
                {
                    result.Complete = false;
                }
            }

            foreach (var child in directories)
            {
                try
                {
                    var info = new DirectoryInfo(child);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    var childNode = new ScanNode
                    {
                        Path = info.FullName,
                        Name = info.Name,
                        Relative = Path.Combine(node.Relative, info.Name),
                        IsDirectory = true
                    };
                    node.Children.Add(childNode);
                    stack.Push((childNode, info.FullName));
                }
                catch
                {
                    result.Complete = false;
                }
            }
        }

        SortNodes(rootNode);
        ComputeNodeSize(rootNode);
        result.Root = rootNode;
        return result;
    }

    private static void SortNodes(ScanNode node)
    {
        node.Children.Sort((left, right) =>
        {
            if (left.IsDirectory != right.IsDirectory) return left.IsDirectory ? -1 : 1;
            return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        });
        foreach (var child in node.Children) SortNodes(child);
    }

    private static long ComputeNodeSize(ScanNode node)
    {
        var size = 0L;
        foreach (var child in node.Children)
            size += child.IsDirectory ? ComputeNodeSize(child) : child.Size;
        node.Size = size;
        return size;
    }

    private void ApplyScan(TransferItem folder, ScanResult result)
    {
        var index = _items.IndexOf(folder);
        if (index < 0 || result.Root is null) return;

        var rows = new List<TransferItem>();
        if (!result.Complete && result.Root.Children.Count == 0)
        {
            folder.SizeKnown = false;
            folder.Error = result.Error;
            folder.State = ItemState.Failed;
            RebuildRows();
            UpdateFooter();
            return;
        }
        if (result.Root.Children.Count == 0)
        {
            // Carpeta vacía: se crea en el destino como entrada de directorio.
            rows.Add(new TransferItem
            {
                ParentId = folder.Id,
                Depth = folder.Depth + 1,
                Kind = ItemKind.DirectoryEntry,
                SourcePath = folder.SourcePath,
                RelativePath = folder.RelativePath,
                SizeBytes = 0,
                SizeKnown = true,
                State = ItemState.Pending
            });
        }
        else
        {
            AppendScanRows(result.Root, folder, rows);
        }

        _items.InsertRange(index + 1, rows);
        folder.SizeBytes = result.Bytes;
        folder.SizeKnown = result.Complete;
        folder.Error = result.Error;
        folder.State = ItemState.Pending;

        RebuildRows();
        UpdateFooter();
        UpdateButtons();
        UpdateJobs();
    }

    private static void AppendScanRows(ScanNode node, TransferItem parent, List<TransferItem> rows)
    {
        foreach (var child in node.Children)
        {
            var isLeafDirectory = child.IsDirectory && child.Children.Count == 0;
            var item = new TransferItem
            {
                ParentId = parent.Id,
                Depth = parent.Depth + 1,
                Kind = child.IsDirectory && !isLeafDirectory ? ItemKind.Folder
                    : isLeafDirectory ? ItemKind.DirectoryEntry
                    : ItemKind.File,
                SourcePath = child.Path,
                RelativePath = child.Relative,
                SizeBytes = child.Size,
                SizeKnown = true,
                State = ItemState.Pending,
                IsExpanded = true
            };
            rows.Add(item);
            if (child.IsDirectory && !isLeafDirectory) AppendScanRows(child, item, rows);
        }
    }

    private List<TransferItem> Children(TransferItem parent) => _items.Where(item => item.ParentId == parent.Id).ToList();

    private IEnumerable<TransferItem> Descendants(TransferItem parent)
    {
        foreach (var child in Children(parent))
        {
            yield return child;
            foreach (var grandChild in Descendants(child)) yield return grandChild;
        }
    }

    private IEnumerable<TransferItem> BlockOf(TransferItem item)
    {
        yield return item;
        foreach (var child in Children(item)) yield return child;
    }

    private List<TransferItem> RootItems() => _items.Where(item => item.ParentId is null).ToList();

    private List<TransferItem> SelectedItems()
        => _list.SelectedItems.Cast<ListViewItem>().Select(row => row.Tag).OfType<TransferItem>().ToList();

    private List<TransferItem> BasisItems()
    {
        var selected = SelectedItems();
        return selected.Count > 0 ? selected : RootItems();
    }

    private bool IsVisibleRow(TransferItem item)
    {
        var parentId = item.ParentId;
        var guard = 0;
        while (parentId is not null && guard++ < 64)
        {
            var parent = _items.FirstOrDefault(candidate => candidate.Id == parentId.Value);
            if (parent is null) return false;
            if (!parent.IsExpanded) return false;
            parentId = parent.ParentId;
        }
        return true;
    }

    private void RebuildRows()
    {
        if (_list is null) return;
        var selectedIds = SelectedItems().Select(item => item.Id).ToHashSet();

        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            _rows.Clear();
            foreach (var item in _items)
            {
                if (!IsVisibleRow(item)) continue;
                var row = new ListViewItem(new[] { item.Name, item.SizeText, item.StateText, string.Empty, string.Empty, string.Empty })
                {
                    Tag = item,
                    ToolTipText = BuildTooltip(item)
                };
                _rows[item.Id] = row;
                _list.Items.Add(row);
                if (selectedIds.Contains(item.Id)) row.Selected = true;
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        RefreshFolders();
        SyncRows();
        UpdateHashColumns();
    }

    private void SyncRows()
    {
        if (_list is null || !IsHandleCreated) return;
        RefreshFolders();

        var changed = false;
        void SetText(ListViewItem.ListViewSubItem subItem, string text)
        {
            if (string.Equals(subItem.Text, text, StringComparison.Ordinal)) return;
            subItem.Text = text;
            changed = true;
        }

        _list.BeginUpdate();
        try
        {
            foreach (ListViewItem row in _list.Items)
            {
                if (row.Tag is not TransferItem item) continue;
                var state = item.StateText;
                if (item.Kind == ItemKind.Folder && item.State is ItemState.Pending or ItemState.Measuring)
                {
                    var count = Children(item).Count;
                    if (count > 0) state += $" · {count}";
                }
                SetText(row.SubItems[1], item.SizeText);
                SetText(row.SubItems[2], state);
                SetText(row.SubItems[3], $"{RowPercent(item):0} %");
                SetText(row.SubItems[4], item.HashSource ?? string.Empty);
                SetText(row.SubItems[5], item.HashTarget ?? string.Empty);
                var tooltip = BuildTooltip(item);
                if (!string.Equals(row.ToolTipText, tooltip, StringComparison.Ordinal)) row.ToolTipText = tooltip;
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        if (changed || _engine.IsRunning) _list.Invalidate();
    }

    private static string BuildTooltip(TransferItem item)
    {
        var text = item.SourcePath;
        if (!string.IsNullOrWhiteSpace(item.DestinationPath)) text += "\n→ " + item.DestinationPath;
        if (!string.IsNullOrWhiteSpace(item.Error)) text += "\n" + item.Error;
        if (!string.IsNullOrWhiteSpace(item.HashSource)) text += "\nHash origen: " + item.HashSource;
        if (!string.IsNullOrWhiteSpace(item.HashTarget)) text += "\nHash destino: " + item.HashTarget;
        return text;
    }

    private void RefreshFolders()
    {
        foreach (var folder in _items.Where(item => item.Kind == ItemKind.Folder).OrderByDescending(item => item.Depth).ToList())
        {
            var children = Children(folder);
            if (children.Count == 0) continue;

            folder.SizeBytes = children.Sum(child => Math.Max(0, child.SizeBytes));
            folder.TransferredBytes = children.Sum(child => Math.Clamp(child.TransferredBytes, 0, Math.Max(child.SizeBytes, child.TransferredBytes)));

            if (folder.Operation == OperationType.Delete && folder.State == ItemState.Completed)
            {
                foreach (var child in Descendants(folder).Prepend(folder))
                {
                    child.Operation = OperationType.Delete;
                    child.State = ItemState.Completed;
                    child.TransferredBytes = child.SizeBytes;
                }
            }

            if (folder.State == ItemState.Measuring && children.All(child => child.State == ItemState.Pending)) continue;

            var states = children.Select(child => child.State).ToList();
            folder.State = states.Any(state => state is ItemState.Running or ItemState.Verifying) ? ItemState.Running
                : states.Any(state => state == ItemState.Paused) ? ItemState.Paused
                : states.Any(state => state == ItemState.Failed) ? ItemState.Failed
                : states.All(state => state is ItemState.Completed or ItemState.Skipped) ? ItemState.Completed
                : states.Any(state => state == ItemState.Cancelled) ? ItemState.Cancelled
                : ItemState.Pending;
        }
    }

    private void UpdateCountLabel(int leaves, long total)
    {
        if (_countLabel is null) return;
        if (_items.Count == 0)
        {
            _countLabel.Text = "Lista vacía — arrastra archivos o carpetas aquí";
            return;
        }
        _countLabel.Text = $"{_items.Count} elemento(s) · {leaves} archivo(s) · {Format.Bytes(total)}";
    }

    private string? _currentStatus;

    private void SetStatus(string message)
    {
        _currentStatus = message;
        if (_statusLabel is null) return;
        if (!string.Equals(_statusLabel.Text, message, StringComparison.Ordinal)) _statusLabel.Text = message;
    }

    private void ToggleExpand(TransferItem item)
    {
        if (item.Kind != ItemKind.Folder) return;
        if (Children(item).Count == 0) return;
        item.IsExpanded = !item.IsExpanded;
        RebuildRows();
    }

    private void ToggleSelectedFolders()
    {
        var folders = SelectedItems().Where(item => item.Kind == ItemKind.Folder).ToList();
        if (folders.Count == 0) { SetStatus("Selecciona una carpeta para expandirla o contraerla."); return; }
        var expand = folders.Any(folder => !folder.IsExpanded);
        foreach (var folder in folders) folder.IsExpanded = expand;
        RebuildRows();
    }

    private void ExpandAll(bool expand)
    {
        foreach (var folder in _items.Where(item => item.Kind == ItemKind.Folder)) folder.IsExpanded = expand;
        RebuildRows();
    }

    private void MoveSelected(int delta)
    {
        if (_engine.IsRunning) { SetStatus("Detén la transferencia antes de cambiar el orden."); return; }
        var selected = SelectedItems();
        if (selected.Count == 0) { SetStatus("Selecciona un elemento para cambiar su orden."); return; }

        var ordered = delta < 0
            ? selected.OrderBy(item => _items.IndexOf(item)).ToList()
            : selected.OrderByDescending(item => _items.IndexOf(item)).ToList();

        var moved = 0;
        foreach (var item in ordered)
            if (MoveBlock(item, delta)) moved++;

        if (moved == 0) { SetStatus("El elemento ya está en el extremo."); return; }

        RebuildRows();
        UpdateFooter();
        UpdateButtons();
        SetStatus("Orden de la cola actualizado.");
    }

    private bool MoveBlock(TransferItem item, int delta)
    {
        var parent = item.ParentId is null ? null : _items.FirstOrDefault(candidate => candidate.Id == item.ParentId.Value);
        var siblings = parent is null ? RootItems() : Children(parent);
        var index = siblings.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= siblings.Count) return false;

        (siblings[index], siblings[target]) = (siblings[target], siblings[index]);

        var rebuilt = new List<TransferItem>();
        void AddNode(TransferItem node)
        {
            rebuilt.Add(node);
            var children = ReferenceEquals(node, parent) ? siblings : Children(node);
            foreach (var child in children) AddNode(child);
        }
        foreach (var root in RootItems()) AddNode(root);

        _items.Clear();
        _items.AddRange(rebuilt);
        return true;
    }

    private void RemoveSelected()
    {
        if (_engine.IsRunning) { SetStatus("Detén la transferencia antes de modificar la lista."); return; }
        var selected = SelectedItems();
        if (selected.Count == 0) { SetStatus("Selecciona uno o varios elementos."); return; }

        var remove = new HashSet<Guid>();
        foreach (var item in selected)
        {
            remove.Add(item.Id);
            foreach (var child in Descendants(item)) remove.Add(child.Id);
        }

        var count = remove.Count;
        _items.RemoveAll(item => remove.Contains(item.Id) || (item.ParentId is Guid parentId && remove.Contains(parentId)));
        RebuildRows();
        UpdateFooter();
        UpdateButtons();
        UpdateJobs();
        SetStatus($"{count} elemento(s) quitados de la lista.");
    }

    private void ClearList()
    {
        if (_engine.IsRunning) { SetStatus("Detén la transferencia antes de limpiar la lista."); return; }
        _items.Clear();
        _rows.Clear();
        _list.Items.Clear();
        RebuildRows();
        UpdateFooter();
        UpdateButtons();
        UpdateJobs();
        SetStatus("Lista vacía.");
    }

    private void RetryFailed()
    {
        var failed = _items.Where(item => item.State is ItemState.Failed or ItemState.Cancelled or ItemState.Skipped).ToList();
        if (failed.Count == 0) { SetStatus("No hay elementos fallidos."); return; }
        foreach (var item in failed)
        {
            item.State = ItemState.Pending;
            item.Error = null;
            item.TransferredBytes = 0;
            item.HashSource = null;
            item.HashTarget = null;
        }
        SyncRows();
        SetStatus($"{failed.Count} elemento(s) listos para reintentar. Pulsa Copiar, Mover o Test.");
    }

    private void SelectRows(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        foreach (ListViewItem row in _list.Items)
            if (row.Tag is TransferItem item && set.Contains(item.Id)) row.Selected = true;
    }

    private void ListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.A && e.Control)
        {
            foreach (ListViewItem row in _list.Items) row.Selected = true;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Delete)
        {
            RemoveSelected();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Up && e.Alt)
        {
            MoveSelected(-1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Down && e.Alt)
        {
            MoveSelected(1);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Right)
        {
            foreach (var folder in SelectedItems().Where(item => item.Kind == ItemKind.Folder))
            {
                folder.IsExpanded = true;
                var first = Children(folder).FirstOrDefault();
                if (first is not null) SelectRows(new[] { first.Id });
            }
            RebuildRows();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Left)
        {
            foreach (var folder in SelectedItems().Where(item => item.Kind == ItemKind.Folder)) folder.IsExpanded = false;
            RebuildRows();
            e.Handled = true;
        }
    }

    private void ListMouseDown(object? sender, MouseEventArgs e)
    {
        var info = _list.HitTest(e.Location);
        if (info.Item?.Tag is not TransferItem item || item.Kind != ItemKind.Folder) return;
        var indent = 8 + item.Depth * 18;
        if (e.X < info.Item.Bounds.Left + indent + 18 && e.X >= info.Item.Bounds.Left + indent - 2)
            ToggleExpand(item);
    }

    private void ListDoubleClick(object? sender, EventArgs e)
    {
        if (_list.SelectedItems.Count != 1) return;
        if (_list.SelectedItems[0].Tag is not TransferItem item) return;
        if (item.Kind == ItemKind.Folder && Children(item).Count > 0)
        {
            ToggleExpand(item);
            return;
        }
        OpenItem(item);
    }

    private void OpenSelected()
    {
        if (_list.SelectedItems.Count != 1) return;
        if (_list.SelectedItems[0].Tag is TransferItem item) OpenItem(item);
    }

    private void OpenItem(TransferItem item)
    {
        try
        {
            if (File.Exists(item.SourcePath))
                Process.Start(new ProcessStartInfo(item.SourcePath) { UseShellExecute = true });
            else if (Directory.Exists(item.SourcePath))
                OpenFolder(item.SourcePath);
        }
        catch (Exception ex)
        {
            SetStatus("No se pudo abrir: " + ex.Message);
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
        catch { }
    }

    private void OpenSelectedFolder()
    {
        if (_list.SelectedItems.Count != 1) return;
        if (_list.SelectedItems[0].Tag is not TransferItem item) return;
        var folder = Directory.Exists(item.SourcePath) ? item.SourcePath : Path.GetDirectoryName(item.SourcePath);
        if (!string.IsNullOrEmpty(folder)) OpenFolder(folder);
    }

    // ---------------------------------------------------------------- transferencias

    private async void StartTransfer(OperationType operation)
    {
        try
        {
            await StartTransferAsync(operation);
        }
        catch (Exception ex)
        {
            FileLog.Write("Error iniciando transferencia: " + ex);
            ShowError("No se pudo iniciar la transferencia: " + CopyEngine.Describe(ex));
        }
    }

    private async Task StartTransferAsync(OperationType operation)
    {
        if (_engine.IsRunning) { SetStatus("Ya hay una transferencia en curso."); return; }

        if (_pendingScans.Count > 0)
        {
            SetStatus("Terminando de analizar las carpetas…");
            try { await Task.WhenAll(_pendingScans.ToArray()); } catch { }
        }

        var basis = BasisItems();
        if (basis.Count == 0)
        {
            SetStatus("Añade archivos o carpetas para empezar. Puedes arrastrarlos a esta ventana.");
            return;
        }

        var deleteTargets = new List<TransferItem>();
        var leaves = new List<TransferItem>();

        if (operation == OperationType.Delete)
        {
            deleteTargets = basis;
            if (_settings.ConfirmDelete)
            {
                var totalDelete = deleteTargets.Sum(item => Math.Max(0, item.SizeBytes));
                var message = $"¿Eliminar {deleteTargets.Count} elemento(s) ({Format.Bytes(totalDelete)})?\n\n" +
                              (_settings.UseRecycleBin
                                  ? "Se enviarán a la Papelera de reciclaje."
                                  : "Se eliminarán de forma permanente.");
                if (MessageBox.Show(this, message, "XoPy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
        }
        else
        {
            foreach (var item in basis)
            {
                if (item.Kind == ItemKind.Folder) leaves.AddRange(Descendants(item));
                else leaves.Add(item);
            }

            // Elementos de carpetas que se hayan añadido sueltos (no seleccionadas como bloque).
            foreach (var folder in _items.Where(item => item.Kind == ItemKind.Folder))
                leaves.AddRange(Children(folder));
            leaves = leaves
                .Where(item => item.Kind == ItemKind.File || (item.Kind == ItemKind.DirectoryEntry && operation != OperationType.Test))
                .Distinct()
                .ToList();

            if (leaves.Count == 0)
            {
                SetStatus("La carpeta está vacía o no se pudo leer. Revisa los permisos.");
                return;
            }
        }

        var destination = _destination.Text.Trim();
        if (operation != OperationType.Delete)
        {
            if (destination.Length == 0)
            {
                PromptDestinationAndStart(operation);
                return;
            }

            try
            {
                destination = Path.GetFullPath(destination);
                if (operation != OperationType.Test) Directory.CreateDirectory(destination);
                if (!Directory.Exists(destination)) throw new DirectoryNotFoundException("La carpeta de destino no existe: " + destination);
            }
            catch (Exception ex)
            {
                ShowError("La carpeta de destino no es válida: " + CopyEngine.Describe(ex));
                return;
            }

            _destination.Text = destination;
            _settings.Destination = destination;
            _store.Save(_settings);
        }

        var required = leaves.Sum(item => Math.Max(0, item.SizeBytes));
        if (operation is OperationType.Copy or OperationType.Move && required > 0 && !ConfirmFreeSpace(destination, required)) return;

        foreach (var leaf in leaves)
        {
            leaf.Operation = operation;
            leaf.State = ItemState.Pending;
            leaf.TransferredBytes = 0;
            leaf.Error = null;
            leaf.CurrentFile = null;
            leaf.HashSource = null;
            leaf.HashTarget = null;
            leaf.DestinationPath = Path.Combine(destination, leaf.RelativePath);
        }

        foreach (var folder in _items.Where(item => item.Kind == ItemKind.Folder))
        {
            folder.Operation = operation;
            folder.State = ItemState.Pending;
            folder.TransferredBytes = 0;
            folder.DestinationPath = string.Empty;
        }

        foreach (var target in deleteTargets)
        {
            target.Operation = operation;
            target.State = ItemState.Pending;
            target.TransferredBytes = 0;
            target.Error = null;
        }

        var targets = operation == OperationType.Delete ? deleteTargets : leaves;
        if (targets.Count == 0) { SetStatus("No hay elementos que transferir."); return; }

        var options = new EngineOptions
        {
            Destination = destination,
            Conflict = _settings.Conflict,
            OnError = _settings.OnError,
            Verify = _settings.WriteChecksumFile && _settings.Verify == VerifyAlgorithm.None ? VerifyAlgorithm.Sha256 : _settings.Verify,
            VerifyAfterCopy = _settings.VerifyAfterCopy,
            ComputeHashes = _settings.WriteChecksumFile,
            RetryCount = _settings.RetryCount,
            Parallelism = _settings.Parallelism,
            BufferSize = _settings.BufferKiB * 1024,
            SpeedLimitBytesPerSecond = _settings.SpeedLimitMiB * 1024L * 1024L,
            PreserveTimestamps = _settings.PreserveTimestamps,
            UseRecycleBin = _settings.UseRecycleBin,
            CopyLockedFiles = _settings.CopyLockedFiles,
            ResumePartials = _settings.ResumePartials,
            SecureDeletePasses = _settings.SecureDeletePasses,
            Destinations = ExtraDestinations(operation)
        };

        _lastOperation = operation;
        _lastDestination = destination;
        SaveQueueFile(_store.LastQueueFile, silent: true);
        _graph.Reset();
        _completedAt = DateTime.MinValue;
        _trayPercent = -1;
        _trayError = false;
        _trayPaused = false;

        SyncRows();
        UpdateHashColumns();
        UpdateFooter();
        UpdateButtons();

        if (!_engine.Start(targets, options))
        {
            SetStatus("No se pudo iniciar la transferencia.");
            UpdateButtons();
            return;
        }

        _wasRunning = true;
        _pauseButton.Text = "Pausar";
        _pauseButton.Image = Icons.Glyph(Icons.Pause, _palette.Text, 18);
        _trayPauseItem.Text = "Pausar";
        SetStatus(operation switch
        {
            OperationType.Move => "Moviendo archivos…",
            OperationType.Delete => "Eliminando archivos…",
            OperationType.Test => "Verificando archivos…",
            _ => "Copiando archivos…"
        });
        UpdateJobs();
        UpdateButtons();
    }

    private List<string> ExtraDestinations(OperationType operation)
    {
        var list = new List<string>();
        if (operation == OperationType.Delete) return list;
        foreach (var candidate in _settings.ExtraDestinations)
        {
            try
            {
                if (!Directory.Exists(candidate)) continue;
                var full = Path.GetFullPath(candidate);
                if (!list.Contains(full, StringComparer.OrdinalIgnoreCase)) list.Add(full);
            }
            catch { }
        }
        return list;
    }

    private void TogglePause()
    {
        if (!_engine.IsRunning) return;
        var paused = !_engine.IsPaused;
        _engine.SetPaused(paused);
        _pauseButton.Text = paused ? "Reanudar" : "Pausar";
        _pauseButton.Image = Icons.Glyph(paused ? Icons.Play : Icons.Pause, _palette.Text, 18);
        _trayPauseItem.Text = paused ? "Reanudar" : "Pausar";
        _pauseButton.BackColor = paused ? _palette.Accent : _palette.Raised;
        _pauseButton.ForeColor = paused ? Color.White : _palette.Text;
        SetStatus(paused ? "Transferencia en pausa." : "Transferencia en curso…");
        UpdateFooter();
    }

    private void SkipCurrent()
    {
        if (!_engine.IsRunning) { SetStatus("No hay ninguna transferencia en curso."); return; }
        _engine.SkipCurrent();
        SetStatus("Saltando el archivo actual…");
    }

    private void StopTransfer()
    {
        if (!_engine.IsRunning) return;
        _engine.Cancel();
        SetStatus("Cancelando… se eliminarán los archivos temporales.");
        UpdateButtons();
    }

    private bool ConfirmFreeSpace(string destination, long requiredBytes)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(destination));
            if (string.IsNullOrEmpty(root)) return true;
            var drive = new DriveInfo(root);
            if (!drive.IsReady || requiredBytes <= 0) return true;
            if (drive.AvailableFreeSpace >= requiredBytes) return true;

            var message = $"En el destino quedan {Format.Bytes(drive.AvailableFreeSpace)} libres y necesitas {Format.Bytes(requiredBytes)}.\n\n¿Quieres continuar de todos modos?";
            return MessageBox.Show(this, message, "Espacio insuficiente", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }
        catch
        {
            return true;
        }
    }

    private ConflictChoice ResolveConflict(ConflictPrompt prompt)
    {
        if (!IsHandleCreated || IsDisposed) return ConflictChoice.Rename;
        try
        {
            return (ConflictChoice)Invoke(new Func<ConflictChoice>(() => AskConflict(prompt)));
        }
        catch
        {
            return ConflictChoice.Rename;
        }
    }

    private ConflictChoice AskConflict(ConflictPrompt prompt)
    {
        var detail = $"Origen: {prompt.SourcePath} ({Format.Bytes(prompt.SourceSize)}, {prompt.SourceModified:g})\n" +
                     $"Destino: {prompt.TargetPath} ({Format.Bytes(prompt.TargetSize)}, {prompt.TargetModified:g})";
        var choice = PromptDialog.Ask(this, _palette, "Ya existe un archivo",
            "El archivo de destino ya existe. ¿Qué quieres hacer con \"" + Path.GetFileName(prompt.TargetPath) + "\"?",
            detail, new[] { "Sobrescribir", "Omitir", "Cambiar nombre", "Cancelar" }, "Aplicar a todos los conflictos", out var applyAll);

        return choice switch
        {
            0 => applyAll ? ConflictChoice.OverwriteAll : ConflictChoice.Overwrite,
            1 => applyAll ? ConflictChoice.SkipAll : ConflictChoice.Skip,
            2 => ConflictChoice.Rename,
            _ => ConflictChoice.Cancel
        };
    }

    private ErrorChoice ResolveError(ErrorPrompt prompt)
    {
        if (!IsHandleCreated || IsDisposed) return ErrorChoice.Skip;
        try
        {
            return (ErrorChoice)Invoke(new Func<ErrorChoice>(() => AskError(prompt)));
        }
        catch
        {
            return ErrorChoice.Skip;
        }
    }

    private ErrorChoice AskError(ErrorPrompt prompt)
    {
        var choice = PromptDialog.Ask(this, _palette, "Error de transferencia",
            "No se pudo transferir \"" + Path.GetFileName(prompt.FilePath) + "\".",
            prompt.Message + $"\n\nIntento {prompt.Attempt}. Archivo: {prompt.FilePath}",
            new[] { "Reintentar", "Omitir", "Omitir todos", "Detener" });
        return choice switch
        {
            0 => ErrorChoice.Retry,
            1 => ErrorChoice.Skip,
            2 => ErrorChoice.SkipAll,
            _ => ErrorChoice.Abort
        };
    }

    private void OnEngineFinished()
    {
        _wasRunning = false;
        _pauseButton.Text = "Pausar";
        _pauseButton.Image = Icons.Glyph(Icons.Pause, _palette.Text, 18);
        _pauseButton.BackColor = _palette.Raised;
        _pauseButton.ForeColor = _palette.Text;
        _trayPauseItem.Text = "Pausar";

        _sessionBytes = _engine.DoneBytes;
        _sessionElapsed = _engine.Elapsed;

        var failed = _items.Count(item => item.State == ItemState.Failed);
        var cancelled = _items.Count(item => item.State == ItemState.Cancelled);
        var skipped = _items.Count(item => item.State == ItemState.Skipped);
        var completed = _items.Count(item => item.State == ItemState.Completed && item.Kind != ItemKind.Folder);

        _store.AppendHistory(new HistoryEntry
        {
            FinishedAt = DateTime.Now,
            Operation = _lastOperation,
            Destination = string.IsNullOrWhiteSpace(_lastDestination) ? _destination.Text.Trim() : _lastDestination,
            FileCount = completed,
            Bytes = _sessionBytes,
            Seconds = _sessionElapsed.TotalSeconds,
            Errors = failed
        });
        _history = _store.LoadHistory();

        SetStatus(failed > 0
            ? $"Terminado con {failed} error(es). Revisa la lista y el registro."
            : cancelled > 0
                ? "Transferencia cancelada."
                : skipped > 0
                    ? "Transferencia completada con archivos omitidos."
                    : "Transferencia completada correctamente.");

        var icon = Icons.ProgressIcon(100, _settings.DarkTheme, false, failed > 0, failed == 0);
        ReplaceTrayIcon(icon);
        _tray.Text = failed > 0 ? $"XoPy — {failed} error(es)" : "XoPy — 100 %";
        _trayPercent = -1;
        _completedAt = DateTime.Now;
        Text = failed > 0 ? "XoPy — completado con errores" : "XoPy — completado";

        SyncRows();
        UpdateFooter();
        UpdateButtons();
        UpdateJobs();
        RefreshLog();

        if (_settings.PlaySoundWhenDone)
        {
            try
            {
                if (failed > 0) SystemSounds.Hand.Play();
                else SystemSounds.Asterisk.Play();
            }
            catch { }
        }

        if (failed == 0 && cancelled == 0) RunCompletionTask(failed);

        if (_lastOperation == OperationType.Move && failed == 0)
            CleanupMovedSourceFolders();

        if (_settings.OpenDestinationWhenDone && failed == 0 && cancelled == 0 && _lastOperation is OperationType.Copy or OperationType.Move)
            OpenFolder(string.IsNullOrWhiteSpace(_lastDestination) ? _destination.Text.Trim() : _lastDestination);

        if (failed == 0 && cancelled == 0 && _settings.ShutdownWhenDone)
        {
            try { Process.Start(new ProcessStartInfo("shutdown", "/s /t 30") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
        }

        if (failed == 0 && cancelled == 0 && _settings.CloseWhenDone)
        {
            _exitRequested = true;
            BeginInvoke(new Action(Close));
        }
    }

    /// <summary>Tras mover, elimina las carpetas de origen que se hayan quedado vacías.</summary>
    private void CleanupMovedSourceFolders()
    {
        foreach (var folder in RootItems().Where(item => item.Kind == ItemKind.Folder))
        {
            try
            {
                if (!Directory.Exists(folder.SourcePath)) continue;
                foreach (var directory in Directory.GetDirectories(folder.SourcePath, "*", SearchOption.AllDirectories)
                             .OrderByDescending(path => path.Length))
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
                    }
                    catch { }
                }
                if (!Directory.EnumerateFileSystemEntries(folder.SourcePath).Any())
                {
                    Directory.Delete(folder.SourcePath);
                    folder.State = ItemState.Completed;
                    folder.Error = null;
                    FileLog.Write("Carpeta de origen eliminada tras mover: " + folder.SourcePath);
                }
            }
            catch { }
        }
        SyncRows();
    }

    // ---------------------------------------------------------------- temporizador

    private void TimerTick()
    {
        if (_engine.IsRunning)
        {
            SyncRows();
            UpdateTrayState();
            UpdateTitle();
        }

        UpdateFooter();
        UpdateButtons();

        if (!_engine.IsRunning && _completedAt != DateTime.MinValue && (DateTime.Now - _completedAt).TotalSeconds > 6)
        {
            _completedAt = DateTime.MinValue;
            RestoreBaseTrayIcon();
        }
    }

    private void UpdateTitle()
    {
        var total = _engine.TotalBytes;
        var percent = total > 0 ? _engine.DoneBytes * 100d / total : 0;
        var speed = Format.Speed(_engine.SpeedBytesPerSecond);
        Text = _engine.IsPaused
            ? $"XoPy — en pausa · {percent:0.0} %"
            : $"XoPy — {percent:0.0} % ({speed})";
    }

    private void UpdateTrayState()
    {
        var total = _engine.TotalBytes;
        var percent = total > 0 ? (int)Math.Clamp(_engine.DoneBytes * 100 / total, 0, 100) : 0;
        var paused = _engine.IsPaused;
        var error = _items.Any(item => item.State == ItemState.Failed);
        if (percent == _trayPercent && paused == _trayPaused && error == _trayError) return;

        _trayPercent = percent;
        _trayPaused = paused;
        _trayError = error;

        ReplaceTrayIcon(Icons.ProgressIcon(percent, _settings.DarkTheme, paused, error, percent >= 100));
        _tray.Text = $"XoPy — {percent} %" + (paused ? " · en pausa" : string.Empty);
    }

    private void ReplaceTrayIcon(Icon icon)
    {
        var previous = _percentIcon;
        try
        {
            _tray.Icon = icon;
            _percentIcon = icon;
            if (previous is not null && !ReferenceEquals(previous, icon)) previous.Dispose();
        }
        catch { }
    }

    private void RestoreBaseTrayIcon()
    {
        try
        {
            _tray.Icon = _baseIcon ?? Icons.AppIcon();
            _tray.Text = "XoPy";
            _trayPercent = -1;
        }
        catch { }
    }

    private void UpdateButtons()
    {
        if (_copyButton is null) return;
        var running = _engine.IsRunning;
        _addFilesButton.Enabled = !running;
        _addFolderButton.Enabled = !running;
        _copyButton.Enabled = !running && _items.Count > 0;
        _moveButton.Enabled = !running && _items.Count > 0;
        _deleteButton.Enabled = !running && _items.Count > 0;
        _testButton.Enabled = !running && _items.Count > 0;
        _pauseButton.Enabled = running;
        _skipButton.Enabled = running;
        _stopButton.Enabled = running;
        _browseButton.Enabled = !running;
        _favoritesButton.Enabled = !running;
        _verifyCheck.Enabled = !running;
        _removeButton.Enabled = !running && _items.Count > 0;
        _clearButton.Enabled = !running && _items.Count > 0;
        _upButton.Enabled = !running && _items.Count > 0;
        _downButton.Enabled = !running && _items.Count > 0;
        _destination.ReadOnly = running;
        if (_trayPauseItem is not null) _trayPauseItem.Enabled = running;
    }

    private void UpdateFooter()
    {
        if (_progress is null) return;

        var leaves = _items.Where(item => item.Kind != ItemKind.Folder).ToList();
        long total;
        long done;
        double percent;

        if (_engine.IsRunning)
        {
            total = _engine.TotalBytes;
            done = _engine.DoneBytes;
            percent = total > 0 ? Math.Clamp(done * 100d / total, 0, 100) : 0;
        }
        else
        {
            total = leaves.Sum(item => Math.Max(0, item.SizeBytes));
            done = leaves.Sum(item => Math.Clamp(item.TransferredBytes, 0, Math.Max(item.SizeBytes, item.TransferredBytes)));
            percent = total > 0 ? Math.Clamp(done * 100d / total, 0, 100) : 0;
            if (leaves.Count > 0 && leaves.All(item => item.State is ItemState.Completed or ItemState.Skipped)) percent = 100;
        }

        UpdateCountLabel(leaves.Count, total);
        _progress.Value = percent;
        _progress.FillColor = _items.Any(item => item.State == ItemState.Failed) && !_engine.IsRunning
            ? _palette.Warning
            : _palette.Accent;

        _summaryLabel.Text = leaves.Count == 0
            ? "Sin archivos en la lista"
            : $"{leaves.Count} archivo(s) · {Format.Bytes(done)} de {Format.Bytes(total)} · {percent:0.0} %";

        if (_engine.IsRunning)
        {
            var speed = _engine.SpeedBytesPerSecond;
            var remaining = speed > 1 && total > done ? TimeSpan.FromSeconds((total - done) / speed) : TimeSpan.Zero;
            _speedLabel.Text = $"{Format.Speed(speed)}  ·  quedan {Format.Duration(remaining)}";
        }
        else
        {
            _speedLabel.Text = _items.Count == 0 ? "Listo" : $"Total {Format.Bytes(total)}";
        }

        var runningLeaf = leaves.FirstOrDefault(item => item.State is ItemState.Running or ItemState.Verifying);
        if (_engine.IsRunning)
        {
            var name = runningLeaf?.RelativePath ?? (runningLeaf is null ? null : Path.GetFileName(runningLeaf.SourcePath));
            _currentLabel.Text = string.IsNullOrEmpty(name)
                ? (_engine.IsPaused ? "En pausa" : "Preparando la transferencia…")
                : (_engine.IsPaused ? "En pausa · " : "Archivo actual: ") + name;
        }
        else if (_items.Count > 0)
        {
            _currentLabel.Text = "Listo para transferir. Elige Copiar, Mover, Test o Eliminar.";
        }
        else
        {
            _currentLabel.Text = "Arrastra archivos o carpetas a la ventana, o usa Añadir archivos.";
        }

        if (_statusLabel is not null && _engine.IsRunning && !string.IsNullOrWhiteSpace(_currentStatus))
            _statusLabel.Text = _currentStatus;

        UpdateTransferHeader(leaves, runningLeaf, done, total, percent);
        if (_engine.IsRunning) _graph?.Add(_engine.SpeedBytesPerSecond);
        UpdateStatusTab(percent, done, total, runningLeaf?.RelativePath ?? string.Empty);
    }

    // ---------------------------------------------------------------- cuadros de diálogo

    private void BrowseDestination()
    {
        try
        {
            if (Directory.Exists(_destination.Text.Trim())) _pickDestination.SelectedPath = _destination.Text.Trim();
        }
        catch { }
        _pickDestination.Description = "Selecciona la carpeta de destino";
        if (_pickDestination.ShowDialog(this) != DialogResult.OK) return;
        _destination.Text = _pickDestination.SelectedPath;
        _settings.Destination = _destination.Text;
        _store.Save(_settings);
        SetStatus("Destino: " + _destination.Text);
    }

    private void ShowOptions()
    {
        using var dialog = new OptionsDialog(_settings, _palette);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var themeChanged = dialog.Result.DarkTheme != _settings.DarkTheme;
        dialog.ApplyTo(_settings);
        _store.Save(_settings);
        RefreshExtraDestinations();

        try
        {
            ShellIntegration.Apply(_settings.ExplorerIntegration);
            ShellIntegration.SetAutoStart(_settings.StartWithWindows);
        }
        catch { }

        LoadSettingsIntoUi();
        UpdateHashColumns();
        UpdateButtons();
        UpdateFooter();
        UpdateTargetTab();
        SetStatus("Preferencias guardadas.");

        if (themeChanged)
        {
            MessageBox.Show(this, "El tema se aplicará la próxima vez que abras XoPy.", "XoPy",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ShowExplorerIntegration()
    {
        var enabled = ShellIntegration.IsEnabled();
        var message = "XoPy añade estas acciones al menú contextual del Explorador:\n\n" +
                      "· Copiar con XoPy (usa el último destino)\n" +
                      "· Copiar a… con XoPy (pregunta el destino)\n" +
                      "· Mover con XoPy / Mover a… con XoPy\n" +
                      "· Verificar con XoPy\n" +
                      "· Pegar con XoPy (al hacer clic con el botón derecho en el fondo de una carpeta)\n\n" +
                      "Además, instala un acceso en «Enviar a», que es la forma más rápida de enviar varios archivos a la vez.\n\n" +
                      "En Windows 11 estas acciones aparecen en «Mostrar más opciones» (Mayús+F10).\n\n" +
                      (enabled ? "La integración está ACTIVADA." : "La integración está DESACTIVADA.") +
                      "\n\n¿Quieres activarla ahora?";

        if (MessageBox.Show(this, message, "Integración con el Explorador",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        try
        {
            _settings.ExplorerIntegration = true;
            ShellIntegration.Apply(true);
            _store.Save(_settings);
            SetStatus("Integración activada. Ya puedes hacer clic derecho en archivos y carpetas.");
        }
        catch (Exception ex)
        {
            ShowError("No se pudo activar la integración: " + ex.Message);
        }
    }

    private void ShowHistory()
    {
        _history = _store.LoadHistory();
        using var dialog = new HistoryDialog(_history, _palette);
        dialog.ShowDialog(this);
        UpdateJobs();
        UpdateTargetTab();
    }

    private void ShowAbout()
    {
        var version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "2.1.0";
        MessageBox.Show(this,
            $"XoPy {version}\n\nGestor de copias gratuito para Windows, inspirado en la interfaz de TeraCopy.\n\n" +
            "· Copia, mueve, elimina y verifica archivos o carpetas completas\n" +
            "· Cola jerárquica: expande carpetas, reordena y quita elementos\n" +
            "· Verificación CRC32, MD5, SHA-1, SHA-256 y SHA-512\n" +
            "· Pausa, saltar, detener, límite de velocidad y transferencias paralelas\n" +
            "· Icono de porcentaje en la bandeja del sistema\n" +
            "· Historial, favoritos e integración con el Explorador\n\n" +
            "Hecho por Quevedo-crack.",
            "Acerca de XoPy", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowError(string message)
    {
        SetStatus(message);
        MessageBox.Show(this, message, "XoPy", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void SaveQueueFile() => SaveQueueFile(null);

    private void SaveQueueFile(string? path, bool silent = false)
    {
        string file;
        if (path is null)
        {
            if (_saveQueue.ShowDialog(this) != DialogResult.OK) return;
            file = _saveQueue.FileName;
        }
        else file = path;

        try
        {
            var snapshot = new QueueSnapshot
            {
                Destination = _destination.Text.Trim(),
                Operation = OperationType.Copy,
                Sources = RootItems().Select(item => item.SourcePath).ToList(),
                ExtraDestinations = _settings.ExtraDestinations.ToList(),
                VerifyAfterCopy = _settings.VerifyAfterCopy,
                Verify = _settings.Verify,
                SavedAt = DateTime.Now
            };
            var directory = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(file, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            if (!silent) SetStatus("Cola guardada en " + file);
        }
        catch (Exception ex)
        {
            if (!silent) ShowError("No se pudo guardar la cola: " + ex.Message);
        }
    }

    private void LoadQueueFile()
    {
        if (_engine.IsRunning) { SetStatus("Detén la transferencia antes de abrir otra cola."); return; }
        if (_loadQueue.ShowDialog(this) != DialogResult.OK) return;
        var loaded = LoadQueueSnapshot(_loadQueue.FileName);
        if (loaded) SetStatus("Cola cargada.");
    }

    private bool LoadQueueSnapshot(string file, bool startAfterLoad = false)
    {
        try
        {
            if (!File.Exists(file)) { ShowError("No existe la cola guardada: " + file); return false; }
            var snapshot = JsonSerializer.Deserialize<QueueSnapshot>(File.ReadAllText(file));
            if (snapshot is null) { ShowError("El archivo no contiene una cola válida."); return false; }

            _items.Clear();
            _rows.Clear();
            _list.Items.Clear();

            if (!string.IsNullOrWhiteSpace(snapshot.Destination))
                _destination.Text = snapshot.Destination;
            if (snapshot.ExtraDestinations is not null)
            {
                _settings.ExtraDestinations = snapshot.ExtraDestinations;
                RefreshExtraDestinations();
            }
            _verifyCheck.Checked = snapshot.VerifyAfterCopy;
            if (startAfterLoad) _settings.Verify = snapshot.Verify;

            _ = AddPathsAsync(snapshot.Sources);
            if (startAfterLoad)
            {
                SetStatus("Reanudando la cola guardada…");
                _ = StartAfterScan(OperationType.Copy);
            }
            return true;
        }
        catch (Exception ex)
        {
            ShowError("No se pudo abrir la cola: " + ex.Message);
            return false;
        }
    }

    private async Task StartAfterScan(OperationType operation)
    {
        if (_pendingScans.Count > 0)
        {
            try { await Task.WhenAll(_pendingScans.ToArray()); } catch { }
        }
        await StartTransferAsync(operation);
    }

    private void ResumeLastQueue()
    {
        if (_engine.IsRunning) { SetStatus("Detén la transferencia antes de reanudar la cola."); return; }
        if (!File.Exists(_store.LastQueueFile))
        {
            SetStatus("Todavía no hay ninguna cola guardada.");
            return;
        }
        LoadQueueSnapshot(_store.LastQueueFile, startAfterLoad: true);
    }

    private enum QueueSortField { Name, Size, Type, Destination }

    private void SortQueue(QueueSortField field, bool invert = false)
    {
        if (_items.Count < 2) { SetStatus("La lista está vacía."); return; }
        if (_engine.IsRunning) { SetStatus("Detén la transferencia para ordenar la cola."); return; }

        Comparison<TransferItem> comparison = field switch
        {
            QueueSortField.Size => (left, right) => left.SizeBytes.CompareTo(right.SizeBytes),
            QueueSortField.Type => (left, right) => string.Compare(TypeKey(left), TypeKey(right), StringComparison.OrdinalIgnoreCase),
            QueueSortField.Destination => (left, right) => string.Compare(DestinationKey(left), DestinationKey(right), StringComparison.OrdinalIgnoreCase),
            _ => (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name)
        };
        int Order(TransferItem left, TransferItem right) => invert ? comparison(right, left) : comparison(left, right);

        var roots = RootItems();
        roots.Sort(Order);

        var children = new Dictionary<Guid, List<TransferItem>>();
        foreach (var item in _items.Where(item => item.ParentId is not null))
        {
            if (!children.TryGetValue(item.ParentId!.Value, out var list))
                children[item.ParentId.Value] = list = new List<TransferItem>();
            list.Add(item);
        }
        foreach (var list in children.Values) list.Sort(Order);

        var rebuilt = new List<TransferItem>();
        void AddNode(TransferItem node)
        {
            rebuilt.Add(node);
            if (children.TryGetValue(node.Id, out var kids))
                foreach (var child in kids) AddNode(child);
        }
        foreach (var root in roots) AddNode(root);

        _items.Clear();
        _items.AddRange(rebuilt);
        RebuildRows();
        UpdateFooter();
        var label = field switch
        {
            QueueSortField.Size => "tamaño",
            QueueSortField.Type => "tipo",
            QueueSortField.Destination => "carpeta de destino",
            _ => "nombre"
        };
        SetStatus($"Cola ordenada por {label}{(invert ? " (descendente)" : string.Empty)}. Las carpetas conservan sus archivos.");
    }

    private static string TypeKey(TransferItem item)
    {
        var extension = Path.GetExtension(item.Name);
        return string.IsNullOrEmpty(extension) ? "\uFFFF" + item.Name : extension + "\u0000" + item.Name;
    }

    private static string DestinationKey(TransferItem item) =>
        string.IsNullOrWhiteSpace(item.RelativePath) ? item.Name : item.RelativePath;

    // ---------------------------------------------------------------- informes y tareas finales

    private void ExportReportHtml()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Exportar informe",
            Filter = "Página HTML (*.html)|*.html",
            FileName = $"xopy-informe-{DateTime.Now:yyyyMMdd-HHmm}.html"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, BuildHtmlReport());
            SetStatus("Informe guardado en " + dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError("No se pudo guardar el informe: " + ex.Message);
        }
    }

    private void ExportReportCsv()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Exportar informe",
            Filter = "Hoja de cálculo CSV (*.csv)|*.csv",
            FileName = $"xopy-informe-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var builder = new StringBuilder();
            builder.AppendLine("Nombre;Origen;Destino;Operacion;Tamano_bytes;Estado;Error;Hash_origen;Hash_destino");
            foreach (var item in _items.Where(item => item.Kind != ItemKind.Folder))
            {
                builder.Append(Csv(item.Name)).Append(';')
                    .Append(Csv(item.SourcePath)).Append(';')
                    .Append(Csv(item.DestinationPath)).Append(';')
                    .Append(item.Operation).Append(';')
                    .Append(item.SizeBytes).Append(';')
                    .Append(Csv(item.StateText)).Append(';')
                    .Append(Csv(item.Error ?? string.Empty)).Append(';')
                    .Append(Csv(item.HashSource ?? string.Empty)).Append(';')
                    .Append(Csv(item.HashTarget ?? string.Empty)).AppendLine();
            }
            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
            SetStatus("Informe CSV guardado en " + dialog.FileName);
        }
        catch (Exception ex)
        {
            ShowError("No se pudo guardar el informe: " + ex.Message);
        }
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private string BuildHtmlReport()
    {
        var builder = new StringBuilder();
        var failed = _items.Count(item => item.State == ItemState.Failed);
        builder.AppendLine("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\">")
            .AppendLine("<title>Informe de XoPy</title><style>")
            .AppendLine("body{font-family:'Segoe UI',sans-serif;background:#16181d;color:#e6e8ee;margin:32px}")
            .AppendLine("h1{font-size:22px;margin:0 0 4px}h2{font-size:15px;margin:24px 0 8px;color:#9aa3b2}")
            .AppendLine("table{border-collapse:collapse;width:100%;font-size:13px}")
            .AppendLine("th,td{border-bottom:1px solid #2b2f38;padding:6px 8px;text-align:left}")
            .AppendLine("th{color:#9aa3b2;font-weight:600}.ok{color:#57d38c}.bad{color:#ff7a7a}.muted{color:#9aa3b2}")
            .AppendLine("</style></head><body>")
            .AppendLine($"<h1>Informe de transferencia</h1><div class=\"muted\">{DateTime.Now:yyyy-MM-dd HH:mm} · {_lastOperation} → {_lastDestination}</div>")
            .AppendLine($"<p>{_items.Count(item => item.State == ItemState.Completed && item.Kind == ItemKind.File)} archivo(s) copiados · {Format.Bytes(_sessionBytes)} · {Format.Duration(_sessionElapsed)} · {failed} error(es)</p>")
            .AppendLine("<table><tr><th>Nombre</th><th>Tamaño</th><th>Estado</th><th>Origen</th><th>Destino</th></tr>");
        foreach (var item in _items.Where(item => item.Kind != ItemKind.Folder))
        {
            var css = item.State == ItemState.Failed ? "bad" : item.State == ItemState.Completed ? "ok" : "muted";
            builder.AppendLine($"<tr><td>{Html(item.Name)}</td><td>{Format.Bytes(item.SizeBytes)}</td><td class=\"{css}\">{Html(item.StateText)}</td><td>{Html(item.SourcePath)}</td><td>{Html(item.DestinationPath)}</td></tr>");
        }
        builder.AppendLine("</table></body></html>");
        return builder.ToString();
    }

    private static string Html(string value) => System.Net.WebUtility.HtmlEncode(value);

    private void RunCompletionTask(int failed)
    {
        WriteChecksumManifest();
        RunCompletionScript(failed);
    }

    private void WriteChecksumManifest()
    {
        if (!_settings.WriteChecksumFile) return;
        try
        {
            var algorithm = _settings.Verify == VerifyAlgorithm.None ? VerifyAlgorithm.Sha256 : _settings.Verify;
            var rows = _items
                .Where(item => item.Kind == ItemKind.File && item.State == ItemState.Completed && !string.IsNullOrWhiteSpace(item.HashSource))
                .ToList();
            if (rows.Count == 0)
            {
                SetStatus("No hay hashes disponibles todavía para crear el archivo de checksums.");
                return;
            }

            var extension = algorithm switch
            {
                VerifyAlgorithm.Crc32 => "sfv",
                VerifyAlgorithm.Md5 => "md5",
                VerifyAlgorithm.Sha1 => "sha1",
                VerifyAlgorithm.Sha512 => "sha512",
                _ => "sha256"
            };
            var directory = string.IsNullOrWhiteSpace(_lastDestination) ? _destination.Text.Trim() : _lastDestination;
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, $"XoPy-checksums-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}");
            var builder = new StringBuilder();
            foreach (var item in rows)
            {
                var relative = string.IsNullOrWhiteSpace(item.RelativePath) ? item.Name : item.RelativePath;
                builder.Append(item.HashSource).Append(" *").AppendLine(relative);
            }
            var temp = file + ".tmp";
            File.WriteAllText(temp, builder.ToString());
            File.Move(temp, file, true);
            FileLog.Write("Archivo de checksums creado: " + file);
            SetStatus("Archivo de checksums creado: " + file);
        }
        catch (Exception ex)
        {
            FileLog.Write("No se pudo crear el archivo de checksums: " + ex.Message);
        }
    }

    private void RunCompletionScript(int failed)
    {
        var script = _settings.CompletionScript;
        if (string.IsNullOrWhiteSpace(script) || !File.Exists(script)) return;
        try
        {
            var arguments = $"\"{_lastDestination}\" {failed} \"{_lastOperation}\"";
            ProcessStartInfo info;
            if (script.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                info = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" {arguments}");
            else if (script.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || script.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
                info = new ProcessStartInfo("cmd.exe", $"/c \"\"{script}\" {arguments}\"");
            else
                info = new ProcessStartInfo(script, arguments);
            info.UseShellExecute = true;
            Process.Start(info);
            FileLog.Write("Script al finalizar lanzado: " + script);
        }
        catch (Exception ex)
        {
            FileLog.Write("No se pudo lanzar el script al finalizar: " + ex.Message);
        }
    }

    private void RefreshExtraDestinations()
    {
        if (_extraDestinations is null) return;
        _extraDestinations.BeginUpdate();
        _extraDestinations.Items.Clear();
        foreach (var path in _settings.ExtraDestinations) _extraDestinations.Items.Add(path);
        _extraDestinations.EndUpdate();
    }

    // ---------------------------------------------------------------- ventana y bandeja

    private void RestoreWindow()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        Show();
        Activate();
        BringToFront();
        try { SetForegroundWindow(Handle); } catch { }
    }

    private void MinimizeToTray()
    {
        Hide();
        ShowInTaskbar = false;
        try
        {
            _tray.ShowBalloonTip(1200, "XoPy", "XoPy sigue abierto en el área de notificación.", ToolTipIcon.Info);
        }
        catch { }
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && _settings.MinimizeToTray) MinimizeToTray();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveWindowGeometry();
        if (_items.Count > 0) SaveQueueFile(_store.LastQueueFile, silent: true);

        if (!_exitRequested && _settings.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            MinimizeToTray();
            return;
        }

        if (_engine.IsRunning)
        {
            var answer = MessageBox.Show(this, "Hay una transferencia en curso. ¿Detenerla y salir?", "XoPy",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) { e.Cancel = true; return; }
            _engine.Cancel();
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            _engine.Cancel();
            _tray.Visible = false;
            _tray.Dispose();
            _trayMenu.Dispose();
            if (_listMenu is not null) _listMenu.Dispose();
            _favoritesMenu.Dispose();
            _percentIcon?.Dispose();
            _baseIcon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
