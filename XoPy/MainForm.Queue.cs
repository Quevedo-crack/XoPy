using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
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
                    ? $"Analizando {Format.Plural(scans.Count, "carpeta")}…"
                    : $"{Format.Files(files.Count)} {(files.Count == 1 ? "añadido" : "añadidos")} a la lista.");
            }

            if (scans.Count > 0)
            {
                await Task.WhenAll(scans);
                RebuildRows();
                UpdateFooter();
                UpdateButtons();
                SetStatus(_items.Count == 0
                    ? "Listo para transferir."
                    : $"Lista preparada: {Format.Files(_items.Count(item => item.Kind == ItemKind.File))} listos para transferir.");
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

        // El árbol se enseña ABIERTO: el usuario pidió ver carpeta → subcarpeta → archivo.
        // Con listas enormes se deja plegado para que la ventana no se arrastre; en ese caso
        // basta con pulsar la flecha de la carpeta (o «Expandir todo» en el menú de la lista).
        ExpandByDefault();

        RebuildRows();
        UpdateFooter();
        UpdateButtons();
        UpdateJobs();
    }

    /// <summary>Cuántas filas se pueden desplegar de golpe sin que la lista se arrastre.</summary>
    private const int MaxAutoExpandRows = 1500;

    /// <summary>
    /// Deja el árbol abierto para que se vea la estructura real (carpeta → subcarpeta →
    /// archivo). Si la lista es muy grande se deja plegada: desplegar 20.000 filas de golpe
    /// deja la ventana inservible.
    /// </summary>
    private void ExpandByDefault()
    {
        // Las filas nacen plegadas; aquí solo se ABREN. Nunca se pliega nada por las malas:
        // si la lista es enorme se deja como está y el usuario abre lo que le interese.
        if (_items.Count > MaxAutoExpandRows) return;
        foreach (var item in _items)
        {
            if (item.Kind != ItemKind.Folder) continue;
            // Si el usuario ya ha plegado algo a mano, no se le vuelve a abrir.
            if (_userCollapsed.Contains(item.Id)) continue;
            item.IsExpanded = true;
        }
    }

    /// <summary>Carpetas que el usuario ha plegado a mano: no se vuelven a abrir solas.</summary>
    private readonly HashSet<Guid> _userCollapsed = new();

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

    /// <summary>Cuántos hijos directos tiene cada elemento. Se rellena al reconstruir la
    /// lista para no recorrer _items una vez por fila al pintar (con miles de archivos eso
    /// convertía cada repintado en una operación cuadrática).</summary>
    private readonly Dictionary<Guid, int> _childCount = new();

    private int ChildCount(TransferItem item)
        => _childCount.TryGetValue(item.Id, out var count) ? count : 0;

    /// <summary>
    /// Junta en el orden visual un elemento con todo lo que cuelga de él, respetando el
    /// orden que ya tenían entre ellos. Se usa con las carpetas PLEGADAS (que se ven como
    /// una sola fila: si sus archivos estuvieran repartidos por la lista, los trozos que
    /// quedan fuera no se verían) y al mover una carpeta entera.
    /// </summary>
    private bool GatherBlock(TransferItem item)
    {
        var block = BlockIds(item);
        if (block.Count < 2) return false;

        var positions = new List<int>();
        for (var index = 0; index < _visualOrder.Count; index++)
            if (block.Contains(_visualOrder[index])) positions.Add(index);
        if (positions.Count < 2) return false;

        // Ya está junto: nada que hacer (el caso normal, y hay que salir barato porque
        // esto se comprueba en cada repintado).
        if (positions[^1] - positions[0] == positions.Count - 1) return false;

        var ordered = positions.Select(index => _visualOrder[index]).ToList();
        ordered.Remove(item.Id);
        ordered.Insert(0, item.Id);

        var at = positions[0];
        _visualOrder.RemoveAll(block.Contains);
        _visualOrder.InsertRange(Math.Min(at, _visualOrder.Count), ordered);
        return true;
    }

    /// <summary>Un elemento y todo su contenido.</summary>
    private HashSet<Guid> BlockIds(TransferItem item)
    {
        var ids = new HashSet<Guid> { item.Id };
        foreach (var child in Descendants(item)) ids.Add(child.Id);
        return ids;
    }

    /// <summary>
    /// Las carpetas de un elemento, de la raíz hacia dentro. No depende del orden: es la
    /// jerarquía real, la que decide la ruta de destino.
    /// </summary>
    private static List<TransferItem> ChainOf(TransferItem item, Dictionary<Guid, TransferItem> byId)
    {
        var chain = new List<TransferItem>();
        var current = item.ParentId;
        var guard = 0;
        while (current is { } id && guard++ < 64 && byId.TryGetValue(id, out var parent))
        {
            chain.Add(parent);
            current = parent.ParentId;
        }
        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// Filas de la lista, en el orden en que se ven.
    ///
    /// El orden lo manda <see cref="_visualOrder"/>, que es una sola secuencia PLANA de
    /// archivos: cualquier archivo puede ir a cualquier sitio, aunque salga de otra
    /// carpeta. Las filas de carpeta no se colocan por su cuenta: son cabeceras que se
    /// dibujan delante de cada tramo seguido de archivos suyos. Por eso, si un archivo de
    /// «carpeta 2» se sube al medio de «carpeta 1», la lista enseña carpeta 1 con sus
    /// archivos, luego carpeta 2 con ese archivo, luego carpeta 1 otra vez con lo que
    /// quedaba y luego carpeta 2 con el resto — con las subcarpetas que hagan falta, para
    /// que nunca se pierda de vista de dónde sale cada archivo ni a dónde va.
    ///
    /// Aquí también se decide qué se ve: la vista («En cola» o «Finalizados») filtra los
    /// archivos, y las cabeceras aparecen solas cuando algún archivo suyo queda debajo.
    /// </summary>
    private List<TransferItem> OrderedTree()
    {
        var flat = VisualItems();
        var byId = new Dictionary<Guid, TransferItem>(flat.Count);
        foreach (var item in flat) byId[item.Id] = item;

        _childCount.Clear();
        foreach (var item in flat)
        {
            if (item.ParentId is not { } parentId) continue;
            _childCount[parentId] = (_childCount.TryGetValue(parentId, out var count) ? count : 0) + 1;
        }

        var chains = new Dictionary<Guid, List<TransferItem>>(flat.Count);
        foreach (var item in flat)
        {
            var chain = ChainOf(item, byId);
            chains[item.Id] = chain;
            item.Depth = chain.Count;
        }

        // Una carpeta PLEGADA se enseña como UNA fila, así que su contenido tiene que estar
        // junto: si sus archivos quedaran repartidos, los tramos de fuera no se verían por
        // ninguna parte. Se busca en una sola pasada (esto corre en cada repintado) y solo
        // se recoloca lo que de verdad esté partido.
        var scattered = new List<TransferItem>();
        var seenUnit = new HashSet<Guid>();
        TransferItem? unit = null;
        foreach (var leaf in flat)
        {
            if (ChildCount(leaf) > 0) continue;
            TransferItem? owner = null;
            foreach (var folder in chains[leaf.Id])
                if (!folder.IsExpanded) { owner = folder; break; }

            if (owner is null) { unit = null; continue; }
            if (ReferenceEquals(owner, unit)) continue;
            unit = owner;
            if (!seenUnit.Add(owner.Id)) scattered.Add(owner);
        }
        if (scattered.Count > 0)
        {
            foreach (var folder in scattered.DistinctBy(item => item.Id)) GatherBlock(folder);
            flat = VisualItems();
        }

        var result = new List<TransferItem>(flat.Count);
        var previous = new List<TransferItem>();
        foreach (var leaf in flat)
        {
            // Las carpetas con contenido no ocupan sitio por sí mismas: salen como cabecera
            // de sus archivos. Las vacías sí son una fila (hay que crearlas en el destino).
            if (ChildCount(leaf) > 0) continue;
            if (IsFinished(leaf) != _showFinished) continue;

            var chain = chains[leaf.Id];

            // ¿Alguna carpeta de la cadena está plegada? Entonces la última fila que se ve
            // es esa carpeta, y el archivo no se dibuja.
            var depth = chain.Count;
            var collapsed = false;
            for (var level = 0; level < chain.Count; level++)
                if (!chain[level].IsExpanded) { depth = level + 1; collapsed = true; break; }

            var common = 0;
            while (common < depth && common < previous.Count && previous[common].Id == chain[common].Id) common++;
            for (var level = common; level < depth; level++) result.Add(chain[level]);

            previous = chain.GetRange(0, depth);
            if (collapsed) continue;
            result.Add(leaf);
        }
        return result;
    }

    /// <summary>
    /// ¿Este archivo ya está hecho? «En cola» muestra lo que falta por copiar (incluido lo
    /// que se está copiando ahora o lo que falló, que hay que reintentar); «Finalizados»
    /// muestra lo ya terminado. Es lo que pedía el usuario: al ordenar por tamaño, la lista
    /// de siempre solo enseña lo pendiente.
    /// </summary>
    private static bool IsFinished(TransferItem item)
        => item.State is ItemState.Completed or ItemState.Skipped;

    /// <summary>Cambia entre «En cola» y «Finalizados» sin perder la selección.</summary>
    private void SetQueueView(bool finished)
    {
        if (_showFinished == finished) return;
        _showFinished = finished;
        RebuildRows();
        UpdateQueueViewButtons();
        UpdateFooter();
        UpdateButtons();
    }

    private void UpdateQueueViewButtons()
    {
        if (_viewQueueButton is null || _viewDoneButton is null) return;
        var files = _items.Where(item => item.Kind != ItemKind.Folder).ToList();
        var done = files.Count(IsFinished);
        var pending = files.Count - done;
        _viewQueueButton.Text = $"En cola ({pending})";
        _viewDoneButton.Text = $"Finalizados ({done})";
        foreach (var (button, active) in new[] { (_viewQueueButton, !_showFinished), (_viewDoneButton, _showFinished) })
        {
            button.BackColor = active ? _palette.Accent : _palette.Raised;
            button.ForeColor = active ? Color.White : _palette.Muted;
            button.FlatAppearance.BorderColor = active ? _palette.Accent : _palette.Border;
        }
    }

    /// <summary>
    /// Lo seleccionado, SIN repetir: una carpeta puede ocupar varias filas (una por tramo)
    /// y al seleccionarla se marcan todas, pero es un solo elemento.
    /// </summary>
    private List<TransferItem> SelectedItems()
        => _list.SelectedItems.Cast<ListViewItem>().Select(row => row.Tag).OfType<TransferItem>()
            .DistinctBy(item => item.Id).ToList();

    private List<TransferItem> BasisItems()
    {
        var selected = SelectedItems();
        return selected.Count > 0 ? selected : RootItems();
    }

    private void RebuildRows() => RebuildRows(null);


    /// <summary>
    /// Vuelve a pintar la lista. <paramref name="keepVisible"/> es el elemento que acaba de
    /// moverse: la vista NO salta a otra parte (se conserva la posición de desplazamiento) y
    /// solo se desplaza si ese elemento se ha quedado fuera de la pantalla.
    /// </summary>
    private void RebuildRows(Guid? keepVisible)
    {
        if (_list is null) return;
        var selectedIds = SelectedItems().Select(item => item.Id).ToHashSet();

        // La posición de desplazamiento se guarda como ÍNDICE de la primera fila visible, no
        // como identificador: al mover un elemento, su fila cambia de sitio y anclarse a ella
        // hacía que la lista se fuera a otra parte (justo la queja del usuario).
        var topIndex = 0;
        try { topIndex = _list.TopItem?.Index ?? 0; } catch { }

        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            _rows.Clear();
            // Árbol: CARPETA → SUBCARPETA → ARCHIVO. Cada fila se dibuja con la sangría de su
            // profundidad y las carpetas se pueden plegar. El orden es libre: la cabecera de
            // la carpeta se repite en cada tramo, así que mover un archivo a cualquier sitio
            // no cambia nunca su raíz ni su ruta de destino.
            var ordered = OrderedTree();
            var seenFolders = new HashSet<Guid>();
            foreach (var item in ordered)
            {
                // Una carpeta partida sale una vez por tramo. Para que nadie crea que la
                // lista se ha duplicado, los tramos siguientes lo dicen.
                var repeated = item.Kind == ItemKind.Folder && ChildCount(item) > 0 && !seenFolders.Add(item.Id);
                var label = repeated ? item.Name + "   (sigue)" : item.Name;
                var row = new ListViewItem(new[] { label, RelativeTargetText(item), item.SizeText, item.StateText, string.Empty, string.Empty, string.Empty })
                {
                    Tag = item,
                    ToolTipText = repeated
                        ? "Mismo grupo de más arriba: aquí siguen más archivos de esta carpeta.\n" + BuildTooltip(item)
                        : BuildTooltip(item)
                };
                // Una carpeta puede salir varias veces (una por tramo). Para desplazarse
                // hasta ella o seleccionarla vale la PRIMERA de sus filas.
                if (!_rows.ContainsKey(item.Id)) _rows[item.Id] = row;
                _list.Items.Add(row);
                if (selectedIds.Contains(item.Id)) row.Selected = true;
            }
        }
        finally
        {
            _list.EndUpdate();
        }

        RefreshFolders();
        // Se apunta aquí cuántos hay terminados: si no, SyncRows creería que la vista ha
        // cambiado y volvería a llamar a RebuildRows (trabajo doble en cada repaso).
        _finishedCount = _items.Count(item => item.Kind != ItemKind.Folder && IsFinished(item));
        SyncRows();
        UpdateHashColumns();
        // Al pasar de lista vacía a con filas se reparte de nuevo el alto: ResizeColumns es
        // quien devuelve la fila del ListView a Percent y, si no, las filas no se ven.
        ResizeColumns();
        RestoreScroll(topIndex, keepVisible);
        UpdateQueueViewButtons();
        UpdateScrollCover();
    }

    /// <summary>
    /// Devuelve la lista exactamente a donde estaba. Solo se desplaza si el elemento recién
    /// movido se ha quedado fuera de la zona visible, y entonces lo justo para verlo.
    /// </summary>
    private void RestoreScroll(int topIndex, Guid? keepVisible)
    {
        if (_list is null || _list.Items.Count == 0) return;
        try
        {
            var wanted = Math.Clamp(topIndex, 0, _list.Items.Count - 1);
            _list.TopItem = _list.Items[wanted];
        }
        catch { }

        if (keepVisible is null) return;
        if (!_rows.TryGetValue(keepVisible.Value, out var row)) return;
        try
        {
            var first = _list.TopItem?.Index ?? 0;
            var rowHeight = Math.Max(1, row.Bounds.Height);
            var visible = Math.Max(1, _list.ClientSize.Height / rowHeight - 1);
            if (row.Index < first || row.Index > first + visible - 1) _list.EnsureVisible(row.Index);
        }
        catch { }
    }

    /// <summary>Cuántos archivos hay terminados. Si cambia, hay que repintar la lista.</summary>
    private int _finishedCount = -1;

    /// <summary>
    /// Quita de la vista «En cola» las filas de lo que ya se ha copiado (y las carpetas que
    /// se quedan sin nada pendiente). No toca la posición de desplazamiento ni los datos:
    /// esos archivos siguen en la lista, solo que ahora se ven en «Finalizados».
    /// </summary>
    private void TrimFinishedRows()
    {
        if (_list is null || _showFinished) return;

        var byId = new Dictionary<Guid, TransferItem>(_items.Count);
        foreach (var item in _items) byId[item.Id] = item;

        var remove = new List<ListViewItem>();
        var keepFolders = new HashSet<Guid>();
        foreach (ListViewItem row in _list.Items)
        {
            if (row.Tag is not TransferItem item || item.Kind == ItemKind.Folder) continue;
            if (IsFinished(item)) { remove.Add(row); continue; }

            // Este archivo sigue pendiente: todas las carpetas por encima de él se quedan.
            var parent = item.ParentId;
            var guard = 0;
            while (parent is not null && guard++ < 64)
            {
                if (!keepFolders.Add(parent.Value)) break;
                parent = byId.TryGetValue(parent.Value, out var upper) ? upper.ParentId : null;
            }
        }
        if (remove.Count == 0) return;

        foreach (ListViewItem row in _list.Items)
        {
            if (row.Tag is not TransferItem folder || folder.Kind != ItemKind.Folder) continue;
            if (keepFolders.Contains(folder.Id)) continue;
            // Una carpeta vacía no depende de sus hijos: sigue su propio estado.
            if (ChildCount(folder) == 0 && !IsFinished(folder)) continue;
            remove.Add(row);
        }

        _list.BeginUpdate();
        try
        {
            foreach (var row in remove)
            {
                if (row.Tag is TransferItem item) _rows.Remove(item.Id);
                _list.Items.Remove(row);
            }

            // Al retirar los archivos ya copiados puede quedarse una cabecera de carpeta sin
            // nada debajo (su tramo se ha vaciado). Se quita de abajo arriba, que así caen
            // también las carpetas que solo contenían a esa.
            for (var index = _list.Items.Count - 1; index >= 0; index--)
            {
                if (_list.Items[index].Tag is not TransferItem folder) continue;
                if (folder.Kind != ItemKind.Folder || ChildCount(folder) == 0) continue;
                var next = index + 1 < _list.Items.Count
                    ? _list.Items[index + 1].Tag as TransferItem
                    : null;
                if (next is null || next.Depth <= folder.Depth) _list.Items.RemoveAt(index);
            }
        }
        finally { _list.EndUpdate(); }

        UpdateQueueViewButtons();
        UpdateScrollCover();
    }

    private void SyncRows()
    {
        if (_list is null || !IsHandleCreated) return;
        RefreshFolders();

        // Los archivos que terminan SALEN de la vista «En cola» y aparecen en «Finalizados»
        // en cuanto se completan, sin esperar a que acabe la copia entera.
        var finished = 0;
        foreach (var item in _items)
            if (item.Kind != ItemKind.Folder && IsFinished(item)) finished++;
        if (finished != _finishedCount)
        {
            _finishedCount = finished;
            // En «Finalizados» ENTRAN filas nuevas: hay que rehacer la lista. En «En cola»
            // solo SALEN, y quitarlas una a una es mucho más barato que reconstruirlo todo
            // en cada archivo terminado (con miles de archivos eso se arrastraba).
            if (_showFinished) { RebuildRows(); return; }
            TrimFinishedRows();
        }

        // PARPADEO DE LA LISTA MIENTRAS SE COPIA. Antes se repintaba la lista ENTERA cada
        // 300 ms aunque solo cambiara el porcentaje de un archivo: con la lista llena eso
        // es un parpadeo continuo. Ahora se apunta qué filas han cambiado de verdad y solo
        // se repintan esas.
        var changed = false;
        var dirty = new List<ListViewItem>();
        ListViewItem? currentRow = null;
        void SetText(ListViewItem.ListViewSubItem subItem, string text)
        {
            if (string.Equals(subItem.Text, text, StringComparison.Ordinal)) return;
            subItem.Text = text;
            changed = true;
            if (currentRow is not null && (dirty.Count == 0 || dirty[^1] != currentRow)) dirty.Add(currentRow);
        }

        _list.BeginUpdate();
        try
        {
            foreach (ListViewItem row in _list.Items)
            {
                if (row.Tag is not TransferItem item) continue;
                currentRow = row;
                var state = item.StateText;
                if (item.Kind == ItemKind.Folder && item.State is ItemState.Pending or ItemState.Measuring)
                {
                    var count = ChildCount(item);
                    if (count > 0) state += $" · {count}";
                }
                SetText(row.SubItems[1], RelativeTargetText(item));
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
            currentRow = null;
            _list.EndUpdate();
        }

        // Más de 40 filas cambiadas a la vez: sale más barato un repintado entero que 40
        // rectángulos sueltos (pasa al empezar y al terminar una copia).
        if (dirty.Count > 40) _list.Invalidate();
        else foreach (var row in dirty)
        {
            try { _list.Invalidate(row.Bounds); } catch { }
        }
        if (!changed && _engine.IsRunning)
        {
            // Aunque no cambie ningún texto, la barra de la fila en curso avanza: se
            // refresca solo esa fila.
            foreach (ListViewItem row in _list.Items)
            {
                if (row.Tag is TransferItem running && running.State == ItemState.Running)
                {
                    try { _list.Invalidate(row.Bounds); } catch { }
                }
            }
        }
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
    /// Subcarpeta DENTRO del destino a la que va este elemento. La carpeta de destino sale ya
    /// en la cabecera de la ventana: repetirla en las 6.000 filas de la lista era uno de los
    /// duplicados de los que se quejaba el usuario. Aquí solo se muestra lo que cambia de una
    /// fila a otra: «—» si va a la raíz del destino, o «\\subcarpeta» si va dentro.
    /// </summary>
    private static string RelativeTargetText(TransferItem item)
    {
        string? folder = null;
        if (!string.IsNullOrWhiteSpace(item.RelativePath))
        {
            folder = item.Kind == ItemKind.File
                ? Path.GetDirectoryName(item.RelativePath)
                : Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(item.RelativePath));
        }
        if (item.Kind != ItemKind.File && !string.IsNullOrWhiteSpace(item.RelativePath))
            folder = Path.TrimEndingDirectorySeparator(item.RelativePath);
        if (string.IsNullOrWhiteSpace(folder)) return "—";
        return Path.DirectorySeparatorChar + folder;
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
    /// <summary>
    /// Texto de la cabecera de la lista. Solo describe LO QUE SE ESTÁ VIENDO en la vista
    /// activa: el total general y el porcentaje viven en el pie y no se repiten aquí.
    /// </summary>
    private void UpdateCountLabel(int leaves, long total)
    {
        if (_countLabel is null) return;
        // Mensajes CORTOS: este hueco es estrecho y el pie ya dice «Arrastra archivos o
        // carpetas a la ventana, o usa Añadir archivos».
        if (_items.Count == 0)
        {
            _countLabel.Text = "Lista vacía";
            return;
        }

        var files = _items.Where(item => item.Kind != ItemKind.Folder).ToList();
        var shown = files.Where(item => IsFinished(item) == _showFinished).ToList();
        if (shown.Count == 0)
        {
            _countLabel.Text = _showFinished ? "Nada terminado" : "Nada pendiente";
            return;
        }
        var bytes = shown.Sum(item => Math.Max(0, item.SizeBytes));
        _countLabel.Text = $"{shown.Count} {(shown.Count == 1 ? "archivo" : "archivos")} · {Format.Bytes(bytes)}";
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
        if (ChildCount(item) == 0) return;
        item.IsExpanded = !item.IsExpanded;
        if (item.IsExpanded) _userCollapsed.Remove(item.Id);
        else _userCollapsed.Add(item.Id);
        RebuildRows();
    }

    private void ToggleSelectedFolders()
    {
        var folders = SelectedItems().Where(item => item.Kind == ItemKind.Folder).ToList();
        if (folders.Count == 0) { SetStatus("Selecciona una carpeta para expandirla o contraerla."); return; }
        var expand = folders.Any(folder => !folder.IsExpanded);
        foreach (var folder in folders)
        {
            folder.IsExpanded = expand;
            if (expand) _userCollapsed.Remove(folder.Id);
            else _userCollapsed.Add(folder.Id);
        }
        RebuildRows();
    }

    private void ExpandAll(bool expand)
    {
        foreach (var folder in _items.Where(item => item.Kind == ItemKind.Folder))
        {
            folder.IsExpanded = expand;
            if (expand) _userCollapsed.Remove(folder.Id);
            else _userCollapsed.Add(folder.Id);
        }
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

        if (moved == 0) { SetStatus("No se puede mover más: ya está en el extremo de la lista."); return; }

        // Se pasa el elemento movido para que la lista NO salte a otra parte: se queda donde
        // estaba y solo se desplaza si la fila se ha salido de la pantalla.
        RebuildRows(ordered[0].Id);
        UpdateFooter();
        UpdateButtons();
        SetStatus("Orden de la cola actualizado.");
    }

    /// <summary>
    /// Lleva lo seleccionado al principio o al final de la lista de un golpe. Con listas
    /// largas, pulsar ↑ cuarenta veces no es forma de trabajar.
    /// </summary>
    private void MoveSelectedToEdge(bool top)
    {
        var selected = SelectedItems();
        if (selected.Count == 0) { SetStatus("Selecciona un elemento para cambiar su orden."); return; }

        OrderedTree();
        var leaves = LeafOrder();
        var block = new HashSet<Guid>();
        foreach (var item in selected) block.UnionWith(BlockIds(item));

        // Al principio: el primer archivo que NO se mueve marca el sitio. Al final no hay
        // nada detrás, así que no hay ancla.
        Guid? anchor = top ? leaves.FirstOrDefault(leaf => !block.Contains(leaf.Id))?.Id : null;

        // En el orden en que se ven: cada uno se mete delante del ancla (o al final), así
        // que el grupo llega entero y conservando su orden.
        var order = InVisualOrder(selected);

        var moved = 0;
        foreach (var item in order)
            if (MoveBlockBefore(item, anchor)) moved++;

        if (moved == 0) { SetStatus(top ? "Ya está al principio de la lista." : "Ya está al final de la lista."); return; }
        RebuildRows(order[0].Id);
        UpdateFooter();
        UpdateButtons();
        SetStatus(top ? "Llevado al principio de la lista." : "Llevado al final de la lista.");
    }

    /// <summary>
    /// Sube o baja un elemento UNA FILA de las que se ven, vaya donde vaya. Un archivo
    /// puede cruzar por encima o por debajo de cualquier otro, aunque pertenezca a otra
    /// carpeta; una carpeta se mueve con todo su contenido, que se junta al moverla.
    ///
    /// Lo que NO cambia nunca es la jerarquía: ni ParentId, ni RelativePath, ni la raíz.
    /// Por eso un archivo que se cuela en medio de otra carpeta sigue copiándose a su
    /// destino de siempre, y la lista lo enseña con su propia cabecera de carpeta.
    /// </summary>
    private bool MoveBlock(TransferItem item, int delta)
    {
        if (delta == 0) return false;

        // OrderedTree() deja al día el recuento de hijos y junta las carpetas plegadas.
        OrderedTree();
        var leaves = LeafOrder();
        var block = BlockIds(item);
        var others = leaves.Where(leaf => !block.Contains(leaf.Id)).ToList();
        if (others.Count == 0) return false;

        var rank = new Dictionary<Guid, int>(leaves.Count);
        for (var index = 0; index < leaves.Count; index++) rank[leaves[index].Id] = index;

        var first = leaves.FindIndex(leaf => block.Contains(leaf.Id));
        if (first < 0) return false;

        // Posición actual expresada como «delante de la hoja nº anchor de las demás».
        var anchor = 0;
        while (anchor < others.Count && rank[others[anchor].Id] < first) anchor++;

        var byId = new Dictionary<Guid, TransferItem>(_items.Count);
        foreach (var entry in _items) byId[entry.Id] = entry;
        var target = delta < 0 ? StepBack(others, anchor, byId) : StepForward(others, anchor, byId);
        if (target < 0 || target == anchor) return false;
        return MoveBlockBefore(item, target < others.Count ? others[target].Id : null);
    }

    /// <summary>
    /// Primera fila VISIBLE antes de <paramref name="anchor"/>. Lo que la vista no enseña
    /// (archivos ya copiados en «En cola», por ejemplo) se salta sin gastar el paso, y una
    /// carpeta plegada cuenta como UNA sola fila: se salta entera.
    /// </summary>
    private int StepBack(List<TransferItem> others, int anchor, Dictionary<Guid, TransferItem> byId)
    {
        var index = anchor - 1;
        while (index >= 0 && IsFinished(others[index]) != _showFinished) index--;
        if (index < 0) return -1;

        var folder = CollapsedOwner(others[index], byId);
        if (folder is null) return index;
        while (index > 0 && CollapsedOwner(others[index - 1], byId)?.Id == folder.Id) index--;
        return index;
    }

    /// <summary>Primera posición DESPUÉS de la siguiente fila visible.</summary>
    private int StepForward(List<TransferItem> others, int anchor, Dictionary<Guid, TransferItem> byId)
    {
        var index = anchor;
        while (index < others.Count && IsFinished(others[index]) != _showFinished) index++;
        if (index >= others.Count) return -1;

        var folder = CollapsedOwner(others[index], byId);
        if (folder is null) return index + 1;
        while (index < others.Count && CollapsedOwner(others[index], byId)?.Id == folder.Id) index++;
        return index;
    }

    /// <summary>
    /// Carpeta plegada más externa que contiene a este archivo (o null si se ve suelto).
    /// Es la fila que el usuario ve en su lugar, y por tanto la unidad que se mueve.
    /// </summary>
    private static TransferItem? CollapsedOwner(TransferItem leaf, Dictionary<Guid, TransferItem> byId)
    {
        foreach (var folder in ChainOf(leaf, byId))
            if (!folder.IsExpanded) return folder;
        return null;
    }

    /// <summary>Archivos (y carpetas vacías) en el orden visual, sin filtrar por vista.</summary>
    private List<TransferItem> LeafOrder()
        => VisualItems().Where(item => ChildCount(item) == 0).ToList();

    /// <summary>
    /// Pone una colección en el orden de la lista. Es lo que hace que el motor copie en el
    /// orden que el usuario ha dejado, y no en el que se añadieron los archivos.
    /// </summary>
    private List<TransferItem> InVisualOrder(IEnumerable<TransferItem> items)
    {
        var rank = new Dictionary<Guid, int>(_visualOrder.Count);
        for (var index = 0; index < _visualOrder.Count; index++) rank[_visualOrder[index]] = index;
        return items
            .OrderBy(item => rank.TryGetValue(item.Id, out var position) ? position : int.MaxValue)
            .ToList();
    }

    /// <summary>
    /// Coloca un elemento —con todo su contenido— justo delante de <paramref name="anchorId"/>,
    /// o al final si no se indica ninguno. Es el ÚNICO sitio que mueve filas de verdad:
    /// toca <see cref="_visualOrder"/> y nada más.
    /// </summary>
    private bool MoveBlockBefore(TransferItem item, Guid? anchorId)
    {
        VisualItems();
        var block = BlockIds(item);
        if (anchorId is { } anchor && block.Contains(anchor)) return false;

        var moving = _visualOrder.Where(block.Contains).ToList();
        if (moving.Count == 0) return false;
        moving.Remove(item.Id);
        moving.Insert(0, item.Id);

        var rest = _visualOrder.Where(id => !block.Contains(id)).ToList();
        var at = rest.Count;
        if (anchorId is { } target)
        {
            var found = rest.IndexOf(target);
            if (found >= 0) at = found;
        }
        if (SameOrder(rest, moving, at)) return false;

        rest.InsertRange(at, moving);
        _visualOrder.Clear();
        _visualOrder.AddRange(rest);
        return true;
    }

    /// <summary>¿El bloque ya estaba exactamente ahí? Entonces no se ha movido nada.</summary>
    private bool SameOrder(List<Guid> rest, List<Guid> moving, int at)
    {
        var before = _visualOrder.IndexOf(moving[0]);
        if (before < 0) return false;
        var expected = at < rest.Count ? rest[at] : (Guid?)null;
        var after = before + moving.Count;
        var actual = after < _visualOrder.Count ? _visualOrder[after] : (Guid?)null;
        return expected == actual && _visualOrder.Skip(before).Take(moving.Count).SequenceEqual(moving);
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

    /// <summary>¿Ha pedido el usuario renombrar? Sin esto, la lista abre la cajita sola.</summary>
    private bool _renameRequested;

    /// <summary>Confirma el nombre editado en la lista (el archivo del disco no se toca).</summary>
    private void ListAfterLabelEdit(object? sender, LabelEditEventArgs e)
    {
        // Se apaga la edición en cuanto acaba: solo F2 o el menú vuelven a encenderla.
        _renameRequested = false;
        try { BeginInvoke(new Action(() => { if (!IsDisposed && _list is not null) _list.LabelEdit = false; })); }
        catch { }

        // e.Label a nulo = edición cancelada (Esc o clic fuera). No es un renombrado.
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
        // MISMO NOMBRE = no ha pasado nada. Antes esto se apuntaba en el registro como un
        // renombrado y el usuario veía «Elemento renombrado en la lista» sin haber escrito.
        if (string.Equals(item.Name, name, StringComparison.Ordinal)) return;
        var previous = item.Name;
        item.Name = name;
        FileLog.Write($"Elemento renombrado en la lista: {item.SourcePath} ({previous}) -> {name}");
        SetStatus($"«{previous}» se llamará «{name}» en el destino (el archivo del disco no se ha tocado).");
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
        _renameRequested = true;
        _list.LabelEdit = true;
        _list.SelectedItems[0].BeginEdit();
    }

    /// <summary>
    /// Atajos de toda la ventana. F9 oculta y muestra la lista de archivos: existe aparte
    /// del menú porque la barra de menús viene escondida de fábrica y los atajos de un
    /// MenuStrip invisible no siempre llegan. Es la tercera puerta de salida (botón, menú
    /// y teclado) para que ocultar la lista nunca deje al usuario encerrado.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F9)
        {
            ToggleCompactView();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
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
        else if (e.KeyCode == Keys.Home && e.Alt)
        {
            MoveSelectedToEdge(top: true);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.End && e.Alt)
        {
            MoveSelectedToEdge(top: false);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Right)
        {
            foreach (var folder in SelectedItems().Where(item => item.Kind == ItemKind.Folder))
            {
                folder.IsExpanded = true;
                _userCollapsed.Remove(folder.Id);
                var first = Children(folder).FirstOrDefault();
                if (first is not null) SelectRows(new[] { first.Id });
            }
            RebuildRows();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Left)
        {
            foreach (var folder in SelectedItems().Where(item => item.Kind == ItemKind.Folder))
            {
                folder.IsExpanded = false;
                _userCollapsed.Add(folder.Id);
            }
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
        // Clic en la flecha de una carpeta: se pliega o despliega. La zona sensible tiene en
        // cuenta la sangría del nivel, igual que la pinta ListDrawSubItem.
        if (e.Button == MouseButtons.Left && info.Item?.Tag is TransferItem folder
            && folder.Kind == ItemKind.Folder && ChildCount(folder) > 0)
        {
            var indent = Math.Clamp(folder.Depth, 0, 12) * 18;
            var left = _list.Items[info.Item.Index].Bounds.Left + 8 + indent;
            if (e.X >= left - 2 && e.X <= left + 16)
            {
                ToggleExpand(folder);
                return;
            }
        }
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

    /// <summary>
    /// Se ha pulsado Copiar/Mover con otra transferencia ya en marcha. Se ofrece lo que pidió
    /// el usuario: añadir a la copia en curso, poner esta lista EN ESPERA (se lanzará sola al
    /// terminar la de delante) o copiar a la vez en otra ventana.
    /// </summary>
    private void HandleBusy(OperationType operation)
    {
        if (!_settings.AskWhenBusy)
        {
            QueueListForLater(operation);
            return;
        }

        var running = _runningList?.DisplayName ?? "la copia en curso";
        var mine = _activeList?.DisplayName ?? "esta lista";

        // CASO EN PAUSA. Una copia parada no termina nunca, así que «esperar turno» sería
        // esperar para siempre: se dice con todas las letras y la primera opción reanuda
        // aquella copia antes de poner esta a la cola.
        if (_engine.IsPaused)
        {
            var paused = PromptDialog.Ask(this, _palette, "La otra copia está en pausa",
                $"«{running}» está EN PAUSA. Mientras siga parada no puede empezar ninguna otra copia.",
                $"· Reanudar aquella y poner «{mine}» en espera: la de delante sigue donde la dejaste y esta arranca sola en cuanto termine.\n" +
                "· Añadir a la de delante: los archivos se meten en la lista que está en pausa (seguirá parada hasta que pulses Reanudar).\n" +
                "· Copiar esta ahora en otra ventana: se abre otro XoPy y copia a la vez, sin tocar la copia en pausa.",
                new[] { "Reanudar aquella y esperar turno", "Añadir a la de delante", "Copiar esta ahora en otra ventana", "Cancelar" });

            switch (paused)
            {
                case 0:
                    QueueListForLater(operation);
                    TogglePause();
                    break;
                case 1:
                    MergeIntoRunningList(operation);
                    break;
                case 2:
                    LaunchSeparateWindow(operation);
                    break;
            }
            return;
        }

        var choice = PromptDialog.Ask(this, _palette, "Ya hay una copia en marcha",
            $"«{running}» está copiando ahora mismo. ¿Qué hago con «{mine}»?",
            "· Esperar turno: esta lista arranca sola en cuanto termine la de delante. Aparece en el panel de trabajos marcada como «Esperando turno».\n" +
            "· Añadir a la de delante: los archivos se meten en la copia que ya está corriendo.\n" +
            "· Copiar a la vez: se abre otra ventana de XoPy y las dos copias van en paralelo (más lento si es el mismo disco).",
            new[] { "Esperar turno", "Añadir a la de delante", "Copiar a la vez", "Cancelar" });

        switch (choice)
        {
            case 0:
                QueueListForLater(operation);
                break;
            case 1:
                MergeIntoRunningList(operation);
                break;
            case 2:
                LaunchSeparateWindow(operation);
                break;
        }
    }

    /// <summary>Deja la lista esperando turno; se lanzará sola al terminar la que va delante.</summary>
    private void QueueListForLater(OperationType operation)
    {
        if (_activeList is null) { SetStatus("Ya hay una transferencia en curso."); return; }
        _activeList.Queued = true;
        _activeList.QueuedOperation = operation;
        UpdateJobs();
        // Si la de delante está parada, se avisa: si no, el usuario se queda mirando una
        // lista «esperando turno» que no arranca nunca y parece que XoPy no hace nada.
        SetStatus(_engine.IsPaused
            ? $"«{_activeList.DisplayName}» esperará turno. Ojo: la copia de delante está EN PAUSA y no arrancará nada hasta que la reanudes."
            : $"«{_activeList.DisplayName}» esperará turno: arrancará al terminar la copia actual.");
    }

    /// <summary>Mueve los archivos de la lista que se está viendo a la que se está copiando.</summary>
    private void MergeIntoRunningList(OperationType operation)
    {
        if (_runningList is null || _activeList is null || _runningList == _activeList)
        {
            _followUpOperation = operation;
            SetStatus("Se copiarán en cuanto termine la transferencia en curso.");
            return;
        }

        var moved = _activeList.Items.ToList();
        _activeList.Items.Clear();
        _runningList.Items.AddRange(moved);
        _followUpOperation = operation;
        SwitchToList(_runningList);
        SetStatus($"{moved.Count} elemento(s) añadidos a «{_runningList.DisplayName}»; se copiarán al llegarles el turno.");
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

    /// <summary>
    /// Mete a XoPy en la lista blanca de la protección contra ransomware de Windows. Es
    /// LA causa del «Acceso denegado (error 5)» al copiar al Escritorio, a Documentos o a
    /// Imágenes en los equipos que la tienen activada: los permisos de la carpeta están
    /// bien, pero Windows corta la escritura de todo programa que no esté en esa lista.
    /// </summary>
    private void AllowControlledFolderAccess()
    {
        var active = CopyEngine.ControlledFolderAccessEnabled();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var estado = active
            ? "El «Acceso controlado a carpetas» está ACTIVADO en este equipo."
            : "El «Acceso controlado a carpetas» parece APAGADO. Si aun así XoPy no puede escribir en " +
              "tus carpetas, prueba igualmente: no cuesta nada y deja a XoPy permitido para siempre.";
        var prueba = CopyEngine.CanWriteInFolder(desktop)
            ? "Ahora mismo XoPy SÍ puede escribir en tu Escritorio."
            : "Ahora mismo XoPy NO puede escribir en tu Escritorio.";

        var choice = PromptDialog.Ask(this, _palette, "Permitir XoPy en Seguridad de Windows",
            "Se añadirá XoPy.exe a la lista de programas permitidos del «Acceso controlado a carpetas». " +
            "Windows pedirá confirmación de administrador.",
            estado + "\n" + prueba + "\n\nEquivale a hacerlo a mano en: Seguridad de Windows → " +
            "Protección contra virus y amenazas → Protección contra ransomware → " +
            "Permitir una aplicación a través del Acceso controlado a carpetas.",
            new[] { "Permitir XoPy", "Cancelar" });
        if (choice != 0) return;

        if (CopyEngine.TryAllowInControlledFolderAccess(out var message))
        {
            var ahora = CopyEngine.CanWriteInFolder(desktop)
                ? " Comprobado: XoPy ya puede escribir en el Escritorio."
                : " Aviso: el Escritorio sigue sin admitir escritura; puede que la causa sea otra (antivirus o permisos de la carpeta).";
            SetStatus(message + ahora);
        }
        else
        {
            ShowError(message);
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

            // Se copia EN EL ORDEN DE LA LISTA. Antes se usaba el orden en que se añadieron
            // los archivos, así que reordenar cambiaba lo que se veía pero no lo que hacía
            // el motor; ahora subir un archivo lo adelanta de verdad.
            leaves = InVisualOrder(leaves);

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
                    // LIMPIEZA PRIMERO, JUICIO DESPUÉS. El aviso «Carpeta de destino no
                    // válida» saltaba con rutas perfectamente buenas: bastaba con que
                    // trajeran comillas («Copiar como ruta de acceso» del Explorador), un
                    // espacio o un punto al final, barras repetidas o un %VARIABLE% sin
                    // resolver para que Path.GetFullPath contestara «parámetro no válido».
                    var cleaned = PathTools.CleanFolder(destination);
                    if (cleaned.Length > 0) destination = cleaned;
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
                    // Si la carpeta YA EXISTE no se vuelve a crear: Directory.CreateDirectory
                    // sobre una carpeta protegida (acceso controlado a carpetas, carpeta de
                    // red con permisos justos) puede fallar y disparaba el aviso «Carpeta de
                    // destino no válida» aunque el destino fuese correcto.
                    if (!Directory.Exists(destination))
                    {
                        if (operation != OperationType.Test) Directory.CreateDirectory(destination);
                        if (!Directory.Exists(destination)) throw new DirectoryNotFoundException("La carpeta de destino no existe: " + destination);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    // El aviso dice la RUTA EXACTA y, si hay un carácter prohibido, cuál es:
                    // «no es válida» a secas no dejaba ni saber qué mirar.
                    var culprit = PathTools.FirstForbiddenChar(destination);
                    var reason = culprit is null
                        ? CopyEngine.Describe(ex, null, destination)
                        : $"El nombre lleva el carácter «{culprit}», que Windows no admite en rutas.";
                    var choice = PromptDialog.Ask(this, _palette,
                        "Carpeta de destino no válida",
                        "No se pudo usar esta carpeta de destino:\n" + destination,
                        reason + "\n\n¿Qué quieres hacer?",
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

            // COMPROBACIÓN DE ESCRITURA ANTES DE EMPEZAR. Si Windows no deja escribir en el
            // destino (acceso controlado a carpetas, carpeta del sistema, unidad en solo
            // lectura), antes se descubría archivo por archivo: doscientos «Acceso denegado
            // (error 5)» seguidos. Ahora se avisa UNA vez, se explica la causa y se ofrece
            // arreglarlo o elegir otra carpeta.
            if (operation != OperationType.Test && !CopyEngine.CanWriteInFolder(destination))
            {
                var hint = CopyEngine.ProtectedFolderHint(destination) ??
                           "Comprueba que la unidad no esté llena ni en solo lectura y que tengas permiso de escritura.";
                var guarded = CopyEngine.ControlledFolderAccessEnabled();
                var buttons = guarded
                    ? new[] { "Permitir XoPy y reintentar", "Elegir otra carpeta…", "Copiar de todas formas", "Cancelar" }
                    : new[] { "Elegir otra carpeta…", "Copiar de todas formas", "Cancelar" };
                var answer = PromptDialog.Ask(this, _palette, "Windows no deja escribir en el destino",
                    "No se puede crear ningún archivo en:\n" + destination, hint, buttons);
                var pick = guarded ? answer : answer + 1;   // misma numeración con o sin el primer botón
                if (pick == 3) return;
                if (pick == 0)
                {
                    if (CopyEngine.TryAllowInControlledFolderAccess(out var message)) SetStatus(message);
                    else { ShowError(message); return; }
                    if (!CopyEngine.CanWriteInFolder(destination))
                    {
                        ShowError("Sigue sin poder escribirse en " + destination + ". Elige otra carpeta de destino.");
                        return;
                    }
                }
                else if (pick == 1)
                {
                    _pickDestination.Description = "Elige otra carpeta de destino";
                    if (_pickDestination.ShowDialog(this) != DialogResult.OK) return;
                    destination = PathTools.CleanFolder(_pickDestination.SelectedPath);
                    try { Directory.CreateDirectory(destination); } catch { }
                    _destination.Text = destination;
                    _settings.Destination = destination;
                    _store.Save(_settings);
                }
            }
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
                SetStatus($"No caben {Format.Files(dropped.Count)}: se copian solo los que entran.");
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
                ? $"{Format.Files(skippedAlreadyThere)} ya están en el destino. Bórralos del destino para volver a copiarlos."
                : "No hay archivos que se puedan copiar: su origen ya no existe. Añádelos de nuevo.");
            SyncRows();
            return;
        }
        if (skippedAlreadyThere > 0)
            SetStatus($"{Format.Files(skippedAlreadyThere)} ya estaban en el destino; se copian los demás.");

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

        var targets = operation == OperationType.Delete ? InVisualOrder(deleteTargets) : leaves;
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

        // Se anota qué lista está copiando el motor: así se puede mirar otra lista mientras
        // la copia sigue, y el panel de trabajos sabe cuál marcar como «Copiando ahora».
        _runningList = _activeList;
        if (_activeList is not null) _activeList.Queued = false;

        if (!_engine.Start(targets, options))
        {
            _runningList = null;
            var missing = targets.Count(item => item.State == ItemState.Failed && item.Error?.Contains("ya no existe") == true);
            SetStatus(missing > 0
                ? $"No se pudo transferir: {Format.Files(missing)} de origen ya no existen (se movieron antes). Añádelos de nuevo."
                : "No hay archivos pendientes que transferir.");
            UpdateButtons();
            return;
        }

        // La ventana se queda COMO LA HAYA DEJADO EL USUARIO. Antes se reducía sola al
        // empezar a copiar; ahora que la vista reducida esconde de verdad la lista de
        // archivos, hacerlo solo equivaldría a quitarle la lista de delante justo cuando
        // más la mira. Ocultarla es una decisión suya (botón «Ocultar lista» o F9).
        // Si el arranque fue en la bandeja y el usuario acaba de lanzar una copia desde el
        // Explorador, la ventana debe aparecer para que vea el progreso.
        if (!Visible)
        {
            try { RestoreWindow(); } catch { }
        }

        // LA COPIA EMPIEZA CON LA LISTA CONTRAÍDA. Con una carpeta de tres mil archivos
        // abierta, la lista era un muro de nombres imposible de seguir; plegada se ven las
        // carpetas y su avance, y quien quiera el detalle abre la que le interese (o pulsa
        // Ver → Expandir todo).
        ExpandAll(false);

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
        var on = _settings.MultiThreadCopy;
        _multiThreadButton.Text = MultiThreadLabel();
        _multiThreadButton.ToolTipText = MultiThreadTooltip();
        _multiThreadButton.AccessibleName = MultiThreadTooltip();
        // El estado se nota de TRES formas a la vez, que es lo que pedía el usuario:
        //   · el texto           «Multihilo · 4»  frente a  «Multihilo: no»
        //   · el fondo           azul de acento   frente a  apagado
        //   · el rayo            blanco encendido frente a  gris
        _multiThreadButton.BackColor = on ? _palette.Accent : _palette.Surface;
        _multiThreadButton.ForeColor = on ? Color.White : _palette.Muted;
        _multiThreadButton.Image = Icons.Glyph(Icons.Lightning, on ? Color.White : _palette.Muted, 18);
        _multiThreadButton.Font = on ? _barFontBold : _barFont;
    }

    private string MultiThreadLabel() =>
        _settings.MultiThreadCopy ? $"Multihilo · {_settings.Parallelism}" : "Multihilo: no";

    private string MultiThreadTooltip() => _settings.MultiThreadCopy
        ? $"Multihilo ACTIVADO: hasta {_settings.Parallelism} archivos a la vez entre discos DISTINTOS. Dentro del mismo disco XoPy copia de uno en uno igualmente, porque en paralelo tarda más. Pulsa para apagarlo."
        : "Multihilo APAGADO: un archivo cada vez (lo más rápido en un disco duro o una memoria USB). Pulsa para activarlo al copiar entre dos discos rápidos distintos.";

    /// <summary>
    /// Activa o desactiva el multihilo con un solo clic y refleja el cambio. Si hay una
    /// copia en marcha el cambio se aplica EN CALIENTE: el motor sube o baja el número de
    /// archivos simultáneos sin parar la transferencia.
    /// </summary>
    private void ToggleMultiThread()
    {
        _settings.MultiThreadCopy = !_settings.MultiThreadCopy;
        _store.Save(_settings);
        UpdateMultiThreadButton();
        var threads = _settings.MultiThreadCopy ? Math.Clamp(_settings.Parallelism, 1, 16) : 1;
        try { _engine.SetParallelism(threads); }
        catch { }
        SetStatus(_settings.MultiThreadCopy
            ? $"Multihilo activado: {threads} archivos a la vez."
            : "Multihilo apagado: un archivo cada vez.");
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
        SetStatus(pause ? $"Pausando {Format.Files(files.Count)}…" : $"Reanudando {Format.Files(files.Count)}…");
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
            ? "Reanudar lo seleccionado"
            : "Pausar lo seleccionado";
    }

    private void ListItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Item is not ListViewItem row || row.Tag is not TransferItem item) return;

        // Si se arrastra una fila que forma parte de la selección, se mueven TODAS: antes
        // se seleccionaban cinco archivos, se arrastraban y solo viajaba uno.
        var dragging = SelectedItems();
        if (!dragging.Any(selected => selected.Id == item.Id)) dragging = new List<TransferItem> { item };

        var payload = new DataObject();
        payload.SetData("XoPy.QueueItem", string.Join(";", InVisualOrder(dragging).Select(entry => entry.Id.ToString("D"))));
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
            if (e.Data.GetData("XoPy.QueueItem") is string idText)
            {
                var ids = idText.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => Guid.TryParse(part, out var parsed) ? parsed : Guid.Empty)
                    .Where(parsed => parsed != Guid.Empty)
                    .ToList();
                if (ids.Count > 0) ReorderByDrop(ids, _list.PointToClient(new Point(e.X, e.Y)));
            }
            return;
        }
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) DropPaths(e);
    }

    /// <summary>
    /// Suelta el elemento arrastrado DELANTE de la fila donde se soltó (o detrás, si se
    /// soltó en su mitad de abajo). No hay límites: un archivo puede cruzarse con los de
    /// cualquier otra carpeta.
    ///
    /// Lo único que cambia es el ORDEN. La carpeta de origen, la ruta relativa y la carpeta
    /// de destino se quedan como estaban: arrastrar una fila nunca cambia dónde se copia un
    /// archivo, solo cuándo. La lista lo enseña con la cabecera de su propia carpeta, así
    /// que siempre se ve de dónde sale y a dónde va.
    /// </summary>
    private void ReorderByDrop(List<Guid> draggedIds, Point point)
    {
        var sources = InVisualOrder(_items.Where(item => draggedIds.Contains(item.Id)));
        if (sources.Count == 0) return;

        var block = new HashSet<Guid>();
        foreach (var source in sources) block.UnionWith(BlockIds(source));

        var targetRow = _list.HitTest(point).Item;
        if (targetRow?.Tag is TransferItem target)
        {
            if (sources.Any(source => source.Id == target.Id)) return;
            if (block.Contains(target.Id))
            {
                SetStatus("Una carpeta no se puede meter dentro de sí misma.");
                return;
            }
        }

        // Se busca el ancla: la primera fila de archivo (o de carpeta plegada o vacía) que
        // hay a partir del punto donde se soltó. Las cabeceras de carpeta no valen como
        // ancla porque no ocupan sitio propio: se dibujan delante de sus archivos.
        var rows = _list.Items.Cast<ListViewItem>().Select(row => row.Tag).OfType<TransferItem>().ToList();
        int dropIndex;
        if (targetRow is not null)
        {
            dropIndex = targetRow.Index;
            if (point.Y >= targetRow.Bounds.Top + targetRow.Bounds.Height / 2) dropIndex++;
        }
        else
        {
            // Soltado en el hueco: arriba del todo o, lo normal, debajo de la última fila
            // para mandarlo al final. Antes no pasaba nada y parecía que el arrastre
            // estuviera roto.
            var last = _list.Items.Count > 0 ? _list.Items[^1] : null;
            dropIndex = last is not null && point.Y > last.Bounds.Bottom ? rows.Count : 0;
        }

        Guid? anchor = null;
        for (var index = dropIndex; index < rows.Count; index++)
        {
            var candidate = rows[index];
            if (block.Contains(candidate.Id)) continue;
            if (ChildCount(candidate) > 0)
            {
                // Carpeta abierta: es solo una cabecera, se mira la fila siguiente. Plegada:
                // es una fila de verdad, y su primer archivo marca el sitio.
                if (candidate.IsExpanded) continue;
                var inside = LeafOrder().FirstOrDefault(leaf => BlockIds(candidate).Contains(leaf.Id));
                if (inside is null) continue;
                anchor = inside.Id;
                break;
            }
            anchor = candidate.Id;
            break;
        }

        var moved = 0;
        foreach (var source in sources)
            if (MoveBlockBefore(source, anchor)) moved++;

        if (moved == 0)
        {
            SetStatus("La fila ya estaba en ese sitio.");
            return;
        }

        RebuildRows(sources[0].Id);
        foreach (var source in sources)
            if (_rows.TryGetValue(source.Id, out var row)) row.Selected = true;
        UpdateFooter();
        var first = sources[0];
        SetStatus(sources.Count > 1
            ? $"{Format.Files(sources.Count)} movidos en la lista. Cada uno sigue copiándose a donde iba."
            : first.Kind == ItemKind.Folder
                ? $"«{first.Name}» movida con todo su contenido. Se copia al mismo sitio de antes."
                : $"«{first.Name}» movido en la lista. Se sigue copiando a {FolderOf(first)}.");
    }

    /// <summary>Carpeta de destino de un archivo, en corto, para contarlo en el pie.</summary>
    private string FolderOf(TransferItem item)
    {
        var relative = item.RelativePath ?? string.Empty;
        var folder = Path.GetDirectoryName(relative);
        return string.IsNullOrWhiteSpace(folder) ? "la carpeta de destino" : $"«{folder}»";
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
        // Como el Explorador de Windows: pegar donde ya está el archivo crea una copia con otro
        // nombre, sin preguntar y sin tocar el original. Antes salía un diálogo por cada archivo.
        var samePath = string.Equals(Path.GetFullPath(prompt.SourcePath), Path.GetFullPath(prompt.TargetPath),
            StringComparison.OrdinalIgnoreCase);
        if (samePath) return ConflictChoice.Rename;

        // Ventana propia: compara los dos archivos lado a lado (tamaño y fecha, con el más
        // nuevo y el más grande señalados) en vez del muro de texto con dos rutas largas.
        BringUpForPrompt($"conflicto con {Path.GetFileName(prompt.TargetPath)}");
        using var dialog = new ConflictDialog(_palette, prompt);
        var result = dialog.ShowDialog(this);
        FileLog.Write("La copia continúa: el usuario ya ha contestado al conflicto.");
        var choice = result == DialogResult.OK ? dialog.Choice : 3;
        var applyAll = dialog.ApplyToAll;

        return choice switch
        {
            0 => applyAll ? ConflictChoice.OverwriteAll : ConflictChoice.Overwrite,
            1 => applyAll ? ConflictChoice.SkipAll : ConflictChoice.Skip,
            2 => applyAll ? ConflictChoice.RenameAll : ConflictChoice.Rename,
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

    /// <summary>
    /// Saca la ventana al frente ANTES de preguntar. La copia se queda esperando la
    /// respuesta, así que un cuadro escondido detrás de otras ventanas (o en la bandeja)
    /// parecía una copia colgada: minutos sin avanzar y sin saber por qué. También queda
    /// anotado en el registro, para que luego se vea dónde se estuvo esperando.
    /// </summary>
    private void BringUpForPrompt(string reason)
    {
        FileLog.Write($"La copia se detiene a preguntar ({reason}); esperando respuesta del usuario.");
        try
        {
            SetStatus($"Esperando tu respuesta: {reason}. La copia sigue parada hasta que contestes.");
            if (!Visible || WindowState == FormWindowState.Minimized) RestoreWindow();
            Activate();
            // Un parpadeo en la barra de tareas por si la ventana está detrás de otra cosa.
            try { FlashWindow(Handle, true); } catch { }
        }
        catch { }
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindow(IntPtr hwnd, bool invert);

    private ErrorChoice AskError(ErrorPrompt prompt)
    {
        BringUpForPrompt($"error con {Path.GetFileName(prompt.FilePath)}");
        // «Hacer lo mismo con el resto»: cuando falla una carpeta entera por la misma causa
        // (doscientos archivos con el mismo «acceso denegado»), contestar doscientas veces
        // no es una opción. Marcando la casilla, la respuesta vale para todos los que
        // fallen después.
        // Si el fallo es la protección contra ransomware de Windows, el cuadro ofrece
        // ARREGLARLO ahí mismo: permitir XoPy y reintentar. Antes solo quedaba omitir
        // archivo por archivo sin saber por qué fallaban todos.
        var guarded = prompt.Message.Contains("Acceso controlado", StringComparison.OrdinalIgnoreCase);
        var buttons = guarded
            ? new[] { "Permitir XoPy y reintentar", "Reintentar", "Omitir", "Omitir todos", "Detener" }
            : new[] { "Reintentar", "Omitir", "Omitir todos", "Detener" };
        var answer = PromptDialog.Ask(this, _palette, "Error de transferencia",
            "No se pudo transferir \"" + Path.GetFileName(prompt.FilePath) + "\".",
            prompt.Message + $"\n\nIntento {prompt.Attempt}. Archivo: {prompt.FilePath}",
            buttons,
            "Hacer lo mismo con el resto de archivos que fallen", out var applyToAll);
        var choice = guarded ? answer : answer + 1;
        if (choice == 0)
        {
            if (CopyEngine.TryAllowInControlledFolderAccess(out var message)) SetStatus(message);
            else ShowError(message);
            return ErrorChoice.Retry;
        }
        if (applyToAll && choice == 2) return ErrorChoice.SkipAll;
        return choice switch
        {
            1 => ErrorChoice.Retry,
            2 => ErrorChoice.Skip,
            3 => ErrorChoice.SkipAll,
            _ => ErrorChoice.Abort
        };
    }

    private void OnEngineFinished()
    {
        _wasRunning = false;
        // La lista que acaba de terminar puede NO ser la que se está viendo (se puede
        // preparar otra mientras se copia): los recuentos y el historial se hacen sobre la
        // que ha copiado de verdad, no sobre la que hay delante.
        var finishedList = _runningList;
        var finishedItems = finishedList?.Items ?? _items;
        _runningList = null;
        _pauseButton.Text = "Pausar";
        _pauseButton.Image = Icons.Glyph(Icons.Pause, _palette.Text, 18);
        _pauseButton.BackColor = _palette.Raised;
        _pauseButton.ForeColor = _palette.Text;
        if (_trayPauseItem is not null) _trayPauseItem.Text = "Pausar";

        _sessionBytes = _engine.DoneBytes;
        _sessionElapsed = _engine.Elapsed;

        var failed = finishedItems.Count(item => item.State == ItemState.Failed);
        var cancelled = finishedItems.Count(item => item.State == ItemState.Cancelled);
        var skipped = finishedItems.Count(item => item.State == ItemState.Skipped);
        var completed = finishedItems.Count(item => item.State == ItemState.Completed && item.Kind != ItemKind.Folder);

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
            ? $"Terminado con {Format.Plural(failed, "error", "errores")}. Revisa la lista y el registro."
            : cancelled > 0
                ? "Transferencia cancelada."
                : skipped > 0
                    ? "Transferencia completada con archivos omitidos."
                    : "Transferencia completada correctamente.");

        // Si quedan copias esperando turno, el icono NO vuelve al de XoPy todavía: enseña
        // el 100 % de la que acaba de terminar con la chapa de las que siguen. El icono
        // normal vuelve cuando termina la última (TimerTick → RestoreBaseTrayIcon).
        var pending = PendingJobPercents();
        var icon = Icons.ProgressIcon(100, _settings.DarkTheme, false, failed > 0, failed == 0, 1 + pending.Count);
        ReplaceTrayIcon(icon);
        SetTrayText(pending.Count > 0
            ? $"XoPy — terminada; quedan {Format.Plural(pending.Count, "copia", "copias")}"
            : failed > 0 ? $"XoPy — {Format.Plural(failed, "error", "errores")}" : "XoPy — terminado (100 %)");
        _trayPercent = -1;
        _trayJobs = 0;
        _completedAt = DateTime.Now;
        Text = failed > 0 ? "XoPy — completado con errores" : "XoPy — completado";

        // Si ya no queda nada pendiente EN LA LISTA QUE SE VE, la vista «En cola» se quedaría
        // vacía: se pasa sola a «Finalizados» para enseñar el resultado en vez de una lista
        // en blanco. Si se está mirando otra lista con cosas pendientes, no se toca nada.
        var quedaPendiente = _items.Any(item => item.Kind != ItemKind.Folder && !IsFinished(item));
        if (!quedaPendiente && _items.Count > 0 && !_showFinished) _showFinished = true;
        UpdateQueueViewButtons();

        _finishedCount = -1;
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

        var followUpLaunched = false;
        if (_followUpOperation is { } followUp)
        {
            _followUpOperation = null;
            if (failed == 0 && cancelled == 0 && finishedItems.Any(item => item.Kind != ItemKind.Folder && item.State != ItemState.Completed))
            {
                followUpLaunched = true;
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

        // ¿Hay alguna lista ESPERANDO TURNO? Arranca sola, que es lo que el usuario espera al
        // haber elegido «Esperar turno» mientras había otra copia en marcha. Si ya se ha
        // relanzado lo que quedaba de esta misma lista, la de espera va después de esa.
        if (!followUpLaunched && StartNextQueuedList()) return;

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
    /// Lanza la siguiente lista que estaba esperando turno. Devuelve true si ha arrancado una.
    /// </summary>
    private bool StartNextQueuedList()
    {
        var next = _lists.FirstOrDefault(list => list.Queued && list.Items.Count > 0);
        if (next is null) return false;
        next.Queued = false;
        var operation = next.QueuedOperation;
        BeginInvoke(new Action(() =>
        {
            try
            {
                SwitchToList(next);
                SetStatus($"Empieza «{next.DisplayName}», que estaba esperando turno.");
                StartTransfer(operation);
            }
            catch (Exception ex)
            {
                FileLog.Write("No se pudo arrancar la lista en espera: " + ex.Message);
            }
        }));
        return true;
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
                // Sin tocar ShowInTaskbar: cambiarlo recrea la ventana y, maximizada, la
                // devolvía a la pantalla ella sola.
                Hide();
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
            ApplyJobsLayout();
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
            // Cuando la columna desaparece, su botón de dentro se va con ella: el de la cabecera
            // de la lista es el único camino de vuelta, así que deja de ser un chevrón suelto y
            // pasa a decir qué hace. Al volver a verse la columna recupera el tamaño de icono.
            if (_collapseButton is not null)
            {
                _collapseButton.Text = visible ? "\u00AB" : "\u00BB  Listas";
                _collapseButton.AutoSize = false;
                _collapseButton.Width = visible ? 26 : 86;
                _collapseButton.AccessibleName = visible
                    ? "Ocultar la columna de listas (F8)"
                    : "Mostrar la columna de listas (F8)";
                _collapseButton.ForeColor = visible ? _palette.Text : _palette.Accent;
            }
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

    /// <summary>
    /// Coloca el panel TRABAJOS según el ajuste guardado y la vista actual. Es el ÚNICO
    /// sitio que toca el ancho de esa columna: la vista reducida y el botón de ocultar
    /// escribían los dos sobre ella y se pisaban entre sí.
    /// </summary>
    private void ApplyJobsLayout()
    {
        var show = _jobsVisible && !_compactView;
        if (_contentGrid is not null && _contentGrid.ColumnStyles.Count > 0)
            _contentGrid.ColumnStyles[0].Width = show ? 228 : 0;
        if (_jobsPanel is not null) _jobsPanel.Visible = show;
        // Al plegar la columna de listas, la lista de archivos se ensancha 228 px de golpe.
        // Si no se reparten otra vez las columnas, esos 228 px quedan a la derecha de la
        // última columna y la cabecera los pinta con el color del sistema: ES EL CUADRADO
        // BLANCO de la esquina derecha.
        ScheduleColumnUpdate();
    }

    /// <summary>
    /// Porcentaje de la copia en marcha. Se calcula con los datos del motor, no con la lista
    /// que se está mirando: si el usuario se pasa a otra lista, el icono de la bandeja y la
    /// barra de tareas tienen que seguir contando la copia de verdad.
    /// </summary>
    private double EnginePercent()
    {
        var total = _engine.TotalBytes;
        return total > 0 ? Math.Clamp(_engine.DoneBytes * 100d / total, 0, 100) : 0;
    }

    /// <summary>Elementos de la lista que el motor está copiando (para el icono y el título).</summary>
    private List<TransferItem> RunningItems()
        => _runningList is not null ? _runningList.Items : _items;

    private void TimerTick()
    {
        if (_engine.IsRunning)
        {
            SyncRows();
            UpdateTrayState();
            UpdateTitle();
            // Si se está mirando otra lista, la columna de trabajos es lo único que enseña
            // el avance de la copia: se refresca para que no se quede congelada.
            if (_runningList is not null && _runningList != _activeList) UpdateJobs();
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
                    var percent = (int)Math.Clamp(Math.Round(EnginePercent()), 0, 100);
                    var paused = _engine.IsPaused;
                    var error = RunningItems().Any(item => item.State == ItemState.Failed);
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
        if (!_engine.IsRunning)
        {
            Text = "XoPy — reemplazo del copiador de Windows";
            return;
        }
        var percent = EnginePercent();
        var speed = Format.Speed(_engine.SpeedBytesPerSecond);
        Text = _engine.IsPaused
            ? $"XoPy — en pausa · {percent:0.0} %"
            : $"XoPy — {percent:0.0} % ({speed})";
    }

    /// <summary>
    /// EL ICONO DE LA BANDEJA ES EL PORCENTAJE. Mientras se copia, junto al reloj se ve el
    /// número de la copia en marcha; si hay más copias esperando turno, el icono lleva una
    /// chapita con cuántas son y el texto al pasar el ratón las enseña TODAS con su
    /// porcentaje. Según van terminando desaparecen de la lista, y cuando acaba la última
    /// vuelve el icono normal de XoPy (lo hace TimerTick con RestoreBaseTrayIcon).
    /// </summary>
    private void UpdateTrayState()
    {
        var percent = (int)Math.Clamp(Math.Round(EnginePercent()), 0, 100);
        var paused = _engine.IsPaused;
        var error = RunningItems().Any(item => item.State == ItemState.Failed);
        var others = PendingJobPercents();
        var jobs = 1 + others.Count;
        if (percent == _trayPercent && paused == _trayPaused && error == _trayError && jobs == _trayJobs) return;

        _trayPercent = percent;
        _trayPaused = paused;
        _trayError = error;
        _trayJobs = jobs;

        ReplaceTrayIcon(Icons.ProgressIcon(percent, _settings.DarkTheme, paused, error, percent >= 100, jobs));
        var text = jobs > 1
            ? $"XoPy — {jobs} copias: {percent} %, " + string.Join(", ", others.Select(value => value + " %"))
            : $"XoPy — {percent} %" + (paused ? " · en pausa" : string.Empty);
        SetTrayText(text);
    }

    /// <summary>
    /// Porcentaje de cada lista que espera turno (las «otras copias activas»). Una lista a
    /// medias enseña lo que lleva copiado, no un cero.
    /// </summary>
    private List<int> PendingJobPercents()
    {
        var result = new List<int>();
        foreach (var list in _lists)
        {
            if (!list.Queued || list.Items.Count == 0) continue;
            var files = list.Items.Where(item => item.Kind != ItemKind.Folder).ToList();
            if (files.Count == 0) continue;
            var total = files.Sum(item => Math.Max(0, item.SizeBytes));
            var done = files.Sum(item => Math.Max(0, Math.Min(item.TransferredBytes, item.SizeBytes)));
            result.Add(total > 0 ? (int)Math.Clamp(Math.Round(done * 100d / total), 0, 100) : 0);
        }
        return result;
    }

    /// <summary>
    /// El texto del icono de la bandeja no admite más de 63 caracteres: pasarse de ahí
    /// tumba el programa con una excepción. Se recorta siempre antes de asignarlo.
    /// </summary>
    private void SetTrayText(string text)
    {
        try
        {
            if (text.Length > 62) text = text[..59] + "…";
            _tray.Text = text;
        }
        catch { }
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
            _tray.Text = TrayBaseText;
            _trayPercent = -1;
            _trayJobs = 0;
        }
        catch { }
    }

    private void UpdateButtons()
    {
        if (_copyButton is null) return;
        var running = _engine.IsRunning;
        // ¿Se está viendo la lista que el motor está copiando, o se está preparando otra?
        var viewingRunning = running && (_runningList is null || _runningList == _activeList);
        var hasItems = _items.Count > 0;

        // Añadir archivos está SIEMPRE permitido: se puede ir preparando la siguiente lista
        // mientras la de delante copia.
        _addFilesButton.Enabled = true;
        _addFolderButton.Enabled = true;
        // Copiar/Mover/Verificar siguen activos con una copia en marcha: al pulsarlos se
        // pregunta si esta lista espera turno, se mete en la de delante o va en paralelo.
        _copyButton.Enabled = hasItems && !viewingRunning;
        _moveButton.Enabled = hasItems && !viewingRunning;
        _testButton.Enabled = hasItems && !viewingRunning;
        _pauseButton.Enabled = running;
        _skipButton.Enabled = running;
        _stopButton.Enabled = running;
        _verifyCheck.Enabled = !viewingRunning;
        // Reordenar y quitar de la LISTA se permite siempre: el motor trabaja con su propia
        // instantánea, así que no cancela ni corrompe la copia. Antes se deshabilitaban con
        // cualquier transferencia en curso y parecía que los botones «no funcionaban».
        _removeButton.Enabled = hasItems;
        _clearButton.Enabled = hasItems;
        _upButton.Enabled = hasItems;
        _downButton.Enabled = hasItems;
        // El destino solo se bloquea en la lista que se está copiando; en las demás se puede
        // elegir destino mientras tanto.
        _destination.ReadOnly = viewingRunning;
        if (_trayPauseItem is not null) _trayPauseItem.Enabled = running;
    }

    /// <summary>
    /// Pie de la ventana: barra de progreso, estado, avance en bytes y velocidad. Cada dato
    /// aparece aquí una sola vez; el archivo en curso y el destino van en la cabecera.
    /// </summary>
    private void UpdateFooter()
    {
        if (_progress is null) return;

        var leaves = _items.Where(item => item.Kind != ItemKind.Folder).ToList();
        long total;
        long done;
        double percent;

        // Se puede estar MIRANDO una lista distinta de la que el motor está copiando. En ese
        // caso los totales son los de la lista que se ve, no los del motor.
        if (_engine.IsRunning && (_runningList is null || _runningList == _activeList))
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
        UpdateCountLabel(leaves.Count, total);
        _progress.Value = percent;
        _progress.FillColor = _items.Any(item => item.State == ItemState.Failed) && !_engine.IsRunning
            ? _palette.Warning
            : _palette.Accent;
        // Sin esto, en reposo la barra se quedaba en blanco aunque hubiera porcentaje.
        _progress.Invalidate();

        // El número de archivos de la vista está en la cabecera de la lista: aquí solo el
        // avance en bytes, que es lo único que no se dice en ninguna otra parte.
        _summaryLabel.Text = leaves.Count == 0
            ? "Sin archivos en la lista"
            : $"{Format.Bytes(done)} de {Format.Bytes(total)} · {percent:0.0} %";

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
            // Parado: el tamaño total ya sale en el resumen de la barra de progreso («… de
            // 22,0 KB») y el gráfico de al lado ya dice «Velocidad · Sin actividad».
            // Esta etiqueta se queda vacía para no decir lo mismo por tercera vez.
            _speedLabel.Text = string.Empty;
        }

        var runningLeaf = leaves.FirstOrDefault(item => item.State is ItemState.Running or ItemState.Verifying);
        if (_engine.IsRunning)
        {
            // El archivo en curso YA está en la primera línea de la cabecera. Aquí se dice
            // lo que queda, que es el dato que faltaba y no se repite en ninguna otra parte.
            var pending = leaves.Count(item => !IsFinished(item) && item.State != ItemState.Failed);
            var pendingBytes = leaves.Where(item => !IsFinished(item) && item.State != ItemState.Failed)
                .Sum(item => Math.Max(0, item.SizeBytes - Math.Max(0, item.TransferredBytes)));
            _currentLabel.Text = _engine.IsPaused
                ? $"En pausa · quedan {Format.Files(pending)} · {Format.Bytes(pendingBytes)}"
                : $"Quedan {Format.Files(pending)} · {Format.Bytes(pendingBytes)}";
        }
        else if (_items.Count > 0)
        {
            // Parado y con lista: esta línea se queda EN BLANCO. La barra de estado de abajo
            // ya dice «Lista preparada: N archivos listos para transferir», y antes aquí
            // ponía «Listo para transferir. Elige Copiar, Mover, Test o Eliminar.» (lo mismo,
            // y encima con nombres de botones que ya no existen).
            _currentLabel.Text = string.Empty;
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
        UpdateMultiThreadButton();
        // La ruta completa puede haber cambiado en Preferencias: cabecera y filas al día.
        if (_list is not null)
        {
            _list.Columns[0].Text = _settings.ShowFullPath ? "Archivo (ruta completa)" : "Nombre";
            _list.Invalidate();
        }
        if (_fullPathMenuItem is not null) _fullPathMenuItem.Checked = _settings.ShowFullPath;
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

        // Reordenar NUNCA cambia la jerarquía: solo el orden en que se copia.
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

        // ---- Vista «En cola» / «Finalizados» -------------------------------------------
        // Lo que ya está copiado tiene que SALIR de la lista por defecto y aparecer en la
        // otra pestaña. Es la petición directa del usuario.
        SortQueue(QueueSortField.Name);
        child1.State = ItemState.Completed;
        _showFinished = false;
        RebuildRows();
        if (_list.Items.Cast<ListViewItem>().Any(row => ReferenceEquals(row.Tag, child1)))
            report.AppendLine("FALLO: un archivo ya copiado sigue en la lista «En cola».");
        if (!_list.Items.Cast<ListViewItem>().Any(row => ReferenceEquals(row.Tag, child2)))
            report.AppendLine("FALLO: un archivo pendiente no aparece en la lista «En cola».");

        _showFinished = true;
        RebuildRows();
        if (!_list.Items.Cast<ListViewItem>().Any(row => ReferenceEquals(row.Tag, child1)))
            report.AppendLine("FALLO: el archivo copiado no aparece en «Finalizados».");
        if (_list.Items.Cast<ListViewItem>().Any(row => ReferenceEquals(row.Tag, child2)))
            report.AppendLine("FALLO: un archivo pendiente aparece en «Finalizados».");
        child1.State = ItemState.Pending;
        _showFinished = false;
        RebuildRows();

        // ---- Jerarquía carpeta → subcarpeta → archivo -----------------------------------
        var tree = OrderedTree();
        if (tree.IndexOf(child1) <= tree.IndexOf(folder) || tree.IndexOf(child2) <= tree.IndexOf(folder))
            report.AppendLine("FALLO: los archivos no se colocan debajo de su carpeta.");
        if (child1.Depth != 1 || folder.Depth != 0)
            report.AppendLine("FALLO: la sangría del árbol no corresponde con la jerarquía.");

        // Reordenar dentro de la carpeta no puede cambiar ni el padre ni la ruta de destino.
        var rutaAntes = child2.RelativePath;
        var padreAntes = child2.ParentId;
        MoveBlock(child2, -1);
        if (child2.ParentId != padreAntes || child2.RelativePath != rutaAntes)
            report.AppendLine("FALLO: reordenar dentro de la carpeta cambió la raíz o el destino.");
        var hermanos = OrderedTree().Where(item => item.ParentId == folder.Id).ToList();
        if (hermanos.Count != 2)
            report.AppendLine("FALLO: reordenar sacó un archivo de su carpeta.");

        // Mover un archivo suelto no lo mete dentro de ninguna carpeta.
        var antesPadreA = a.ParentId;
        MoveBlock(a, 1);
        MoveBlock(a, -1);
        if (a.ParentId != antesPadreA)
            report.AppendLine("FALLO: mover un archivo suelto lo metió dentro de una carpeta.");

        // ---- Orden LIBRE entre carpetas distintas --------------------------------------
        // Lo que pidió el usuario: un archivo de «Otra» se cuela entre los dos de
        // «Carpeta». La lista tiene que partir el tramo y repetir la cabecera, para que se
        // siga viendo de qué carpeta sale cada archivo:
        //   Carpeta · foto1 · Otra · documento · Carpeta · foto2
        SortQueue(QueueSortField.Name);
        var rutaOtro = otherChild.RelativePath;
        var padreOtro = otherChild.ParentId;
        if (!MoveBlockBefore(otherChild, child2.Id))
            report.AppendLine("FALLO: no se pudo cruzar un archivo con los de otra carpeta.");
        RebuildRows();
        var cruzado = _list.Items.Cast<ListViewItem>()
            .Select(row => (row.Tag as TransferItem)?.Name ?? "?").ToList();
        if (otherChild.ParentId != padreOtro || otherChild.RelativePath != rutaOtro)
            report.AppendLine("FALLO: cruzar un archivo con otra carpeta le cambió la raíz o el destino.");
        if (!cruzado.SequenceEqual(new[]
            { "a.txt", "b.txt", "Carpeta", "foto1.jpg", "Otra", "documento.txt", "Carpeta", "foto2.jpg" }))
            report.AppendLine("FALLO: la carpeta partida no repite su cabecera en cada tramo: "
                + string.Join(" · ", cruzado));

        // Y el motor tiene que copiar EN ESE ORDEN, no en el de llegada.
        var copiaria = InVisualOrder(_items.Where(item => item.Kind == ItemKind.File))
            .Select(item => item.Name).ToList();
        if (!copiaria.SequenceEqual(new[] { "a.txt", "b.txt", "foto1.jpg", "documento.txt", "foto2.jpg" }))
            report.AppendLine("FALLO: el motor no copiaría en el orden de la lista: "
                + string.Join(" · ", copiaria));

        // Plegar la carpeta partida la vuelve a juntar: si no, los trozos de fuera del
        // primer tramo no se verían por ninguna parte.
        folder.IsExpanded = false;
        RebuildRows();
        var plegada = _list.Items.Cast<ListViewItem>()
            .Select(row => (row.Tag as TransferItem)?.Name ?? "?").ToList();
        if (plegada.Count(name => name == "Carpeta") != 1 || plegada.Contains("foto2.jpg"))
            report.AppendLine("FALLO: la carpeta plegada no se enseña como una sola fila: "
                + string.Join(" · ", plegada));
        folder.IsExpanded = true;
        RebuildRows();

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
            $"XoPy {version}\n(reemplazo del copiador de Windows)\n\n" +
            "Gestor de copias gratuito para Windows, inspirado en la interfaz de TeraCopy.\n\n" +
            "· Copia, mueve, elimina y verifica archivos o carpetas completas\n" +
            "· Cola jerárquica: expande carpetas, reordena y quita elementos\n" +
            "· Verificación CRC32, MD5, SHA-1, SHA-256 y SHA-512\n" +
            "· Pausa, saltar, detener, límite de velocidad y transferencias paralelas\n" +
            "· Icono de porcentaje en la bandeja del sistema\n" +
            "· Historial, favoritos e integración con el Explorador\n\n" +
            "Hecho por Quevedo-crack.",
            "Acerca de XoPy", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// Enseña un error. CON UNA COPIA EN MARCHA NO SE ABRE NINGÚN CUADRO: se escribe en la
    /// barra de estado y en el registro. Abrir cuadros mientras se copia es lo que llenaba
    /// la pantalla de avisos apilados sobre una transferencia en curso y dejaba la ventana
    /// sin responder hasta cerrarlos uno a uno.
    /// </summary>
    private void ShowError(string message)
    {
        FileLog.Write("Error mostrado al usuario: " + message);
        SetStatus(message);
        if (_engine.IsRunning)
        {
            FileLog.Write("Aviso mostrado solo en la barra de estado: hay una copia en marcha.");
            return;
        }
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

    private enum QueueSortField { Name, Size, FolderSize, Type, Destination, Source, State, AllSize, Modified }

    /// <summary>
    /// Fecha de modificación del origen, en caché. Se lee del disco una vez por elemento:
    /// preguntarle al sistema de archivos en cada comparación convertía una ordenación de
    /// 3.000 filas en miles de accesos a disco.
    /// </summary>
    private readonly Dictionary<string, DateTime> _modifiedCache = new(StringComparer.OrdinalIgnoreCase);

    private DateTime ModifiedOf(TransferItem item)
    {
        var path = item.SourcePath;
        if (string.IsNullOrEmpty(path)) return DateTime.MinValue;
        if (_modifiedCache.TryGetValue(path, out var cached)) return cached;
        var stamp = DateTime.MinValue;
        try
        {
            if (File.Exists(path)) stamp = File.GetLastWriteTimeUtc(path);
            else if (Directory.Exists(path)) stamp = Directory.GetLastWriteTimeUtc(path);
        }
        catch { }
        _modifiedCache[path] = stamp;
        return stamp;
    }

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
            QueueSortField.Modified => (left, right) => ModifiedOf(left).CompareTo(ModifiedOf(right)),
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
        // Se recuerda SIEMPRE, también cuando se ordena desde el menú: así la flechita de la
        // cabecera dice por dónde está ordenada la lista y el siguiente clic invierte.
        _lastSortField = field;
        _lastSortInverted = invert;
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
            QueueSortField.Modified => "fecha de modificación",
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

        // LAS CARPETAS SE ABREN. Una carpeta PLEGADA es una sola fila, así que la lista
        // vuelve a juntar todos sus archivos (GatherBlock) y deshacía justo lo que se
        // acaba de ordenar: por eso «los más grandes primero» no se notaba ni en la lista
        // ni en el orden real de la copia. Con las carpetas abiertas, el orden por tamaño
        // de archivo se respeta de principio a fin.
        foreach (var folder in _items.Where(item => item.Kind == ItemKind.Folder)) folder.IsExpanded = true;

        _visualOrder.Clear();
        _visualOrder.AddRange(files.Select(item => item.Id));
        // Las carpetas van al principio de su nivel para que el árbol siga teniendo sentido.
        var folders = _items.Where(item => item.Kind == ItemKind.Folder).Select(item => item.Id);
        _visualOrder.InsertRange(0, folders);

        RebuildRows();
        UpdateFooter();
        var mayor = Format.Bytes(files[0].SizeBytes);
        SetStatus(invert
            ? $"Primero los archivos más grandes (el mayor ocupa {mayor}). Se copiarán en ese orden, vengan de la carpeta que vengan."
            : "Primero los archivos más pequeños. Se copiarán en ese orden, vengan de la carpeta que vengan.");
    }

    /// <summary>
    /// Le da la vuelta al orden que haya puesto ahora mismo. Evita tener que repetir cada
    /// criterio dos veces en el menú («A → Z» y «Z → A», «mayor → menor» y al revés…),
    /// que era la mitad de las opciones repetidas que molestaban al usuario.
    /// </summary>
    private void InvertCurrentSort()
    {
        if (_items.Count < 2) { SetStatus("La lista está vacía."); return; }
        if (_lastSortField is null)
        {
            // Sin orden previo (lista colocada a mano): se le da la vuelta tal cual.
            var reversed = VisualItems();
            reversed.Reverse();
            _visualOrder.Clear();
            _visualOrder.AddRange(reversed.Select(item => item.Id));
            RebuildRows();
            UpdateFooter();
            SetStatus("Lista del revés.");
            return;
        }
        if (_lastSortField == QueueSortField.AllSize) SortAllFilesBySize(!_lastSortInverted);
        else SortQueue(_lastSortField.Value, !_lastSortInverted);
    }

    private QueueSortField? _lastSortField;
    private bool _lastSortInverted;

    /// <summary>
    /// Columna que lleva la flechita de orden, o -1 si la lista está ordenada por algo que
    /// no es una columna (el orden libre a mano, por ejemplo).
    /// </summary>
    private int SortedColumn() => _lastSortField switch
    {
        QueueSortField.Name => 0,
        QueueSortField.Destination => 1,
        QueueSortField.Size or QueueSortField.FolderSize or QueueSortField.AllSize => 2,
        QueueSortField.State => 3,
        _ => -1
    };

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

        // Un clic ordena, el siguiente invierte. Y PUNTO. Antes, al tercer clic en «Tamaño»
        // la lista cambiaba sola a «todos los archivos mezclados sin carpetas» y parecía que
        // se hubiera descolocado: ese modo ahora solo se pide a propósito, desde el menú
        // Ordenar o con el botón derecho.
        var invert = _lastSortField == field && !_lastSortInverted;
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
            .AppendLine($"<p>{Format.Files(_items.Count(item => item.State == ItemState.Completed && item.Kind == ItemKind.File))} copiados · {Format.Bytes(_sessionBytes)} · {Format.Duration(_sessionElapsed)} · {Format.Plural(failed, "error", "errores")}</p>")
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
            // Solo se toca si de verdad hace falta: asignarla recrea la ventana y da un
            // parpadeo (y con la ventana maximizada, saltos raros).
            if (!ShowInTaskbar) ShowInTaskbar = true;
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
            if (string.IsNullOrEmpty(_tray.Text)) _tray.Text = TrayBaseText;
            _tray.Visible = true;
        }
        catch (Exception ex)
        {
            FileLog.Write("No se pudo mostrar el icono de la bandeja: " + ex.Message);
        }
    }

    /// <summary>
    /// Garantiza que la ventana se ve. Es la última red de seguridad contra el peor fallo
    /// posible: que XoPy esté en ejecución y el usuario no vea nada por ninguna parte.
    /// </summary>
    private void EnsureWindowVisible(string motivo = "")
    {
        try
        {
            Program.Log("EnsureWindowVisible " + motivo);
            _startHidden = false;
            if (!ShowInTaskbar) ShowInTaskbar = true;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (!Visible) Show();
            Activate();
            BringToFront();
        }
        catch (Exception ex)
        {
            FileLog.Write("No se pudo mostrar la ventana: " + ex.Message);
        }
    }

    /// <summary>
    /// Oculta la ventana en el área de notificación. Con <paramref name="silent"/> no muestra
    /// globo. Si el icono de la bandeja NO llega a aparecer, la ventana se queda visible:
    /// sin icono y sin ventana, XoPy estaría en marcha sin forma humana de abrirlo.
    /// </summary>
    private void MinimizeToTray(bool silent = false)
    {
        EnsureTrayIcon();
        Program.Log($"MinimizeToTray icono_visible={_tray.Visible}");
        if (!_tray.Visible)
        {
            FileLog.Write("No hay icono en la bandeja: la ventana se deja visible para no perder XoPy de vista.");
            EnsureWindowVisible();
            return;
        }
        // OJO: aquí NO se toca ShowInTaskbar. Cambiar esa propiedad obliga a Windows a
        // volver a crear la ventana, y con la ventana maximizada eso la devolvía a la
        // pantalla justo después de cerrarla («se cierra y se vuelve a abrir sola»). Una
        // ventana oculta ya no tiene botón en la barra de tareas, así que no hacía falta.
        Hide();
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
        // El reparto de franjas (barra, cabecera, pestañas, lista y pie) se recalcula aquí
        // mismo: esperar al temporizador dejaba el pie fuera de sitio durante el arrastre.
        try { LayoutRootRows(); } catch { }
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

        // Cerrar con la X NO termina el proceso: XoPy se queda junto al reloj esperando, como
        // SuperCopier y TeraCopy, y basta con pulsar su icono para volver a abrirlo. Es lo que
        // pidió el usuario. Para salir de verdad: clic derecho en el icono → Salir (o el menú
        // Archivo → Salir), que es lo que pone _exitRequested a true.
        // Mientras hay una transferencia en marcha SIEMPRE se va a la bandeja, aunque el
        // ajuste esté desactivado: cerrar la ventana no puede abortar una copia a medias.
        var keepAlive = _settings.MinimizeToTray || _engine.IsRunning;
        FileLog.Write($"Cierre de la ventana: motivo={e.CloseReason}, salir_pedido={_exitRequested}, " +
                      $"bandeja={_settings.MinimizeToTray}, copiando={_engine.IsRunning}");
        if (!_exitRequested && keepAlive && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            MinimizeToTray();
            if (!_settings.TrayHintShown)
            {
                _settings.TrayHintShown = true;
                _store.Save(_settings);
                try
                {
                    _tray.ShowBalloonTip(5000, "XoPy sigue aquí",
                        "No se ha cerrado: está junto al reloj. Pulsa su icono para volver a abrirlo. " +
                        "Para salir del todo: clic derecho en el icono → Salir.", ToolTipIcon.Info);
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

        // Cierre limpio: se retira la marca del gancho de teclado. Si XoPy se cuelga o lo
        // matan, la marca se queda y el próximo arranque sabe que algo fue mal.
        ClearHookGuard();
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
            _jobsBoldFont?.Dispose();
            _tabFontActive?.Dispose();
            _tabFontIdle?.Dispose();
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
            _barFont.Dispose();
            _barFontBold.Dispose();
        }
        base.Dispose(disposing);
    }
}
