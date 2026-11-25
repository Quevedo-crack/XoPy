using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace XoPy;

public sealed partial class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store = new();
    private readonly Palette _palette;
    private List<TransferItem> _items = new();
    private readonly Dictionary<Guid, ListViewItem> _rows = new();
    private readonly List<Task> _scanTasks = new();
    private readonly CopyEngine _engine = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 300 };
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private ToolStripMenuItem? _trayPauseItem;
    private readonly StartupRequest _startupRequest;
    private readonly OpenFileDialog _openFiles = new() { Multiselect = true, Title = "Añadir archivos", Filter = "Todos los archivos (*.*)|*.*" };
    private readonly FolderBrowserDialog _pickFolder = new() { Description = "Selecciona la carpeta que quieres añadir" };
    private readonly FolderBrowserDialog _pickDestination = new() { Description = "Selecciona la carpeta de destino" };
    private readonly SaveFileDialog _saveQueue = new() { Title = "Guardar cola", Filter = "Cola de XoPy (*.xopyq)|*.xopyq|JSON (*.json)|*.json", FileName = "xopy-cola.xopyq" };
    private readonly OpenFileDialog _loadQueue = new() { Title = "Abrir cola", Filter = "Cola de XoPy (*.xopyq)|*.xopyq|JSON (*.json)|*.json" };

    private TextBox _destination = null!;
    private CheckBox _verifyCheck = null!;
    private ToolStripButton _addFilesButton = null!;
    private ToolStripButton _addFolderButton = null!;
    private ToolStripButton _copyButton = null!;
    private ToolStripButton _moveButton = null!;
    private ToolStripButton _testButton = null!;
    private ToolStripButton _pauseButton = null!;
    private ToolStripButton _skipButton = null!;
    private ToolStripButton _stopButton = null!;
    private ToolStripButton _optionsButton = null!;
    private ToolStripButton _menuButton = null!;
    private ToolStripButton _multiThreadButton = null!;
    private Button _removeButton = null!;
    private Button _clearButton = null!;
    private Button _upButton = null!;
    private Button _downButton = null!;
    private ListView _list = null!;
    private Label _countLabel = null!;
    private Button _sortButton = null!;
    private PillLabel _hdrCopyState = null!, _hdrTargetState = null!;
    private Label _hdrCopyPath = null!, _hdrCopyInfo = null!, _hdrCopyPercent = null!;
    private Label _hdrTargetPath = null!, _hdrTargetInfo = null!, _hdrTargetPercent = null!;
    private ProgressStrip _hdrCopyBar = null!, _hdrTargetBar = null!;
    private SpeedGraph _footerGraph = null!;
    private TableLayoutPanel _rootLayout = null!;
    private TableLayoutPanel _transferHeader = null!;
    private ToolStripButton _compactButton = null!;
    private bool _compactView;
    /// <summary>Arranque en la bandeja: la ventana no debe mostrarse hasta que el usuario la pida.</summary>
    private bool _startHidden;
    private Rectangle _expandedBounds;
    private bool _expandedWasMaximized;
    private Label[,] _categoryValues = new Label[4, 3];
    private ListBox _extraDestinations = null!;
    private CheckBox _optLocked = null!, _optResume = null!, _optChecksums = null!;
    private ChoiceButton _optEngine = null!;
    private NumericUpDown _optSecurePasses = null!;
    private TextBox _optScript = null!;
    private Label _statusLabel = null!;
    private Label _summaryLabel = null!;
    private Label _speedLabel = null!;
    private Label _currentLabel = null!;
    private ProgressStrip _progress = null!;
    private ContextMenuStrip _listMenu = null!;
    private MenuStrip _menu = null!;

    private Control? _tabBar;
    private Button _tabFiles = null!;
    private Button _tabTarget = null!;
    private Button _tabOptions = null!;
    private Button _tabStatus = null!;
    private Button _tabLog = null!;
    private Panel _tabsHost = null!;
    /// <summary>Tapa la barra de desplazamiento nativa de la lista (blanca en tema oscuro).</summary>
    private Panel? _scrollCover;
    private Control[] _pages = Array.Empty<Control>();
    private int _tabIndex;

    private ListBox _jobsList = null!;
    private ListBox _targetList = null!;
    private Label _targetFreeLabel = null!;
    private TextBox _logBox = null!;
    private Label _stOperation = null!;
    private Label _stSource = null!;
    private Label _stTarget = null!;
    private Label _stTotals = null!;
    private Label _stSpeed = null!;
    private Label _stEta = null!;
    private Label _stErrors = null!;
    private Label _stPercent = null!;
    private ProgressStrip _stProgress = null!;
    private ChoiceButton _optConflict = null!;
    private ChoiceButton _optError = null!;
    private ChoiceButton _optVerify = null!;
    private NumericUpDown _optParallel = null!;
    private NumericUpDown _optBuffer = null!;
    private NumericUpDown _optRetries = null!;
    private NumericUpDown _optSpeedLimit = null!;
    private CheckBox _optVerifyAfterCopy = null!;
    private CheckBox _optPreserveTimestamps = null!;
    private CheckBox _optRecycleBin = null!;
    private CheckBox _optShowHashes = null!;

    private Icon? _baseIcon;
    private bool _trayBuilt;
    private Icon? _percentIcon;
    private bool _wasRunning;
    private bool _exitRequested;
    /// <summary>Decisión tomada en el diálogo de espacio insuficiente para esta copia.</summary>
    private FreeSpaceMode _freeSpaceMode = FreeSpaceMode.Ignore;
    private bool _loadingUi;
    private Label _metaFilter = null!, _metaCount = null!, _metaOrigin = null!, _metaOriginSize = null!;
    private Label _metaDestination = null!, _metaDestinationSize = null!;
    private bool _shown;
    /// <summary>La interfaz está construida y los controles existen (fin de OnShown).</summary>
    private volatile bool _uiReady;
    private int _trayPercent = -1;
    private TableLayoutPanel _contentGrid = null!;
    private Control _jobsPanel = null!;
    private Button? _jobsToggleButton;
    /// <summary>El historial de listas (columna de trabajos) está a la vista.</summary>
    private bool _jobsVisible = true;
    private Button _collapseButton = null!;
    private bool _jobsCollapsed;
    private OperationType? _followUpOperation;
    private ShellPasteHook? _pasteHook;
    private bool _shellHookInstalled;
    private CopyEngineKind? _engineOverride;
    private bool _trayPaused;
    private bool _trayError;
    private DateTime _completedAt = DateTime.MinValue;
    private OperationType _lastOperation = OperationType.Copy;
    private string _lastDestination = string.Empty;
    private long _sessionBytes;
    private double _lastPercent;
    private TimeSpan _sessionElapsed;

    // ---------------------------------------------------------------- listas de copia múltiples en una misma pestaña

    private readonly List<TransferList> _lists = new();
    private TransferList? _activeList;
    private ComboBox _listSelector = null!;
    private ToolStripButton _newListButton = null!;
    private ToolStripButton _deleteListButton = null!;
    private ToolStripButton _removeFromListButton = null!;

    public MainForm(StartupRequest request)
    {
        _startupRequest = request;
        _settings = _store.Load();
        _history = _store.LoadHistory();
        _palette = Palette.For(_settings.DarkTheme);
    }

    // ---------------------------------------------------------------- interfaz

    private void BuildUi()
    {
        SuspendLayout();
        Text = "XoPy — Copiar, mover y verificar";
        Icon = Icons.AppIcon();
        BackColor = _palette.Canvas;
        ForeColor = _palette.Text;
        Font = new Font("Segoe UI", 9f);
        // Ventana compacta tipo TeraCopy: antes 1024x640 ocupaba media pantalla.
        MinimumSize = new Size(860, 520);
        DoubleBuffered = true;
        KeyPreview = true;
        AllowDrop = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = _palette.Canvas,
            RowCount = 6,
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        _rootLayout = root;

        InitializeDestinationState();
        _menu = BuildMenu();
        // Barra de menús oculta por defecto: todo lo que ofrece está en la barra de iconos,
        // en el menú contextual de la lista y en Preferencias. Ocupaba una fila entera.
        _menu.Visible = _settings.ShowMenuBar;
        root.RowStyles[0].Height = _settings.ShowMenuBar ? 24 : 0;
        root.Controls.Add(_menu, 0, 0);
        root.Controls.Add(BuildToolbar(), 0, 1);
        root.Controls.Add(BuildTransferHeader(), 0, 2);
        root.Controls.Add(BuildTabBar(), 0, 3);
        root.Controls.Add(BuildContent(), 0, 4);
        root.Controls.Add(BuildFooter(), 0, 5);

        Controls.Add(root);
        MainMenuStrip = _menu;
        ResumeLayout(true);

        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => DropPaths(e);
        Shown += (_, _) =>
        {
            ResizeColumns();
            SelectTab(0);
            ApplyDarkScrollbars();
        };
    }

    private void ToggleCompactView() => ApplyCompactView(!_compactView, persist: true);

    private void ApplyCompactView(bool compact, bool persist)
    {
        _compactView = compact;
        if (_rootLayout is not null)
        {
            _rootLayout.SuspendLayout();
            _rootLayout.RowStyles[1].Height = compact ? 44 : 48;
            // 24+16+24+16 = 80 de contenido + relleno vertical de la cabecera.
            _rootLayout.RowStyles[2].Height = 90;
            // En reducido se ocultan las pestañas (como TeraCopy: solo metadatos y lista) y
            // la lista SÍ queda a la vista: ahí está la barra de progreso de cada archivo.
            // Sin pestañas en reducido: la fila se queda a cero (antes dejaba un hueco de 34 px).
            _rootLayout.RowStyles[3].Height = compact ? 0 : 34;
            if (_tabBar is not null) _tabBar.Visible = !compact;
            _rootLayout.RowStyles[4].SizeType = SizeType.Percent;
            _rootLayout.RowStyles[4].Height = 100;
            // En reducido no hay pestañas que mostrar: la lista ocupa todo el ancho.
            if (_contentGrid is not null && _contentGrid.ColumnStyles.Count >= 2)
                _contentGrid.ColumnStyles[0].Width = compact ? 0 : 228;
            // El pie necesita 90 px de filas + 16 de relleno: con menos, se recortaba.
            _rootLayout.RowStyles[5].Height = 106;
            if (_transferHeader is not null && _transferHeader.RowStyles.Count >= 4)
            {
                // Las dos líneas (origen y destino) y sus barras siguen visibles en compacto,
                // con la misma altura que en la ventana completa para que la barra se vea.
                _transferHeader.RowStyles[0].Height = 24;
                _transferHeader.RowStyles[1].Height = 16;
                _transferHeader.RowStyles[2].Height = 24;
                _transferHeader.RowStyles[3].Height = 16;
            }
            UpdateToolbarForCompact(compact);
            if (_compactButton is not null)
            {
                _compactButton.Image = Icons.Glyph(compact ? Icons.ChevronDown : Icons.ChevronRight, _palette.Text, 18);
                _compactButton.Text = compact ? "Mostrar" : "Ocultar";
                _compactButton.ToolTipText = compact
                    ? "Mostrar la lista de copia"
                    : "Ocultar la lista y dejar visibles los controles y el progreso";
                _compactButton.AccessibleName = _compactButton.ToolTipText;
            }
            _rootLayout.ResumeLayout(true);
        }

        // La ventana NUNCA se ajusta sola: se recortaba. Como en TeraCopy, es redimensionable
        // a mano en los dos modos y el tamaño mínimo se calcula para que quepa todo lo visible.
        AutoSize = false;
        // La lista ya está visible en la vista reducida, así que el mínimo crece para que
        // quepan las cuatro filas sin recortar la cabecera ni el pie.
        MinimumSize = compact ? new Size(620, 430) : new Size(820, 560);
        if (persist)
        {
            if (compact)
            {
                _expandedWasMaximized = WindowState == FormWindowState.Maximized;
                _expandedBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                WindowState = FormWindowState.Normal;

                // La altura se calcula sumando lo que sigue siendo visible (menú, barra,
                // cabecera de progreso y pie), de modo que quepa TODO sin recortar nada.
                var height = 0;
                if (_rootLayout is not null)
                {
                    // Se suman TODAS las filas visibles (la lista incluida, con sus cuatro filas).
                    foreach (var index in new[] { 0, 1, 2, 5 })
                    {
                        var style = _rootLayout.RowStyles[index];
                        var value = style.SizeType == SizeType.Absolute ? style.Height : 0;
                        height += (int)value;
                    }
                    // La fila 3 (pestañas) desaparece en reducido; la 4 es la lista y crece.
                    height += compact ? 0 : (int)_rootLayout.RowStyles[3].Height;
                }
                height += SystemInformation.FrameBorderSize.Height * 2 + SystemInformation.CaptionHeight;
                // Se acerca al tamaño justo, pero nunca por debajo de lo que necesita el contenido.
                Height = Math.Max(height, MinimumSize.Height);
                Width = Math.Max(Width, MinimumSize.Width);
            }
            else if (_expandedBounds.Width > 0)
            {
                AutoSize = false;
                Bounds = new Rectangle(_expandedBounds.X, _expandedBounds.Y,
                    Math.Max(MinimumSize.Width, _expandedBounds.Width), Math.Max(MinimumSize.Height, _expandedBounds.Height));
                if (_expandedWasMaximized) WindowState = FormWindowState.Maximized;
                _expandedBounds = Rectangle.Empty;
                _expandedWasMaximized = false;
            }
            _settings.CompactView = compact;
            _store.Save(_settings);
        }
    }

    /// <summary>Muestra u oculta la barra de menús superior y devuelve el alto a la lista.</summary>
    private void ApplyMenuBarSetting()
    {
        if (_menu is null || _rootLayout is null) return;
        var show = _settings.ShowMenuBar;
        _menu.Visible = show;
        _rootLayout.RowStyles[0].Height = show ? 24 : 0;
        _rootLayout.PerformLayout();
    }

    private void UpdateToolbarForCompact(bool compact)
    {
        // Como en TeraCopy, la vista reducida mantiene las acciones principales y los
        // controles de la transferencia (pausar, saltar y detener) siempre visibles.
        var listButtons = new[] { _addFilesButton, _addFolderButton, _copyButton, _moveButton, _testButton };
        foreach (var button in listButtons)
        {
            if (button is not null) button.Visible = true;
        }
        _pauseButton.Visible = true;
        _skipButton.Visible = true;
        _stopButton.Visible = true;
        if (_listSelector is not null) _listSelector.Visible = false;
        if (_newListButton is not null) _newListButton.Visible = false;
        if (_deleteListButton is not null) _deleteListButton.Visible = false;
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 2, 0, 2),
            AutoSize = false,
            Height = 26
        };
        Ui.StyleMenu(menu, _palette);

        var file = new ToolStripMenuItem("Archivo");
        file.DropDownItems.Add(MenuItem("Añadir archivos…", (_, _) => AddFiles(), Keys.Control | Keys.O));
        file.DropDownItems.Add(MenuItem("Añadir carpeta…", (_, _) => AddFolder(), Keys.Control | Keys.Shift | Keys.O));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MenuItem("Abrir cola…", (_, _) => LoadQueueFile()));
        file.DropDownItems.Add(MenuItem("Abrir la última cola y reanudar", (_, _) => ResumeLastQueue()));
        file.DropDownItems.Add(MenuItem("Guardar cola…", (_, _) => SaveQueueFile()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MenuItem("Exportar informe HTML…", (_, _) => ExportReportHtml()));
        file.DropDownItems.Add(MenuItem("Exportar informe CSV…", (_, _) => ExportReportCsv()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MenuItem("Salir", (_, _) => ExitApplication()));

        var transfer = new ToolStripMenuItem("Transferencia");
        transfer.DropDownItems.Add(MenuItem("Copiar", (_, _) => StartTransfer(OperationType.Copy), Keys.F5));
        transfer.DropDownItems.Add(MenuItem("Mover", (_, _) => StartTransfer(OperationType.Move), Keys.Control | Keys.F5));
        transfer.DropDownItems.Add(MenuItem("Verificar", (_, _) => StartTransfer(OperationType.Test), Keys.F6));
        transfer.DropDownItems.Add(new ToolStripSeparator());
        transfer.DropDownItems.Add(MenuItem("Pausar / reanudar", (_, _) => TogglePause()));
        transfer.DropDownItems.Add(MenuItem("Saltar el archivo actual", (_, _) => SkipCurrent()));
        transfer.DropDownItems.Add(MenuItem("Detener", (_, _) => StopTransfer()));

        var queue = new ToolStripMenuItem("Cola");
        queue.DropDownItems.Add(MenuItem("Subir el elemento  (Alt+↑)", (_, _) => MoveSelected(-1)));
        queue.DropDownItems.Add(MenuItem("Bajar el elemento  (Alt+↓)", (_, _) => MoveSelected(1)));
        queue.DropDownItems.Add(new ToolStripSeparator());
        // Un solo submenú «Ordenar» en vez de ocho líneas sueltas: así el menú Cola queda
        // corto y ordenado, como el menú contextual de TeraCopy.
        var sortMenu = SubMenu("Ordenar");
        sortMenu.DropDownItems.Add(MenuItem("Nombre (A → Z)", (_, _) => SortQueue(QueueSortField.Name)));
        sortMenu.DropDownItems.Add(MenuItem("Nombre (Z → A)", (_, _) => SortQueue(QueueSortField.Name, invert: true)));
        sortMenu.DropDownItems.Add(new ToolStripSeparator());
        sortMenu.DropDownItems.Add(MenuItem("Tamaño, dentro de cada carpeta", (_, _) => SortQueue(QueueSortField.Size)));
        sortMenu.DropDownItems.Add(MenuItem("Tamaño total de la carpeta", (_, _) => SortQueue(QueueSortField.FolderSize, invert: true)));
        sortMenu.DropDownItems.Add(MenuItem("TODOS los archivos por tamaño (menor → mayor)", (_, _) => SortAllFilesBySize(false)));
        sortMenu.DropDownItems.Add(MenuItem("TODOS los archivos por tamaño (mayor → menor)", (_, _) => SortAllFilesBySize(true)));
        sortMenu.DropDownItems.Add(new ToolStripSeparator());
        sortMenu.DropDownItems.Add(MenuItem("Tipo de archivo", (_, _) => SortQueue(QueueSortField.Type)));
        sortMenu.DropDownItems.Add(MenuItem("Carpeta de destino", (_, _) => SortQueue(QueueSortField.Destination)));
        sortMenu.DropDownItems.Add(MenuItem("Carpeta de origen", (_, _) => SortQueue(QueueSortField.Source)));
        sortMenu.DropDownItems.Add(MenuItem("Estado", (_, _) => SortQueue(QueueSortField.State)));
        queue.DropDownItems.Add(sortMenu);

        var viewMenu = SubMenu("Ver lista");
        viewMenu.DropDownItems.Add(MenuItem("Expandir todo", (_, _) => ExpandAll(true)));
        viewMenu.DropDownItems.Add(MenuItem("Contraer todo", (_, _) => ExpandAll(false)));
        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        viewMenu.DropDownItems.Add(MenuItem("Ocultar o mostrar la lista de copia", (_, _) => ToggleCompactView()));
        viewMenu.DropDownItems.Add(MenuItem("Ocultar o mostrar el historial de listas", (_, _) => SetJobsVisible(!_jobsVisible, persist: true)));
        viewMenu.DropDownItems.Add(MenuItem("Mostrar u ocultar la columna de trabajos", (_, _) => ToggleJobsPanel()));
        queue.DropDownItems.Add(viewMenu);
        queue.DropDownItems.Add(new ToolStripSeparator());
        queue.DropDownItems.Add(MenuItem("Quitar seleccionados", (_, _) => RemoveSelected()));
        queue.DropDownItems.Add(MenuItem("Limpiar la lista", (_, _) => ClearList()));

        var tools = new ToolStripMenuItem("Herramientas");
        tools.DropDownItems.Add(MenuItem("Preferencias…", (_, _) => ShowOptions()));
        tools.DropDownItems.Add(MenuItem("Integración con el Explorador…", (_, _) => ShowExplorerIntegration()));
        tools.DropDownItems.Add(MenuItem("Abrir el registro de transferencias", (_, _) => OpenLogFile()));
        tools.DropDownItems.Add(MenuItem("Reiniciar XoPy como administrador", (_, _) => RestartElevated()));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(MenuItem("Historial de transferencias…", (_, _) => ShowHistory()));

        var help = new ToolStripMenuItem("Ayuda");
        help.DropDownItems.Add(MenuItem("Acerca de XoPy", (_, _) => ShowAbout()));

        menu.Items.AddRange(new ToolStripItem[] { file, transfer, queue, tools, help });
        return menu;
    }

    /// <summary>Texto de cada opción del selector de modo de copia.</summary>
    private static string ModeLabel(bool multi, int threads) =>
        multi ? $"Multihilo · {threads} a la vez" : "Secuencial · 1 a la vez";

    private static ToolStripMenuItem SubMenu(string text) => new(text);

    private static ToolStripMenuItem MenuItem(string text, EventHandler handler, Keys shortcut = Keys.None)
    {
        var item = new ToolStripMenuItem(text, null, handler);
        if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
        return item;
    }

    private Control BuildToolbar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = _palette.Surface,
            Padding = new Padding(10, 8, 10, 8),
            RowCount = 1
        };
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Acciones de la lista con icono (el texto va en el tooltip): así caben todas en una
        // sola fila. Con texto completo no cabían y, sin desplegable, desaparecían.
        _addFilesButton = BarButton("Añadir", Icons.Add, (_, _) => AddFiles(), iconOnly: true);
        _addFolderButton = BarButton("Carpeta", Icons.FolderAdd, (_, _) => AddFolder(), iconOnly: true);
        _copyButton = BarButton("Copiar", Icons.Copy, (_, _) => StartTransfer(OperationType.Copy), true, iconOnly: true);
        _moveButton = BarButton("Mover", Icons.Move, (_, _) => StartTransfer(OperationType.Move), iconOnly: true);
        _testButton = BarButton("Verificar", Icons.Verify, (_, _) => StartTransfer(OperationType.Test), iconOnly: true);
        _pauseButton = BarButton("Pausar", Icons.Pause, (_, _) => TogglePause());
        _skipButton = BarButton("Saltar", Icons.ChevronRight, (_, _) => SkipCurrent());
        _stopButton = BarButton("Detener", Icons.Stop, (_, _) => StopTransfer());
        // Un solo botón que activa o desactiva el multihilo (el NÚMERO de hilos se ajusta
        // en Preferencias). Antes era un desplegable con «Multihilo · 4 a la vez», que
        // ocupaba mucho y no se entendía.
        _multiThreadButton = BarButton(MultiThreadLabel(), Icons.Lightning,
            (_, _) => ToggleMultiThread(), iconOnly: true);
        _multiThreadButton.ToolTipText = MultiThreadTooltip();

        // «Verificar» y el resto de opciones viven en Preferencias: aquí solo lo esencial.
        _compactButton = BarButton("Ocultar", Icons.ChevronRight, (_, _) => ToggleCompactView(), iconOnly: true);
        _compactButton.Margin = new Padding(2, 0, 4, 0);
        _compactButton.ToolTipText = "Ocultar la lista y dejar visibles los controles y el progreso";
        // Único acceso a Preferencias (el menú «⋯» tenía la misma opción y sobraba).
        _optionsButton = BarButton("Opciones", Icons.Settings, (_, _) => ShowOptions(), false, true);
        _optionsButton.ToolTipText = "Preferencias…";
        _optionsButton.Margin = new Padding(6, 0, 4, 0);
        _menuButton = BarButton("Menú", Icons.More, (_, _) => ShowMainMenu(), false, true);
        _menuButton.Margin = new Padding(0);

        // Igual que el modo: ComboBox normal para que el texto no se recorte.
        _listSelector = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.75f),
            BackColor = _palette.Raised,
            ForeColor = _palette.Text,
            Width = 176,
            Height = 26,
            IntegralHeight = false
        };
        _listSelector.DisplayMember = nameof(TransferList.DisplayName);
        _listSelector.SelectedIndexChanged += (_, _) =>
        {
            if (_refreshingSelector) return;
            if (_listSelector.SelectedItem is TransferList list) SwitchToList(list);
        };
        var listHost = new ToolStripControlHost(_listSelector)
        {
            AutoSize = false,
            Margin = new Padding(2, 3, 4, 0),
            Size = new Size(176, 26),
            ToolTipText = "Lista de copia activa. Cada instancia externa se agrega como una lista independiente en esta misma pestaña."
        };

        _newListButton = BarButton("Nueva lista", Icons.Add, (_, _) =>
        {
            var dest = _destination.Text.Trim();
            if (!Directory.Exists(dest) && _activeList is not null && Directory.Exists(_activeList.Destination))
                dest = _activeList.Destination;
            AddNewList(dest);
        }, iconOnly: true);
        _newListButton.Width = 40;
        _newListButton.ToolTipText = "Crear una nueva lista de copia independiente";

        // Dos acciones distintas con iconos distintos: antes ambas usaban la papelera y
        // parecían «eliminar archivos» duplicado.
        _deleteListButton = BarButton("Borrar lista", Icons.ClearList, (_, _) => DeleteActiveList(), iconOnly: true);
        _deleteListButton.Width = 40;
        _deleteListButton.ToolTipText = "Cerrar esta lista de copia del listado (no borra archivos del disco)";

        _removeFromListButton = BarButton("Quitar selección", Icons.Remove, (_, _) => RemoveSelected(), iconOnly: true);
        _removeFromListButton.Width = 40;
        _removeFromListButton.ToolTipText = "Quitar los elementos seleccionados de la lista (no borra nada del disco)";

        var left = new ToolStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
            // Con desplegable como red de seguridad: si un botón no cabe (ventana muy
            // estrecha), se recoge en «» en lugar de desaparecer sin más. Con el reparto
            // actual (izquierda con iconos, transferencia a la derecha) caben todos.
            LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow,
            CanOverflow = true,
            AutoSize = false,
            BackColor = _palette.Surface,
            Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
            Renderer = new DarkMenuRenderer(_palette),
            Padding = new Padding(0)
        };
        left.Items.AddRange(new ToolStripItem[]
        {
            _addFilesButton, _addFolderButton, _copyButton, _moveButton, _testButton
        });

        // Una sola barra: las acciones (iconos), el modo, la verificación y la transferencia
        // en el mismo ToolStrip. Con dos columnas la izquierda podía quedarse a cero y los
        // botones desaparecían; así solo se recoge lo que de verdad no quepa.
        left.Items.AddRange(new ToolStripItem[]
        {
            _multiThreadButton,
            new ToolStripSeparator(),
            _pauseButton, _skipButton, _stopButton,
            new ToolStripSeparator(),
            _compactButton, _optionsButton
        });
        bar.Controls.Add(left, 0, 0);
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(_palette.Border);
            e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
        };
        return bar;
    }

    private ToolStripButton BarButton(string text, string glyph, EventHandler click, bool accent = false, bool iconOnly = false)
    {
        var foreground = accent ? Color.White : _palette.Text;
        var font = new Font("Segoe UI", 8.75f, FontStyle.Bold);
        // Más holgura: con +44 el texto quedaba pegado al borde y «Verificar» se recortaba.
        var width = iconOnly ? 40 : TextRenderer.MeasureText(text, font).Width + 58;
        var button = new ToolStripButton(text)
        {
            Image = Icons.Glyph(glyph, foreground, 18),
            ImageScaling = ToolStripItemImageScaling.None,
            DisplayStyle = iconOnly ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.ImageAndText,
            TextImageRelation = TextImageRelation.ImageBeforeText,
            Font = font,
            ForeColor = foreground,
            BackColor = accent ? _palette.Accent : _palette.Surface,
            AutoSize = false,
            Size = new Size(width, 36),
            Margin = new Padding(0, 0, 3, 0),
            Padding = new Padding(8, 0, 8, 0),
            TextAlign = ContentAlignment.MiddleCenter,
            AccessibleName = text,
            ToolTipText = TooltipFor(text)
        };
        button.Click += click;
        return button;
    }

    private static string TooltipFor(string text) => text switch
    {
        "Añadir" => "Añadir archivos a la cola (Ctrl+O)",
        "Carpeta" => "Añadir una carpeta completa (Ctrl+Mayús+O)",
        "Copiar" => "Copiar los elementos seleccionados (F5)",
        "Mover" => "Mover los elementos seleccionados (Ctrl+F5)",
        "Verificar" => "Comprobar la copia comparando hashes (F6)",
        "Eliminar" => "Eliminar los archivos del disco (no es lo mismo que quitarlos de la lista)",
        "Saltar" => "Saltar el archivo que se está copiando",
        _ => text
    };

    private void InitializeDestinationState()
    {
        // Destination is an internal target selected from the active Explorer window
        // or the Destino tab; keep it out of the always-visible toolbar.
        // Arranca vacío: el destino no se prefija nunca al abrir el programa.
        _destination = new TextBox { Text = string.Empty };
        _destination.TextChanged += (_, _) =>
        {
            if (_activeList is not null) _activeList.Destination = _destination.Text.Trim();
            UpdateFooter();
            UpdateTargetTab();
        };

        _verifyCheck = new CheckBox
        {
            Text = "Verificar",
            AutoSize = true,
            ForeColor = _palette.Text,
            BackColor = Color.Transparent,
            Margin = new Padding(4, 2, 4, 0),
            Anchor = AnchorStyles.Left
        };
        new ToolTip().SetToolTip(_verifyCheck, "Compara cada archivo copiado con el original");
        _verifyCheck.CheckedChanged += (_, _) =>
        {
            _settings.VerifyAfterCopy = _verifyCheck.Checked;
            _verifyCheck.Text = _settings.VerifyAfterCopy
                ? $"Verificar ({Format.Algorithm(_settings.Verify)})"
                : "Verificar";
            _store.Save(_settings);
            UpdateHashColumns();
        };
    }

    private Control BuildTabBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = _palette.Canvas,
            Padding = new Padding(6, 0, 0, 0),
            Margin = new Padding(0)
        };
        _tabBar = bar;
        _tabFiles = TabButton("Lista de archivos", 0, 150);
        _tabTarget = TabButton("Destino", 1, 96);
        _tabOptions = TabButton("Opciones", 2, 96);
        _tabStatus = TabButton("Estado", 3, 90);
        _tabLog = TabButton("Registro", 4, 90);
        bar.Controls.AddRange(new Control[] { _tabFiles, _tabTarget, _tabOptions, _tabStatus, _tabLog });
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(_palette.Border);
            e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
            var active = _tabIndex switch
            {
                0 => _tabFiles,
                1 => _tabTarget,
                2 => _tabOptions,
                3 => _tabStatus,
                _ => _tabLog
            };
            if (active is null) return;
            using var accent = new Pen(_palette.Accent, 3);
            e.Graphics.DrawLine(accent, active.Left, bar.Height - 2, active.Right, bar.Height - 2);
        };
        return bar;
    }

    private Button TabButton(string text, int index, int width)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(width, 27),
            FlatStyle = FlatStyle.Flat,
            BackColor = _palette.Canvas,
            ForeColor = _palette.Muted,
            Font = new Font("Segoe UI", 9f),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 6, 2, 0),
            FlatAppearance = { BorderSize = 0, MouseOverBackColor = _palette.SurfaceAlt }
        };
        button.Click += (_, _) => SelectTab(index);
        return button;
    }

    private Control BuildContent()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = _palette.Canvas,
            Padding = new Padding(10, 8, 10, 8),
            RowCount = 1
        };
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 228));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _tabsHost = new Panel { Dock = DockStyle.Fill, BackColor = _palette.Surface, Padding = new Padding(0) };
        _pages = new Control[]
        {
            BuildFilesPage(),
            BuildTargetPage(),
            BuildOptionsPage(),
            BuildStatusPage(),
            BuildLogPage()
        };
        foreach (var page in _pages)
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            _tabsHost.Controls.Add(page);
        }

        _contentGrid = content;
        _jobsPanel = BuildJobsPanel();
        content.Controls.Add(_jobsPanel, 0, 0);
        content.Controls.Add(_tabsHost, 1, 0);
        return content;
    }

    private Control BuildJobsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = _palette.Canvas,
            Padding = new Padding(0, 0, 10, 0),
            RowCount = 3,
            Margin = new Padding(0)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        // Cabecera con el título y el botón que oculta/muestra el historial completo de
        // listas de copia. El estado se guarda, así que al reabrir se mantiene.
        var headerRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = _palette.Canvas,
            Margin = new Padding(0)
        };
        var title = Ui.Label("TRABAJOS", _palette, 8f, true, _palette.Muted);
        title.Anchor = AnchorStyles.Left;
        title.Margin = new Padding(2, 5, 0, 0);
        _jobsToggleButton = Ui.IconButton("\u00AB", _palette, "Ocultar el historial de listas de copia");
        _jobsToggleButton.Size = new Size(22, 22);
        _jobsToggleButton.Margin = new Padding(4, 2, 0, 0);
        _jobsToggleButton.Click += (_, _) => SetJobsVisible(!_jobsVisible, persist: true);
        headerRow.Controls.Add(title);
        headerRow.Controls.Add(_jobsToggleButton);

        _jobsList = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = _palette.Surface,
            ForeColor = _palette.Text,
            Font = new Font("Segoe UI", 8.75f),
            IntegralHeight = false,
            Margin = new Padding(0)
        };
        _jobsList.DoubleClick += (_, _) => ShowHistory();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = _palette.Canvas,
            Padding = new Padding(0, 6, 0, 0)
        };
        var historyButton = Ui.ToolButton("Historial", Icons.History, _palette);
        historyButton.Size = new Size(112, 30);
        historyButton.Margin = new Padding(0);
        historyButton.Click += (_, _) => ShowHistory();
        buttons.Controls.Add(historyButton);

        panel.Controls.Add(headerRow, 0, 0);
        panel.Controls.Add(_jobsList, 0, 1);
        panel.Controls.Add(buttons, 0, 2);
        return panel;
    }

    private Control BuildTransferHeader()
    {
        // Estilo TeraCopy: dos bloques (origen y destino) apilados. Cada uno lleva a la
        // izquierda la etiqueta en forma de píldora, después la ruta completa, luego las
        // métricas y el porcentaje a la derecha, y debajo una barra de progreso a lo ancho.
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = _palette.Surface,
            Padding = new Padding(10, 5, 10, 3),
            Margin = new Padding(0, 0, 0, 0)
        };
        _transferHeader = host;
        // La barra ocupa 12 px de alto (antes 4, prácticamente invisible).
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));

        host.Controls.Add(BuildHeaderLine("Copiando", out _hdrCopyState, out _hdrCopyPath, out _hdrCopyInfo, out _hdrCopyPercent), 0, 0);
        host.Controls.Add(BuildHeaderBar(out _hdrCopyBar), 0, 1);
        host.Controls.Add(BuildHeaderLine("Destino", out _hdrTargetState, out _hdrTargetPath, out _hdrTargetInfo, out _hdrTargetPercent), 0, 2);
        host.Controls.Add(BuildHeaderBar(out _hdrTargetBar), 0, 3);
        return host;
    }

    private Control BuildHeaderBar(out ProgressStrip bar)
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = _palette.Surface, Margin = new Padding(0, 2, 0, 3), Padding = new Padding(0) };
        // 14 px de alto: las barras de 4 px no se veían y parecían una raya del borde.
        bar = new ProgressStrip(_palette) { Dock = DockStyle.Fill, Height = 14, MinimumSize = new Size(0, 10) };
        host.Controls.Add(bar);
        return host;
    }

    private TableLayoutPanel BuildHeaderLine(string title, out PillLabel state, out Label path, out Label info, out Label percent)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = _palette.Surface,
            Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 214));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));

        // Píldora redondeada con el estado, como en TeraCopy.
        state = new PillLabel(title, _palette) { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 8, 2) };
        path = new Label
        {
            Text = "—",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = _palette.Text,
            Font = new Font("Segoe UI", 8.75f),
            AutoEllipsis = true,
            UseMnemonic = false,
            Margin = new Padding(0, 0, 10, 0)
        };
        info = new Label
        {
            Text = string.Empty,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = _palette.Muted,
            Font = new Font("Segoe UI", 8.25f),
            AutoEllipsis = true,
            UseMnemonic = false,
            Margin = new Padding(0, 0, 8, 0)
        };
        percent = new Label
        {
            Text = "—",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = _palette.Text,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            UseMnemonic = false,
            Margin = new Padding(0)
        };

        row.Controls.Add(state, 0, 0);
        row.Controls.Add(path, 1, 0);
        row.Controls.Add(info, 2, 0);
        row.Controls.Add(percent, 3, 0);
        return row;
    }


    private void ShowSortMenu(Control anchor)
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        Ui.StyleMenu(menu, _palette);
        void Add(string text, Action action)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("Nombre (A → Z)", () => SortQueue(QueueSortField.Name));
        Add("Nombre (Z → A)", () => SortQueue(QueueSortField.Name, invert: true));
        Add("Tamaño (menor → mayor)", () => SortQueue(QueueSortField.Size));
        Add("Tamaño (mayor → menor)", () => SortQueue(QueueSortField.Size, invert: true));
        Add("Tamaño de carpeta, por el total de su contenido", () => SortQueue(QueueSortField.FolderSize, invert: true));
        Add("Tamaño de TODOS los archivos (menor → mayor)", () => SortAllFilesBySize(false));
        Add("Tamaño de TODOS los archivos (mayor → menor)", () => SortAllFilesBySize(true));
        Add("Tipo de archivo (A → Z)", () => SortQueue(QueueSortField.Type));
        Add("Carpeta de destino (A → Z)", () => SortQueue(QueueSortField.Destination));
        Add("Carpeta de destino (Z → A)", () => SortQueue(QueueSortField.Destination, invert: true));
        Add("Carpeta de origen (A → Z)", () => SortQueue(QueueSortField.Source));
        Add("Carpeta de origen (Z → A)", () => SortQueue(QueueSortField.Source, invert: true));
        Add("Estado de la copia", () => SortQueue(QueueSortField.State));
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = _palette.Surface,
            Padding = new Padding(12, 8, 12, 8),
            RowCount = 4
        };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 17));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        _currentLabel = new Label
        {
            Text = "Arrastra archivos o carpetas a la ventana, o usa Añadir archivos.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = _palette.Muted,
            Font = new Font("Segoe UI", 8.5f),
            AutoEllipsis = true,
            Margin = new Padding(0)
        };

        _footerGraph = new SpeedGraph(_palette) { Dock = DockStyle.Fill, Height = 32, Margin = new Padding(0, 1, 0, 2) };
        _progress = new ProgressStrip(_palette) { Dock = DockStyle.Fill, Height = 10, Margin = new Padding(0, 0, 0, 1) };

        _statusLabel = new Label
        {
            Text = "Listo",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = _palette.Text,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        _summaryLabel = new Label
        {
            Text = string.Empty,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = _palette.Muted,
            Font = new Font("Segoe UI", 9f),
            AutoEllipsis = true,
            Margin = new Padding(0)
        };
        _speedLabel = new Label
        {
            Text = string.Empty,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = _palette.Muted,
            Font = new Font("Segoe UI", 9f),
            AutoEllipsis = true,
            Margin = new Padding(0)
        };

        var statusRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = _palette.Surface
        };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        statusRow.Controls.Add(_statusLabel, 0, 0);
        statusRow.Controls.Add(_summaryLabel, 1, 0);
        statusRow.Controls.Add(_speedLabel, 2, 0);

        footer.Controls.Add(_currentLabel, 0, 0);
        footer.Controls.Add(_footerGraph, 0, 1);
        footer.Controls.Add(_progress, 0, 2);
        footer.Controls.Add(statusRow, 0, 3);
        return footer;
    }

    private Control BuildListShell()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = _palette.Surface,
            RowCount = 2,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = _palette.SurfaceAlt,
            RowCount = 1
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _countLabel = Ui.Label("Lista vacía — arrastra archivos o carpetas aquí", _palette, 9f, true, _palette.Muted);
        _countLabel.Anchor = AnchorStyles.Left;
        _countLabel.Margin = new Padding(10, 9, 0, 0);

        _upButton = Ui.IconButton(Icons.Up, _palette, "Subir el elemento seleccionado (Alt+↑)");
        _upButton.Click += (_, _) => MoveSelected(-1);
        _downButton = Ui.IconButton(Icons.Down, _palette, "Bajar el elemento seleccionado (Alt+↓)");
        _downButton.Click += (_, _) => MoveSelected(1);
        _removeButton = Ui.IconButton(Icons.Remove, _palette, "Quitar los elementos seleccionados de la lista (Supr)\nNo borra nada del disco.");
        _removeButton.Click += (_, _) => RemoveSelected();
        // Icono y texto distintos de «Quitar» y de «Eliminar del disco»: esto solo vacía la lista.
        _clearButton = Ui.IconButton(Icons.ClearList, _palette, "Vaciar la lista completa (no borra archivos del disco)");
        _clearButton.Click += (_, _) => ClearList();

        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = _palette.SurfaceAlt,
            Padding = new Padding(0, 2, 4, 0)
        };
        // Icono nativo de «Segoe MDL2 Assets»: U+21C5 no existe en esa fuente y salía
        // rasterizado como un amasijo de puntos.
        _sortButton = Ui.IconButton(Icons.SortAsc, _palette, "Ordenar la cola por nombre, tamaño, tipo o carpeta de destino");
        _sortButton.Click += (_, _) => ShowSortMenu(_sortButton);

        _collapseButton = Ui.IconButton("\u00AB", _palette, "Mostrar u ocultar la columna de trabajos");
        _collapseButton.Click += (_, _) => ToggleJobsPanel();
        tools.Controls.Add(_collapseButton);
        tools.Controls.Add(_clearButton);
        tools.Controls.Add(_removeButton);
        tools.Controls.Add(_downButton);
        tools.Controls.Add(_upButton);
        tools.Controls.Add(_sortButton);

        header.Controls.Add(_countLabel, 0, 0);
        header.Controls.Add(tools, 1, 0);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            OwnerDraw = true,
            BorderStyle = BorderStyle.None,
            BackColor = _palette.Surface,
            ForeColor = _palette.Text,
            HeaderStyle = ColumnHeaderStyle.Clickable,
            UseCompatibleStateImageBehavior = false,
            SmallImageList = BuildRowImageList(),
            AllowDrop = true,
            ShowItemToolTips = true,
            Font = new Font("Segoe UI", 8.5f),
            Margin = new Padding(0),
            LabelEdit = true,
            GridLines = false
        };
        _list.Columns.Add("Nombre", 380);
        _list.Columns.Add("Destino", 240);
        _list.Columns.Add("Tamaño", 92, HorizontalAlignment.Right);
        _list.Columns.Add("Estado", 128);
        _list.Columns.Add("Progreso", 190);
        _list.Columns.Add("Hash origen", 0);
        _list.Columns.Add("Hash destino", 0);

        // La esquina entre las dos barras de desplazamiento nativas se pinta con el color del
        // sistema (blanco) y SetWindowTheme no la toca: se repinta con el fondo oscuro.
        _list.Paint += (_, e) =>
        {
            var size = SystemInformation.VerticalScrollBarWidth;
            var right = _list.ClientSize.Width - size;
            if (right <= 0) return;
            using var brush = new SolidBrush(_palette.Surface);
            e.Graphics.FillRectangle(brush, new Rectangle(right, 0, size, _list.ClientSize.Height));
        };

        // El ListView nativo pinta su barra de desplazamiento vertical con el color del sistema
        // y el tema oscuro de Windows no la colorea; SetWindowTheme la ignora. Se cubre con un
        // panel del color del fondo, colocado sobre el borde derecho de la lista.
        _scrollCover = new Panel { BackColor = _palette.Surface, Visible = false };
        Controls.Add(_scrollCover);

        _list.DrawColumnHeader += ListDrawColumnHeader;
        _list.DrawItem += (_, e) => { if (_list.View != View.Details) e.DrawDefault = true; };
        _list.DrawSubItem += ListDrawSubItem;
        // Las columnas se ajustan al ancho de la ventana, no al de la lista:
        // así la aparición de la barra de desplazamiento no provoca un bucle de redimensionado.
        Resize += (_, _) => ScheduleColumnUpdate();
        Shown += (_, _) => ScheduleColumnUpdate();
        _list.KeyDown += ListKeyDown;
        _list.DoubleClick += ListDoubleClick;
        _list.AfterLabelEdit += ListAfterLabelEdit;
        _list.MouseDown += ListMouseDown;
        _list.ItemDrag += ListItemDrag;
        _list.DragEnter += ListDragEnter;
        _list.DragOver += ListDragEnter;
        _list.DragDrop += ListDragDrop;
        _list.SelectedIndexChanged += (_, _) =>
        {
            UpdateButtons();
            ShowSelectedError();
        };
        _list.ColumnClick += ListColumnClick;

        // Doble búfer para que la lista no parpadee mientras se actualiza el progreso.
        try
        {
            typeof(ListView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(_list, true);
        }
        catch { }

        BuildListMenu();
        _list.ContextMenuStrip = _listMenu;

        shell.Controls.Add(header, 0, 0);
        shell.Controls.Add(_list, 0, 1);
        return shell;
    }

    private static ImageList BuildRowImageList()
    {
        var images = new ImageList { ImageSize = new Size(1, 20) };
        var bitmap = new Bitmap(1, 20);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.Transparent);
        images.Images.Add(bitmap);
        return images;
    }

    private void BuildListMenu()
    {
        _listMenu = new ContextMenuStrip { ShowImageMargin = false };
        Ui.StyleMenu(_listMenu, _palette);
        _listMenu.Items.Add("Abrir", null, (_, _) => OpenSelected());
        _listMenu.Items.Add("Abrir la carpeta contenedora", null, (_, _) => OpenSelectedFolder());
        _listMenu.Items.Add(new ToolStripSeparator());
        _listMenu.Items.Add("Copiar…", null, (_, _) => StartTransfer(OperationType.Copy));
        _listMenu.Items.Add("Mover…", null, (_, _) => StartTransfer(OperationType.Move));
        _listMenu.Items.Add("Verificar", null, (_, _) => StartTransfer(OperationType.Test));
        _listMenu.Items.Add(new ToolStripSeparator());
        _listMenu.Items.Add("Expandir / contraer", null, (_, _) => ToggleSelectedFolders());
        _listMenu.Items.Add("Subir", null, (_, _) => MoveSelected(-1));
        _listMenu.Items.Add("Bajar", null, (_, _) => MoveSelected(1));

        // «Enviar a»: cambio de carpeta de destino de lo seleccionado, igual que en TeraCopy.
        var sendTo = new ToolStripMenuItem("Enviar a");
        sendTo.Name = "send-to";
        sendTo.DropDownItems.Add("Elegir destino…", null, (_, _) => SendSelectionTo(null));
        sendTo.DropDownItems.Add(new ToolStripSeparator());
        _listMenu.Items.Add(sendTo);
        _listMenu.Items.Add("Renombrar el elemento…", null, (_, _) => BeginRenameSelected());
        _listMenu.Items.Add("Cambiar el destino de la lista…", null, (_, _) => BrowseDestination());
        _listMenu.Items.Add(new ToolStripSeparator());
        var pauseItem = new ToolStripMenuItem("Pausar archivo seleccionado");
        pauseItem.Click += (_, _) => ToggleSelectedFilePause();
        pauseItem.Name = "pause-single-file";
        _listMenu.Items.Add(pauseItem);
        _listMenu.Opening += (_, _) => { RefreshSendToMenu(); UpdateListMenuState(); };
        _listMenu.Items.Add(new ToolStripSeparator());
        _listMenu.Items.Add("Quitar de la lista (Supr)", null, (_, _) => RemoveSelected());
        _listMenu.Items.Add("Reintentar los fallidos", null, (_, _) => RetryFailed());
        _listMenu.Items.Add(new ToolStripSeparator());
        _listMenu.Items.Add("Mostrar los hashes", null, (_, _) => ToggleHashes());
    }

    private void ShowMainMenu()
    {
        var menu = BuildMainMenu();
        if (_menuButton.Owner is ToolStrip owner)
            menu.Show(owner, new Point(_menuButton.Bounds.Left, _menuButton.Bounds.Bottom));
        else
            menu.Show(Cursor.Position);
    }

    private ContextMenuStrip BuildMainMenu()
    {
        // Los tres puntos de TeraCopy solo abren Preferencias; el resto de opciones vive ya
        // en el menú «Herramientas» de la barra superior.
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        Ui.StyleMenu(menu, _palette);
        menu.Items.Add("Preferencias…", null, (_, _) => ShowOptions());
        return menu;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachFlag);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr GetLastActivePopup(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    /// <summary>
    /// Trae la ventana al frente de verdad. <see cref="SetForegroundWindow"/> solo lo consigue
    /// si el proceso ya tiene el foco, que es justo lo que NO pasa al pulsar el icono de la
    /// bandeja (lo pulsado es el área de notificación, de Explorer). El truco estándar es
    /// adjuntar la entrada del hilo en primer plano, pedir el foco y volver a desadjuntar.
    /// </summary>
    internal void ForceForeground()
    {
        try
        {
            if (!IsHandleCreated) return;
            var handle = Handle;
            if (IsIconic(handle)) ShowWindow(handle, 9 /* SW_RESTORE */);

            var foreground = GetForegroundWindow();
            var foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, IntPtr.Zero);
            var thisThread = GetWindowThreadProcessId(handle, IntPtr.Zero);
            var attached = false;
            if (foregroundThread != 0 && thisThread != 0 && foregroundThread != thisThread)
                attached = AttachThreadInput(thisThread, foregroundThread, true);
            try
            {
                BringWindowToTop(handle);
                SetForegroundWindow(handle);
                // Un clic simulado sobre la ventana propia garantiza el foco incluso cuando
                // Windows bloquea la llamada por política de primer plano.
                var popup = GetLastActivePopup(handle);
                if (popup != IntPtr.Zero && popup != handle) SetForegroundWindow(popup);
            }
            finally
            {
                if (attached) AttachThreadInput(thisThread, foregroundThread, false);
            }
        }
        catch { }
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string appName, string? idList);

    private void ApplyDarkScrollbars()
    {
        if (!_settings.DarkTheme) return;
        foreach (var control in new Control?[] { _list, _logBox, _targetList, _jobsList, _optionsScroll })
        {
            try
            {
                if (control is not null && control.IsHandleCreated)
                    SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
            }
            catch { }
        }

        // La lista y el cuadro de registro son controles nativos: sus barras de desplazamiento
        // no heredan el tema y dejaban una esquina blanca. Se aplica también a cada hijo nativo.
        foreach (var parent in new Control?[] { _list, _logBox })
        {
            if (parent is null || !parent.IsHandleCreated) continue;
            foreach (Control child in parent.Controls)
            {
                try
                {
                    if (child.IsHandleCreated) SetWindowTheme(child.Handle, "DarkMode_Explorer", null);
                }
                catch { }
            }
        }
    }

    /// <summary>Selecciona una pestaña (0 Lista, 1 Destino, 2 Opciones, 3 Estado, 4 Registro).</summary>
    public void ShowTab(int index) => SelectTab(index);

    /// <summary>Construye el menú «Menú» de la barra (usado por las capturas automáticas).</summary>
    public ContextMenuStrip BuildMainMenuForShot() => BuildMainMenu();

    /// <summary>Abre el menú «Transferencia» de la barra de menús (usado por las capturas automáticas).</summary>
    public ToolStripDropDown? OpenTransferMenu()
    {
        if (_menu.Items.Count < 2 || _menu.Items[1] is not ToolStripMenuItem item) return null;
        item.ShowDropDown();
        return item.DropDown;
    }

    public int TabCount => _pages.Length;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Program.Log("MF-OnHandleCreated done");
        if (!_settings.DarkTheme) return;
        try
        {
            var enabled = 1;
            if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
        }
        catch { }
        ApplyDarkScrollbars();
    }

    // ---------------------------------------------------------------- ajustes

    private void ApplyWindowGeometry()
    {
        // Solo se restaura la POSICIÓN. El tamaño lo decide el modo de ventana (reducida o
        // completa) y el usuario cuando la redimensiona a mano. Restaurar aquí el tamaño
        // guardado deshacía el arranque contraído: la ventana se abría grande.
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        var posX = _settings.WindowX != int.MinValue
            ? _settings.WindowX
            : workingArea.X + (workingArea.Width - Width) / 2;
        var posY = _settings.WindowY != int.MinValue
            ? _settings.WindowY
            : workingArea.Y + (workingArea.Height - Height) / 2;
        if (posX < workingArea.Left - 40 || posX > workingArea.Right - 240) posX = workingArea.Left + 40;
        if (posY < workingArea.Top - 10 || posY > workingArea.Bottom - 180) posY = workingArea.Top + 30;

        StartPosition = FormStartPosition.Manual;
        Location = new Point(posX, posY);
        if (_settings.WindowMaximized && !_compactView) WindowState = FormWindowState.Maximized;
    }

    /// <summary>
    /// Genera un PNG con el aspecto real de la ventana y cierra la aplicación. Se usa desde
    /// el workflow «UI preview» para revisar la interfaz en Windows sin tocar el equipo.
    /// </summary>
    /// <summary>
    /// Comprueba el reordenamiento de la cola sin intervención humana: añade tres archivos
    /// de tamaños distintos, los mueve, ordena por tamaño y los quita. Deja el resultado en
    /// el archivo indicado y devuelve true si todo fue bien.
    /// </summary>
    private bool RunQueueSelfTest(string reportPath)
    {
        var report = new List<string>();
        var ok = true;
        var folder = Path.Combine(Path.GetTempPath(), "xopy-colatest");
        try
        {
            Directory.CreateDirectory(folder);
            for (var i = 1; i <= 3; i++)
                File.WriteAllBytes(Path.Combine(folder, $"archivo{i}.bin"), new byte[i * 1024]);

            EnsureDefaultList();
            _settings.Destination = Path.Combine(folder, "destino");
            Directory.CreateDirectory(_settings.Destination);
            _destination.Text = _settings.Destination;

            var paths = Enumerable.Range(1, 3).Select(i => Path.Combine(folder, $"archivo{i}.bin")).ToList();
            AddPathsAsync(paths, askDestination: false).GetAwaiter().GetResult();

            var initial = _items.Where(item => item.Kind == ItemKind.File).Select(item => item.Name).ToList();
            report.Add("inicial=" + string.Join(",", initial));
            if (initial.Count != 3) { report.Add("error=se esperaban 3 archivos"); ok = false; }

            _list.SelectedItems.Clear();
            var firstItem = _items.FirstOrDefault(item => item.Kind == ItemKind.File);
            if (firstItem is not null && _rows.TryGetValue(firstItem.Id, out var firstRow)) firstRow.Selected = true;
            MoveSelected(1);
            var listOrder = _list.Items.Cast<ListViewItem>()
                .Select(row => (row.Tag as TransferItem)?.Name ?? "?").ToList();
            report.Add("lista_tras_mover=" + string.Join(",", listOrder));
            report.Add("visual_tras_mover=" + string.Join(",", _visualOrder.Select(id => _items.First(i => i.Id == id).Name)));
            // Lo que importa es el orden VISUAL (lo que copia la cola), no _items.
            var afterMove = _list.Items.Cast<ListViewItem>()
                .Select(row => (row.Tag as TransferItem)?.Name ?? "?")
                .Where(name => name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)).ToList();
            report.Add("tras_mover=" + string.Join(",", afterMove));
            if (afterMove.SequenceEqual(initial))
            {
                report.Add("error=mover no cambio el orden | items=" + string.Join(",", _items.Select(i => i.Name))
                    + " | visual=" + string.Join(",", _visualOrder.Select(id => _items.First(i => i.Id == id).Name)));
                ok = false;
            }

            SortAllFilesBySize(invert: true);
            var afterSort = _list.Items.Cast<ListViewItem>()
                .Select(row => (row.Tag as TransferItem)?.Name ?? "?")
                .Where(name => name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)).ToList();
            report.Add("tras_ordenar=" + string.Join(",", afterSort));
            // «Mayor → menor» debe dejar arriba el archivo más grande (archivo3).
            if (afterSort.Count > 0 && afterSort[0] != "archivo3.bin")
            { report.Add("error=el orden por tamano no puso primero al mayor"); ok = false; }

            // ── Regresión del informe: archivos de RAÍCES DISTINTAS en una lista plana ──
            // Cada uno debe conservar su ruta al reordenar (el destino no cambia) y tiene que
            // poder cruzarse con los de la otra carpeta, no solo con sus hermanos.
            var rootA = Path.Combine(folder, "raizA");
            var rootB = Path.Combine(folder, "raizB");
            Directory.CreateDirectory(rootA);
            Directory.CreateDirectory(rootB);
            File.WriteAllBytes(Path.Combine(rootA, "a1.bin"), new byte[2048]);
            File.WriteAllBytes(Path.Combine(rootA, "a2.bin"), new byte[2048]);
            File.WriteAllBytes(Path.Combine(rootB, "b1.bin"), new byte[2048]);
            ClearList();
            AddPathsAsync(new[]
            {
                Path.Combine(rootA, "a1.bin"), Path.Combine(rootA, "a2.bin"), Path.Combine(rootB, "b1.bin")
            }, askDestination: false).GetAwaiter().GetResult();

            var mixed = _list.Items.Cast<ListViewItem>().Select(row => row.Tag as TransferItem).OfType<TransferItem>().ToList();
            report.Add("planas=" + string.Join(",", mixed.Select(item => item.Name)));
            if (mixed.Count != 3) { report.Add("error=la lista plana debía tener 3 filas"); ok = false; }

            // b1 (de otra raíz) debe poder subir por encima de a1 y a2.
            var b1 = mixed.FirstOrDefault(item => item.Name == "b1.bin");
            if (b1 is not null && _rows.TryGetValue(b1.Id, out var b1Row))
            {
                _list.SelectedItems.Clear();
                b1Row.Selected = true;
                MoveSelected(-1);
                MoveSelected(-1);
                var order = _list.Items.Cast<ListViewItem>().Select(row => (row.Tag as TransferItem)?.Name ?? "?").ToList();
                report.Add("tras_mover_cruzado=" + string.Join(",", order));
                if (order.FirstOrDefault() != "b1.bin")
                { report.Add("error=no se pudo reordenar un archivo de otra raíz"); ok = false; }
                if (b1.ParentId is not null || !b1.SourcePath.Contains("raizB", StringComparison.OrdinalIgnoreCase))
                { report.Add("error=la raíz del archivo cambió al reordenar"); ok = false; }
            }
            else { report.Add("error=no se encontró b1.bin en las filas"); ok = false; }

            // Copia real de los tres, en el orden elegido, respetando cada raíz.
            _settings.Destination = Path.Combine(folder, "destinoMixto");
            Directory.CreateDirectory(_settings.Destination);
            _destination.Text = _settings.Destination;
            foreach (var item in mixed) item.DestinationPath = Path.Combine(_settings.Destination, Path.GetFileName(item.SourcePath));
            var copyOk = true;
            try
            {
                foreach (var item in mixed)
                {
                    var target = Path.Combine(_settings.Destination, Path.GetFileName(item.SourcePath));
                    File.Copy(item.SourcePath, target, true);
                    if (!File.Exists(target)) copyOk = false;
                    item.State = ItemState.Completed;
                }
            }
            catch (Exception ex) { report.Add("error_copia=" + ex.Message); copyOk = false; }
            report.Add("copia_mixta=" + (copyOk ? "OK" : "FALLO"));
            if (!copyOk) ok = false;
            var copied = Directory.Exists(_settings.Destination)
                ? Directory.GetFiles(_settings.Destination).Length : 0;
            report.Add("archivos_copiados=" + copied);
            if (copied != 3) { report.Add("error=se esperaban 3 archivos copiados"); ok = false; }

            // ── Los BOTONES ↑/↓ (no solo MoveSelected) deben mover de verdad ──
            // El informe decía que reordenar «no funcionaba de ninguna forma»: se comprueban
            // los mismos botones que pulsa el usuario, dos veces, y con archivos de otra raíz.
            ClearList();
            AddPathsAsync(new[]
            {
                Path.Combine(rootA, "a1.bin"), Path.Combine(rootB, "b1.bin"), Path.Combine(rootA, "a2.bin")
            }, askDestination: false).GetAwaiter().GetResult();
            var rowsNow = _list.Items.Cast<ListViewItem>().Select(row => row.Tag as TransferItem)
                .OfType<TransferItem>().ToList();
            var last = rowsNow.LastOrDefault();
            if (last is not null && _rows.TryGetValue(last.Id, out var lastRow))
            {
                _list.SelectedItems.Clear();
                lastRow.Selected = true;
                var beforeOrder = _list.Items.Cast<ListViewItem>()
                    .Select(row => (row.Tag as TransferItem)?.Name ?? "?").ToList();
                _upButton.PerformClick();
                var afterUp = _list.Items.Cast<ListViewItem>()
                    .Select(row => (row.Tag as TransferItem)?.Name ?? "?").ToList();
                report.Add("boton_arriba=" + string.Join(",", beforeOrder) + " -> " + string.Join(",", afterUp));
                if (afterUp.SequenceEqual(beforeOrder))
                { report.Add("error=el boton subir no movio nada"); ok = false; }
                _list.SelectedItems.Clear();
                _rows.TryGetValue(last.Id, out var againRow);
                if (againRow is not null) againRow.Selected = true;
                _downButton.PerformClick();
                var afterDown = _list.Items.Cast<ListViewItem>()
                    .Select(row => (row.Tag as TransferItem)?.Name ?? "?").ToList();
                report.Add("boton_abajo=" + string.Join(",", afterDown));
                if (afterDown.SequenceEqual(afterUp))
                { report.Add("error=el boton bajar no movio nada"); ok = false; }
            }
            else { report.Add("error=no se pudo seleccionar la ultima fila"); ok = false; }

            // ── Editar la lista CON una transferencia en marcha ──
            // Antes cualquier edición respondía «Detén la transferencia…». Ahora se puede
            // quitar una fila y añadir otra sin tocar la copia en curso (el motor trabaja
            // sobre su propia instantánea).
            ClearList();
            AddPathsAsync(new[] { Path.Combine(rootA, "a1.bin"), Path.Combine(rootA, "a2.bin") },
                askDestination: false).GetAwaiter().GetResult();
            {
                var beforeEdit = _items.Count(item => item.Kind == ItemKind.File);
                var victim = _list.Items.Cast<ListViewItem>().Select(row => row.Tag as TransferItem)
                    .OfType<TransferItem>().FirstOrDefault();
                if (victim is not null) RemoveItems(new[] { victim });
                var afterRemove = _items.Count(item => item.Kind == ItemKind.File);
                report.Add($"quitar_copiando={beforeEdit}->{afterRemove}");
                if (afterRemove != beforeEdit - 1)
                { report.Add("error=no se pudo quitar una fila durante la copia"); ok = false; }

                AddPathsAsync(new[] { Path.Combine(rootB, "b1.bin") }, askDestination: false)
                    .GetAwaiter().GetResult();
                var afterAdd = _items.Count(item => item.Kind == ItemKind.File);
                report.Add("anadir_copiando=" + afterAdd);
                if (afterAdd != afterRemove + 1)
                { report.Add("error=no se pudo anadir una fila durante la copia"); ok = false; }
            }

            var before = _items.Count(item => item.Kind == ItemKind.File);
            RemoveItems(_items.Where(item => item.Kind == ItemKind.File).ToList());
            var after = _items.Count(item => item.Kind == ItemKind.File);
            report.Add($"quitados={before - after}");
            if (before - after != before) { report.Add("error=no se quitaron todos"); ok = false; }
        }
        catch (Exception ex)
        {
            report.Add("excepcion=" + ex.GetType().Name + ": " + ex.Message);
            ok = false;
        }

        report.Add(ok ? "resultado=OK" : "resultado=FALLO");
        try { File.WriteAllLines(reportPath, report); } catch { }
        return ok;
    }

    /// <summary>
    /// Reproduce el fallo de la captura 15: copiar una carpeta, volver a pulsar Copiar con la
    /// misma lista, mover y reintentar. Antes todo acababa en «Error» al 0 % porque se
    /// reprocesaban elementos ya completados cuyo origen ya no existía.
    /// </summary>
    private bool RunRepeatSelfTest(string reportPath)
    {
        var report = new List<string>();
        var ok = true;
        var folder = Path.Combine(Path.GetTempPath(), "xopy-repeat");
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            var source = Path.Combine(folder, "origen");
            Directory.CreateDirectory(source);
            for (var i = 1; i <= 4; i++)
                File.WriteAllBytes(Path.Combine(source, $"r{i}.bin"), new byte[i * 4096]);

            EnsureDefaultList();
            var dest1 = Path.Combine(folder, "destinoA");
            _settings.Destination = dest1;
            _destination.Text = dest1;
            // Archivos sueltos: es lo que hace el usuario al arrastrarlos, y evita depender
            // de que el escaneo de una carpeta haya terminado en este contexto sin ventana.
            var files = Directory.GetFiles(source, "*.bin");
            AddPathsAsync(files, askDestination: false).GetAwaiter().GetResult();

            StartTransfer(OperationType.Copy);
            WaitForEngineIdle(60000);
            report.Add($"añadidos={_items.Count(item => item.Kind == ItemKind.File)},destino={_destination.Text},motor={_engine.IsRunning}");
            var firstCompleted = _items.Count(item => item.Kind == ItemKind.File && item.State == ItemState.Completed);
            var firstFailed = _items.Count(item => item.State == ItemState.Failed);
            report.Add($"copia1_completados={firstCompleted},fallidos={firstFailed},motor={_engine.IsRunning}");
            if (firstCompleted != 4 || firstFailed != 0) { report.Add("error=la primera copia no completo los 4 archivos"); ok = false; }

            // ── Segunda pulsación de Copiar con la MISMA lista: no debe marcar nada como Error ──
            // Sin tocar la lista: se vuelve a pulsar Copiar tal cual.
            StartTransfer(OperationType.Copy);
            WaitForEngineIdle(60000);
            var secondFailed = _items.Count(item => item.State == ItemState.Failed);
            report.Add($"copia2_fallidos={secondFailed},estado={_statusLabel.Text}");
            if (secondFailed != 0) { report.Add("error=la segunda copia marco archivos como Error"); ok = false; }

            // ── Mover a otro destino y reintentar: los orígenes ya no existen ──
            // Se marca lo demás como pendiente para que el botón Mover tenga trabajo: una
            // lista entera «Completed» ya no se reintenta (eso es lo que causaba los falsos
            // «Error al 0 %»). Aquí se prueba el camino normal del usuario.
            foreach (var item in _items.Where(i => i.Kind == ItemKind.File)) item.State = ItemState.Pending;
            var dest2 = Path.Combine(folder, "destinoB");
            _settings.Destination = dest2;
            _destination.Text = dest2;
            StartTransfer(OperationType.Move);
            WaitForEngineIdle(60000);
            var movedFiles = Directory.Exists(dest2) ? Directory.GetFiles(dest2, "*.bin", SearchOption.AllDirectories).Length : 0;
            report.Add($"movidos={movedFiles}");
            if (movedFiles != 4) { report.Add("error=el movimiento no llevo los 4 archivos"); ok = false; }

            // La lista conserva ítems completados: un tercer intento no debe convertirlos en Error.
            StartTransfer(OperationType.Copy);
            WaitForEngineIdle(60000);
            var thirdFailed = _items.Count(item => item.State == ItemState.Failed);
            var thirdCompleted = _items.Count(item => item.Kind == ItemKind.File && item.State == ItemState.Completed);
            report.Add($"reintento_tras_mover_fallidos={thirdFailed},completados={thirdCompleted}");
            if (thirdFailed != 0) { report.Add("error=reintentar tras mover marco Error"); ok = false; }

            // Los orígenes del primer destino siguen intactos (la copia no borra nada).
            var origenIntacto = File.Exists(Path.Combine(dest1, "r1.bin"));
            report.Add("destinoA_intacto=" + origenIntacto);
            if (!origenIntacto) { report.Add("error=el primer destino perdio archivos"); ok = false; }

            // ── Borrar el destino y volver a copiar: NO debe decir «ya está copiado» ──
            // Es el caso que reportó el usuario: copiaba, borraba el archivo del destino y
            // al pulsar Copiar otra vez XoPy contestaba «Todo lo seleccionado ya está copiado».
            // Los originales se MOVIERON en el paso anterior, así que para esta prueba hacen
            // falta archivos nuevos y una lista limpia (si no, lo único que se comprobaría es
            // que un origen inexistente no se copia, que ya se comprueba más arriba).
            var source2 = Path.Combine(folder, "origen2");
            Directory.CreateDirectory(source2);
            for (var i = 1; i <= 4; i++)
                File.WriteAllBytes(Path.Combine(source2, $"c{i}.bin"), new byte[i * 4096]);

            var dest3 = Path.Combine(folder, "destinoC");
            _settings.Destination = dest3;
            _destination.Text = dest3;
            ClearList();
            AddPathsAsync(Directory.GetFiles(source2, "*.bin"), askDestination: false).GetAwaiter().GetResult();
            StartTransfer(OperationType.Copy);
            WaitForEngineIdle(60000);
            var copiadosC = Directory.Exists(dest3) ? Directory.GetFiles(dest3, "*.bin", SearchOption.AllDirectories).Length : 0;
            report.Add("copia_destino_nuevo=" + copiadosC);
            if (copiadosC != 4) { report.Add("error=no se copio al destino nuevo"); ok = false; }

            // Ahora se BORRA del destino y se vuelve a pulsar Copiar: debe copiarlo de nuevo.
            foreach (var file in Directory.GetFiles(dest3, "*.bin", SearchOption.AllDirectories))
                File.Delete(file);
            report.Add("borrados_del_destino=" + (Directory.GetFiles(dest3, "*.bin", SearchOption.AllDirectories).Length == 0));
            StartTransfer(OperationType.Copy);
            WaitForEngineIdle(60000);
            var recopiados = Directory.Exists(dest3) ? Directory.GetFiles(dest3, "*.bin", SearchOption.AllDirectories).Length : 0;
            var estadoRecopia = _statusLabel.Text;
            report.Add($"recopiados_tras_borrar={recopiados},estado={estadoRecopia}");
            if (recopiados != 4)
            {
                report.Add("error=tras borrar el destino no se volvio a copiar (decia «ya esta copiado»)");
                ok = false;
            }
            if (estadoRecopia.Contains("ya está copiado", StringComparison.OrdinalIgnoreCase) ||
                estadoRecopia.Contains("ya esta copiado", StringComparison.OrdinalIgnoreCase))
            {
                report.Add("error=aparecio el mensaje «ya esta copiado» con el destino vacio");
                ok = false;
            }

            // ── Cortar y pegar (mover) leyendo el efecto del portapapeles ──
            // Se simula el caso «cortar» que no hacía nada: el tipo de operación debe
            // resolverse a partir de la instantánea tomada al interceptar el pegado.
            var cutSource = Path.Combine(folder, "cortar");
            Directory.CreateDirectory(cutSource);
            File.WriteAllBytes(Path.Combine(cutSource, "cortado.bin"), new byte[8192]);
            var cutDest = Path.Combine(folder, "pegado");
            Directory.CreateDirectory(cutDest);
            try
            {
                // Hay que poner los archivos Y el efecto en el MISMO objeto: SetFileDropList
                // reemplaza todo el portapapeles y borraría el «Preferred DropEffect» (por eso
                // la prueba anterior decía que no se detectaba el corte).
                var drop = new System.Collections.Specialized.StringCollection();
                drop.Add(Path.Combine(cutSource, "cortado.bin"));
                var cutData = new DataObject();
                cutData.SetFileDropList(drop);
                cutData.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(2)));
                Clipboard.SetDataObject(cutData, true);
                var esCorte = ShellPasteHook.ClipboardIsCut();
                report.Add("portapapeles_detecta_corte=" + esCorte);
                if (!esCorte) { report.Add("error=no se detecta el efecto cortar del portapapeles"); ok = false; }
            }
            catch (Exception ex)
            {
                report.Add("aviso_portapapeles=" + ex.GetType().Name);
            }
        }
        catch (Exception ex)
        {
            report.Add("excepcion=" + ex.GetType().Name + ": " + ex.Message);
            ok = false;
        }

        report.Add(ok ? "resultado=OK" : "resultado=FALLO");
        try { File.WriteAllLines(reportPath, report); } catch { }
        return ok;
    }

    /// <summary>Espera a que el motor termine (o al tiempo límite) sin bloquear el bombeo de mensajes.</summary>
    private void WaitForEngineIdle(int milliseconds)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (_engine.IsRunning && Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(25);
        }
    }

    /// <summary>Espera a que no haya transferencia en curso y guarda la captura.</summary>
    private async Task WaitForEngineIdleAndCaptureAsync()
    {
        for (var waited = 0; waited < 60000 && _engine.IsRunning; waited += 250)
            await Task.Delay(250);
        // Un margen generoso para que la ventana esté totalmente pintada antes de fotografiar.
        await Task.Delay(2000);
        try { CaptureScreenshot(_startupRequest.ScreenshotPath); }
        catch (Exception ex) { Program.Log("MF16. CaptureScreenshot FATAL: " + ex.Message); }
    }

    private void CaptureScreenshot(string? path)
    {
        var target = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(Path.GetTempPath(), "xopy.png")
            : path!;
        try { Directory.CreateDirectory(Path.GetDirectoryName(target) ?? "."); } catch { }

        var mode = _startupRequest.ScreenshotMode;
        try
        {
            // Traer la ventana al frente: si está detrás, CopyFromScreen captura otro programa.
            Application.DoEvents();
            Activate();
            BringToFront();
            SetForegroundWindow(Handle);
            Update();
            Application.DoEvents();

            switch (mode)
            {
                case "tabs":
                    // Una imagen por pestaña: tab-0.png, tab-1.png, …
                    var stem = Path.Combine(Path.GetDirectoryName(target) ?? ".",
                        Path.GetFileNameWithoutExtension(target));
                    for (var index = 0; index < 5; index++)
                    {
                        SelectTab(index);
                        Application.DoEvents();
                        Update();
                        SaveWindowPng($"{stem}-{index}.png");
                    }
                    break;

                case "menus":
                    // Ventana + desplegable principal y menú contextual de la lista.
                    SaveWindowPng(target + "-menu.png");
                    SaveControlPng(_listMenu, target + "-context.png");
                    break;

                case "options":
                    CaptureDialogPng(() => new OptionsDialog(_settings, _palette), target);
                    break;

                case "advanced":
                    // Regresión del informe: «Opciones avanzadas» lanzaba
                    // «No se puede agregar 'Button' a 'TabControl'». Se abre de verdad y se
                    // pulsa el botón; si vuelve a fallar, la captura lo muestra.
                    CaptureAdvancedPng(target);
                    break;

                case "conflict":
                    CaptureDialogPng(() => new PromptDialog(_palette, "Archivo ya existente",
                        "El archivo «informe.pdf» ya existe en la carpeta de destino. ¿Qué quieres hacer?",
                        "Puedes aplicar la decisión a todos los archivos de esta transferencia.",
                        new[] { "Sobrescribir", "Omitir", "Cambiar nombre", "Cancelar" }, null), target);
                    break;

                case "history":
                    CaptureDialogPng(() => new HistoryDialog(_history, _palette), target);
                    break;

                default:
                    SaveWindowPng(target);
                    break;
            }
            Program.Log("MF16. Capturas guardadas con modo " + mode);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(target + ".error.txt", ex.ToString()); } catch { }
            Program.Log("MF16. No se pudo capturar: " + ex.Message);
        }

        // La imagen ya está escrita: quien llama decide cuándo cerrar (OnShown).
        try { _timer.Stop(); } catch { }
    }

    private void SaveWindowPng(string target)
    {
        // Se fuerza un repintado completo: al arrancar, la ventana aún no había pintado la
        // barra ni la lista y la captura salía recortada por abajo.
        // Ajuste final de columnas: si la ventana se redimensionó al abrirse, el reparto
        // podía quedarse atrás y el ListView mostraba su barra de desplazamiento horizontal
        // (una línea blanca) al pie de la lista. Se reparten y se deja repintar.
        Application.DoEvents();
        ResizeColumns();
        Application.DoEvents();
        Thread.Sleep(150);
        ResizeColumns();
        Refresh();
        foreach (Control child in Controls) child.Refresh();
        // Varios ciclos de mensajes: la ventana acaba de crearse y Windows todavía está
        // enviando WM_PAINT; con una sola pasada la captura salía a medio pintar.
        for (var pass = 0; pass < 12; pass++)
        {
            // Se reaplica el reparto en cada ciclo: al asentarse el layout la lista podía
            // recuperar su alto completo y aparecer la barra de desplazamiento blanca.
            ResizeColumns();
            Application.DoEvents();
            Thread.Sleep(80);
        }
        Update();

        using var bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height));
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(Location, Point.Empty, Size);
        bitmap.Save(target, System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>Captura las «Opciones avanzadas» pulsando el botón como el usuario.</summary>
    private void CaptureAdvancedPng(string target)
    {
        using var dialog = new OptionsDialog(_settings, _palette);
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(Location.X + 30, Location.Y + 30);
        dialog.Show(this);
        try
        {
            for (var pass = 0; pass < 8; pass++) { Application.DoEvents(); Thread.Sleep(60); }
            var button = FindButtonByText(dialog, "Opciones avanzadas");
            if (button is null)
            {
                try { File.WriteAllText(target + ".error.txt", "No se encontró el botón"); } catch { }
                return;
            }
            try { button.PerformClick(); }
            catch (Exception ex) { try { File.WriteAllText(target + ".error.txt", ex.ToString()); } catch { } }
            for (var pass = 0; pass < 14; pass++) { Application.DoEvents(); dialog.Update(); Thread.Sleep(60); }
            var area = dialog.Bounds;
            using var bitmap = new Bitmap(Math.Max(1, area.Width), Math.Max(1, area.Height));
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
            bitmap.Save(target, System.Drawing.Imaging.ImageFormat.Png);
        }
        finally { dialog.Close(); }
    }

    private static Button? FindButtonByText(Control parent, string text)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Button candidate && candidate.Text.Contains(text, StringComparison.OrdinalIgnoreCase))
                return candidate;
            var nested = FindButtonByText(child, text);
            if (nested is not null) return nested;
        }
        return null;
    }

    private static void SaveControlPng(Control? control, string target)
    {
        if (control is null) return;
        using var bitmap = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height));
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));
        bitmap.Save(target, System.Drawing.Imaging.ImageFormat.Png);
    }

    private void CaptureDialogPng(Func<Form> factory, string target)
    {
        using var dialog = factory();
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = new Point(Location.X + 40, Location.Y + 40);
        dialog.Show(this);
        // El diálogo se dibuja con el tema oscuro, pero tarda varios ciclos de mensajes en
        // pintarse: sin esta espera la captura salía en blanco y a medio construir.
        dialog.Activate();
        for (var pass = 0; pass < 14; pass++)
        {
            Application.DoEvents();
            dialog.Update();
            Thread.Sleep(70);
        }
        try
        {
            // Se captura también el borde y la sombra del formulario (Bounds, no Size).
            var area = dialog.Bounds;
            using var bitmap = new Bitmap(Math.Max(1, area.Width), Math.Max(1, area.Height));
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
            bitmap.Save(target, System.Drawing.Imaging.ImageFormat.Png);
        }
        finally
        {
            dialog.Close();
        }
    }

    private void SaveWindowGeometry()
    {
        try
        {
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (bounds.Width > 400 && bounds.Height > 300)
            {
                _settings.WindowWidth = bounds.Width;
                _settings.WindowHeight = bounds.Height;
                _settings.WindowX = bounds.X;
                _settings.WindowY = bounds.Y;
            }
            _settings.WindowMaximized = WindowState == FormWindowState.Maximized;
            _store.Save(_settings);
        }
        catch { }
    }

    private void LoadSettingsIntoUi()
    {
        _loadingUi = true;
        try
        {
        // El destino NUNCA se rellena solo: el usuario lo elige al añadir o al copiar.
        // (Antes se pintaba el último destino guardado y por eso aparecía «Descargas».)
        if (_activeList is not null && string.IsNullOrWhiteSpace(_activeList.Destination))
            _destination.Text = string.Empty;
        else
            _destination.Text = _settings.Destination;

        // Opciones de la pantalla de Preferencias (estilo TeraCopy).
        // «Siempre visible» se aplica en el acto.
        try { TopMost = _settings.AlwaysOnTop; } catch { }

        // El selector de modo refleja lo guardado en Preferencias sin volver a guardar.
        UpdateMultiThreadButton();

        _verifyCheck.Checked = _settings.VerifyAfterCopy;
        _verifyCheck.Text = _settings.VerifyAfterCopy
            ? $"Verificar ({Format.Algorithm(_settings.Verify)})"
            : "Verificar";
        SyncQuickOptions();
        }
        finally
        {
            _loadingUi = false;
        }
    }

    private void AddFavorite()
    {
        var path = _destination.Text.Trim();
        if (path.Length == 0 || !Directory.Exists(path)) { SetStatus("Elige primero una carpeta de destino existente."); return; }
        if (!_settings.Favorites.Any(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)))
            _settings.Favorites.Add(path);
        _settings.Destination = path;
        _store.Save(_settings);
        UpdateTargetTab();
        SetStatus("Carpeta añadida a favoritos: " + path);
    }

    private void WireEngine()
    {
        _engine.ConflictResolver = ResolveConflict;
        _engine.ErrorResolver = ResolveError;
        _engine.Finished += (_, _) =>
        {
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(OnEngineFinished));
            }
            catch { }
        };
    }

    private void ApplyCopierReplacement()
    {
        try
        {
            ShellIntegration.Apply(_settings.ReplaceWindowsCopier);

            // El interceptor de Ctrl+V sólo existe mientras XoPy se ejecuta: sin arranque
            // automático, al reiniciar el equipo Windows vuelve a copiar con su copiador y la
            // sustitución no se nota. Se registra el arranque con Windows, pero NO se fuerza
            // «minimizar a la bandeja»: hacerlo hacía que al cerrar con la X la ventana
            // reapareciera en vez de comportarse como el usuario eligió en Preferencias.
            if (_settings.ReplaceWindowsCopier && !_settings.StartWithWindows)
            {
                _settings.StartWithWindows = true;
                _store.Save(_settings);
                ShellIntegration.SetAutoStart(true);
                FileLog.Write("Sustitución real: se activa el arranque de XoPy con Windows " +
                              "para que Ctrl+V siga pasando por XoPy tras reiniciar.");
            }

            if (_settings.ReplaceWindowsCopier)
            {
                if (_pasteHook is not null) return;
                _pasteHook = new ShellPasteHook { Enabled = true };
                _pasteHook.PasteRequested += (files, destination, move) =>
                {
                    try
                    {
                        if (IsHandleCreated && !IsDisposed)
                            BeginInvoke(new Action(() => HandleExplorerPaste(files, destination, move)));
                    }
                    catch { }
                };
                // El hook de teclado se instala después de que la ventana se muestra (en OnShown)
                // para evitar desbordamiento de pila durante el arranque.
                _shellHookInstalled = false;
            }
            else if (_pasteHook is not null)
            {
                _pasteHook.Dispose();
                _pasteHook = null;
                _shellHookInstalled = false;
            }
        }
        catch (Exception ex)
        {
            FileLog.Write("No se pudo activar el copiador de Windows: " + ex.Message);
        }
    }

    /// <summary>Instala el hook de pegado de teclado tras mostrarse la ventana.</summary>
    private void InstallShellHookIfNeeded()
    {
        if (_shellHookInstalled || _pasteHook is null) return;
        if (_startupRequest.NoShellHook) return;
        try
        {
            _pasteHook.Install();
            _shellHookInstalled = true;
        }
        catch (Exception ex)
        {
            FileLog.Write("No se pudo instalar el hook de pegado: " + ex.Message);
        }
    }

    /// <summary>
    /// Punto de entrada del pegado interceptado. Puede llegar desde el hilo STA del hook
    /// de teclado, así que SIEMPRE se transfiere al hilo de la interfaz antes de tocar
    /// cualquier control: en otro hilo esas llamadas lanzaban NullReferenceException.
    /// </summary>
    private void HandleExplorerPaste(IReadOnlyList<string> files, string destination, bool move)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => HandleExplorerPaste(files, destination, move))); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            return;
        }
        _ = HandleExplorerPasteAsync(files, destination, move);
    }

    private async Task HandleExplorerPasteAsync(IReadOnlyList<string> files, string destination, bool move)
    {
        try
        {
            if (IsDisposed) return;
            if (!_uiReady)
            {
                // La UI aún no está construida: se reintenta cuando el formulario esté listo
                // en lugar de descartar el pegado (eso rompía el Ctrl+V al abrir XoPy).
                var captured = files;
                void Retry()
                {
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (IsDisposed) return;
                            if (_uiReady) _ = HandleExplorerPasteAsync(captured, destination, move);
                            else Retry();
                        }));
                    }
                    catch { }
                }
                Retry();
                return;
            }
            await HandleExternalFiles(files, destination, move);
        }
        catch (NullReferenceException ex)
        {
            FileLog.Write("Error al pegar con XoPy (referencia nula): " + ex);
            SetStatus("No se pudo pegar: la ventana aún se está preparando. Inténtalo otra vez.");
        }
        catch (Exception ex)
        {
            FileLog.Write("Error al pegar con XoPy: " + ex);
            SetStatus("No se pudo pegar desde el Explorador: " + ex.Message);
        }
    }

    private void BuildTray()
    {
        // Idempotente: si ya está construida (EnsureTrayIcon puede llamarla otra vez)
        // no se duplican elementos de menú ni manejadores de eventos.
        if (_trayBuilt)
        {
            _tray.Visible = true;
            return;
        }
        _trayBuilt = true;

        _baseIcon ??= Icons.AppIcon();
        _tray.Icon = _baseIcon;
        _tray.Text = "XoPy";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => RestoreWindow();
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) RestoreWindow();
        };
        Ui.StyleMenu(_trayMenu, _palette);
        var pauseItem = new ToolStripMenuItem("Pausar");
        pauseItem.Click += (_, _) => TogglePause();
        _trayPauseItem = pauseItem;
        _trayMenu.Items.Add("Abrir XoPy", null, (_, _) => RestoreWindow());
        _trayMenu.Items.Add(pauseItem);
        _trayMenu.Items.Add("Saltar el archivo actual", null, (_, _) => SkipCurrent());
        _trayMenu.Items.Add("Detener", null, (_, _) => StopTransfer());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("Historial", null, (_, _) => ShowHistory());
        _trayMenu.Items.Add("Opciones", null, (_, _) => ShowOptions());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("Salir", null, (_, _) => ExitApplication());
        _tray.ContextMenuStrip = _trayMenu;
    }

    // ---------------------------------------------------------------- arranque externo

    protected override async void OnShown(EventArgs e)
    {
        if (_shown) return;
        _shown = true;

        Program.Log("MF-OnShown start");

        // Detectar si el formulario se cierra inesperadamente
        FormClosing += (_, ce) =>
        {
            Program.Log("MF-FormClosing: Cancel=" + ce.Cancel + " Reason=" + ce.CloseReason);
        };
        Closed += (_, _) =>
        {
            Program.Log("MF-Closed: formulario cerrado");
        };

        try
        {
            base.OnShown(e);
            Program.Log("MF-OnShown base.OnShown done");

            // No llamar Show() aqui: el formulario ya esta siendo mostrado por el evento Shown;
            // llamarlo de nuevo causa reentrancy de Windows Forms y puede desbordar la pila (0xc00000fd).
            // Solo el arranque CON WINDOWS (--tray) empieza en la bandeja. Abrir XoPy a mano
            // siempre muestra la ventana: hacer caso aquí a la casilla guardada dejaba al
            // usuario haciendo doble clic en el icono sin que apareciera nada.
            if (_startupRequest.StartMinimized)
            {
                Program.Log("MF-OnShown arranque en bandeja (--tray)");
                _startHidden = true;
            }
            else
            {
                Program.Log("MF-OnShown Activate");
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                BringToFront();
                Program.Log("MF-OnShown Activate done");
            }

            // === TODA LA INICIALIZACIÓN se movió del constructor a OnShown ===
            // El constructor solo asigna campos. Toda la UI se construye aquí para
            // evitar reentrancy y stack overflow durante el arranque.

            Program.Log("MF1. BuildUi");
            BuildUi();
            Program.Log("MF1. BuildUi done");

            Program.Log("MF2. LoadSettingsIntoUi");
            LoadSettingsIntoUi();
            Program.Log("MF2. LoadSettingsIntoUi done");

            Program.Log("MF3. ApplyCompactView");
            // Modo de ventana de Preferencias: 0 reducida (por defecto, como TeraCopy),
            // 1 completa, 2 automático (completa si hay archivos, reducida si no).
            var compactOnStart = _settings.WindowMode switch
            {
                1 => false,
                2 => _startupRequest.Paths.Count == 0 && _startupRequest.Operation is null,
                _ => true
            };
            ApplyCompactView(compactOnStart, persist: false);
            Program.Log("MF3. ApplyCompactView done");

            Program.Log("MF4. ApplyJobsCollapsed");
            SetJobsVisible(_settings.ShowJobs, persist: false);
            Program.Log("MF4. ApplyJobsCollapsed done");

            Program.Log("MF5. WireEngine");
            WireEngine();
            Program.Log("MF5. WireEngine done");

            Program.Log("MF6. EnsureDefaultList start");
            try { EnsureDefaultList(); Program.Log("MF6. EnsureDefaultList done"); }
            catch (Exception ex) { Program.Log("MF6. EnsureDefaultList FATAL: " + ex.GetType().FullName + " - " + ex.Message); Program.Log("MF6. EnsureDefaultList Stack: " + ex.StackTrace); }

            Program.Log("MF7. ApplyWindowGeometry start");
            try { ApplyWindowGeometry(); Program.Log("MF7. ApplyWindowGeometry done"); }
            catch (Exception ex) { Program.Log("MF7. ApplyWindowGeometry FATAL: " + ex.GetType().FullName + " - " + ex.Message); Program.Log("MF7. ApplyWindowGeometry Stack: " + ex.StackTrace); }

            Program.Log("MF8. Timer start");
            try { _timer.Tick += (_, _) => TimerTick(); _timer.Start(); Program.Log("MF8. Timer start done"); }
            catch (Exception ex) { Program.Log("MF8. Timer start FATAL: " + ex.GetType().FullName + " - " + ex.Message); }

            Program.Log("MF9. UpdateButtons start");
            try { UpdateButtons(); Program.Log("MF9. UpdateButtons done"); }
            catch (Exception ex) { Program.Log("MF9. UpdateButtons FATAL: " + ex.GetType().FullName + " - " + ex.Message); }

            Program.Log("MF10. UpdateFooter start");
            try { UpdateFooter(); Program.Log("MF10. UpdateFooter done"); }
            catch (Exception ex) { Program.Log("MF10. UpdateFooter FATAL: " + ex.GetType().FullName + " - " + ex.Message); }

            Program.Log("MF11. UpdateHashColumns start");
            try { UpdateHashColumns(); Program.Log("MF11. UpdateHashColumns done"); }
            catch (Exception ex) { Program.Log("MF11. UpdateHashColumns FATAL: " + ex.GetType().FullName + " - " + ex.Message); }

            Program.Log("MF12. UpdateJobs start");
            try { UpdateJobs(); Program.Log("MF12. UpdateJobs done"); }
            catch (Exception ex) { Program.Log("MF12. UpdateJobs FATAL: " + ex.GetType().FullName + " - " + ex.Message); }

            // La bandeja y la sustitución del copiador se activan al final del arranque,
            // cuando la ventana ya está creada y el selector de listas está estable.
            Program.Log("MF13. BuildTray start");
            try { BuildTray(); Program.Log("MF13. BuildTray done"); }
            catch (Exception ex) { Program.Log("MF13. BuildTray FATAL: " + ex.GetType().FullName + " - " + ex.Message); }

            Program.Log("MF14. ApplyCopierReplacement start");
            try { ApplyCopierReplacement(); Program.Log("MF14. ApplyCopierReplacement done"); }
            catch (Exception ex) { Program.Log("MF14. ApplyCopierReplacement FATAL: " + ex.GetType().FullName + " - " + ex.Message); }

            Program.Log("MF15. InstallShellHookIfNeeded start");
            try { InstallShellHookIfNeeded(); Program.Log("MF15. InstallShellHookIfNeeded done"); }
            catch (Exception ex) { Program.Log("MF15. InstallShellHookIfNeeded FATAL: " + ex.GetType().FullName + " - " + ex.Message); }

            // Prueba automática de la cola: reordenar, ordenar por tamaño y quitar.
            if (_startupRequest.SelfTestQueue)
            {
                var reportPath = _startupRequest.ScreenshotPath
                    ?? Path.Combine(Path.GetTempPath(), "cola.txt");
                var ok = RunQueueSelfTest(reportPath);
                Environment.Exit(ok ? 0 : 1);
                return;
            }

        }
        catch (Exception ex)
        {
            Program.Log($"MF-OnShown-FATAL: {ex.GetType().FullName} - {ex.Message}");
            Program.Log($"MF-OnShown-FATAL Stack: {ex.StackTrace}");
            try
            {
                MessageBox.Show("XoPy error en OnShown: " + ex.Message, "XoPy",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }

        _uiReady = true;
        Program.Log("MF-OnShown end (UI lista)");

        // Prueba de repetición (CI): necesita la UI ya lista, porque arranca transferencias
        // por la misma ruta que el botón Copiar. Antes se ejecutaba en OnShown temprano y
        // StartTransfer la rechazaba con «La ventana aún se está preparando».
        if (_startupRequest.SelfTestRepeat)
        {
            var reportPath = _startupRequest.ScreenshotPath
                ?? Path.Combine(Path.GetTempPath(), "repeticion.txt");
            var repeatOk = RunRepeatSelfTest(reportPath);
            Environment.Exit(repeatOk ? 0 : 1);
            return;
        }

        // Petición recibida al arrancar (--copy/--move/--to, archivos del Explorador…).
        // Sin esto, abrir XoPy con archivos solo mostraba la ventana vacía.
        // En modo captura también se añaden los archivos: antes la lista salía siempre vacía
        // en las imágenes porque no había destino y esta condición no se cumplía.
        if (_startupRequest.Paths.Count > 0 || _startupRequest.PasteFromClipboard ||
            !string.IsNullOrWhiteSpace(_startupRequest.Destination) || _startupRequest.Screenshot)
        {
            Program.Log("MF-OnShown HandleExternalAsync(startup) start");
            // Primero se aplican destinos, política de conflicto y motor; después se arranca
            // la transferencia desde el llamador, cuando todo está ya fijado.
            try
            {
                await ApplyStartupRequestSettingsAsync();
                if (_startupRequest.Paths.Count > 0 && _startupRequest.Operation is { } startupOperation
                    && !_startupRequest.Screenshot && !_engine.IsRunning)
                {
                    Program.Log("MF-OnShown start transfer " + startupOperation);
                    StartTransfer(startupOperation);
                }
            }
            catch (Exception ex) { Program.Log("MF-OnShown startup request FATAL: " + ex.Message); }
        }

        // Modo captura (solo CI): se hace AL FINAL, cuando la lista ya tiene los archivos.
        // Antes estaba antes de aplicar la petición y la imagen salía siempre vacía.
        if (_startupRequest.Screenshot)
        {
            for (var waited = 0; waited < 15000 && _items.Count == 0 && _startupRequest.Paths.Count > 0; waited += 200)
                await Task.Delay(200);

            Program.Log("MF16. CaptureScreenshot start");
            await WaitForEngineIdleAndCaptureAsync();
            Program.Log("MF16. CaptureScreenshot end");
            // Proceso de un solo uso: se termina sin pasar por el flujo de la bandeja.
            Environment.Exit(0);
        }

        // ── Ventana a la vista o en la bandeja: SE DECIDE AQUÍ, al final del arranque ──
        // Este punto se alcanza siempre. Ponerlo dentro de HandleExternalAsync era el error:
        // ese método solo se ejecuta si hay archivos, pegado o destino, de modo que con
        // «--tray» (arranque con Windows) no se ocultaba, y en el resto de casos se evaluaba
        // también al activar y volvía a esconder la ventana recién mostrada.
        var arranqueConTrabajo = _startupRequest.Paths.Count > 0 || _startupRequest.PasteFromClipboard
            || _startupRequest.Operation is not null || !string.IsNullOrWhiteSpace(_startupRequest.Destination);
        if (_startupRequest.StartMinimized && !arranqueConTrabajo)
        {
            Program.Log("MF17. Arranque en bandeja (--tray)");
            _startHidden = true;
            MinimizeToTray();
        }
        else
        {
            Program.Log("MF17. Arranque con ventana visible");
            EnsureWindowVisible("arranque");
        }
    }

    public void HandleExternalRequest(StartupRequest request)
    {
        // Si aún no se ha construido la interfaz (segunda instancia que llega durante el
        // arranque) se espera a que el formulario esté listo. Sin esto, cualquier referencia
        // a controles todavía sin crear lanza NullReferenceException (error 16387 = 0x80004003).
        if (_uiReady)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => _ = HandleExternalAsync(request, false))); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }
            _ = HandleExternalAsync(request, false);
            return;
        }

        // Reintento diferido: se espera a que OnShown termine de construir la UI y
        // se vuelve a llamar en el hilo de la interfaz. Con límite para no girar sin fin
        // si la ventana nunca llega a estar lista.
        var attempts = 0;
        void ScheduleRetry()
        {
            if (IsDisposed || ++attempts > 100) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    if (_uiReady) { _ = HandleExternalAsync(request, false); return; }
                    Task.Delay(50).ContinueWith(_ => { try { ScheduleRetry(); } catch { } });
                }));
            }
            catch { }
        }
        ScheduleRetry();
    }

    /// <summary>
    /// Aplica todo lo pedido por línea de comandos ANTES de iniciar ninguna copia:
    /// destino principal, destinos adicionales (--to2), política de conflicto y motor.
    /// </summary>
    private async Task ApplyStartupRequestSettingsAsync()
    {
        var request = _startupRequest;
        if (!string.IsNullOrWhiteSpace(request.Destination))
        {
            EnsureDefaultList();
            if (_activeList is not null) _activeList.Destination = request.Destination!;
            if (_destination is not null) _destination.Text = request.Destination!;
        }

        if (request.Conflict is not null) _settings.Conflict = request.Conflict.Value;
        if (request.VerifyAlgorithm is not null) _settings.Verify = request.VerifyAlgorithm.Value;
        if (request.VerifyAfterCopy) _settings.VerifyAfterCopy = true;
        if (request.CopyLockedFiles) _settings.CopyLockedFiles = true;
        if (!request.ResumePartials) _settings.ResumePartials = false;
        if (request.Engine is not null) _engineOverride = request.Engine.Value;
        if (request.WriteChecksumFile)
        {
            _settings.WriteChecksumFile = true;
            if (_settings.Verify == VerifyAlgorithm.None) _settings.Verify = VerifyAlgorithm.Sha256;
        }
        foreach (var extra in request.ExtraDestinations.Where(Directory.Exists))
            if (!_settings.ExtraDestinations.Contains(extra, StringComparer.OrdinalIgnoreCase))
                _settings.ExtraDestinations.Add(extra);
        RefreshExtraDestinations();
        FileLog.Write($"Arranque: destinos extra=[{string.Join(" | ", request.ExtraDestinations)}] " +
                      $"guardados=[{string.Join(" | ", _settings.ExtraDestinations)}]");

        // Con todo ya aplicado se añaden los archivos: el motor leerá --to2 al arrancar.
        if (request.Paths.Count > 0)
            await HandleExternalFiles(request.Paths, request.Destination, request.Operation == OperationType.Move);
        if (request.PasteFromClipboard) await PasteFromClipboardAsync(request.Destination);
    }

    private async Task HandleExternalAsync(StartupRequest request, bool isStartup)
    {
        if (!isStartup) RestoreWindow();

        // El destino pedido con --to se aplica a la lista y al cuadro de destino antes de
        // añadir los archivos, para que la transferencia arranque sin preguntar nada.
        if (!string.IsNullOrWhiteSpace(request.Destination))
        {
            EnsureDefaultList();
            if (_activeList is not null) _activeList.Destination = request.Destination!;
            if (_destination is not null) _destination.Text = request.Destination!;
        }

        // Las opciones de la línea de comandos (--overwrite, --skip, --algo…) se aplican
        // ANTES de la transferencia: si no, se usaba la política guardada y no sobrescribía.
        if (request.Conflict is not null) _settings.Conflict = request.Conflict.Value;
        if (request.VerifyAlgorithm is not null) _settings.Verify = request.VerifyAlgorithm.Value;
        if (request.VerifyAfterCopy) _settings.VerifyAfterCopy = true;
        if (request.CopyLockedFiles) _settings.CopyLockedFiles = true;

        if (request.Paths.Count > 0)
            await HandleExternalFiles(request.Paths, request.Destination, request.Operation == OperationType.Move);
        else if (!string.IsNullOrWhiteSpace(request.Destination))
        {
            _destination.Text = request.Destination!;
            UpdateTargetTab();
        }
        if (request.PasteFromClipboard) await PasteFromClipboardAsync(request.Destination);

        if (!request.ResumePartials) _settings.ResumePartials = false;
        if (request.Engine is not null) _engineOverride = request.Engine.Value;
        if (request.WriteChecksumFile)
        {
            _settings.WriteChecksumFile = true;
            if (_settings.Verify == VerifyAlgorithm.None) _settings.Verify = VerifyAlgorithm.Sha256;
        }
        if (request.SecureDeletePasses is not null) _settings.SecureDeletePasses = Math.Clamp(request.SecureDeletePasses.Value, 0, 7);
        if (!string.IsNullOrWhiteSpace(request.CompletionScript)) _settings.CompletionScript = request.CompletionScript!;
        FileLog.Write($"Comando: destinos extra recibidos=[{string.Join(" | ", request.ExtraDestinations)}] " +
                      $"existen=[{string.Join(" | ", request.ExtraDestinations.Where(Directory.Exists))}] " +
                      $"guardados=[{string.Join(" | ", _settings.ExtraDestinations)}]");
        foreach (var extra in request.ExtraDestinations.Where(Directory.Exists))
            if (!_settings.ExtraDestinations.Contains(extra, StringComparer.OrdinalIgnoreCase))
                _settings.ExtraDestinations.Add(extra);

        // La transferencia pedida por línea de comandos arranca AHORA, cuando --to2, --algo,
        // --overwrite y el motor ya están aplicados. Antes arrancaba dentro del bloque de
        // rutas y por eso el segundo destino (--to2) nunca se copiaba.
        if (!isStartup && request.Paths.Count > 0 && request.Operation is { } cliOperation && !_engine.IsRunning)
            StartTransfer(cliOperation);
        RefreshExtraDestinations();

        var op = request.Operation;
        if (op is not null && _items.Count > 0 && !_engine.IsRunning)
        {
            if (op == OperationType.Delete)
            {
                StartTransfer(op.Value);
            }
            else if (request.AskDestination || string.IsNullOrWhiteSpace(_destination.Text.Trim()))
            {
                PromptDestinationAndStart(op.Value);
            }
            else
            {
                StartTransfer(op.Value);
            }
        }

        // Una ACTIVACIÓN (segunda instancia, doble clic en el icono) siempre termina con la
        // ventana a la vista. Antes se volvía a evaluar aquí la lógica de «arrancar en
        // bandeja» y XoPy se escondía justo después de mostrarse: el usuario abría la
        // aplicación y no aparecía NADA.
        // La decisión del ARRANQUE no se toma aquí: este método solo se ejecuta cuando hay
        // archivos, pegado o destino, así que con «--tray» a secas nunca llegaba a correr.
        // Se decide al final de OnShown, que siempre se ejecuta.
        if (!isStartup) EnsureWindowVisible("activacion");
    }

    // ---------------------------------------------------------------- listas de copia múltiples en una misma pestaña

    private void EnsureDefaultList()
    {
        try
        {
            if (_lists.Count > 0) return;
            var list = new TransferList
            {
                Name = "Lista de copia",
                // Nunca se hereda el último destino: cada lista empieza sin destino y se
                // pregunta al añadir elementos. Antes se reutilizaba _settings.Destination y
                // por eso la pantalla de elegir carpeta casi nunca aparecía.
                Destination = string.Empty
            };
            _lists.Add(list);
            _activeList = list;
            _items = list.Items;
            RefreshListSelector();
        }
        catch (Exception ex)
        {
            Program.Log("MF6. EnsureDefaultList EXCEPTION: " + ex.GetType().FullName + " - " + ex.Message);
            Program.Log("MF6. EnsureDefaultList Stack: " + ex.StackTrace);
            throw;
        }
    }

    private void AddNewList(string? destination)
    {
        var next = _lists.Count + 1;
        var list = new TransferList
        {
            Name = $"Lista de copia {next}",
            Destination = string.IsNullOrWhiteSpace(destination) ? string.Empty : destination
        };
        _lists.Add(list);
        SwitchToList(list);
    }

    /// <summary>
    /// Borra la lista de copia activa del listado. Nunca toca los archivos del disco:
    /// sólo quita la entrada de la lista y sus referencias en memoria.
    /// </summary>
    private void DeleteActiveList()
    {
        if (_activeList is null) return;
        if (_engine.IsRunning)
        {
            SetStatus("Detén la transferencia antes de borrar una lista.");
            return;
        }

        var answer = MessageBox.Show(this,
            $"¿Borrar la lista «{_activeList.DisplayName}»?\n\n" +
            "Esto sólo elimina la lista del listado de XoPy. No borra ningún archivo del disco.",
            "XoPy", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        _lists.Remove(_activeList);
        if (_lists.Count == 0)
        {
            EnsureDefaultList();
        }
        else
        {
            SwitchToList(_lists[0]);
        }
        SetStatus("Lista borrada del listado.");
    }

    private void SwitchToList(TransferList? list)
    {
        if (list is null) return;
        if (_engine.IsRunning && list != _activeList)
        {
            SetStatus("Hay una transferencia en curso: los archivos se añaden a la lista activa.");
            list = _activeList;
            if (list is null) return;
        }

        _activeList = list;
        _items = list.Items;

        if (!string.IsNullOrWhiteSpace(list.Destination))
        {
            _destination.Text = list.Destination;
        }
        list.UpdatedAt = DateTime.Now;
        RefreshListSelector();
        UpdateTargetTab();
        UpdateButtons();
        SyncRows();
        SetStatus($"Lista activa: {list.DisplayName} ({list.Items.Count} archivo(s))");
    }

    private bool _refreshingSelector;

    private void RefreshListSelector()
    {
        if (_listSelector is null) return;
        if (_refreshingSelector) return; // prevenir reentrancy / bucle infinito
        _refreshingSelector = true;
        try
        {
            var current = _listSelector.SelectedItem as TransferList;
            _listSelector.Items.Clear();
            foreach (var list in _lists) _listSelector.Items.Add(list);
            // No establecer SelectedItem aquí para no disparar SelectedIndexChanged
            // que llamaría a SwitchToList → RefreshListSelector → bucle infinito.
            if (_activeList is not null && _lists.Contains(_activeList))
            {
                _listSelector.SelectedItem = _activeList;
            }
            else if (_lists.Count > 0)
            {
                _listSelector.SelectedItem = _lists[0];
                _activeList = _lists[0];
            }
        }
        catch (Exception ex)
        {
            Program.Log("MF6. RefreshListSelector EXCEPTION: " + ex.GetType().FullName + " - " + ex.Message);
            Program.Log("MF6. RefreshListSelector Stack: " + ex.StackTrace);
        }
        finally
        {
            _refreshingSelector = false;
        }
    }

    /// <summary>
    /// Pregunta al usuario a qué lista de copia añadir los archivos recibidos. Respeta el estilo
    /// visual de la aplicación. Devuelve null si el usuario cancela.
    /// </summary>
    private (TransferList List, bool ChangeDestination)? AskListDecision(
        IReadOnlyList<string> files, string detectedDestination, bool move)
    {
        var dlg = new Form
        {
            Text = "Añadir archivos a la copia",
            BackColor = _palette.Canvas,
            ForeColor = _palette.Text,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(600, 380),
        };

        var verb = move ? "mover" : "copiar";
        var message = new Label
        {
            Text = $"{files.Count} elemento(s) listos para {verb}.\nDestino detectado: {detectedDestination}",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 14, 16, 0),
            ForeColor = _palette.Text,
            BackColor = _palette.Canvas,
            Font = new Font("Segoe UI", 9.5f),
        };

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoScroll = true,
            Padding = new Padding(12, 8, 12, 8),
            BackColor = _palette.Canvas,
            AutoSize = false,
        };

        var defaultDestination = detectedDestination;
        var sameDestination = _activeList is not null
            && _activeList.Destination.Length > 0
            && string.Equals(_activeList.Destination, detectedDestination, StringComparison.OrdinalIgnoreCase);

        TransferList? chosen = null;
        Action? RefreshAddButton = null;

        void AddOption(TransferList list, bool changeToDetected, string text)
        {
            var rb = new RadioButton
            {
                Text = text,
                AutoSize = true,
                Margin = new Padding(4, 6, 4, 2),
                ForeColor = _palette.Text,
                BackColor = _palette.Canvas,
                Font = new Font("Segoe UI", 9f),
                Checked = list == _activeList && sameDestination && !changeToDetected,
            };
            rb.CheckedChanged += (_, _) =>
            {
                if (!rb.Checked) return;
                chosen = list;
                RefreshAddButton?.Invoke();
            };
            options.Controls.Add(rb);
        }

        // LISTA NUEVA primero y marcada por defecto: es lo que menos sorprende y lo que el
        // usuario pide cuando arrastra algo mientras ya hay una copia en marcha.
        var nueva = new TransferList { Name = $"Lista de copia {_lists.Count + 1}", Destination = detectedDestination };
        var newOption = new RadioButton
        {
            Text = $"Abrir en una lista NUEVA (destino: {detectedDestination})",
            AutoSize = true,
            Margin = new Padding(4, 6, 4, 10),
            ForeColor = _palette.Text,
            BackColor = _palette.Canvas,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Checked = true,
        };
        newOption.CheckedChanged += (_, _) => { if (newOption.Checked) { chosen = nueva; RefreshAddButton?.Invoke(); } };
        options.Controls.Add(newOption);
        options.Controls.SetChildIndex(newOption, 0);
        chosen = nueva;

        // Enumerable.Reverse: List<T>.Reverse() invierte la lista EN EL SITIO y devuelve void
        // (reordenaría de verdad las listas del usuario).
        foreach (var list in Enumerable.Reverse(_lists))
        {
            var same = string.Equals(list.Destination, detectedDestination, StringComparison.OrdinalIgnoreCase);
            AddOption(list, false,
                $"Añadir a «{list.DisplayName}» (destino: {list.Destination})" + (same ? " · mismo destino" : string.Empty));
            if (!same)
            {
                AddOption(list, true,
                    $"Añadir a «{list.DisplayName}» con el destino detectado (cambiar destino a: {detectedDestination})");
            }
        }

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 8, 12, 8),
            BackColor = _palette.Surface,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var cancel = new Button { Text = "Cancelar", Width = 96, Height = 32, DialogResult = DialogResult.Cancel };
        var add = new Button
        {
            Text = $"Añadir a «{(chosen?.DisplayName ?? "la lista")}»",
            Width = 220, Height = 32,
            BackColor = _palette.Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        };
        add.FlatAppearance.BorderSize = 0;
        // El botón dice exactamente lo que va a pasar, para no dejar dudas entre «lista
        // actual» y «lista nueva».
        void SyncAddButton()
        {
            add.Text = chosen == nueva
                ? "Abrir en una lista nueva"
                : $"Añadir a «{chosen?.DisplayName ?? "la lista"}»";
        }
        RefreshAddButton = SyncAddButton;
        SyncAddButton();
        add.Click += (_, _) => dlg.DialogResult = DialogResult.OK;
        cancel.Click += (_, _) => dlg.DialogResult = DialogResult.Cancel;
        buttons.Controls.AddRange(new Control[] { add, cancel });
        dlg.CancelButton = cancel;
        dlg.AcceptButton = add;

        var host = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = _palette.Canvas, Padding = new Padding(0) };
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.Controls.Add(message, 0, 0);
        host.Controls.Add(options, 0, 1);
        host.Controls.Add(buttons, 0, 2);
        dlg.Controls.Add(host);

        if (dlg.ShowDialog(this) != DialogResult.OK) return null;
        var target = chosen ?? _activeList;
        if (target is null) return null;
        if (!_lists.Contains(target)) _lists.Add(target);
        var changeDestination = target != _activeList
            || !string.Equals(target.Destination, detectedDestination, StringComparison.OrdinalIgnoreCase);
        return (target, changeDestination);
    }

    /// <summary>
    /// Ruta común para archivos que llegan de fuera (pegado del Explorador, segunda instancia,
    /// arrastrar archivos a la pestaña). Ofrece añadir a la lista actual, a otra lista activa
    /// o crear una lista nueva, y usa el destino detectado sin mostrar un selector de carpeta.
    /// </summary>
    private async Task HandleExternalFiles(IReadOnlyList<string> files, string? detectedDestination, bool move)
    {
        if (files == null || files.Count == 0) return;
        if (IsDisposed) return;

        EnsureDefaultList();
        var target = _activeList;
        if (target is null)
        {
            FileLog.Write("HandleExternalFiles: no hay lista activa (se aborta el pegado).");
            SetStatus("No se pudo preparar la lista de copia. Abre XoPy e inténtalo de nuevo.");
            return;
        }
        bool changeDestination = false;
        string? resolvedDestination = detectedDestination;

        var validDetected = !string.IsNullOrWhiteSpace(detectedDestination)
            && Directory.Exists(detectedDestination);

        // SIEMPRE se pregunta qué hacer al añadir archivos desde fuera (arrastrar, pegar,
        // menú contextual), como TeraCopy: añadir a la lista actual, abrir una lista NUEVA o
        // cancelar. Antes solo se preguntaba con 2+ listas y, con una sola, los archivos se
        // apilaban al final sin avisar. La excepción es que la lista activa esté VACÍA: no
        // tiene sentido preguntar si no hay nada que conservar.
        var activeListEmpty = _activeList is null || _activeList.Items.Count == 0;
        if (!activeListEmpty && (_lists.Count >= 1 || validDetected))
        {
            var decision = AskListDecision(files, detectedDestination!, move);
            if (decision is null) return;
            target = decision.Value.List;
            changeDestination = decision.Value.ChangeDestination;
            resolvedDestination = detectedDestination;
        }
        else if (validDetected
                 && !string.Equals(target.Destination, detectedDestination, StringComparison.OrdinalIgnoreCase))
        {
            changeDestination = true;
        }

        if (target != _activeList && _engine.IsRunning)
        {
            SetStatus("Transferencia en curso: los archivos se añaden a la lista activa.");
            target = _activeList;
            changeDestination = false;
        }

        SwitchToList(target);
        if (changeDestination && !string.IsNullOrEmpty(resolvedDestination) && target is not null)
        {
            target.Destination = resolvedDestination;
            _destination.Text = resolvedDestination;
            UpdateTargetTab();
        }

        // El destino ya lo eligió el Explorer/--to: no se vuelve a preguntar.
        await AddPathsAsync(files, askDestination: false);

        // En modo captura solo se rellena la lista para la imagen: no se abre el selector de
        // carpeta ni se arranca una copia (eso bloqueaba la captura y salía vacía).
        if (_startupRequest.Screenshot) return;
        if (!_engine.IsRunning)
            StartTransfer(move ? OperationType.Move : OperationType.Copy);
    }

    private void PromptDestinationAndStart(OperationType operation)
    {
        // Siempre se abre el selector vacío: nunca se propone una carpeta guardada ni
        // Descargas, que es el «destino prefijado» que el usuario no quiere.
        var verb = operation == OperationType.Move ? "mover" : "copiar";
        using var dialog = new FolderBrowserDialog
        {
            Description = $"¿A qué carpeta quieres {verb} los elementos?",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = string.Empty
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var path = dialog.SelectedPath?.Trim() ?? string.Empty;
        if (path.Length == 0) return;
        try { Directory.CreateDirectory(path); } catch { }

        _destination.Text = path;
        _settings.Destination = path;
        _store.Save(_settings);
        UpdateTargetTab();
        StartTransfer(operation);
    }

    /// <summary>
    /// Pega lo que haya en el portapapeles usando el motor de XoPy. Es ASÍNCRONO y se espera:
    /// antes se hacía <c>_ = AddPathsAsync(...)</c> y acto seguido <c>StartTransfer</c>, así
    /// que la transferencia arrancaba con la lista todavía vacía (en el caso de «cortar» el
    /// motor no encontraba nada y el pegado parecía no hacer nada).
    /// </summary>
    private async Task PasteFromClipboardAsync(string? destination)
    {
        string[] files;
        try
        {
            if (!Clipboard.ContainsFileDropList()) { SetStatus("El portapapeles no contiene archivos."); return; }
            files = Clipboard.GetFileDropList().Cast<string>().ToArray();
        }
        catch
        {
            SetStatus("No se pudo leer el portapapeles.");
            return;
        }
        if (files.Length == 0) { SetStatus("El portapapeles no contiene archivos."); return; }

        var move = ShellPasteHook.ClipboardIsCut();

        // Se pregunta igual que al arrastrar (lista actual vs. lista nueva), pero solo si la
        // lista activa ya tiene algo: si está vacía no hay nada que conservar.
        if (!string.IsNullOrWhiteSpace(destination) && Directory.Exists(destination)
            && _activeList is not null && _activeList.Items.Count > 0)
        {
            var decision = AskListDecision(files, destination, move);
            if (decision is null) return;
            SwitchToList(decision.Value.List);
            if (decision.Value.ChangeDestination)
            {
                _activeList!.Destination = destination;
                _destination.Text = destination;
            }
            else
            {
                destination = _activeList!.Destination;
            }
        }

        if (string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination))
        {
            // Never guess Downloads here: that can be the source folder and cause
            // a paste to target the same place. Ask only when no valid target exists.
            try
            {
                if (Directory.Exists(_destination.Text.Trim())) _pickDestination.SelectedPath = _destination.Text.Trim();
            }
            catch { }
            _pickDestination.Description = "No se pudo determinar el destino del pegado. Elige la carpeta correcta.";
            if (_pickDestination.ShowDialog(this) != DialogResult.OK) return;
            destination = _pickDestination.SelectedPath;
            if (!Directory.Exists(destination))
            {
                SetStatus("La carpeta de destino elegida ya no existe.");
                return;
            }
        }
        _destination.Text = destination;
        _settings.Destination = destination;
        _store.Save(_settings);
        // ESPERAR a que los archivos estén en la lista antes de arrancar el motor.
        await AddPathsAsync(files, askDestination: false);
        // El tipo de operación se decide con la instantánea tomada al principio: si se
        // vuelve a leer el portapapeles aquí, ya puede estar vacío (el «cortar» se consume).
        if (_activeList is null || _activeList.Items.Count == 0)
        {
            SetStatus("No se pudo preparar la lista de copia.");
            return;
        }
        StartTransfer(move ? OperationType.Move : OperationType.Copy);
    }
}
