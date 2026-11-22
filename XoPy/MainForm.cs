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
    private ToolStripMenuItem _trayPauseItem = null!;
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
    private ToolStripButton _deleteButton = null!;
    private ToolStripButton _testButton = null!;
    private ToolStripButton _pauseButton = null!;
    private ToolStripButton _skipButton = null!;
    private ToolStripButton _stopButton = null!;
    private ToolStripButton _optionsButton = null!;
    private ToolStripButton _menuButton = null!;
    private ToolStripComboBox _copyMode = null!;
    private Button _removeButton = null!;
    private Button _clearButton = null!;
    private Button _upButton = null!;
    private Button _downButton = null!;
    private ListView _list = null!;
    private Label _countLabel = null!;
    private Button _sortButton = null!;
    private Label _hdrCopyState = null!, _hdrCopyPath = null!, _hdrCopyInfo = null!, _hdrCopyPercent = null!;
    private Label _hdrTargetState = null!, _hdrTargetPath = null!, _hdrTargetInfo = null!, _hdrTargetPercent = null!;
    private ProgressStrip _hdrCopyBar = null!, _hdrTargetBar = null!;
    private SpeedGraph _footerGraph = null!;
    private TableLayoutPanel _rootLayout = null!;
    private TableLayoutPanel _transferHeader = null!;
    private ToolStripButton _compactButton = null!;
    private bool _compactView;
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

    private Button _tabFiles = null!;
    private Button _tabTarget = null!;
    private Button _tabOptions = null!;
    private Button _tabStatus = null!;
    private Button _tabLog = null!;
    private Panel _tabsHost = null!;
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
    private Icon? _percentIcon;
    private bool _wasRunning;
    private bool _exitRequested;
    private int _trayPercent = -1;
    private TableLayoutPanel _contentGrid = null!;
    private Control _jobsPanel = null!;
    private Button _collapseButton = null!;
    private bool _jobsCollapsed;
    private OperationType? _followUpOperation;
    private ShellPasteHook? _pasteHook;
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
    private ToolStripComboBox _listSelector = null!;
    private ToolStripButton _newListButton = null!;
    private ToolStripButton _deleteListButton = null!;
    private ToolStripButton _removeFromListButton = null!;

    public MainForm(StartupRequest request)
    {
        _startupRequest = request;
        _settings = _store.Load();
        _history = _store.LoadHistory();
        _palette = Palette.For(_settings.DarkTheme);

        BuildUi();
        LoadSettingsIntoUi();
        ApplyCompactView(_settings.CompactView, persist: false);
        ApplyCopierReplacement();
        ApplyJobsCollapsed(_settings.CollapseJobs);
        WireEngine();
        EnsureDefaultList();
        BuildTray();
        ApplyWindowGeometry();

        if (_settings.ExplorerIntegration)
        {
            _settings.ExplorerIntegration = false;
            _store.Save(_settings);
        }

        _timer.Tick += (_, _) => TimerTick();
        _timer.Start();

        UpdateButtons();
        UpdateFooter();
        UpdateHashColumns();
        UpdateJobs();
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
        MinimumSize = new Size(1024, 640);
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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        _rootLayout = root;

        InitializeDestinationState();
        _menu = BuildMenu();
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
            _rootLayout.RowStyles[1].Height = compact ? 44 : 52;
            _rootLayout.RowStyles[2].Height = compact ? 56 : 64;
            _rootLayout.RowStyles[3].Height = compact ? 0 : 38;
            _rootLayout.RowStyles[4].SizeType = compact ? SizeType.Absolute : SizeType.Percent;
            _rootLayout.RowStyles[4].Height = compact ? 0 : 100;
            _rootLayout.RowStyles[5].Height = compact ? 88 : 112;
            if (_transferHeader is not null && _transferHeader.RowStyles.Count > 1)
            {
                // Ambas barras (general y del archivo actual) siguen visibles aunque la lista esté oculta.
                _transferHeader.RowStyles[0].Height = compact ? 24 : 30;
                _transferHeader.RowStyles[1].Height = compact ? 24 : 30;
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

        MinimumSize = compact ? new Size(1024, 230) : new Size(1024, 560);
        if (persist)
        {
            if (compact)
            {
                _expandedWasMaximized = WindowState == FormWindowState.Maximized;
                _expandedBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                WindowState = FormWindowState.Normal;
                Height = 230;
            }
            else if (_expandedBounds.Width > 0)
            {
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

    private void UpdateToolbarForCompact(bool compact)
    {
        // Al ocultar la lista se quitan las opciones que solo tienen sentido con la lista visible
        // (añadir/copiar/mover/verificar/eliminar) y se priorizan pausa/reanudar y detener.
        var listButtons = new[] { _addFilesButton, _addFolderButton, _copyButton, _moveButton, _testButton, _deleteButton };
        foreach (var button in listButtons)
        {
            if (button is not null) button.Visible = !compact;
        }
        _pauseButton.Visible = true;
        _skipButton.Visible = true;
        _stopButton.Visible = true;
        if (_listSelector is not null) _listSelector.Visible = true;
        if (_newListButton is not null) _newListButton.Visible = true;
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
        transfer.DropDownItems.Add(MenuItem("Eliminar…", (_, _) => StartTransfer(OperationType.Delete), Keys.Shift | Keys.Delete));
        transfer.DropDownItems.Add(MenuItem("Verificar", (_, _) => StartTransfer(OperationType.Test), Keys.F6));
        transfer.DropDownItems.Add(new ToolStripSeparator());
        transfer.DropDownItems.Add(MenuItem("Pausar / reanudar", (_, _) => TogglePause()));
        transfer.DropDownItems.Add(MenuItem("Saltar el archivo actual", (_, _) => SkipCurrent()));
        transfer.DropDownItems.Add(MenuItem("Detener", (_, _) => StopTransfer()));

        var queue = new ToolStripMenuItem("Cola");
        queue.DropDownItems.Add(MenuItem("Subir el elemento  (Alt+↑)", (_, _) => MoveSelected(-1)));
        queue.DropDownItems.Add(MenuItem("Bajar el elemento  (Alt+↓)", (_, _) => MoveSelected(1)));
        queue.DropDownItems.Add(new ToolStripSeparator());
        queue.DropDownItems.Add(MenuItem("Ordenar por nombre", (_, _) => SortQueue(QueueSortField.Name)));
        queue.DropDownItems.Add(MenuItem("Ordenar por tamaño", (_, _) => SortQueue(QueueSortField.Size)));
        queue.DropDownItems.Add(MenuItem("Ordenar por tipo", (_, _) => SortQueue(QueueSortField.Type)));
        queue.DropDownItems.Add(MenuItem("Ordenar por carpeta de destino", (_, _) => SortQueue(QueueSortField.Destination)));
        queue.DropDownItems.Add(MenuItem("Ordenar por carpeta de origen", (_, _) => SortQueue(QueueSortField.Source)));
        queue.DropDownItems.Add(MenuItem("Ordenar por estado", (_, _) => SortQueue(QueueSortField.State)));
        queue.DropDownItems.Add(MenuItem("Invertir el orden", (_, _) => SortQueue(QueueSortField.Name, invert: true)));
        queue.DropDownItems.Add(new ToolStripSeparator());
        queue.DropDownItems.Add(MenuItem("Expandir todo", (_, _) => ExpandAll(true)));
        queue.DropDownItems.Add(MenuItem("Contraer todo", (_, _) => ExpandAll(false)));
        queue.DropDownItems.Add(new ToolStripSeparator());
        queue.DropDownItems.Add(MenuItem("Quitar seleccionados", (_, _) => RemoveSelected()));
        queue.DropDownItems.Add(MenuItem("Limpiar la lista", (_, _) => ClearList()));
        queue.DropDownItems.Add(new ToolStripSeparator());
        queue.DropDownItems.Add(MenuItem("Ocultar o mostrar la lista de copia", (_, _) => ToggleCompactView()));
        queue.DropDownItems.Add(MenuItem("Mostrar u ocultar la columna de trabajos", (_, _) => ToggleJobsPanel()));

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
            ColumnCount = 2,
            BackColor = _palette.Surface,
            Padding = new Padding(10, 8, 10, 8),
            RowCount = 1
        };
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _addFilesButton = BarButton("Añadir", Icons.Add, (_, _) => AddFiles());
        _addFolderButton = BarButton("Carpeta", Icons.FolderAdd, (_, _) => AddFolder());
        _copyButton = BarButton("Copiar", Icons.Copy, (_, _) => StartTransfer(OperationType.Copy), true);
        _moveButton = BarButton("Mover", Icons.Move, (_, _) => StartTransfer(OperationType.Move));
        _testButton = BarButton("Verificar", Icons.Verify, (_, _) => StartTransfer(OperationType.Test));
        _deleteButton = BarButton("Eliminar", Icons.Delete, (_, _) => StartTransfer(OperationType.Delete));
        _pauseButton = BarButton("Pausar", Icons.Pause, (_, _) => TogglePause());
        _skipButton = BarButton("Saltar", Icons.ChevronRight, (_, _) => SkipCurrent());
        _stopButton = BarButton("Detener", Icons.Stop, (_, _) => StopTransfer());
        _copyMode = new ToolStripComboBox
        {
            AutoSize = false,
            Width = 116,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 8.5f),
            ToolTipText = "Modo de copia: secuencial o varios archivos en paralelo"
        };
        _copyMode.Items.AddRange(new object[] { "Secuencial", "Multihilo" });
        _copyMode.SelectedIndex = _settings.MultiThreadCopy ? 1 : 0;
        _copyMode.SelectedIndexChanged += (_, _) => ChangeCopyMode();
        _compactButton = BarButton("Ocultar", Icons.ChevronRight, (_, _) => ToggleCompactView());
        _compactButton.Margin = new Padding(2, 0, 4, 0);
        _compactButton.ToolTipText = "Ocultar la lista y dejar visibles los controles y el progreso";
        _optionsButton = BarButton("Opciones", Icons.Settings, (_, _) => ShowOptions(), false, true);
        _optionsButton.Margin = new Padding(6, 0, 4, 0);
        _menuButton = BarButton("Menú", Icons.More, (_, _) => ShowMainMenu(), false, true);
        _menuButton.Margin = new Padding(0);

        _listSelector = new ToolStripComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 220,
            DisplayMember = nameof(TransferList.DisplayName),
            ToolTipText = "Lista de copia activa. Cada instancia externa se agrega como una lista independiente en esta misma pestaña."
        };
        _listSelector.SelectedIndexChanged += (_, _) =>
        {
            if (_listSelector.SelectedItem is TransferList list) SwitchToList(list);
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

        _deleteListButton = BarButton("Eliminar lista", Icons.Delete, (_, _) => DeleteActiveList(), iconOnly: true);
        _deleteListButton.Width = 40;
        _deleteListButton.ToolTipText = "Borrar esta lista de copia (sólo del listado, no borra los archivos del disco)";

        _removeFromListButton = BarButton("Quitar selección", Icons.Delete, (_, _) => RemoveSelected(), iconOnly: true);
        _removeFromListButton.Width = 40;
        _removeFromListButton.ToolTipText = "Quitar del listado los archivos seleccionados (no borra nada del disco)";

        var left = new ToolStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
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
            _addFilesButton, _addFolderButton, _copyButton, _moveButton, _testButton, _deleteButton,
            _pauseButton, _skipButton, _stopButton
        });

        var right = new ToolStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
            LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow,
            CanOverflow = false,
            AutoSize = true,
            BackColor = _palette.Surface,
            Font = new Font("Segoe UI", 8.75f, FontStyle.Bold),
            Renderer = new DarkMenuRenderer(_palette),
            Padding = new Padding(0)
        };
        right.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripLabel("Modo:"), _copyMode,
            new ToolStripControlHost(_verifyCheck) { Margin = new Padding(2, 2, 4, 0), AutoSize = true },
            new ToolStripLabel("  Lista:"),
            _listSelector, _newListButton, _deleteListButton,
            _removeFromListButton,
            _compactButton, _optionsButton, _menuButton
        });

        bar.Controls.Add(left, 0, 0);
        bar.Controls.Add(right, 1, 0);
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
        var width = iconOnly ? 40 : TextRenderer.MeasureText(text, font).Width + 44;
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
            Padding = new Padding(6, 0, 6, 0),
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
        "Test" => "Comprobar la copia comparando hashes (F6)",
        "Eliminar" => "Eliminar los archivos del disco (no es lo mismo que quitarlos de la lista)",
        "Saltar" => "Saltar el archivo que se está copiando",
        _ => text
    };

    private void InitializeDestinationState()
    {
        // Destination is an internal target selected from the active Explorer window
        // or the Destino tab; keep it out of the always-visible toolbar.
        _destination = new TextBox { Text = _settings.Destination };
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

        var title = Ui.Label("TRABAJOS", _palette, 8f, true, _palette.Muted);
        title.Anchor = AnchorStyles.Left;
        title.Margin = new Padding(2, 5, 0, 0);

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

        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(_jobsList, 0, 1);
        panel.Controls.Add(buttons, 0, 2);
        return panel;
    }

    private Control BuildTransferHeader()
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = _palette.Surface,
            Padding = new Padding(12, 4, 12, 2),
            Margin = new Padding(0)
        };
        _transferHeader = host;
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        host.Controls.Add(BuildHeaderRow("Copiando", out _hdrCopyState, out _hdrCopyPath, out _hdrCopyInfo, out _hdrCopyBar, out _hdrCopyPercent), 0, 0);
        host.Controls.Add(BuildHeaderRow("Destino", out _hdrTargetState, out _hdrTargetPath, out _hdrTargetInfo, out _hdrTargetBar, out _hdrTargetPercent), 0, 1);
        return host;
    }

    private TableLayoutPanel BuildHeaderRow(string title, out Label state, out Label path, out Label info,
        out ProgressStrip bar, out Label percent)
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = _palette.Surface,
            Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));

        state = new Label
        {
            Text = title,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = _palette.Text,
            BackColor = _palette.SurfaceAlt,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Margin = new Padding(0, 3, 8, 3)
        };
        path = new Label
        {
            Text = "—",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = _palette.Text,
            Font = new Font("Segoe UI", 8.75f),
            AutoEllipsis = true,
            Margin = new Padding(0, 3, 10, 3)
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
            Margin = new Padding(0, 3, 10, 3)
        };
        bar = new ProgressStrip(_palette) { Dock = DockStyle.Fill, Height = 10, Margin = new Padding(0, 10, 10, 10) };
        percent = new Label
        {
            Text = "—",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = _palette.Text,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Margin = new Padding(0, 3, 0, 3)
        };

        row.Controls.Add(state, 0, 0);
        row.Controls.Add(path, 1, 0);
        row.Controls.Add(info, 2, 0);
        row.Controls.Add(bar, 3, 0);
        row.Controls.Add(percent, 4, 0);
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
        _removeButton = Ui.IconButton(Icons.Remove, _palette, "Quitar de la lista (Supr)");
        _removeButton.Click += (_, _) => RemoveSelected();
        _clearButton = Ui.IconButton(Icons.Delete, _palette, "Limpiar la lista");
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
        _sortButton = Ui.IconButton("\u21C5", _palette, "Ordenar la cola por nombre, tamaño, tipo o carpeta de destino");
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
            LabelEdit = false,
            GridLines = false
        };
        _list.Columns.Add("Nombre", 520);
        _list.Columns.Add("Tamaño", 96, HorizontalAlignment.Right);
        _list.Columns.Add("Estado", 132);
        _list.Columns.Add("Progreso", 210);
        _list.Columns.Add("Hash origen", 0);
        _list.Columns.Add("Hash destino", 0);

        _list.DrawColumnHeader += ListDrawColumnHeader;
        _list.DrawItem += (_, e) => { if (_list.View != View.Details) e.DrawDefault = true; };
        _list.DrawSubItem += ListDrawSubItem;
        // Las columnas se ajustan al ancho de la ventana, no al de la lista:
        // así la aparición de la barra de desplazamiento no provoca un bucle de redimensionado.
        Resize += (_, _) => ScheduleColumnUpdate();
        Shown += (_, _) => ScheduleColumnUpdate();
        _list.KeyDown += ListKeyDown;
        _list.DoubleClick += ListDoubleClick;
        _list.MouseDown += ListMouseDown;
        _list.ItemDrag += ListItemDrag;
        _list.DragEnter += ListDragEnter;
        _list.DragOver += ListDragEnter;
        _list.DragDrop += ListDragDrop;
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();

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
        _listMenu.Items.Add("Test (verificar)", null, (_, _) => StartTransfer(OperationType.Test));
        _listMenu.Items.Add("Eliminar del disco…", null, (_, _) => StartTransfer(OperationType.Delete));
        _listMenu.Items.Add(new ToolStripSeparator());
        _listMenu.Items.Add("Expandir / contraer", null, (_, _) => ToggleSelectedFolders());
        _listMenu.Items.Add("Subir", null, (_, _) => MoveSelected(-1));
        _listMenu.Items.Add("Bajar", null, (_, _) => MoveSelected(1));
        _listMenu.Items.Add(new ToolStripSeparator());
        var pauseItem = new ToolStripMenuItem("Pausar archivo seleccionado");
        pauseItem.Click += (_, _) => ToggleSelectedFilePause();
        pauseItem.Name = "pause-single-file";
        _listMenu.Items.Add(pauseItem);
        _listMenu.Opening += (_, _) => UpdateListMenuState();
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
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        Ui.StyleMenu(menu, _palette);
        menu.Items.Add("Preferencias…", null, (_, _) => ShowOptions());
        menu.Items.Add("Integración con el Explorador…", null, (_, _) => ShowExplorerIntegration());
        menu.Items.Add("Historial de transferencias…", null, (_, _) => ShowHistory());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Abrir el registro", null, (_, _) => OpenLogFile());
        menu.Items.Add("Mostrar los hashes", null, (_, _) => ToggleHashes());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Acerca de XoPy", null, (_, _) => ShowAbout());
        return menu;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

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
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        var width = _settings.WindowWidth >= MinimumSize.Width ? _settings.WindowWidth : 1200;
        var height = _compactView ? MinimumSize.Height : _settings.WindowHeight >= MinimumSize.Height ? _settings.WindowHeight : 740;
        if (!_startupRequest.Screenshot)
        {
            width = Math.Min(width, workingArea.Width - 20);
            height = Math.Min(height, workingArea.Height - 20);
        }

        var x = _settings.WindowX != int.MinValue ? _settings.WindowX : workingArea.X + (workingArea.Width - width) / 2;
        var y = _settings.WindowY != int.MinValue ? _settings.WindowY : workingArea.Y + (workingArea.Height - height) / 2;
        if (x < workingArea.Left - 40 || x > workingArea.Right - 240) x = workingArea.Left + 40;
        if (y < workingArea.Top - 10 || y > workingArea.Bottom - 180) y = workingArea.Top + 30;

        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(x, y, width, height);
        if (_settings.WindowMaximized) WindowState = FormWindowState.Maximized;
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
        _destination.Text = _settings.Destination;
        _verifyCheck.Checked = _settings.VerifyAfterCopy;
        _verifyCheck.Text = _settings.VerifyAfterCopy
            ? $"Verificar ({Format.Algorithm(_settings.Verify)})"
            : "Verificar";
        SyncQuickOptions();
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
                _pasteHook.Install();
            }
            else if (_pasteHook is not null)
            {
                _pasteHook.Dispose();
                _pasteHook = null;
            }
        }
        catch (Exception ex)
        {
            FileLog.Write("No se pudo activar el copiador de Windows: " + ex.Message);
        }
    }

    private async void HandleExplorerPaste(IReadOnlyList<string> files, string destination, bool move)
    {
        try
        {
            if (IsDisposed) return;
            await HandleExternalFiles(files, destination, move);
        }
        catch (Exception ex)
        {
            FileLog.Write("Error al pegar con XoPy: " + ex);
        }
    }

    private void BuildTray()
    {
        _baseIcon = Icons.AppIcon();
        _tray.Icon = _baseIcon;
        _tray.Text = "XoPy";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => RestoreWindow();
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) RestoreWindow();
        };
        Ui.StyleMenu(_trayMenu, _palette);
        _trayPauseItem = new ToolStripMenuItem("Pausar");
        _trayPauseItem.Click += (_, _) => TogglePause();
        _trayMenu.Items.Add("Abrir XoPy", null, (_, _) => RestoreWindow());
        _trayMenu.Items.Add(_trayPauseItem);
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

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // La ventana debe verse siempre al abrir XoPy (salvo si se pidió arrancar minimizado).
        if (!_settings.StartMinimized && !_startupRequest.StartMinimized)
        {
            try
            {
                Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                BringToFront();
            }
            catch { }
        }
        _ = HandleExternalAsync(_startupRequest, true);
    }

    public void HandleExternalRequest(StartupRequest request) => _ = HandleExternalAsync(request, false);

    private async Task HandleExternalAsync(StartupRequest request, bool isStartup)
    {
        if (!isStartup) RestoreWindow();

        if (!string.IsNullOrWhiteSpace(request.Destination) && _activeList is not null)
            _activeList.Destination = request.Destination;

        if (request.Paths.Count > 0)
            await HandleExternalFiles(request.Paths, request.Destination, request.Operation == OperationType.Move);
        else if (!string.IsNullOrWhiteSpace(request.Destination))
        {
            _destination.Text = request.Destination!;
            UpdateTargetTab();
        }
        if (request.PasteFromClipboard) PasteFromClipboard(request.Destination);

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
        if (request.SecureDeletePasses is not null) _settings.SecureDeletePasses = Math.Clamp(request.SecureDeletePasses.Value, 0, 7);
        if (!string.IsNullOrWhiteSpace(request.CompletionScript)) _settings.CompletionScript = request.CompletionScript!;
        foreach (var extra in request.ExtraDestinations.Where(Directory.Exists))
            if (!_settings.ExtraDestinations.Contains(extra, StringComparer.OrdinalIgnoreCase))
                _settings.ExtraDestinations.Add(extra);
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

        if (request.StartMinimized || (isStartup && _settings.StartMinimized && request.Operation is null && request.Paths.Count == 0))
            MinimizeToTray();
    }

    // ---------------------------------------------------------------- listas de copia múltiples en una misma pestaña

    private void EnsureDefaultList()
    {
        if (_lists.Count > 0) return;
        var list = new TransferList
        {
            Name = "Lista de copia",
            Destination = string.IsNullOrWhiteSpace(_settings.Destination)
                ? AppSettings.DefaultDestination() : _settings.Destination
        };
        _lists.Add(list);
        _activeList = list;
        _items = list.Items;
        RefreshListSelector();
    }

    private void AddNewList(string? destination)
    {
        var next = _lists.Count + 1;
        var list = new TransferList
        {
            Name = $"Lista de copia {next}",
            Destination = string.IsNullOrWhiteSpace(destination) ? AppSettings.DefaultDestination() : destination
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

    private void RefreshListSelector()
    {
        if (_listSelector is null) return;
        var current = _listSelector.SelectedItem as TransferList;
        _listSelector.Items.Clear();
        foreach (var list in _lists) _listSelector.Items.Add(list);
        _listSelector.SelectedItem = _activeList ?? current;
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
        var sameDestination = _activeList is { Length: > 0 }
            && string.Equals(_activeList.Destination, detectedDestination, StringComparison.OrdinalIgnoreCase);

        TransferList? chosen = null;

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
                if (rb.Checked) chosen = list;
            };
            options.Controls.Add(rb);
        }

        foreach (var list in _lists)
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

        var nueva = new TransferList { Name = $"Lista de copia {_lists.Count + 1}", Destination = detectedDestination };
        AddOption(nueva, true, $"Crear una nueva lista (destino: {detectedDestination})");

        if (chosen is null) chosen = sameDestination && _activeList is not null ? _activeList : _lists.FirstOrDefault();

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
        var target = chosen ?? _activeList!;
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
        var target = _activeList!;
        bool changeDestination = false;
        string? resolvedDestination = detectedDestination;

        var validDetected = !string.IsNullOrWhiteSpace(detectedDestination)
            && Directory.Exists(detectedDestination);

        // Con varias listas activas el usuario elige a cuál añadir; con una sola y un destino
        // detectado válido se usa directamente (sin selector de carpeta).
        if (_lists.Count > 1 && validDetected)
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
        if (changeDestination && resolvedDestination is { Length: > 0 })
        {
            target.Destination = resolvedDestination;
            _destination.Text = resolvedDestination;
            UpdateTargetTab();
        }

        await AddPathsAsync(files);
        if (!_engine.IsRunning)
            StartTransfer(move ? OperationType.Move : OperationType.Copy);
    }

    private void PromptDestinationAndStart(OperationType operation)
    {
        // En lugar de abrir directamente el selector, se ofrece qué hacer con el destino.
        var verb = operation == OperationType.Move ? "mover" : "copiar";
        var choice = PromptDialog.Ask(this, _palette,
            "Falta la carpeta de destino",
            $"¿A qué carpeta quieres {verb} los elementos?",
            null,
            new[] { "Elegir carpeta…", "Usar carpeta predeterminada", "Cancelar" });
        if (choice == 2) return;
        if (choice == 1)
        {
            var downloads = AppSettings.DefaultDestination();
            _destination.Text = downloads;
            _settings.Destination = downloads;
            _store.Save(_settings);
            StartTransfer(operation);
            return;
        }

        try
        {
            if (Directory.Exists(_destination.Text.Trim())) _pickDestination.SelectedPath = _destination.Text.Trim();
        }
        catch { }
        _pickDestination.Description = operation == OperationType.Move
            ? "¿A qué carpeta quieres mover los elementos?"
            : "¿A qué carpeta quieres copiar los elementos?";
        if (_pickDestination.ShowDialog(this) != DialogResult.OK) return;
        _destination.Text = _pickDestination.SelectedPath;
        _settings.Destination = _pickDestination.SelectedPath;
        _store.Save(_settings);
        StartTransfer(operation);
    }

    private void PasteFromClipboard(string? destination)
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

        if (_lists.Count > 1 && !string.IsNullOrWhiteSpace(destination) && Directory.Exists(destination))
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
        _ = AddPathsAsync(files);
        var operation = ShellPasteHook.ClipboardIsCut() ? OperationType.Move : OperationType.Copy;
        StartTransfer(operation);
    }
}
