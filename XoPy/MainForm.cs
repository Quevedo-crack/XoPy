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
    private readonly List<TransferItem> _items = new();
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
    private Button _favoritesButton = null!;
    private readonly ContextMenuStrip _favoritesMenu = new();
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
    private Button _browseButton = null!;
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
    private SpeedGraph _graph = null!;
    private Label[,] _categoryValues = new Label[4, 3];
    private ListBox _extraDestinations = null!;
    private CheckBox _optLocked = null!, _optResume = null!, _optChecksums = null!, _optNative = null!;
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
    private bool _trayPaused;
    private bool _trayError;
    private DateTime _completedAt = DateTime.MinValue;
    private OperationType _lastOperation = OperationType.Copy;
    private string _lastDestination = string.Empty;
    private long _sessionBytes;
    private double _lastPercent;
    private TimeSpan _sessionElapsed;

    public MainForm(StartupRequest request)
    {
        _startupRequest = request;
        _settings = _store.Load();
        _history = _store.LoadHistory();
        _palette = Palette.For(_settings.DarkTheme);

        BuildUi();
        LoadSettingsIntoUi();
        WireEngine();
        BuildTray();
        ApplyWindowGeometry();

        try
        {
            if (ShellIntegration.IsEnabled() && !_settings.ExplorerIntegration)
            {
                _settings.ExplorerIntegration = true;
                _store.Save(_settings);
            }
            if (_settings.ExplorerIntegration) ShellIntegration.Apply(true);
        }
        catch { }

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
            RowCount = 7,
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));

        _menu = BuildMenu();
        root.Controls.Add(_menu, 0, 0);
        root.Controls.Add(BuildToolbar(), 0, 1);
        root.Controls.Add(BuildDestinationBar(), 0, 2);
        root.Controls.Add(BuildTransferHeader(), 0, 3);
        root.Controls.Add(BuildTabBar(), 0, 4);
        root.Controls.Add(BuildContent(), 0, 5);
        root.Controls.Add(BuildFooter(), 0, 6);

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
        transfer.DropDownItems.Add(MenuItem("Eliminar", (_, _) => StartTransfer(OperationType.Delete)));
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
        queue.DropDownItems.Add(MenuItem("Invertir el orden", (_, _) => SortQueue(QueueSortField.Name, invert: true)));
        queue.DropDownItems.Add(new ToolStripSeparator());
        queue.DropDownItems.Add(MenuItem("Expandir todo", (_, _) => ExpandAll(true)));
        queue.DropDownItems.Add(MenuItem("Contraer todo", (_, _) => ExpandAll(false)));
        queue.DropDownItems.Add(new ToolStripSeparator());
        queue.DropDownItems.Add(MenuItem("Quitar seleccionados", (_, _) => RemoveSelected()));
        queue.DropDownItems.Add(MenuItem("Limpiar la lista", (_, _) => ClearList()));

        var tools = new ToolStripMenuItem("Herramientas");
        tools.DropDownItems.Add(MenuItem("Preferencias…", (_, _) => ShowOptions()));
        tools.DropDownItems.Add(MenuItem("Integración con el Explorador…", (_, _) => ShowExplorerIntegration()));
        tools.DropDownItems.Add(MenuItem("Abrir el registro de transferencias", (_, _) => OpenLogFile()));
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
        _testButton = BarButton("Test", Icons.Verify, (_, _) => StartTransfer(OperationType.Test));
        _deleteButton = BarButton("Eliminar", Icons.Delete, (_, _) => StartTransfer(OperationType.Delete));
        _pauseButton = BarButton("Pausar", Icons.Pause, (_, _) => TogglePause());
        _skipButton = BarButton("Saltar", Icons.ChevronRight, (_, _) => SkipCurrent());
        _stopButton = BarButton("Detener", Icons.Stop, (_, _) => StopTransfer());
        _optionsButton = BarButton("Opciones", Icons.Settings, (_, _) => ShowOptions(), false, true);
        _optionsButton.Margin = new Padding(6, 0, 4, 0);
        _menuButton = BarButton("Menú", Icons.More, (_, _) => ShowMainMenu(), false, true);
        _menuButton.Margin = new Padding(0);

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
        right.Items.AddRange(new ToolStripItem[] { _optionsButton, _menuButton });

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

    private Control BuildDestinationBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            BackColor = _palette.Canvas,
            Padding = new Padding(10, 9, 10, 7),
            RowCount = 1
        };
        bar.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var label = Ui.Label("Destino:", _palette, 9f, true, _palette.Muted);
        label.Anchor = AnchorStyles.Left;
        label.Margin = new Padding(0, 8, 8, 0);

        _destination = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = _palette.Surface,
            ForeColor = _palette.Text,
            Font = new Font("Segoe UI", 9.5f),
            Margin = new Padding(0, 5, 6, 4),
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        _destination.TextChanged += (_, _) =>
        {
            UpdateFooter();
            UpdateTargetTab();
        };
        _destination.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (_items.Count > 0) StartTransfer(OperationType.Copy);
        };

        _browseButton = Ui.ToolButton("Examinar", Icons.Folder, _palette);
        _browseButton.Size = new Size(106, 30);
        _browseButton.Margin = new Padding(0, 2, 6, 0);
        _browseButton.Anchor = AnchorStyles.Left;
        _browseButton.Click += (_, _) => BrowseDestination();

        _favoritesButton = Ui.ToolButton("Favoritos", Icons.Star, _palette);
        _favoritesButton.Size = new Size(112, 30);
        _favoritesButton.Margin = new Padding(0, 2, 6, 0);
        _favoritesButton.Anchor = AnchorStyles.Left;
        _favoritesButton.Click += (_, _) => ShowFavoritesMenu();

        Ui.StyleMenu(_favoritesMenu, _palette);
        _favoritesMenu.ShowImageMargin = true;

        _verifyCheck = new CheckBox
        {
            Text = "Verificar",
            AutoSize = true,
            ForeColor = _palette.Text,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 8, 0, 0),
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

        bar.Controls.Add(label, 0, 0);
        bar.Controls.Add(_destination, 1, 0);
        bar.Controls.Add(_browseButton, 2, 0);
        bar.Controls.Add(_favoritesButton, 3, 0);
        bar.Controls.Add(_verifyCheck, 4, 0);
        return bar;
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

        content.Controls.Add(BuildJobsPanel(), 0, 0);
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
            RowCount = 3
        };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 13));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

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

        _progress = new ProgressStrip(_palette) { Dock = DockStyle.Fill, Height = 12, Margin = new Padding(0, 1, 0, 1) };

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
        footer.Controls.Add(_progress, 0, 1);
        footer.Controls.Add(statusRow, 0, 2);
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
            Font = new Font("Segoe UI", 9f),
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
        _list.Resize += (_, _) =>
        {
            if (!IsHandleCreated || _columnUpdatePending) return;
            _columnUpdatePending = true;
            BeginInvoke(new Action(() =>
            {
                _columnUpdatePending = false;
                if (!IsDisposed) UpdateHashColumns();
            }));
        };
        _list.KeyDown += ListKeyDown;
        _list.DoubleClick += ListDoubleClick;
        _list.MouseDown += ListMouseDown;
        _list.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        _list.DragDrop += (_, e) => DropPaths(e);
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
        var images = new ImageList { ImageSize = new Size(1, 30) };
        var bitmap = new Bitmap(1, 30);
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
        var height = _settings.WindowHeight >= MinimumSize.Height ? _settings.WindowHeight : 740;
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

    private void ShowFavoritesMenu()
    {
        _favoritesMenu.Items.Clear();
        var favorites = _settings.Favorites.Where(Directory.Exists).ToList();
        if (favorites.Count == 0)
        {
            _favoritesMenu.Items.Add(new ToolStripMenuItem("Sin favoritos todavía") { Enabled = false });
        }
        foreach (var favorite in favorites)
        {
            var path = favorite;
            var item = new ToolStripMenuItem(path);
            item.Click += (_, _) =>
            {
                _destination.Text = path;
                SetStatus("Destino: " + path);
            };
            _favoritesMenu.Items.Add(item);
        }
        _favoritesMenu.Items.Add(new ToolStripSeparator());
        var add = new ToolStripMenuItem("Guardar la carpeta de destino en favoritos");
        add.Click += (_, _) => AddFavorite();
        _favoritesMenu.Items.Add(add);
        _favoritesMenu.Show(_favoritesButton, new Point(0, _favoritesButton.Height));
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

    private void BuildTray()
    {
        _baseIcon = Icons.AppIcon();
        _tray.Icon = _baseIcon;
        _tray.Text = "XoPy";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => RestoreWindow();
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
        _ = HandleExternalAsync(_startupRequest, true);
    }

    public void HandleExternalRequest(StartupRequest request) => _ = HandleExternalAsync(request, false);

    private async Task HandleExternalAsync(StartupRequest request, bool isStartup)
    {
        if (!isStartup) RestoreWindow();

        if (request.Paths.Count > 0) await AddPathsAsync(request.Paths);
        if (!string.IsNullOrWhiteSpace(request.Destination) && Directory.Exists(request.Destination))
            _destination.Text = request.Destination!;
        if (request.PasteFromClipboard) PasteFromClipboard(request.Destination ?? _destination.Text);

        if (request.Conflict is not null) _settings.Conflict = request.Conflict.Value;
        if (request.VerifyAlgorithm is not null) _settings.Verify = request.VerifyAlgorithm.Value;
        if (request.VerifyAfterCopy) _settings.VerifyAfterCopy = true;
        if (request.CopyLockedFiles) _settings.CopyLockedFiles = true;
        if (!request.ResumePartials) _settings.ResumePartials = false;
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

    private void PromptDestinationAndStart(OperationType operation)
    {
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
        if (string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination))
        {
            SetStatus("Copia primero archivos y usa «Pegar con XoPy» sobre una carpeta.");
            return;
        }
        _destination.Text = destination;
        _ = AddPathsAsync(files);
        StartTransfer(OperationType.Copy);
    }
}
