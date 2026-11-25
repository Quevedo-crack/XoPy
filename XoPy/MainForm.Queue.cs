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
    private readonly List<Guid> _visualOrder = new();

    // ---------------------------------------------------------------- cola

    private void AddFiles()
    {
        // Añadir con la copia en marcha está permitido: los nuevos se suman a la cola como
        // pendientes y se copian después; no se toca el archivo que se está escribiendo.
        if (_openFiles.ShowDialog(this) != DialogResult.OK) return;
        _ = AddPathsAsync(_openFiles.FileNames);
    }

    private void AddFolder()
    {
        if (_pickFolder.ShowDialog(this) != DialogResult.OK) return;
        _ = AddPathsAsync(new[] { _pickFolder.SelectedPath });
    }

    private void DropPaths(DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        if (!_uiReady)
        {
            // Soltar archivos durante el arranque hacía fallar la UI a medio construir.
            SetStatus("La ventana aún se está preparando. Suelta los archivos en un segundo.");
            return;
        }
        _ = HandleExternalFiles(paths, null, false);
    }

    /// <param name="askDestination">
    /// true (por defecto) muestra la pantalla de elegir carpeta al terminar de añadir, aunque
    /// la lista ya tuviera un destino. Solo se pasa false cuando el destino es explícito
    /// (pegado desde el Explorador, --to, «Enviar a»), para no preguntar dos veces.
    /// </param>
    private async Task AddPathsAsync(IEnumerable<string> paths, TransferList? targetList = null, bool askDestination = true)
    {
        try
        {
            if (targetList is not null && targetList != _activeList) SwitchToList(targetList);
            EnsureDefaultList();

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
                            IsExpanded = false
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
                SetStatus(_items.Count == 0
                    ? "Listo para transferir."
                    : $"Lista preparada: {_items.Count(item => item.Kind == ItemKind.File)} archivo(s) listos para transferir.");
            }

            // Al añadir archivos se pregunta SIEMPRE la carpeta de destino, aunque la lista
            // conserve la última usada: el destino nunca viene prefijado (pedido explícito).
            if (askDestination && (targetList ?? _activeList) is { } list &&
                _items.Count > 0 && !_engine.IsRunning)
                PromptDestinationForList(list, sayNothingIfCancelled: true);

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
                IsExpanded = false
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

    // Keep display order separate from _items: transfer order and source hierarchy
    // must not change when a file is merely dragged to another visual position.
    private List<TransferItem> VisualItems()
    {
        var byId = _items.ToDictionary(item => item.Id);
        _visualOrder.RemoveAll(id => !byId.ContainsKey(id));
        var present = _visualOrder.ToHashSet();

        for (var index = 0; index < _items.Count;)
        {
            if (present.Contains(_items[index].Id))
            {
                index++;
                continue;
            }

            var start = index;
            var additions = new List<Guid>();
            while (index < _items.Count && !present.Contains(_items[index].Id))
            {
                var id = _items[index++].Id;
                present.Add(id);
                additions.Add(id);
            }

            var insertAt = -1;
            for (var previous = start - 1; previous >= 0; previous--)
            {
                var visualIndex = _visualOrder.IndexOf(_items[previous].Id);
                if (visualIndex < 0) continue;
                insertAt = visualIndex + 1;
                break;
            }
            if (insertAt < 0)
            {
                for (var next = index; next < _items.Count; next++)
                {
                    var visualIndex = _visualOrder.IndexOf(_items[next].Id);
                    if (visualIndex < 0) continue;
                    insertAt = visualIndex;
                    break;
                }
            }
            if (insertAt < 0) insertAt = _visualOrder.Count;
            _visualOrder.InsertRange(insertAt, additions);
        }

        return _visualOrder.Select(id => byId[id]).ToList();
    }

    private void ResetVisualOrder() => _visualOrder.Clear();

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

        // Al reconstruir la lista (tras mover, borrar o reordenar) se perdía el scroll y el
        // usuario volvía al principio de 6.000 archivos. Se recuerda la fila que estaba
        // arriba y se devuelve exactamente ahí al terminar.
        var anchorId = (_list.TopItem?.Tag as TransferItem)?.Id;

        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            _rows.Clear();
            // Lista PLANA, como TeraCopy: una fila por archivo, venga de la carpeta que venga.
            // La carpeta no ocupa fila (por eso antes había que desplegarla para ver el
            // contenido), y así se puede reordenar archivo a archivo conservando su raíz, que
            // se muestra en la propia fila. La ruta de destino sigue saliendo de RelativePath,
            // que no se toca al reordenar.
            foreach (var item in VisualItems())
            {
                if (item.Kind == ItemKind.Folder) continue;
                var row = new ListViewItem(new[] { item.Name, DestinationText(item), item.SizeText, item.StateText, string.Empty, string.Empty, string.Empty })
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
        // Al pasar de lista vacía a con filas se reparte de nuevo el alto: ResizeColumns es
        // quien devuelve la fila del ListView a Percent y, si no, las filas no se ven.
        ResizeColumns();
        // Devolver la vista a la fila donde estaba antes de reconstruir.
        if (anchorId is not null && _rows.TryGetValue(anchorId.Value, out var anchorRow))
        {
            try { _list.EnsureVisible(anchorRow.Index); } catch { }
        }
        UpdateScrollCover();
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
                SetText(row.SubItems[1], DestinationText(item));
                SetText(row.SubItems[2], item.SizeText);
                SetText(row.SubItems[3], state);
                SetText(row.SubItems[4], $"{RowPercent(item):0} %");
                SetText(row.SubItems[5], item.HashSource ?? string.Empty);
                SetText(row.SubItems[6], item.HashTarget ?? string.Empty);
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

    /// <summary>
    /// Carpeta real a la que irá (o fue) este elemento: destino elegido + ruta relativa
    /// dentro de la carpeta de origen. Las carpetas muestran su carpeta contenedora.
    /// </summary>
    private string DestinationText(TransferItem item)
    {
        // El destino mostrado es el que se usará al copiar: el de la lista activa (que
        // refleja el cuadro de destino) o, si aún no hay, el último usado. Los elementos
        // ya copiados muestran su ruta real vía DestinationPath.
        var baseDestination = _activeList?.Destination ?? string.Empty;
        if (string.IsNullOrWhiteSpace(baseDestination)) baseDestination = (_destination?.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(baseDestination)) baseDestination = _lastDestination;
        if (string.IsNullOrWhiteSpace(baseDestination)) return string.Empty;

        if (!string.IsNullOrWhiteSpace(item.DestinationPath))
        {
            return item.Kind == ItemKind.DirectoryEntry
                ? item.DestinationPath
                : Path.GetDirectoryName(item.DestinationPath) ?? baseDestination;
        }

        var relative = item.RelativePath;
        if (string.IsNullOrWhiteSpace(relative)) return baseDestination;

        if (item.Kind == ItemKind.File)
        {
            var folder = Path.GetDirectoryName(relative);
            return string.IsNullOrEmpty(folder) ? baseDestination : Path.Combine(baseDestination, folder);
        }

        // Carpeta: el destino de la carpeta es su carpeta contenedora dentro del destino.
        var parentRelative = Path.GetDirectoryName(relative);
        return string.IsNullOrEmpty(parentRelative) ? baseDestination : Path.Combine(baseDestination, parentRelative);
    }

    /// <summary>
    /// Al seleccionar una fila con error se muestra el motivo completo en la barra de estado.
    /// Antes el texto solo estaba en el tooltip y era difícil de encontrar cuando la lista
    /// entera estaba en «Error» al 0 %.
    /// </summary>
    private void ShowSelectedError()
    {
        if (_list is null || _statusLabel is null) return;
        var withError = _list.SelectedItems.Cast<ListViewItem>()
            .Select(row => row.Tag as TransferItem)
            .OfType<TransferItem>()
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Error));
        if (withError is null) return;
        SetStatus($"{withError.Name}: {withError.Error}");
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

    /// <summary>
    /// Cabecera de la lista. El total de archivos y el tamaño están en la barra de progreso
    /// del pie, así que aquí solo va lo que NO se repite. (En la captura 18 salían
    /// «6387 archivo(s)» dos veces y «6,2 GB» tres.)
    /// </summary>
    private void UpdateCountLabel(int leaves, long total)
    {
        if (_countLabel is null) return;
        if (_items.Count == 0)
        {
            _countLabel.Text = "Lista vacía — arrastra archivos o carpetas aquí";
            return;
        }
        var folders = _items.Count - leaves;
        _countLabel.Text = folders > 0
            ? $"{_items.Count} elemento(s) · {folders} carpeta(s)"
            : $"{leaves} archivo(s)";
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
        // Se puede reordenar con la copia en marcha o en pausa: el nuevo orden afecta
        // solo a lo que falta por copiar, nunca al archivo que ya se está escribiendo.
        var selected = SelectedItems();
        if (selected.Count == 0) { SetStatus("Selecciona un elemento para cambiar su orden."); return; }

        var visual = VisualItems();
        var ordered = delta < 0
            ? selected.OrderBy(item => visual.IndexOf(item)).ToList()
            : selected.OrderByDescending(item => visual.IndexOf(item)).ToList();

        var moved = 0;
        foreach (var item in ordered)
            if (MoveBlock(item, delta)) moved++;

        if (moved == 0) { SetStatus("El elemento ya está en el extremo."); return; }

        RebuildRows();
        UpdateFooter();
        UpdateButtons();
        SetStatus("Orden de la cola actualizado.");
    }

    /// <summary>
    /// Reordena un elemento una posición arriba o abajo. Los ARCHIVOS se mueven siempre
    /// dentro de la lista plana de archivos, sin importar de qué carpeta vengan: es lo que
    /// pide poder ordenar archivo a archivo aunque estén en raíces distintas. Las carpetas
    /// conservan su propia posición y se reordenan solo entre carpetas.
    /// </summary>
    private bool MoveBlock(TransferItem item, int delta)
    {
        if (item.Kind == ItemKind.Folder)
        {
            var folders = VisualItems().Where(candidate => candidate.Kind == ItemKind.Folder).ToList();
            return SwapInVisualOrder(folders, item, delta);
        }

        // Archivos (y carpetas vacías, que son un archivo-índice): una sola secuencia plana.
        // No se toca ParentId, RelativePath ni Depth: la ruta de destino al copiar no cambia.
        var files = VisualItems()
            .Where(candidate => candidate.Kind != ItemKind.Folder)
            .ToList();
        return SwapInVisualOrder(files, item, delta);
    }

    private bool SwapInVisualOrder(List<TransferItem> sequence, TransferItem item, int delta)
    {
        var index = sequence.IndexOf(item);
        if (index < 0) return false;
        var target = index + delta;
        if (target < 0 || target >= sequence.Count) return false;

        var first = _visualOrder.IndexOf(sequence[index].Id);
        var second = _visualOrder.IndexOf(sequence[target].Id);
        if (first < 0 || second < 0) return false;
        (_visualOrder[first], _visualOrder[second]) = (_visualOrder[second], _visualOrder[first]);
        return true;
    }

    private void RemoveSelected() => RemoveItems(SelectedItems());

    private void RemoveItems(IReadOnlyCollection<TransferItem> selected)
    {
        // Se puede quitar de la lista con la copia en marcha: el motor trabaja sobre su
        // propia instantánea, así que quitar una fila no cancela ni corrompe la transferencia.
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
        // Igual que al quitar filas: vaciar la lista no toca la transferencia en curso.
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

        // Si el origen ya no existe (por ejemplo, tras mover) no se puede reintentar: se
        // explica el motivo en vez de volver a marcarlo como «Error» al 0 %.
        var missing = failed.Where(item => !File.Exists(item.SourcePath) && !Directory.Exists(item.SourcePath)).ToList();
        foreach (var item in missing)
            item.Error = "El archivo de origen ya no existe (se movió o se borró). Añádelo de nuevo.";
        var retry = failed.Except(missing).ToList();
        if (retry.Count == 0)
        {
            SyncRows();
            SetStatus($"{missing.Count} elemento(s) no se pueden reintentar: su origen ya no existe. Añádelos de nuevo.");
            return;
        }

        foreach (var item in retry)
        {
            item.State = ItemState.Pending;
            item.Error = null;
            item.TransferredBytes = 0;
            item.HashSource = null;
            item.HashTarget = null;
        }
        SyncRows();
        SetStatus(missing.Count > 0
            ? $"{retry.Count} listo(s) para reintentar; {missing.Count} sin origen en disco."
            : $"{retry.Count} elemento(s) listos para reintentar. Pulsa Copiar, Mover o Test.");
    }

    private void SelectRows(IEnumerable<Guid> ids)
    {
        var set = ids.ToHashSet();
        foreach (ListViewItem row in _list.Items)
            if (row.Tag is TransferItem item && set.Contains(item.Id)) row.Selected = true;
    }

    /// <summary>Confirma el nombre editado en la lista (el archivo del disco no se toca).</summary>
    private void ListAfterLabelEdit(object? sender, LabelEditEventArgs e)
    {
        if (e.Label is null || e.Item < 0 || e.Item >= _list.Items.Count) return;
        var name = e.Label.Trim();
        if (name.Length == 0) { e.CancelEdit = true; return; }
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            e.CancelEdit = true;
            SetStatus("Ese nombre tiene caracteres que no se permiten.");
            return;
        }
        if (_list.Items[e.Item].Tag is not TransferItem item) { e.CancelEdit = true; return; }
        item.Name = name;
        FileLog.Write($"Elemento renombrado en la lista: {item.SourcePath} -> {name}");
        SetStatus("Nombre cambiado en la lista (el archivo del disco no se ha tocado).");
    }

    /// <summary>
    /// Edición en línea de la lista: renombra el elemento (solo el nombre visible, el archivo
    /// del disco no se toca). F2 o «Renombrar el elemento…» del menú contextual.
    /// </summary>
    private void BeginRenameSelected()
    {
        if (_list is null || _list.SelectedItems.Count == 0) return;
        // Editable también con la copia en marcha: el nombre visible de la lista no afecta
        // a la transferencia que ya está en curso (el motor trabaja sobre su instantánea).
        _list.SelectedItems[0].BeginEdit();
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
        else if (e.KeyCode == Keys.F2)
        {
            BeginRenameSelected();
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
        if (e.Button == MouseButtons.Right && info.Item is not null && !info.Item.Selected)
        {
            _list.SelectedItems.Clear();
            info.Item.Selected = true;
        }
        // La lista es plana (una fila por archivo): no hay carpetas que desplegar.
        _ = e;
    }

    private void ListDoubleClick(object? sender, EventArgs e)
    {
        if (_list.SelectedItems.Count != 1) return;
        if (_list.SelectedItems[0].Tag is not TransferItem item) return;
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

    private void HandleBusy(OperationType operation)
    {
        if (!_settings.AskWhenBusy)
        {
            SetStatus("Ya hay una transferencia en curso.");
            return;
        }

        var answer = MessageBox.Show(this,
            "Ya hay una transferencia en curso.\n\n" +
            "Sí: añadir estos elementos a la copia actual (se harán en cuanto termine).\n" +
            "No: abrir otra ventana de XoPy para copiarlos a la vez.",
            "XoPy", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return;
        if (answer == DialogResult.Yes)
        {
            _followUpOperation = operation;
            SetStatus("Se copiarán en cuanto termine la transferencia en curso.");
            return;
        }

        LaunchSeparateWindow(operation);
    }

    private void LaunchSeparateWindow(OperationType operation)
    {
        var sources = BasisItems().Select(item => item.SourcePath)
            .Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        if (sources.Count == 0)
        {
            SetStatus("No hay nada que copiar en otra ventana.");
            return;
        }

        var arguments = new StringBuilder("--multi ");
        arguments.Append(operation switch
        {
            OperationType.Move => "--move ",
            OperationType.Test => "--test ",
            OperationType.Delete => "--delete ",
            _ => "--copy "
        });
        var destination = _destination.Text.Trim();
        if (destination.Length > 0) arguments.Append("--to \"").Append(destination).Append("\" ");
        if (_settings.VerifyAfterCopy) arguments.Append("--verify ");
        foreach (var source in sources) arguments.Append('"').Append(source).Append("\" ");

        try
        {
            Process.Start(new ProcessStartInfo(Application.ExecutablePath, arguments.ToString()) { UseShellExecute = false });
            SetStatus("Abriendo otra ventana de XoPy para copiar en paralelo…");
        }
        catch (Exception ex)
        {
            SetStatus("No se pudo abrir otra ventana: " + CopyEngine.Describe(ex));
        }
    }

    private void RestartElevated()
    {
        try
        {
            var info = new ProcessStartInfo(Application.ExecutablePath)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            if (_items.Count > 0)
            {
                var arguments = new StringBuilder("--multi --copy ");
                var destination = _destination.Text.Trim();
                if (destination.Length > 0) arguments.Append("--to \"").Append(destination).Append("\" ");
                foreach (var path in RootItems().Select(item => item.SourcePath))
                    arguments.Append('"').Append(path).Append("\" ");
                info.Arguments = arguments.ToString();
            }

            Process.Start(info);
            ExitApplication();
        }
        catch (Exception ex)
        {
            ShowError("No se pudo reiniciar como administrador: " + CopyEngine.Describe(ex));
        }
    }

    private async Task StartTransferAsync(OperationType operation)
    {
        if (_engine.IsRunning)
        {
            HandleBusy(operation);
            return;
        }

        if (!_uiReady)
        {
            // Nunca arrancar una transferencia con controles a medio crear: es la causa del
            // NullReferenceException que mostraba «No se pudo iniciar la transferencia».
            SetStatus("La ventana aún se está preparando. Espera un momento e inténtalo de nuevo.");
            return;
        }

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
                var message = $"¿Eliminar {deleteTargets.Count} elemento(s) del disco ({Format.Bytes(totalDelete)})?\n\n" +
                              (_settings.UseRecycleBin
                                  ? "Se enviarán a la Papelera de reciclaje.\n\nSi solo quieres quitarlos de la lista, pulsa Supr o el botón — de la barra de la lista."
                                  : "Se eliminarán de forma permanente.\n\nSi solo quieres quitarlos de la lista, pulsa Supr o el botón — de la barra de la lista.");
                if (MessageBox.Show(this, message, "XoPy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
        }
        else
        {
            // Solo se transfiere lo seleccionado (o todo si no hay selección). Antes además se
            // añadían los hijos de TODAS las carpetas de la lista: al volver a pulsar Copiar
            // entraban archivos que el usuario no había elegido y se remarcaban como Error.
            var bases = new HashSet<Guid>(basis.Select(item => item.Id));
            foreach (var item in basis)
            {
                if (item.Kind == ItemKind.Folder)
                {
                    leaves.AddRange(Descendants(item));
                    for (var folder = item.ParentId; folder is { } pid;)
                    {
                        var parent = _items.FirstOrDefault(candidate => candidate.Id == pid);
                        if (parent is null) break;
                        bases.Add(parent.Id);
                        folder = parent.ParentId;
                    }
                }
                else
                {
                    leaves.Add(item);
                }
            }
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

            // Mover a la MISMA carpeta del archivo no mueve nada: solo crearía un nombre
            // nuevo y, con la operación de mover, borraría el original sin necesidad. Se
            // bloquea de raíz antes de tocar el disco.
            if (operation == OperationType.Move)
            {
                var sameFolder = basis
                    .Where(item => item.Kind == ItemKind.File)
                    .FirstOrDefault(item =>
                    {
                        var parent = Path.GetDirectoryName(Path.GetFullPath(item.SourcePath));
                        return parent is not null &&
                               string.Equals(Path.GetFullPath(parent).TrimEnd('\\'),
                                   Path.GetFullPath(destination).TrimEnd('\\'),
                                   StringComparison.OrdinalIgnoreCase);
                    });
                if (sameFolder is not null)
                {
                    SetStatus("Para mover, elige una carpeta distinta de la de origen. " +
                              "Los archivos ya están ahí: no se ha tocado nada.");
                    PromptDestinationForList(_activeList!, sayNothingIfCancelled: true);
                    return;
                }
            }

            // Si el destino no es válido se ofrece qué hacer, en lugar de mostrar solo un error.
            var overlapConfirmed = false;
            while (true)
            {
                try
                {
                    destination = Path.GetFullPath(destination);
                    if (operation is OperationType.Copy or OperationType.Move &&
                        FindNestedSourceOverlap(destination, basis) is { } overlappingSource && !overlapConfirmed)
                    {
                        var choice = PromptDialog.Ask(this, _palette, "Origen y destino coinciden",
                            $"El destino es la carpeta de origen o está dentro de ella:\n{overlappingSource}\n\n" +
                            "Si continúas, XoPy creará una copia dentro de esa carpeta. ¿Qué quieres hacer?",
                            null, new[] { "Continuar", "Elegir otro destino…", "Cancelar" });
                        if (choice == 2) return;
                        if (choice == 1)
                        {
                            _pickDestination.Description = "Elige otra carpeta de destino";
                            if (_pickDestination.ShowDialog(this) != DialogResult.OK) return;
                            destination = _pickDestination.SelectedPath;
                            overlapConfirmed = false;
                            continue;
                        }
                        overlapConfirmed = true;
                    }
                    if (operation != OperationType.Test) Directory.CreateDirectory(destination);
                    if (!Directory.Exists(destination)) throw new DirectoryNotFoundException("La carpeta de destino no existe: " + destination);
                    break;
                }
                catch (Exception ex)
                {
                    var choice = PromptDialog.Ask(this, _palette,
                        "Carpeta de destino no válida",
                        "No se pudo usar la carpeta de destino:\n" + CopyEngine.Describe(ex) +
                        "\n\n¿Qué quieres hacer?",
                        null,
                        // Sin «usar carpeta predeterminada»: Descargas nunca se propone sola.
                        new[] { "Elegir otra carpeta…", "Crear la carpeta", "Cancelar" });
                    if (choice == 2) return;
                    if (choice == 1)
                    {
                        try { Directory.CreateDirectory(destination); }
                        catch { /* se vuelve a preguntar */ }
                        continue;
                    }

                    try
                    {
                        if (Directory.Exists(destination)) _pickDestination.SelectedPath = destination;
                    }
                    catch { }
                    _pickDestination.Description = "Elige otra carpeta de destino";
                    if (_pickDestination.ShowDialog(this) != DialogResult.OK) return;
                    destination = _pickDestination.SelectedPath;
                    overlapConfirmed = false;
                }
            }

            _destination.Text = destination;
            _settings.Destination = destination;
            _store.Save(_settings);
        }

        var additionalDestinations = ExtraDestinations(operation);
        if (operation is OperationType.Copy or OperationType.Move)
        {
            foreach (var extraDestination in additionalDestinations)
            {
                var overlappingSource = FindNestedSourceOverlap(extraDestination, basis);
                if (overlappingSource is null) continue;
                var choice = PromptDialog.Ask(this, _palette, "Origen y destino coinciden",
                    $"El destino adicional es la carpeta de origen o está dentro de ella:\n{overlappingSource}\n\n" +
                    "Si continúas, XoPy creará una copia dentro de esa carpeta. ¿Qué quieres hacer?",
                    null, new[] { "Continuar", "Cancelar" });
                if (choice != 0) return;
            }
        }

        var required = leaves.Sum(item => Math.Max(0, item.SizeBytes));
        _freeSpaceMode = FreeSpaceMode.Ignore;
        // La comprobación de espacio se puede desactivar en Preferencias (como en TeraCopy).
        if (_settings.CheckFreeSpace && operation is OperationType.Copy or OperationType.Move
            && required > 0 && !ConfirmFreeSpace(destination, required)) return;

        // «Copiar solo lo que quepa»: se descartan los archivos que no entrarían, en vez de
        // empezar y dejar media copia fallida.
        if (_freeSpaceMode == FreeSpaceMode.FitOnly)
        {
            var free = FreeBytesFor(destination);
            if (free > 0)
            {
                var budget = free;
                var kept = new List<TransferItem>();
                foreach (var leaf in leaves)
                {
                    var size = Math.Max(0, leaf.SizeBytes);
                    if (size > budget) continue;
                    budget -= size;
                    kept.Add(leaf);
                }
                var dropped = leaves.Except(kept).ToList();
                foreach (var leaf in dropped) leaf.State = ItemState.Skipped;
                leaves = kept;
                SetStatus($"No caben {dropped.Count} archivo(s): se copian solo los que entran.");
            }
        }
        _freeSpaceMode = FreeSpaceMode.Ignore;

        // En MOVER se guarda el origen antes de tocar el disco: si algo va mal y el usuario
        // necesita recuperar algo, queda una lista con las rutas exactas en
        // %LOCALAPPDATA%\XoPy\mover-*.txt (ubicación de los archivos que se van a mover).
        if (operation == OperationType.Move)
        {
            try
            {
                Directory.CreateDirectory(_store.DataFolder);
                var listPath = Path.Combine(_store.DataFolder, $"mover-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                var lines = leaves
                    .Where(item => item.Kind is ItemKind.File or ItemKind.DirectoryEntry)
                    .Select(item => item.SourcePath + " -> " + Path.Combine(destination, item.RelativePath))
                    .ToList();
                lines.Insert(0, "# XoPy: archivos que se van a mover. Guárdalo para poder recuperarlos.");
                File.WriteAllLines(listPath, lines);
            }
            catch { }
        }

        // Un elemento «Completed» se vuelve a copiar si su archivo YA NO ESTÁ en el destino
        // (por ejemplo, porque el usuario lo borró): antes se decía «ya está copiado» y no
        // había forma de volver a copiarlo desde XoPy. Solo se salta lo que de verdad sigue
        // estando en el destino, y nunca se reprocesa un origen que ya no existe (eso es lo
        // que provocaba el «Error al 0 %» de la captura 15).
        var skippedAlreadyThere = 0;
        var effective = new List<TransferItem>(leaves.Count);
        foreach (var leaf in leaves)
        {
            if (leaf.State == ItemState.Completed)
            {
                if (!File.Exists(leaf.SourcePath) && !Directory.Exists(leaf.SourcePath))
                {
                    leaf.Error = "El archivo de origen ya no existe (se movió o se borró). Añádelo de nuevo.";
                    continue;
                }
                var expected = Path.Combine(destination, leaf.RelativePath);
                if (string.Equals(leaf.DestinationPath, expected, StringComparison.OrdinalIgnoreCase) && File.Exists(expected))
                {
                    skippedAlreadyThere++;
                    continue;
                }
            }
            effective.Add(leaf);
        }
        leaves = effective;
        if (leaves.Count == 0)
        {
            SetStatus(skippedAlreadyThere > 0
                ? $"{skippedAlreadyThere} archivo(s) ya están en el destino. Bórralos del destino para volver a copiarlos."
                : "No hay archivos que se puedan copiar: su origen ya no existe. Añádelos de nuevo.");
            SyncRows();
            return;
        }
        if (skippedAlreadyThere > 0)
            SetStatus($"{skippedAlreadyThere} archivo(s) ya estaban en el destino; se copian los demás.");

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

        // Las carpetas quedan como contenedor; su estado lo decide el resultado de sus hijos.
        foreach (var folder in _items.Where(item => item.Kind == ItemKind.Folder && item.State != ItemState.Completed))
        {
            folder.Operation = operation;
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
            Parallelism = _settings.MultiThreadCopy ? _settings.Parallelism : 1,
            BufferSize = _settings.BufferKiB * 1024,
            SpeedLimitBytesPerSecond = _settings.SpeedLimitMiB * 1024L * 1024L,
            PreserveTimestamps = _settings.PreserveTimestamps,
            UseRecycleBin = _settings.UseRecycleBin,
            CopyLockedFiles = _settings.CopyLockedFiles,
            Engine = _engineOverride ?? _settings.Engine,
            ResumePartials = _settings.ResumePartials,
            SecureDeletePasses = _settings.SecureDeletePasses,
            Destinations = additionalDestinations
        };

        _lastOperation = operation;
        _lastDestination = destination;
        SaveQueueFile(_store.LastQueueFile, silent: true);
        _footerGraph.Reset();
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
            var missing = targets.Count(item => item.State == ItemState.Failed && item.Error?.Contains("ya no existe") == true);
            SetStatus(missing > 0
                ? $"No se pudo transferir: {missing} archivo(s) de origen ya no existen (se movieron antes). Añádelos de nuevo."
                : "No hay archivos pendientes que transferir.");
            UpdateButtons();
            return;
        }

        // Al empezar una copia la ventana pasa sola a su versión reducida (como TeraCopy):
        // antes había que pulsar «Ocultar» a mano y el usuario veía la ventana completa.
        // Solo si el usuario no la había dejado ya reducida, para no pelearse con su elección.
        if (!_compactView && !IsDisposed && IsHandleCreated)
        {
            try { ApplyCompactView(true, persist: false); } catch { }
        }
        // Si el arranque fue en la bandeja y el usuario acaba de lanzar una copia desde el
        // Explorador, la ventana debe aparecer para que vea el progreso.
        if (!Visible)
        {
            try { RestoreWindow(); } catch { }
        }

        _wasRunning = true;
        _pauseButton.Text = "Pausar";
        _pauseButton.Image = Icons.Glyph(Icons.Pause, _palette.Text, 18);
        if (_trayPauseItem is not null) _trayPauseItem.Text = "Pausar";
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

    private static string? FindNestedSourceOverlap(string destination, IReadOnlyCollection<TransferItem> basis)
    {
        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        var normalizedDestination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        foreach (var sourceFolder in basis.Where(item => item.Kind == ItemKind.Folder))
        {
            var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceFolder.SourcePath));
            var childPrefix = source.EndsWith(Path.DirectorySeparatorChar) || source.EndsWith(Path.AltDirectorySeparatorChar)
                ? source
                : source + Path.DirectorySeparatorChar;
            if (normalizedDestination.Equals(source, comparison) || normalizedDestination.StartsWith(childPrefix, comparison))
                return source;
        }
        return null;
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

    /// <summary>
    /// Refleja el estado del multihilo en el botón: activado se ve con fondo de acento y el
    /// icono en blanco; desactivado queda apagado. Solo cambiar el color del texto no se
    /// distinguía a simple vista y el usuario no sabía si estaba activado.
    /// </summary>
    private void UpdateMultiThreadButton()
    {
        if (_multiThreadButton is null) return;
        _multiThreadButton.Text = MultiThreadLabel();
        _multiThreadButton.ToolTipText = MultiThreadTooltip();
        // Activado: fondo de acento y rayo blanco; desactivado: apagado. Además del color, el
        // TEXTO cambia («Multihilo · N» / «Un archivo a la vez»), que se ve sin depender del tema.
        _multiThreadButton.BackColor = _settings.MultiThreadCopy ? _palette.Accent : _palette.Surface;
        _multiThreadButton.ForeColor = _settings.MultiThreadCopy ? Color.White : _palette.Muted;
        _multiThreadButton.Image = Icons.Glyph(Icons.Lightning,
            _settings.MultiThreadCopy ? Color.White : _palette.Muted, 18);
    }

    private string MultiThreadLabel() =>
        _settings.MultiThreadCopy ? $"Multihilo · {_settings.Parallelism}" : "Un archivo a la vez";

    private string MultiThreadTooltip() => _settings.MultiThreadCopy
        ? $"Multihilo activado: {_settings.Parallelism} archivos copiándose a la vez. Pulsa para copiar de uno en uno (el número de hilos se ajusta en Preferencias)."
        : "Un archivo a la vez. Pulsa para activar el multihilo (el número de hilos se ajusta en Preferencias).";

    /// <summary>Activa o desactiva el multihilo con un solo clic y refleja el cambio.</summary>
    private void ToggleMultiThread()
    {
        _settings.MultiThreadCopy = !_settings.MultiThreadCopy;
        _store.Save(_settings);
        UpdateMultiThreadButton();
        SetStatus(_settings.MultiThreadCopy
            ? $"Multihilo activado ({_settings.Parallelism} archivos a la vez)."
            : "Modo secuencial: un archivo a la vez.");
    }



    private void ToggleSelectedFilePause()
    {
        if (!_engine.IsRunning) { SetStatus("No hay una transferencia en curso."); return; }
        var files = SelectedItems()
            .SelectMany(item => item.Kind == ItemKind.Folder ? Descendants(item) : new[] { item })
            .Where(item => item.Kind == ItemKind.File)
            .DistinctBy(item => item.Id)
            .ToList();
        if (files.Count == 0) { SetStatus("Selecciona uno o varios archivos en la lista."); return; }

        var pause = files.Any(item => !_engine.IsItemPaused(item) && item.State is not (ItemState.Completed or ItemState.Failed or ItemState.Skipped or ItemState.Cancelled));
        foreach (var item in files)
        {
            if (pause) _engine.PauseItem(item);
            else _engine.ResumeItem(item);
        }
        SetStatus(pause ? $"Pausando {files.Count} archivo(s) seleccionado(s)…" : $"Reanudando {files.Count} archivo(s)…");
        SyncRows();
        UpdateFooter();
    }

    private void UpdateListMenuState()
    {
        if (_listMenu is null) return;
        var pauseItem = _listMenu.Items.Find("pause-single-file", false).FirstOrDefault();
        if (pauseItem is null) return;
        var files = SelectedItems().SelectMany(item => item.Kind == ItemKind.Folder ? Descendants(item) : new[] { item })
            .Where(item => item.Kind == ItemKind.File).DistinctBy(item => item.Id).ToList();
        var canPause = _engine.IsRunning && files.Any(item => item.State is not (ItemState.Completed or ItemState.Failed or ItemState.Skipped or ItemState.Cancelled));
        pauseItem.Enabled = canPause;
        pauseItem.Text = files.Count > 0 && files.All(_engine.IsItemPaused)
            ? "Reanudar archivo(s) seleccionado(s)"
            : "Pausar archivo(s) seleccionado(s)";
    }

    private void ListItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Item is not ListViewItem row || row.Tag is not TransferItem item) return;
        var payload = new DataObject();
        payload.SetData("XoPy.QueueItem", item.Id.ToString("D"));
        _list.DoDragDrop(payload, DragDropEffects.Move);
    }

    private void ListDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent("XoPy.QueueItem") == true)
            e.Effect = DragDropEffects.Move;
        else if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
        else
            e.Effect = DragDropEffects.None;
    }

    private void ListDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent("XoPy.QueueItem") == true)
        {
            if (e.Data.GetData("XoPy.QueueItem") is string idText && Guid.TryParse(idText, out var id))
            {
                var point = _list.PointToClient(new Point(e.X, e.Y));
                ReorderByDrop(id, point);
            }
            return;
        }
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) DropPaths(e);
    }

    private void ReorderByDrop(Guid draggedId, Point point)
    {
        var source = _items.FirstOrDefault(item => item.Id == draggedId);
        var targetRow = _list.HitTest(point).Item;
        var target = targetRow?.Tag as TransferItem;
        if (source is null || target is null || ReferenceEquals(source, target))
            return;

        // Arrastrar a OTRA carpeta de la lista mueve el elemento a ese grupo: cambia
        // ParentId y Depth de todo el bloque (archivo o carpeta con su contenido). Es
        // un reordenamiento de la cola, no un cambio de carpeta de destino en el disco.
        if (source.ParentId != target.ParentId)
        {
            // Si se suelta sobre un archivo, el bloque va al mismo grupo que ese archivo.
            var newParent = target.Kind == ItemKind.Folder
                ? target
                : target.ParentId is { } parentId ? _items.FirstOrDefault(item => item.Id == parentId) : null;
            if (newParent is not null && (ReferenceEquals(newParent, source) || IsAncestor(source, newParent)))
            {
                SetStatus("No se puede mover una carpeta dentro de sí misma.");
                return;
            }
            Reparent(source, target);
            return;
        }

        var parent = source.ParentId is null ? null : _items.FirstOrDefault(item => item.Id == source.ParentId.Value);
        var siblings = parent is null ? RootItems() : Children(parent);
        var oldIndex = siblings.IndexOf(source);
        var targetIndex = siblings.IndexOf(target);
        if (oldIndex < 0 || targetIndex < 0) return;
        var after = point.Y >= targetRow!.Bounds.Top + targetRow.Bounds.Height / 2;
        var desired = targetIndex + (after ? 1 : 0);
        if (oldIndex < desired) desired--;
        while (oldIndex != desired)
        {
            var delta = desired < oldIndex ? -1 : 1;
            if (!MoveBlock(source, delta)) break;
            siblings = parent is null ? RootItems() : Children(parent);
            oldIndex = siblings.IndexOf(source);
        }
        RebuildRows();
        if (_rows.TryGetValue(source.Id, out var row)) row.Selected = true;
        SetStatus(source.Kind == ItemKind.Folder
            ? "Carpeta reordenada junto con todo su contenido."
            : "Archivo reordenado dentro de su carpeta.");
    }

    /// <summary>¿<paramref name="candidate"/> está dentro de <paramref name="ancestor"/>?</summary>
    private bool IsAncestor(TransferItem candidate, TransferItem node)
    {
        var current = node.ParentId;
        var guard = 0;
        while (current is not null && guard++ < 512)
        {
            if (current.Value == candidate.Id) return true;
            current = _items.FirstOrDefault(item => item.Id == current.Value)?.ParentId;
        }
        return false;
    }

    /// <summary>
    /// Pasa <paramref name="source"/> (con todo su contenido) al grupo de <paramref name="target"/>
    /// y lo coloca justo donde se soltó. Sirve para pasar de la raíz a una subcarpeta y al revés.
    /// </summary>
    private void Reparent(TransferItem source, TransferItem target)
    {
        var newParent = target.Kind == ItemKind.Folder
            ? target
            : target.ParentId is { } parentId ? _items.FirstOrDefault(item => item.Id == parentId) : null;
        var depthDelta = (newParent?.Depth + 1 ?? 0) - source.Depth;

        // Se recogen los descendientes ANTES de tocar los parentescos.
        var block = new List<TransferItem> { source };
        block.AddRange(Descendants(source));

        source.ParentId = newParent?.Id;
        foreach (var node in block)
            node.Depth = Math.Max(0, node.Depth + depthDelta);

        // La ruta relativa manda en la ruta de destino: se recalcula con los nombres reales
        // para que el bloque se copie dentro de la carpeta nueva y no en la de antes.
        var origin = _destination?.Text.Trim() ?? string.Empty;
        void Repath(TransferItem node)
        {
            var parent = node.ParentId is { } pid ? _items.FirstOrDefault(item => item.Id == pid) : null;
            var name = node.Name;
            if (string.IsNullOrEmpty(name)) return;
            node.RelativePath = parent is null || string.IsNullOrWhiteSpace(parent.RelativePath)
                ? name
                : Path.Combine(parent.RelativePath, name);
            if (node.Kind == ItemKind.File && origin.Length > 0)
                node.DestinationPath = Path.Combine(origin, node.RelativePath);
            foreach (var child in Children(node)) Repath(child);
        }
        Repath(source);

        // Se recoloca el bloque completo junto al destino, en el orden de _items.
        var blockIds = block.Select(item => item.Id).ToHashSet();
        var remaining = _items.Where(item => !blockIds.Contains(item.Id)).ToList();
        var targetIndex = remaining.IndexOf(target);
        if (targetIndex < 0) targetIndex = remaining.Count;
        remaining.InsertRange(targetIndex, block);

        _items.Clear();
        _items.AddRange(remaining);
        ResetVisualOrder();
        RebuildRows();

        if (newParent is not null && !newParent.IsExpanded)
        {
            newParent.IsExpanded = true;
            RebuildRows();
        }
        if (_rows.TryGetValue(source.Id, out var row)) row.Selected = true;
        SetStatus(newParent is null
            ? $"«{source.Name}» movido a la raíz de la lista."
            : $"«{source.Name}» movido dentro de «{newParent.Name}».");
        UpdateFooter();
    }

    private void TogglePause()
    {
        if (!_engine.IsRunning) return;
        var paused = !_engine.IsPaused;
        _engine.SetPaused(paused);
        _pauseButton.Text = paused ? "Reanudar" : "Pausar";
        _pauseButton.Image = Icons.Glyph(paused ? Icons.Play : Icons.Pause, _palette.Text, 18);
        if (_trayPauseItem is not null) _trayPauseItem.Text = paused ? "Reanudar" : "Pausar";
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

    /// <summary>Espacio libre en la unidad del destino (0 si no se puede saber).</summary>
    private static long FreeBytesFor(string destination)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(destination));
            if (string.IsNullOrEmpty(root)) return 0;
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : 0;
        }
        catch
        {
            return 0;
        }
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

            var message = $"En el destino quedan {Format.Bytes(drive.AvailableFreeSpace)} libres y necesitas {Format.Bytes(requiredBytes)}.\n\n" +
                          "Elige cómo continuar:";
            // Antes solo había Sí/No. Ahora se ofrecen las salidas reales, como TeraCopy:
            // continuar (fallarán los archivos que no quepan), copiar solo lo que quepa o
            // cancelar para liberar espacio.
            var choice = PromptDialog.Ask(this, _palette, "Espacio insuficiente", message,
                $"Faltan {Format.Bytes(requiredBytes - drive.AvailableFreeSpace)}.",
                new[] { "Continuar de todos modos", "Copiar solo lo que quepa", "Cancelar" });
            // 0 = continuar · 1 = solo lo que quepa · 2 (o cerrar) = cancelar.
            _freeSpaceMode = choice switch
            {
                0 => FreeSpaceMode.Ignore,
                1 => FreeSpaceMode.FitOnly,
                _ => FreeSpaceMode.Cancel
            };
            return _freeSpaceMode != FreeSpaceMode.Cancel;
        }
        catch
        {
            return true;
        }
    }

    private ConflictChoice ResolveConflict(ConflictPrompt prompt)
    {
        if (!IsHandleCreated || IsDisposed) return ConflictChoice.Rename;

        // Si el usuario (o un script) fijó una política explícita —«--rename», «--overwrite»,
        // «--skip»— se aplica sin preguntar. Antes, al pegar en la misma carpeta salía un
        // diálogo modal aunque se hubiera pedido «renombrar»: en modo desatendido se quedaba
        // esperando para siempre y en el Explorador parecía que no hacía nada.
        var samePath = string.Equals(Path.GetFullPath(prompt.SourcePath), Path.GetFullPath(prompt.TargetPath),
            StringComparison.OrdinalIgnoreCase);
        if (samePath && _settings.Conflict != ConflictPolicy.Ask)
        {
            return _settings.Conflict switch
            {
                ConflictPolicy.Rename => ConflictChoice.Rename,
                ConflictPolicy.Skip => ConflictChoice.Skip,
                ConflictPolicy.Overwrite => ConflictChoice.Rename, // nunca sobrescribir el origen
                _ => ConflictChoice.Rename
            };
        }
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
        // Como el Explorador de Windows: pegar donde ya está el archivo crea una copia con otro
        // nombre, sin preguntar y sin tocar el original. Antes salía un diálogo por cada archivo.
        var samePath = string.Equals(Path.GetFullPath(prompt.SourcePath), Path.GetFullPath(prompt.TargetPath),
            StringComparison.OrdinalIgnoreCase);
        if (samePath) return ConflictChoice.Rename;

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
        if (_trayPauseItem is not null) _trayPauseItem.Text = "Pausar";

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
        if (IsHandleCreated) Taskbar.Clear(Handle);

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
        {
            CleanupMovedSourceFolders();
            // Tras un «cortar y pegar» el portapapeles se queda con los archivos ya movidos:
            // el Explorador los seguía mostrando como pendientes de pegar. Se vacía, como
            // hace Windows al terminar el movimiento.
            try
            {
                if (ShellPasteHook.ClipboardIsCut()) Clipboard.Clear();
            }
            catch { }
        }

        if (_followUpOperation is { } followUp)
        {
            _followUpOperation = null;
            if (failed == 0 && cancelled == 0 && _items.Any(item => item.Kind != ItemKind.Folder && item.State != ItemState.Completed))
            {
                try { BeginInvoke(new Action(() => StartTransfer(followUp))); } catch { }
            }
        }

        if (_settings.OpenDestinationWhenDone && failed == 0 && cancelled == 0 && _lastOperation is OperationType.Copy or OperationType.Move)
            OpenFolder(string.IsNullOrWhiteSpace(_lastDestination) ? _destination.Text.Trim() : _lastDestination);

        if (failed == 0 && cancelled == 0 && _settings.ShutdownWhenDone)
        {
            try { Process.Start(new ProcessStartInfo("shutdown", "/s /t 30") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
        }

        // En modo captura el cierre lo controla CaptureScreenshot, justo después del PNG.
        if (_startupRequest.Screenshot) return;

        // ---exit: proceso de un solo uso (automatizaciones e integraciones).
        // Se espera a que TODOS los destinos estén escritos: salir en cuanto acaba la copia
        // principal dejaba el segundo destino a medias.
        if (_startupRequest.ExitWhenDone)
        {
            _ = Task.Run(async () =>
            {
                for (var waited = 0; waited < 120000 && _engine.IsRunning; waited += 200)
                    await Task.Delay(200);
                await Task.Delay(800);
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        try { _exitRequested = true; } catch { }
                        try { Application.Exit(); } catch { }
                        Environment.Exit(0);
                    }));
                }
                catch { Environment.Exit(0); }
            });
            return;
        }

        // «Cerrar al terminar» solo cierra la VENTANA de la copia; el proceso, el icono de la
        // bandeja y el interceptor de Ctrl+V siguen activos para las próximas copias. Antes se
        // hacía Application.Exit(), que mataba la aplicación entera y dejaba de funcionar.
        if (failed == 0 && cancelled == 0 && _settings.CloseWhenDone && _lastOperation is OperationType.Copy or OperationType.Move)
        {
            BeginInvoke(new Action(() => HideAfterTransfer()));
        }
    }

    /// <summary>
    /// Oculta la ventana al terminar una copia dejando XoPy en marcha: el icono de la bandeja,
    /// el interceptor de Ctrl+V y la escucha IPC siguen activos para las próximas copias.
    /// Antes esto llamaba a Application.Exit() y la aplicación dejaba de responder.
    /// </summary>
    private void HideAfterTransfer()
    {
        try
        {
            SaveWindowGeometry();
            // Si el usuario no quiere bandeja, la ventana se minimiza al terminar: ocultarla
            // sin icono la dejaba inaccesible y parecía que se «reabría» sola.
            if (_settings.MinimizeToTray)
            {
                EnsureTrayIcon();
                Hide();
                ShowInTaskbar = false;
            }
            else
            {
                WindowState = FormWindowState.Minimized;
                ShowInTaskbar = true;
            }
            SetStatus("Copia terminada. XoPy sigue en marcha para las próximas copias.");
        }
        catch (Exception ex)
        {
            FileLog.Write("No se pudo ocultar la ventana: " + ex.Message);
        }
    }

    /// <summary>
    /// Tras mover, elimina las carpetas de origen que se hayan quedado TOTALMENTE vacías.
    /// Solo se hace cuando no hubo ningún error y la carpeta no contiene nada: con un
    /// destino equivocado o un fallo a medias, borrar la carpeta dejaba al usuario sin el
    /// original y sin copia donde esperaba. Ante la duda, no se borra nada.
    /// </summary>
    private void CleanupMovedSourceFolders()
    {
        if (_items.Any(item => item.State == ItemState.Failed)) return;
        foreach (var folder in RootItems().Where(item => item.Kind == ItemKind.Folder))
        {
            try
            {
                if (!Directory.Exists(folder.SourcePath)) continue;
                // Si la carpeta aún tiene CUALQUIER archivo (aunque sea de otra lista), se deja.
                if (Directory.EnumerateFiles(folder.SourcePath, "*", SearchOption.AllDirectories).Any()) continue;
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
                    FileLog.Write("Carpeta de origen vacía eliminada tras mover: " + folder.SourcePath);
                }
            }
            catch { }
        }
        SyncRows();
    }

    // ---------------------------------------------------------------- temporizador

    private void ToggleJobsPanel() => SetJobsVisible(_jobsCollapsed, persist: true);

    /// <summary>
    /// Muestra u oculta el historial de listas de copia. El estado se guarda en los ajustes
    /// y también hay un botón en la cabecera del panel (y en el menú Ver).
    /// </summary>
    private void SetJobsVisible(bool visible, bool persist)
    {
        _jobsVisible = visible;
        _jobsCollapsed = !visible;
        try
        {
            if (_contentGrid is not null && _contentGrid.ColumnStyles.Count > 0)
                _contentGrid.ColumnStyles[0].Width = visible ? 228 : 0;
            if (_jobsPanel is not null) _jobsPanel.Visible = visible;
            // « y » son caracteres de texto, no del set de iconos: se cambia el TEXTO del
            // botón en lugar de generar la imagen con la fuente de iconos (que no los tiene).
            if (_jobsToggleButton is not null)
            {
                _jobsToggleButton.Text = visible ? "\u00AB" : "\u00BB";
                // Button no tiene ToolTipText (eso es de ToolStripButton): se usa el nombre
                // accesible, que es lo que lee el lector de pantalla y lo que cambia de sentido.
                _jobsToggleButton.AccessibleName = visible
                    ? "Ocultar el historial de listas de copia"
                    : "Mostrar el historial de listas de copia";
            }
            if (_collapseButton is not null) _collapseButton.Text = visible ? "\u00AB" : "\u00BB";
        }
        catch { }
        if (persist)
        {
            _settings.ShowJobs = visible;
            _settings.CollapseJobs = !visible;
            _store.Save(_settings);
            SetStatus(visible ? "Historial de listas visible." : "Historial de listas oculto.");
        }
    }

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

        // Barra de progreso y porcentaje de la barra de tareas de Windows.
        try
        {
            if (IsHandleCreated && !IsDisposed)
            {
                if (_engine.IsRunning)
                {
                    var percent = (int)Math.Clamp(Math.Round(_lastPercent), 0, 100);
                    var paused = _engine.IsPaused;
                    var error = _items.Any(item => item.State == ItemState.Failed);
                    Taskbar.SetProgress(Handle, percent, paused, error, true);
                    Taskbar.SetOverlay(Handle, percent, paused, error, _settings.DarkTheme);
                }
                else
                {
                    Taskbar.Clear(Handle);
                }
            }
        }
        catch { }

        if (!_engine.IsRunning && _completedAt != DateTime.MinValue && (DateTime.Now - _completedAt).TotalSeconds > 6)
        {
            _completedAt = DateTime.MinValue;
            RestoreBaseTrayIcon();
        }
    }

    private void UpdateTitle()
    {
        var percent = _lastPercent;
        var speed = Format.Speed(_engine.SpeedBytesPerSecond);
        Text = _engine.IsPaused
            ? $"XoPy — en pausa · {percent:0.0} %"
            : $"XoPy — {percent:0.0} % ({speed})";
    }

    private void UpdateTrayState()
    {
        var percent = (int)Math.Clamp(Math.Round(_lastPercent), 0, 100);
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
        _testButton.Enabled = !running && _items.Count > 0;
        _pauseButton.Enabled = running;
        _skipButton.Enabled = running;
        _stopButton.Enabled = running;
        _verifyCheck.Enabled = !running;
        // Reordenar y quitar de la LISTA se permite siempre: el motor trabaja con su propia
        // instantánea, así que no cancela ni corrompe la copia. Antes se deshabilitaban con
        // cualquier transferencia en curso y parecía que los botones «no funcionaban».
        _removeButton.Enabled = _items.Count > 0;
        _clearButton.Enabled = _items.Count > 0;
        _upButton.Enabled = _items.Count > 0;
        _downButton.Enabled = _items.Count > 0;
        _destination.ReadOnly = running;
        if (_trayPauseItem is not null) _trayPauseItem.Enabled = running;
    }

    /// <summary>
    /// Rellena el bloque de metadatos de la pestaña Lista (estilo TeraCopy): cuántos archivos
    /// hay, de qué carpeta vienen y a qué carpeta van, con el tamaño al lado.
    /// </summary>
    private void UpdateMetaBlock(List<TransferItem> leaves, long done, long total)
    {
        if (_metaFilter is null) return;

        // «Archivos» describe QUÉ hay en la lista (filtro/selección), y la columna derecha
        // solo muestra la velocidad cuando hay copia en marcha. El tamaño total ya está en el
        // pie, así que repetirlo aquí era otro duplicado de la captura 18.
        var files = leaves.Count(item => item.Kind == ItemKind.File);
        _metaFilter.Text = files == 0
            ? "Ninguno"
            : files == 1 ? "1 archivo listo para copiar" : $"{files} archivos listos para copiar";
        _metaCount.Text = _engine.IsRunning ? Format.Bytes((long)Math.Max(0, _engine.SpeedBytesPerSecond)) + "/s" : string.Empty;

        // Origen: la carpeta común de los elementos añadidos (o el origen del archivo actual).
        var roots = RootItems().Select(item => item.SourcePath).Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        var origin = roots.Count switch
        {
            0 => "Sin origen",
            1 => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(roots[0])) ?? roots[0],
            _ => $"{roots.Count} orígenes distintos"
        };
        _metaOrigin.Text = origin;
        _metaOriginSize.Text = total > 0 ? Format.Bytes(total) : "0 B";

        var destination = _destination?.Text.Trim() ?? string.Empty;
        _metaDestination.Text = string.IsNullOrWhiteSpace(destination) ? "Sin destino: elige una carpeta" : destination;
        _metaDestination.ForeColor = string.IsNullOrWhiteSpace(destination) ? _palette.Warning : _palette.Text;
        _metaDestinationSize.Text = _activeList?.DisplayName ?? string.Empty;
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

        _lastPercent = percent;
        UpdateMetaBlock(leaves, done, total);
        UpdateCountLabel(leaves.Count, total);
        _progress.Value = percent;
        _progress.FillColor = _items.Any(item => item.State == ItemState.Failed) && !_engine.IsRunning
            ? _palette.Warning
            : _palette.Accent;
        // Sin esto, en reposo la barra se quedaba en blanco aunque hubiera porcentaje.
        _progress.Invalidate();

        _summaryLabel.Text = leaves.Count == 0
            ? "Sin archivos en la lista"
            : $"{leaves.Count} archivo(s) · {Format.Bytes(done)} de {Format.Bytes(total)} · {percent:0.0} %";

        if (_engine.IsRunning && _engine.IsPaused)
        {
            _speedLabel.Text = "En pausa · 0 B/s";
        }
        else if (_engine.IsRunning)
        {
            var speed = _engine.SpeedBytesPerSecond;
            var remaining = _engine.Remaining;
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
        if (_engine.IsRunning) _footerGraph?.Add(_engine.SpeedBytesPerSecond);
        UpdateStatusTab(percent, done, total, runningLeaf?.RelativePath ?? string.Empty);
    }

    // ---------------------------------------------------------------- cuadros de diálogo

    /// <summary>
    /// Pregunta la carpeta de destino de una lista concreta. Nunca propone una ruta
    /// guardada antes: el usuario elige. Devuelve true si quedó un destino válido.
    /// </summary>
    private bool PromptDestinationForList(TransferList list, bool sayNothingIfCancelled = false)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = $"¿A qué carpeta quieres copiar los elementos de «{list.DisplayName}»?",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = string.Empty
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            if (!sayNothingIfCancelled)
                SetStatus("Sin destino: elige una carpeta para poder copiar.");
            return false;
        }

        var path = dialog.SelectedPath?.Trim() ?? string.Empty;
        if (path.Length == 0) return false;
        try { Directory.CreateDirectory(path); } catch { }

        list.Destination = path;
        if (list == _activeList) _destination.Text = path;
        _settings.Destination = path;
        _store.Save(_settings);
        UpdateTargetTab();
        SetStatus("Destino: " + path);
        return true;
    }

    /// <summary>
    /// Rellena «Enviar a» con las carpetas favoritas y los destinos adicionales de las
    /// preferencias, para cambiar el destino de lo seleccionado sin salir de la lista.
    /// </summary>
    private void RefreshSendToMenu()
    {
        var sendTo = _listMenu.Items.Find("send-to", false).FirstOrDefault() as ToolStripMenuItem;
        if (sendTo is null) return;

        // Se reconstruye cada vez que se abre el menú para reflejar los favoritos actuales.
        while (sendTo.DropDownItems.Count > 2) sendTo.DropDownItems.RemoveAt(2);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in _settings.Favorites.Concat(_settings.ExtraDestinations))
        {
            if (string.IsNullOrWhiteSpace(folder) || !seen.Add(folder)) continue;
            var target = folder;
            sendTo.DropDownItems.Add(new ToolStripMenuItem(ShortPath(target), null,
                (_, _) => SendSelectionTo(target)) { ToolTipText = target });
        }

        // Destinos recientes del historial: así «Enviar a» funciona aunque el usuario no haya
        // guardado ningún favorito (era el caso en el que el menú salía vacío y parecía roto).
        var recent = _history
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Destination) && Directory.Exists(entry.Destination))
            .Select(entry => entry.Destination)
            .Where(folder => seen.Add(folder))
            .Take(5)
            .ToList();
        if (recent.Count > 0)
        {
            sendTo.DropDownItems.Add(new ToolStripSeparator());
            foreach (var folder in recent)
            {
                var target = folder;
                sendTo.DropDownItems.Add(new ToolStripMenuItem("Reciente: " + ShortPath(target), null,
                    (_, _) => SendSelectionTo(target)) { ToolTipText = target });
            }
        }

        if (sendTo.DropDownItems.Count == 2)
        {
            var empty = new ToolStripMenuItem("(No hay carpetas favoritas: usa «Elegir destino…»)") { Enabled = false };
            sendTo.DropDownItems.Add(empty);
        }
    }

    /// <summary>Cambia el destino de los elementos seleccionados a <paramref name="folder"/>.</summary>
    private void SendSelectionTo(string? folder)
    {
        var selected = SelectedItems().ToList();
        if (selected.Count == 0)
        {
            SetStatus("Selecciona primero los elementos que quieres enviar.");
            return;
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "¿A qué carpeta quieres enviar los elementos seleccionados?",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
                SelectedPath = string.Empty
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            folder = dialog.SelectedPath?.Trim();
            if (string.IsNullOrWhiteSpace(folder)) return;
        }

        var destination = folder!;
        try { Directory.CreateDirectory(destination); } catch { }

        // Se recogen TODOS los archivos afectados: los seleccionados y, si se seleccionó una
        // carpeta, todo su contenido. Antes, al seleccionar archivos sueltos de una subcarpeta
        // se les aplanaba la ruta («CARS\CHE\x.dds» pasaba a «x.dds») y el envío acababa
        // dejando los archivos sueltos en la raíz del destino o, con la lista plegada, sin
        // mover nada: eso era el «Enviar a no funciona» con el historial oculto.
        var roots = selected.Where(item => item.ParentId is null || !selected.Any(other => other.Id == item.ParentId)).ToList();
        // HashSet para no procesar dos veces el mismo archivo (si se seleccionó una carpeta y
        // además un archivo suelto dentro de ella, antes se recalculaba dos veces su destino).
        var seenLeaves = new HashSet<Guid>();
        foreach (var root in roots)
        {
            foreach (var leaf in root.Kind == ItemKind.Folder ? Descendants(root).Append(root) : new[] { root })
            {
                if (leaf.Kind != ItemKind.File) continue;
                if (!seenLeaves.Add(leaf.Id)) continue;
                // La ruta relativa se CONSERVA tal cual (incluye sus subcarpetas). Solo se
                // rellena con el nombre si viniera vacía.
                if (string.IsNullOrWhiteSpace(leaf.RelativePath)) leaf.RelativePath = leaf.Name;
                leaf.DestinationPath = Path.Combine(destination, leaf.RelativePath);
            }
        }

        if (_activeList is not null)
        {
            _activeList.Destination = destination;
            if (_destination is not null) _destination.Text = destination;
        }
        _settings.Destination = destination;
        _store.Save(_settings);
        RebuildRows();
        UpdateTargetTab();
        UpdateFooter();
        SetStatus($"Destino cambiado: {destination}");
    }

    private static string ShortPath(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        var name = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(name)) name = trimmed;
        var parent = Path.GetFileName(Path.GetDirectoryName(trimmed) ?? string.Empty);
        return string.IsNullOrEmpty(parent) ? name : parent + "\\" + name;
    }

    private void BrowseDestination()
    {
        if (PromptDestinationForList(_activeList ?? new TransferList { Name = "Lista de copia" }))
            SetStatus("Destino: " + _destination.Text.Trim());
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
        ApplyMenuBarSetting();
        ApplyCopierReplacement();
        SetJobsVisible(_settings.ShowJobs, persist: false);
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
        var legacy = ShellIntegration.HasLegacyEntries();
        var message = "XoPy ya NO añade acciones con su nombre ni accesos «Enviar a».\n\n" +
                      "La extensión de Windows añade una opción llamada «Pegar» que abre XoPy; además,\n" +
                      "Ctrl+V se intercepta cuando el portapapeles contiene archivos. Si venías de «Cortar»,\n" +
                      "se mueve y se borra el origen. En Windows 11 la opción de la extensión puede estar\n" +
                      "en «Mostrar más opciones». La opción de pegado incorporada por Windows no se\n" +
                      "modifica ni se elimina.\n\n" +
                      "Las entradas antiguas («Copiar con XoPy», «Mover con XoPy», «Pegar con XoPy»,\n" +
                      "«Verificar con XoPy», etc.) se borran automáticamente al abrir XoPy.\n\n" +
                      (legacy
                          ? "Todavía quedan entradas antiguas en el menú contextual.\n¿Quieres quitarlas ahora?"
                          : "No quedan entradas antiguas en el menú contextual.");

        var choice = PromptDialog.Ask(this, _palette,
            "Integración con el Explorador",
            message,
            null,
            legacy ? new[] { "Quitar entradas antiguas", "Cerrar" } : new[] { "Cerrar" });
        if (choice != 0 || !legacy) return;

        try
        {
            ShellIntegration.Cleanup();
            SetStatus("Entradas antiguas del menú contextual eliminadas.");
        }
        catch (Exception ex)
        {
            ShowError("No se pudieron quitar las entradas antiguas: " + ex.Message);
        }
    }

    /// <summary>Prueba automática de reordenar, ordenar y quitar de la lista sin tocar el disco.</summary>
    public string QueueSelfTest()
    {
        var report = new StringBuilder();
        _items.Clear();
        _rows.Clear();
        _list.Items.Clear();

        TransferItem Make(string name, long size, Guid? parent = null, int depth = 0) => new()
        {
            Kind = ItemKind.File,
            SourcePath = @"X:\inexistente\" + name,
            RelativePath = name,
            SizeBytes = size,
            SizeKnown = true,
            ParentId = parent,
            Depth = depth,
            State = ItemState.Pending
        };

        var folder = new TransferItem
        {
            Kind = ItemKind.Folder,
            SourcePath = @"X:\inexistente\Carpeta",
            RelativePath = "Carpeta",
            SizeBytes = 200,
            SizeKnown = true,
            State = ItemState.Pending
        };
        var child1 = Make("foto1.jpg", 120, folder.Id, 1);
        child1.RelativePath = Path.Combine("Carpeta", "foto1.jpg");
        var child2 = Make("foto2.jpg", 80, folder.Id, 1);
        child2.RelativePath = Path.Combine("Carpeta", "foto2.jpg");
        var otherFolder = new TransferItem
        {
            Kind = ItemKind.Folder,
            SourcePath = @"X:\inexistente\Otra",
            RelativePath = "Otra",
            SizeBytes = 40,
            SizeKnown = true,
            State = ItemState.Pending
        };
        var otherChild = Make("documento.txt", 40, otherFolder.Id, 1);
        otherChild.RelativePath = Path.Combine("Otra", "documento.txt");
        var a = Make("a.txt", 300);
        var b = Make("b.txt", 100);

        _items.AddRange(new[] { folder, child1, child2, otherFolder, otherChild, a, b });
        RebuildRows();

        // Arrastrar sólo reordena dentro del mismo padre: nunca cambia un archivo de carpeta.
        if (!MoveBlock(a, -1) || RootItems().IndexOf(a) != RootItems().IndexOf(b) - 1)
            report.AppendLine("FALLO: no se pudo reordenar un archivo dentro de su mismo nivel.");
        if (!Children(folder).SequenceEqual(new[] { child1, child2 }) || a.ParentId is not null || a.RelativePath != "a.txt" ||
            b.RelativePath != "b.txt")
            report.AppendLine("FALLO: el orden visual cambió la jerarquía o las rutas de transferencia.");
        if (child2.Depth != 1 || child2.ParentId != folder.Id || child2.RelativePath != Path.Combine("Carpeta", "foto2.jpg") ||
            otherChild.Depth != 1 || otherChild.ParentId != otherFolder.Id || otherChild.RelativePath != Path.Combine("Otra", "documento.txt"))
            report.AppendLine("FALLO: el orden visual cambió la jerarquía o rutas de transferencia.");

        SortQueue(QueueSortField.Size);
        var roots = RootItems();
        if (!roots.SequenceEqual(new[] { otherFolder, b, folder, a }))
            report.AppendLine("FALLO: ordenar por tamaño no conservó los grupos raíz.");
        // El tamaño de una carpeta debe ser el total de su contenido (120+80=200 y 40).
        if (!Children(folder).SequenceEqual(new[] { child2, child1 }))
            report.AppendLine("FALLO: ordenar por tamaño no ordenó el contenido interno de la carpeta.");

        SortQueue(QueueSortField.Name);
        if (!RootItems().SequenceEqual(new[] { a, b, folder, otherFolder }))
            report.AppendLine("FALLO: el orden por nombre no conservó los grupos raíz.");

        SortQueue(QueueSortField.Source);
        if (RootItems().Count != 4)
            report.AppendLine("FALLO: ordenar por carpeta de origen perdió elementos.");

        SortQueue(QueueSortField.Destination);
        if (RootItems().Count != 4)
            report.AppendLine("FALLO: ordenar por carpeta de destino perdió elementos.");

        var before = _items.Count;
        RemoveItems(new[] { child1 });
        if (_items.Any(item => item.Id == child1.Id) || _items.Count != before - 1)
            report.AppendLine("FALLO: quitar de la lista no eliminó solo el elemento seleccionado.");
        if (_items.Count(item => item.Kind == ItemKind.File) != 4)
            report.AppendLine("FALLO: el recuento de archivos tras quitar es incorrecto.");
        if (_items.Any(item => File.Exists(item.SourcePath)))
            report.AppendLine("FALLO: la prueba tocó archivos reales.");

        _items.Clear();
        _rows.Clear();
        _list.Items.Clear();
        return report.Length == 0
            ? "OK: reordenar, ordenar y quitar de la lista funcionan y no borran archivos."
            : report.ToString();
    }

    private void ShowHistory()
    {
        _history = _store.LoadHistory();
        using var dialog = new HistoryDialog(_history, _palette);
        dialog.ShowDialog(this);
        if (dialog.Modified)
        {
            _store.SaveHistory(dialog.Entries);
            _history = _store.LoadHistory();
            SetStatus($"Historial actualizado: {_history.Count} entrada(s).");
        }
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
        FileLog.Write("Error mostrado al usuario: " + message);
        SetStatus(message);
        try { MessageBox.Show(this, message, "XoPy", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        catch { }
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

            _ = AddPathsAsync(snapshot.Sources, askDestination: false);
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

    private enum QueueSortField { Name, Size, FolderSize, Type, Destination, Source, State, AllSize }

    private void SortQueue(QueueSortField field, bool invert = false)
    {
        if (_items.Count < 2) { SetStatus("La lista está vacía."); return; }
        // Ordenar durante la copia o en pausa es válido: afecta a los pendientes.

        // Las carpetas se comparan por el tamaño REAL de todo su contenido (incluidas las
        // subcarpetas), no por el campo SizeBytes de la cabecera, y los archivos por su
        // propio tamaño. Al ordenar por tamaño se ordena también dentro de cada carpeta.
        long TotalSize(TransferItem item)
        {
            if (item.Kind == ItemKind.File || item.Kind == ItemKind.DirectoryEntry) return Math.Max(0, item.SizeBytes);
            var sum = 0L;
            foreach (var child in Children(item)) sum += TotalSize(child);
            return sum;
        }

        var sizes = _items.ToDictionary(item => item.Id, TotalSize);

        Comparison<TransferItem> comparison = field switch
        {
            QueueSortField.Size => (left, right) => sizes[left.Id].CompareTo(sizes[right.Id]),
            QueueSortField.FolderSize => (left, right) => sizes[left.Id].CompareTo(sizes[right.Id]),
            QueueSortField.AllSize => (left, right) => sizes[left.Id].CompareTo(sizes[right.Id]),
            QueueSortField.Type => (left, right) => string.Compare(TypeKey(left), TypeKey(right), StringComparison.OrdinalIgnoreCase),
            QueueSortField.Destination => (left, right) => string.Compare(DestinationKey(left), DestinationKey(right), StringComparison.OrdinalIgnoreCase),
            QueueSortField.Source => (left, right) => string.Compare(SourceKey(left), SourceKey(right), StringComparison.OrdinalIgnoreCase),
            QueueSortField.State => (left, right) => left.State.CompareTo(right.State),
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
        ResetVisualOrder();
        RebuildRows();
        UpdateFooter();
        var label = field switch
        {
            QueueSortField.Size => "tamaño (las carpetas por el total de su contenido)",
            QueueSortField.FolderSize => "tamaño de carpeta",
            QueueSortField.AllSize => "tamaño de todos los archivos",
            QueueSortField.Type => "tipo",
            QueueSortField.Destination => "carpeta de destino",
            QueueSortField.Source => "carpeta de origen",
            QueueSortField.State => "estado",
            _ => "nombre"
        };
        SetStatus($"Cola ordenada por {label}{(invert ? " (descendente)" : string.Empty)}. Las carpetas conservan sus archivos.");
    }

    /// <summary>
    /// Ordena TODOS los archivos de la lista por tamaño, sin importar de qué carpeta
    /// vengan, para que la copia vaya del más pequeño al más grande (o al revés).
    /// La jerarquía no se toca: cada archivo conserva su carpeta de destino real.
    /// </summary>
    private void SortAllFilesBySize(bool invert = false)
    {
        if (_items.Count < 2) { SetStatus("La lista está vacía."); return; }

        var files = _items
            .Where(item => item.Kind is ItemKind.File or ItemKind.DirectoryEntry)
            .OrderBy(item => Math.Max(0, item.SizeBytes))
            .ToList();
        if (invert) files.Reverse();
        if (files.Count == 0) { SetStatus("No hay archivos que ordenar."); return; }

        // Clic en «Tamaño» otra vez invierte; se recuerda para alternar como en el Explorador.
        _lastSortField = QueueSortField.AllSize;
        _lastSortInverted = invert;

        _visualOrder.Clear();
        _visualOrder.AddRange(files.Select(item => item.Id));
        // Las carpetas van al principio de su nivel para que el árbol siga teniendo sentido.
        var folders = _items.Where(item => item.Kind == ItemKind.Folder).Select(item => item.Id);
        _visualOrder.InsertRange(0, folders);

        RebuildRows();
        UpdateFooter();
        SetStatus($"Archivos ordenados por tamaño, todos juntos {(invert ? "(mayor → menor)" : "(menor → mayor)")}. " +
                  "Cada uno mantiene su carpeta de destino.");
    }

    private QueueSortField? _lastSortField;
    private bool _lastSortInverted;
    private int _sizeColumnClicks;

    /// <summary>
    /// Clic en una cabecera: ordena por esa columna y alterna ascendente/descendente.
    /// Nombre → nombre · Destino → carpeta de destino · Tamaño → tamaño (carpetas por
    /// el total de su contenido) · Estado → estado · Progreso → tamaño también.
    /// </summary>
    private void ListColumnClick(object? sender, ColumnClickEventArgs e)
    {
        var field = e.Column switch
        {
            0 => QueueSortField.Name,
            1 => QueueSortField.Destination,
            2 => QueueSortField.Size,
            3 => QueueSortField.State,
            4 => QueueSortField.Size,
            _ => QueueSortField.Name
        };
        if (e.Column is 5 or 6)
        {
            SetStatus("Las columnas de hash se muestran al verificar; no se ordena por ellas.");
            return;
        }

        var invert = _lastSortField == field && !_lastSortInverted;
        _lastSortField = field;
        _lastSortInverted = invert;

        // En la columna Tamaño: primer clic ordena cada carpeta por dentro (asc), segundo
        // invierte, y a partir del tercero ordena TODOS los archivos juntos.
        if (e.Column == 2 && _sizeColumnClicks >= 2)
        {
            _sizeColumnClicks++;
            SortAllFilesBySize(_sizeColumnClicks % 2 == 0);
            return;
        }
        if (e.Column == 2) _sizeColumnClicks++;
        SortQueue(field, invert);
    }

    private static string TypeKey(TransferItem item)
    {
        var extension = Path.GetExtension(item.Name);
        return string.IsNullOrEmpty(extension) ? "\uFFFF" + item.Name : extension + "\u0000" + item.Name;
    }

    private static string SourceKey(TransferItem item)
    {
        var directory = Path.GetDirectoryName(item.SourcePath);
        return string.IsNullOrEmpty(directory) ? item.Name : directory;
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
        try
        {
            // Orden importante: primero devolver la entrada en la barra de tareas y
            // estado Normal, después mostrar. Al revés, la ventana quedaba «visible»
            // pero sin foco y parecía que XoPy no abría.
            EnsureTrayIcon();
            // El usuario ha pedido ver la ventana: el «arranque oculto» ya no aplica.
            _startHidden = false;
            ShowInTaskbar = true;
            // Restaurar SIEMPRE al frente: antes, con el proceso ya minimizado, la ventana
            // volvía en estado minimizado en la barra de tareas. Pero si estaba maximizada,
            // se devuelve maximizada: al pasar por Normal se perdía el maximizado y la
            // ventana volvía más pequeña de lo que el usuario la tenía.
            var wasMaximized = WindowState == FormWindowState.Maximized || _settings.WindowMaximized;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (!Visible) Show();
            Show();
            if (wasMaximized) WindowState = FormWindowState.Maximized;
            Activate();
            BringToFront();
            // Windows solo concede SetForegroundWindow a procesos con permiso del usuario.
            // TopMost temporal ayuda a sacar la ventana cuando está minimizada u ocultada en bandeja.
            TopMost = true;
            TopMost = false;
            // El clic se ha producido en el área de notificación (de Explorer), así que este
            // proceso NO tiene el foco y SetForegroundWindow se ignora. ForceForeground usa
            // AttachThreadInput, que sí funciona en ese caso.
            ForceForeground();
            UpdateFooter();
            UpdateButtons();
        }
        catch { }
    }

    /// <summary>
    /// Crea el icono de la bandeja si aún no existe. Sin él, ocultar la ventana dejaba
    /// XoPy inaccesible: parecía que la aplicación se había cerrado y no volvía al pulsarla.
    /// </summary>
    private void EnsureTrayIcon()
    {
        try
        {
            if (_tray.Visible) return;
            if (_trayPauseItem is null) BuildTray();
            _baseIcon ??= Icons.AppIcon();
            if (_tray.Icon is null) _tray.Icon = _baseIcon;
            if (string.IsNullOrEmpty(_tray.Text)) _tray.Text = "XoPy";
            _tray.Visible = true;
        }
        catch (Exception ex)
        {
            FileLog.Write("No se pudo mostrar el icono de la bandeja: " + ex.Message);
        }
    }

    /// <summary>
    /// Oculta la ventana en el área de notificación. Con <paramref name="silent"/> no muestra
    /// globo: se usa al arrancar en la bandeja, donde un aviso emergente también se percibía
    /// como «la ventana se abre sola».
    /// </summary>
    private void MinimizeToTray(bool silent = false)
    {
        EnsureTrayIcon();
        Hide();
        ShowInTaskbar = false;
        if (silent) return;
        try
        {
            _tray.ShowBalloonTip(1500, "XoPy", "XoPy sigue abierto en el área de notificación. " +
                "Haz clic en el icono para volver.", ToolTipIcon.Info);
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
        if (WindowState != FormWindowState.Minimized || !_settings.MinimizeToTray || _exitRequested) return;
        // Hide() dentro de OnResize deja el estado de la ventana a medias (Visible=false con
        // WindowState=Minimized) y luego no vuelve al pulsar el icono. Se aplaza al bucle
        // de mensajes para que el minimizado termine antes de ocultar.
        try { BeginInvoke(new Action(() => { if (!_exitRequested && WindowState == FormWindowState.Minimized) MinimizeToTray(); })); }
        catch { }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveWindowGeometry();
        if (_items.Count > 0) SaveQueueFile(_store.LastQueueFile, silent: true);

        // Cerrar con la X se comporta como el usuario espera: la ventana se cierra y NO se
        // reabre sola. Solo se queda en la bandeja si el usuario marcó expresamente
        // «Minimizar al cerrar», y en ese caso se pregunta una sola vez si prefiere salir.
        if (!_exitRequested && _settings.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            MinimizeToTray();
            if (!_settings.TrayHintShown)
            {
                _settings.TrayHintShown = true;
                _store.Save(_settings);
                try
                {
                    _tray.ShowBalloonTip(4000, "XoPy sigue activo",
                        "Está minimizado en el área de notificación. Para cerrarlo del todo: " +
                        "clic derecho en el icono → Salir.", ToolTipIcon.Info);
                }
                catch { }
            }
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
            _resizeDebounce.Stop();
            _resizeDebounce.Dispose();
            _engine.Cancel();
            _tray.Visible = false;
            _tray.Dispose();
            _trayPauseItem?.Dispose();
            _trayMenu.Dispose();
            if (_listMenu is not null) _listMenu.Dispose();
            _percentIcon?.Dispose();
            _baseIcon?.Dispose();
            _pasteHook?.Dispose();
            _pasteHook = null;
            _destination?.Dispose();
        }
        base.Dispose(disposing);
    }
}
