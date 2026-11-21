using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Media;
using System.Text.Json;
using System.Windows.Forms;

namespace XoPy;

public sealed class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _store = new();
    private readonly Palette _palette;
    private readonly List<TransferItem> _items = new();
    private readonly Dictionary<Guid, ListViewItem> _rows = new();
    private readonly CopyEngine _engine = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 350 };
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private ToolStripMenuItem _trayPauseItem = null!;
    private readonly StartupRequest _startupRequest;
    private readonly OpenFileDialog _openFiles = new() { Multiselect = true, Title = "Añadir archivos", Filter = "Todos los archivos (*.*)|*.*" };
    private readonly FolderBrowserDialog _pickFolder = new() { Description = "Selecciona la carpeta que quieres añadir" };
    private readonly FolderBrowserDialog _pickDestination = new() { Description = "Selecciona la carpeta de destino" };
    private readonly SaveFileDialog _saveQueue = new() { Title = "Guardar cola", Filter = "Cola de XoPy (*.xopyq)|*.xopyq|JSON (*.json)|*.json", FileName = "xopy-cola.xopyq" };
    private readonly OpenFileDialog _loadQueue = new() { Title = "Abrir cola", Filter = "Cola de XoPy (*.xopyq)|*.xopyq|JSON (*.json)|*.json" };

    private ListView _list = null!;
    private TextBox _destination = null!;
    private ComboBox _favorites = null!;
    private CheckBox _verifyCheck = null!;
    private Button _addFilesButton = null!;
    private Button _addFolderButton = null!;
    private Button _copyButton = null!;
    private Button _moveButton = null!;
    private Button _deleteButton = null!;
    private Button _testButton = null!;
    private Button _pauseButton = null!;
    private Button _stopButton = null!;
    private Button _optionsButton = null!;
    private Button _browseButton = null!;
    private Button _favoriteButton = null!;
    private Button _removeButton = null!;
    private Button _clearButton = null!;
    private Label _statusLabel = null!;
    private Label _summaryLabel = null!;
    private Label _speedLabel = null!;
    private Label _countLabel = null!;
    private ProgressStrip _progress = null!;
    private ContextMenuStrip _listMenu = null!;
    private Icon? _trayIcon;
    private Font _headerFont = null!;
    private Font _glyphFont = null!;
    private Font _percentFont = null!;
    private bool _wasRunning;
    private bool _exitRequested;
    private int _trayPercent = -1;
    private int _sortColumn = -1;
    private bool _sortAscending = true;
    private OperationType _lastOperation = OperationType.Copy;

    public MainForm(StartupRequest request)
    {
        _startupRequest = request;
        _settings = _store.Load();
        _palette = Palette.For(_settings.DarkTheme);

        BuildUi();
        LoadSettingsIntoUi();
        WireEngine();
        BuildTray();

        _timer.Tick += (_, _) => TimerTick();
        _timer.Start();

        UpdateButtons();
        UpdateFooter();
    }

    // ---------------------------------------------------------------- interfaz

    private void BuildUi()
    {
        SuspendLayout();
        _headerFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        _glyphFont = new Font("Segoe MDL2 Assets", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
        _percentFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        Text = "XoPy — Copiar, mover y verificar";
        Icon = Icons.AppIcon();
        BackColor = _palette.Canvas;
        ForeColor = _palette.Text;
        Font = new Font("Segoe UI", 9f);
        MinimumSize = new Size(940, 580);
        Size = new Size(1140, 720);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        KeyPreview = true;
        AllowDrop = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = _palette.Canvas,
            Padding = new Padding(0),
            AutoSize = false
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));

        var menu = BuildMenu();
        root.Controls.Add(menu, 0, 0);
        root.Controls.Add(BuildToolbar(), 0, 1);
        root.Controls.Add(BuildDestinationBar(), 0, 2);
        root.Controls.Add(BuildList(), 0, 3);
        root.Controls.Add(BuildFooter(), 0, 4);

        Controls.Add(root);
        MainMenuStrip = menu;
        ResumeLayout(true);

        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => DropPaths(e);
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip
        {
            Dock = DockStyle.Fill,
            BackColor = _palette.Canvas,
            ForeColor = _palette.Text,
            Renderer = new ToolStripProfessionalRenderer(new MenuColors(_palette)),
            Padding = new Padding(6, 2, 0, 2),
            AutoSize = false,
            Height = 26
        };

        var file = new ToolStripMenuItem("Archivo");
        file.DropDownItems.Add(MenuItem("Añadir archivos…", (_, _) => AddFiles(), Keys.Control | Keys.O));
        file.DropDownItems.Add(MenuItem("Añadir carpeta…", (_, _) => AddFolder(), Keys.Control | Keys.Shift | Keys.O));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MenuItem("Abrir cola…", (_, _) => LoadQueueFile()));
        file.DropDownItems.Add(MenuItem("Guardar cola…", (_, _) => SaveQueueFile()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(MenuItem("Salir", (_, _) => ExitApplication()));

        var transfer = new ToolStripMenuItem("Transferencia");
        transfer.DropDownItems.Add(MenuItem("Copiar", (_, _) => StartTransfer(OperationType.Copy), Keys.F5));
        transfer.DropDownItems.Add(MenuItem("Mover", (_, _) => StartTransfer(OperationType.Move), Keys.Control | Keys.F5));
        transfer.DropDownItems.Add(MenuItem("Eliminar", (_, _) => StartTransfer(OperationType.Delete)));
        transfer.DropDownItems.Add(MenuItem("Verificar", (_, _) => StartTransfer(OperationType.Test), Keys.F6));
        transfer.DropDownItems.Add(new ToolStripSeparator());
        transfer.DropDownItems.Add(MenuItem("Pausar / reanudar", (_, _) => TogglePause()));
        transfer.DropDownItems.Add(MenuItem("Detener", (_, _) => StopTransfer()));

        var tools = new ToolStripMenuItem("Herramientas");
        tools.DropDownItems.Add(MenuItem("Preferencias…", (_, _) => ShowOptions()));
        tools.DropDownItems.Add(MenuItem("Integración con el Explorador…", (_, _) => ShowExplorerIntegration()));
        tools.DropDownItems.Add(MenuItem("Quitar seleccionados", (_, _) => RemoveSelected()));
        tools.DropDownItems.Add(MenuItem("Limpiar la lista", (_, _) => ClearList()));
        tools.DropDownItems.Add(new ToolStripSeparator());
        tools.DropDownItems.Add(MenuItem("Historial de transferencias…", (_, _) => ShowHistory()));

        var help = new ToolStripMenuItem("Ayuda");
        help.DropDownItems.Add(MenuItem("Acerca de XoPy", (_, _) => ShowAbout()));

        menu.Items.AddRange(new ToolStripItem[] { file, transfer, tools, help });
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
            RowCount = 1,
            BackColor = _palette.Surface,
            Padding = new Padding(10, 8, 10, 8),
            Height = 57
        };
        bar.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _addFilesButton = Ui.ToolButton("Añadir archivos", Icons.Add, _palette);
        _addFilesButton.Size = new Size(136, 38);
        _addFilesButton.Click += (_, _) => AddFiles();

        _addFolderButton = Ui.ToolButton("Añadir carpeta", Icons.FolderAdd, _palette);
        _addFolderButton.Size = new Size(134, 38);
        _addFolderButton.Click += (_, _) => AddFolder();

        _copyButton = Ui.ToolButton("Copiar", Icons.Copy, _palette, _palette.Accent);
        _copyButton.Click += (_, _) => StartTransfer(OperationType.Copy);

        _moveButton = Ui.ToolButton("Mover", Icons.Move, _palette);
        _moveButton.Click += (_, _) => StartTransfer(OperationType.Move);

        _deleteButton = Ui.ToolButton("Eliminar", Icons.Delete, _palette);
        _deleteButton.Click += (_, _) => StartTransfer(OperationType.Delete);

        _testButton = Ui.ToolButton("Verificar", Icons.Verify, _palette);
        _testButton.Click += (_, _) => StartTransfer(OperationType.Test);

        _pauseButton = Ui.ToolButton("Pausar", Icons.Pause, _palette);
        _pauseButton.Click += (_, _) => TogglePause();

        _stopButton = Ui.ToolButton("Detener", Icons.Stop, _palette);
        _stopButton.Click += (_, _) => StopTransfer();

        _optionsButton = Ui.ToolButton("Preferencias", Icons.Settings, _palette);
        _optionsButton.Click += (_, _) => ShowOptions();
        _optionsButton.Margin = new Padding(0);

        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            BackColor = _palette.Surface,
            Margin = new Padding(0)
        };
        left.Controls.AddRange(new Control[] { _addFilesButton, _addFolderButton, _copyButton, _moveButton, _deleteButton, _testButton, _pauseButton, _stopButton });

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = _palette.Surface
        };
        right.Controls.Add(_optionsButton);

        bar.Controls.Add(left, 0, 0);
        bar.Controls.Add(right, 1, 0);
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(_palette.Border);
            e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
        };
        return bar;
    }

    private Control BuildDestinationBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            BackColor = _palette.Canvas,
            Padding = new Padding(10, 9, 10, 8),
            Height = 54
        };
        bar.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var label = Ui.Label("Copiar a:", _palette, 9f, true, _palette.Muted);
        label.Anchor = AnchorStyles.Left;
        label.Margin = new Padding(0, 8, 8, 0);

        _destination = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = _palette.Surface,
            ForeColor = _palette.Text,
            Font = new Font("Segoe UI", 9.5f),
            Margin = new Padding(0, 6, 6, 4),
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        _destination.TextChanged += (_, _) => UpdateFooter();

        _browseButton = Ui.ToolButton("Examinar", Icons.Folder, _palette);
        _browseButton.Size = new Size(104, 30);
        _browseButton.Margin = new Padding(0, 3, 6, 0);
        _browseButton.Anchor = AnchorStyles.Left;
        _browseButton.Click += (_, _) => BrowseDestination();

        _favorites = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Width = 168,
            BackColor = _palette.Surface,
            ForeColor = _palette.Text,
            Margin = new Padding(0, 5, 4, 0),
            Anchor = AnchorStyles.Left
        };
        _favorites.SelectedIndexChanged += (_, _) =>
        {
            if (_favorites.SelectedItem is string path && Directory.Exists(path)) _destination.Text = path;
        };

        _favoriteButton = Ui.IconButton(Icons.Star, _palette, "Guardar la carpeta actual en favoritos");
        _favoriteButton.Size = new Size(30, 28);
        _favoriteButton.Margin = new Padding(0, 5, 10, 0);
        _favoriteButton.Anchor = AnchorStyles.Left;
        _favoriteButton.Click += (_, _) => AddFavorite();

        _verifyCheck = new CheckBox
        {
            Text = "Verificar",
            AutoSize = true,
            ForeColor = _palette.Text,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 9, 0, 0),
            Checked = _settings.VerifyAfterCopy,
            Anchor = AnchorStyles.Left
        };
        _verifyCheck.CheckedChanged += (_, _) =>
        {
            _settings.VerifyAfterCopy = _verifyCheck.Checked;
            _store.Save(_settings);
        };

        bar.Controls.Add(label, 0, 0);
        bar.Controls.Add(_destination, 1, 0);
        bar.Controls.Add(_browseButton, 2, 0);
        bar.Controls.Add(_favorites, 3, 0);
        bar.Controls.Add(_favoriteButton, 4, 0);
        bar.Controls.Add(_verifyCheck, 5, 0);
        return bar;
    }

    private Control BuildList()
    {
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = _palette.Canvas,
            Padding = new Padding(10, 0, 10, 0),
            Margin = new Padding(0)
        };
        container.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        container.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = _palette.Canvas,
            Height = 34
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _countLabel = Ui.Label("Lista vacía", _palette, 9f, true, _palette.Muted);
        _countLabel.Anchor = AnchorStyles.Left;
        _countLabel.Margin = new Padding(2, 8, 0, 0);

        _removeButton = Ui.IconButton(Icons.Remove, _palette, "Quitar de la lista");
        _removeButton.Click += (_, _) => RemoveSelected();
        _clearButton = Ui.IconButton(Icons.Delete, _palette, "Limpiar la lista");
        _clearButton.Click += (_, _) => ClearList();

        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = _palette.Canvas
        };
        tools.Controls.Add(_clearButton);
        tools.Controls.Add(_removeButton);

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
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0, 0, 0, 8)
        };
        _list.Columns.Add("Nombre", 420);
        _list.Columns.Add("Tamaño", 100, HorizontalAlignment.Right);
        _list.Columns.Add("Estado", 130);
        _list.Columns.Add("Progreso", 260);

        _list.DrawColumnHeader += ListDrawColumnHeader;
        _list.DrawItem += (_, e) => { if (_list.View != View.Details) e.DrawDefault = true; };
        _list.DrawSubItem += ListDrawSubItem;
        _list.ColumnClick += ListColumnClick;
        _list.KeyDown += ListKeyDown;
        _list.DoubleClick += ListDoubleClick;
        _list.DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        _list.DragDrop += (_, e) => DropPaths(e);

        BuildListMenu();
        _list.ContextMenuStrip = _listMenu;

        container.Controls.Add(header, 0, 0);
        container.Controls.Add(_list, 0, 1);
        return container;
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
        _listMenu = new ContextMenuStrip
        {
            BackColor = _palette.Surface,
            ForeColor = _palette.Text,
            Renderer = new ToolStripProfessionalRenderer(new MenuColors(_palette)),
            ShowImageMargin = false
        };
        _listMenu.Items.Add("Abrir archivo", null, (_, _) => OpenSelected());
        _listMenu.Items.Add("Abrir carpeta contenedora", null, (_, _) => OpenSelectedFolder());
        _listMenu.Items.Add(new ToolStripSeparator());
        _listMenu.Items.Add("Copiar", null, (_, _) => StartTransfer(OperationType.Copy));
        _listMenu.Items.Add("Mover", null, (_, _) => StartTransfer(OperationType.Move));
        _listMenu.Items.Add("Verificar", null, (_, _) => StartTransfer(OperationType.Test));
        _listMenu.Items.Add("Eliminar", null, (_, _) => StartTransfer(OperationType.Delete));
        _listMenu.Items.Add(new ToolStripSeparator());
        _listMenu.Items.Add("Quitar de la lista", null, (_, _) => RemoveSelected());
        _listMenu.Items.Add("Reintentar los fallidos", null, (_, _) => RetryFailed());
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = _palette.Surface,
            Padding = new Padding(12, 9, 12, 7),
            Height = 84
        };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));

        _progress = new ProgressStrip(_palette) { Dock = DockStyle.Fill, Height = 12, Margin = new Padding(0, 0, 0, 2) };

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
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        statusRow.Controls.Add(_statusLabel, 0, 0);
        statusRow.Controls.Add(_summaryLabel, 1, 0);
        statusRow.Controls.Add(_speedLabel, 2, 0);

        footer.Controls.Add(_progress, 0, 0);
        footer.Controls.Add(statusRow, 0, 1);
        footer.Controls.Add(new Label
        {
            Text = "XoPy · copias verificadas · sin límites",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = _palette.Muted,
            Font = new Font("Segoe UI", 7.75f)
        }, 0, 2);

        return footer;
    }

    // ---------------------------------------------------------------- ajustes

    private void LoadSettingsIntoUi()
    {
        _destination.Text = _settings.Destination;
        _verifyCheck.Checked = _settings.VerifyAfterCopy;
        _verifyCheck.Text = $"Verificar ({Format.Algorithm(_settings.Verify)})";
        RefreshFavorites();
    }

    private void RefreshFavorites()
    {
        _favorites.Items.Clear();
        foreach (var favorite in _settings.Favorites.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            _favorites.Items.Add(favorite);
        if (_favorites.Items.Count > 0)
        {
            var match = _favorites.Items.Cast<string>().FirstOrDefault(path =>
                string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), _settings.Destination.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));
            if (match is not null) _favorites.SelectedItem = match;
        }
    }

    private void AddFavorite()
    {
        var path = _destination.Text.Trim();
        if (path.Length == 0 || !Directory.Exists(path)) { SetStatus("Elige una carpeta existente antes de guardarla en favoritos."); return; }
        if (!_settings.Favorites.Any(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase)))
            _settings.Favorites.Add(path);
        _store.Save(_settings);
        RefreshFavorites();
        SetStatus("Carpeta añadida a favoritos.");
    }

    private void WireEngine()
    {
        _engine.ConflictResolver = ResolveConflict;
        _engine.ErrorResolver = ResolveError;
        _engine.Log = message => Debug.WriteLine("[XoPy] " + message);
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
        _trayIcon = Icons.AppIcon();
        _tray.Icon = _trayIcon;
        _tray.Text = "XoPy";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => RestoreWindow();
        _trayMenu.BackColor = _palette.Surface;
        _trayMenu.ForeColor = _palette.Text;
        _trayMenu.Renderer = new ToolStripProfessionalRenderer(new MenuColors(_palette));
        _trayPauseItem = new ToolStripMenuItem("Pausar");
        _trayPauseItem.Click += (_, _) => TogglePause();
        _trayMenu.Items.Add("Abrir XoPy", null, (_, _) => RestoreWindow());
        _trayMenu.Items.Add(_trayPauseItem);
        _trayMenu.Items.Add("Detener", null, (_, _) => StopTransfer());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("Historial", null, (_, _) => ShowHistory());
        _trayMenu.Items.Add("Preferencias", null, (_, _) => ShowOptions());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("Salir", null, (_, _) => ExitApplication());
        _tray.ContextMenuStrip = _trayMenu;
    }

    // ---------------------------------------------------------------- arranque externo

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var request = _startupRequest;
        if (request.Paths.Count > 0) AddPaths(request.Paths);
        if (!string.IsNullOrWhiteSpace(request.Destination) && Directory.Exists(request.Destination))
            _destination.Text = request.Destination!;
        if (request.PasteFromClipboard) PasteFromClipboard(request.Destination ?? _destination.Text);
        if (request.StartMinimized || _settings.StartMinimized) MinimizeToTray();
        if (request.Operation is OperationType op && _items.Count > 0)
        {
            var ready = op is OperationType.Delete or OperationType.Test || !string.IsNullOrWhiteSpace(request.Destination);
            if (ready) BeginInvoke(new Action(() => StartTransfer(op)));
        }
    }

    public void HandleExternalRequest(StartupRequest request)
    {
        RestoreWindow();
        if (request.Paths.Count > 0) AddPaths(request.Paths);
        if (!string.IsNullOrWhiteSpace(request.Destination) && Directory.Exists(request.Destination))
            _destination.Text = request.Destination!;
        if (request.PasteFromClipboard) PasteFromClipboard(request.Destination ?? _destination.Text);
        if (request.Operation is OperationType op && _items.Count > 0 && !_engine.IsRunning)
        {
            var ready = op is OperationType.Delete or OperationType.Test || !string.IsNullOrWhiteSpace(request.Destination);
            if (ready) BeginInvoke(new Action(() => StartTransfer(op)));
        }
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
            SetStatus("Abre el menú de una carpeta para pegar con XoPy.");
            return;
        }
        _destination.Text = destination;
        AddPaths(files);
        StartTransfer(OperationType.Copy);
    }

    // ---------------------------------------------------------------- cola

    private void AddFiles()
    {
        if (_openFiles.ShowDialog(this) != DialogResult.OK) return;
        AddPaths(_openFiles.FileNames);
    }

    private void AddFolder()
    {
        if (_pickFolder.ShowDialog(this) != DialogResult.OK) return;
        AddPaths(new[] { _pickFolder.SelectedPath });
    }

    private void DropPaths(DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var raw in paths)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var path = Path.GetFullPath(raw.Trim().Trim('"'));
                if (_items.Any(existing => string.Equals(existing.SourcePath, path, StringComparison.OrdinalIgnoreCase))) continue;

                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    _items.Add(new TransferItem
                    {
                        SourcePath = path,
                        IsFolder = false,
                        SizeBytes = info.Length,
                        FileCount = 1,
                        SizeKnown = true,
                        State = ItemState.Pending,
                        Operation = OperationType.Copy
                    });
                    added++;
                }
                else if (Directory.Exists(path))
                {
                    var item = new TransferItem
                    {
                        SourcePath = path,
                        IsFolder = true,
                        SizeKnown = false,
                        State = ItemState.Measuring,
                        Operation = OperationType.Copy
                    };
                    _items.Add(item);
                    added++;
                    _ = MeasureItemAsync(item);
                }
            }
            catch { }
        }

        if (added > 0)
        {
            SyncRows();
            UpdateFooter();
            SetStatus($"{added} elemento(s) añadido(s) a la lista.");
        }
    }

    private async Task MeasureItemAsync(TransferItem item)
    {
        var size = await Task.Run(() => Measure(item.SourcePath));
        if (IsDisposed) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                item.SizeBytes = size.Bytes;
                item.FileCount = size.Files;
                item.SizeKnown = true;
                if (item.State == ItemState.Measuring) item.State = ItemState.Pending;
                SyncRows();
                UpdateFooter();
            }));
        }
        catch { }
    }

    private static (long Bytes, int Files) Measure(string root)
    {
        long bytes = 0;
        var files = 0;
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var directory = stack.Pop();
            string[] entries;
            string[] children;
            try
            {
                entries = Directory.GetFiles(directory);
                children = Directory.GetDirectories(directory);
            }
            catch { continue; }
            foreach (var file in entries)
            {
                try
                {
                    var attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    bytes += new FileInfo(file).Length;
                    files++;
                }
                catch { }
            }
            foreach (var child in children)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                    stack.Push(child);
                }
                catch { }
            }
        }
        return (bytes, files);
    }

    private void SyncRows()
    {
        foreach (var item in _items)
        {
            if (!_rows.TryGetValue(item.Id, out var row))
            {
                row = new ListViewItem(new[] { item.Name, item.SizeText, item.StateText, string.Empty })
                {
                    Tag = item,
                    ImageIndex = 0
                };
                _rows[item.Id] = row;
                _list.Items.Add(row);
            }
            row.SubItems[1].Text = item.SizeText;
            row.SubItems[2].Text = item.StateText;
            row.SubItems[3].Text = $"{item.Percent:0} %";
            row.ToolTipText = item.Error ?? item.SourcePath;
        }

        foreach (var id in _rows.Keys.ToList())
        {
            if (_items.Any(item => item.Id == id)) continue;
            _list.Items.Remove(_rows[id]);
            _rows.Remove(id);
        }

        _countLabel.Text = _items.Count == 0
            ? "Lista vacía — arrastra archivos o carpetas aquí"
            : $"{_items.Count} elemento(s) · {_items.Sum(i => i.FileCount)} archivo(s) · {Format.Bytes(_items.Sum(i => Math.Max(0, i.SizeBytes)))}";
        _list.Invalidate();
    }

    private void RemoveSelected()
    {
        if (_engine.IsRunning) { SetStatus("Detén la transferencia antes de modificar la lista."); return; }
        var selected = _list.SelectedItems.Cast<ListViewItem>().Select(row => row.Tag).OfType<TransferItem>().ToList();
        if (selected.Count == 0) { SetStatus("Selecciona uno o varios elementos."); return; }
        foreach (var item in selected) _items.Remove(item);
        SyncRows();
        UpdateFooter();
        UpdateButtons();
        SetStatus($"{selected.Count} elemento(s) quitados de la lista.");
    }

    private void ClearList()
    {
        if (_engine.IsRunning) { SetStatus("Detén la transferencia antes de limpiar la lista."); return; }
        _items.Clear();
        _rows.Clear();
        _list.Items.Clear();
        SyncRows();
        UpdateFooter();
        UpdateButtons();
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
        }
        SyncRows();
        SetStatus($"{failed.Count} elemento(s) listos para reintentar. Pulsa Copiar.");
    }

    // ---------------------------------------------------------------- transferencias

    private void StartTransfer(OperationType operation)
    {
        if (_engine.IsRunning) { SetStatus("Ya hay una transferencia en curso."); return; }

        var selected = _list.SelectedItems.Cast<ListViewItem>().Select(row => row.Tag).OfType<TransferItem>().ToList();
        var candidates = selected.Count > 0 ? selected : _items.ToList();
        if (candidates.Count == 0) { SetStatus("Añade archivos o carpetas para empezar."); return; }

        var pending = candidates.Where(item => item.State is not (ItemState.Completed or ItemState.Running)).ToList();
        var targets = pending.Count > 0 ? pending : candidates.ToList();
        if (targets.Count == 0) { SetStatus("No hay elementos que transferir."); return; }

        foreach (var item in targets.Where(item => !item.SizeKnown))
        {
            var measured = Measure(item.SourcePath);
            item.SizeBytes = measured.Bytes;
            item.FileCount = measured.Files;
            item.SizeKnown = true;
        }

        var destination = _destination.Text.Trim();
        if (operation is OperationType.Copy or OperationType.Move or OperationType.Test)
        {
            if (destination.Length == 0)
            {
                BrowseDestination();
                destination = _destination.Text.Trim();
                if (destination.Length == 0) return;
            }
            try { Directory.CreateDirectory(destination); }
            catch (Exception ex) { ShowError("No se pudo usar la carpeta de destino: " + ex.Message); return; }

            _settings.Destination = destination;
            _store.Save(_settings);

            if (operation != OperationType.Test && !ConfirmFreeSpace(destination, targets.Sum(item => item.SizeBytes))) return;
        }

        if (operation == OperationType.Delete && _settings.ConfirmDelete)
        {
            var message = $"¿Eliminar {targets.Count} elemento(s)?\n\n" +
                          (_settings.UseRecycleBin
                              ? "Se enviarán a la Papelera de reciclaje."
                              : "Se eliminarán de forma permanente.");
            if (MessageBox.Show(this, message, "XoPy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }

        foreach (var item in targets)
        {
            item.Operation = operation;
            item.State = ItemState.Pending;
            item.TransferredBytes = 0;
            item.Error = null;
            item.CurrentFile = null;
            item.DestinationPath = operation == OperationType.Delete
                ? string.Empty
                : Path.Combine(destination, Path.GetFileName(Path.TrimEndingDirectorySeparator(item.SourcePath)));
        }

        _lastOperation = operation;
        _wasRunning = true;
        _trayPercent = -1;
        SyncRows();
        UpdateFooter();
        UpdateButtons();

        var options = new EngineOptions
        {
            Destination = destination,
            Conflict = _settings.Conflict,
            OnError = _settings.OnError,
            Verify = _settings.Verify,
            VerifyAfterCopy = _settings.VerifyAfterCopy,
            RetryCount = _settings.RetryCount,
            Parallelism = _settings.Parallelism,
            BufferSize = _settings.BufferKiB * 1024,
            SpeedLimitBytesPerSecond = _settings.SpeedLimitMiB * 1024L * 1024L,
            PreserveTimestamps = _settings.PreserveTimestamps,
            UseRecycleBin = _settings.UseRecycleBin
        };

        if (!_engine.Start(targets, options))
        {
            _wasRunning = false;
            SetStatus("No se pudo iniciar la transferencia.");
            UpdateButtons();
            return;
        }

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

    private void TogglePause()
    {
        if (!_engine.IsRunning) return;
        var paused = !_engine.IsPaused;
        _engine.SetPaused(paused);
        _pauseButton.Text = paused ? "Reanudar" : "Pausar";
        _pauseButton.Image = Icons.Glyph(paused ? Icons.Play : Icons.Pause, _palette.Text, 18);
        _trayPauseItem.Text = paused ? "Reanudar" : "Pausar";
        SetStatus(paused ? "Transferencia en pausa." : "Transferencia en curso…");
    }

    private void StopTransfer()
    {
        if (!_engine.IsRunning) return;
        _engine.Cancel();
        SetStatus("Cancelando… los archivos temporales se eliminarán.");
        UpdateButtons();
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
        _trayPauseItem.Text = "Pausar";

        var failed = _items.Count(item => item.State == ItemState.Failed);
        var cancelled = _items.Count(item => item.State == ItemState.Cancelled);
        var completed = _items.Count(item => item.State == ItemState.Completed);
        var bytes = _engine.DoneBytes;
        var elapsed = _engine.Elapsed;

        _store.AppendHistory(new HistoryEntry
        {
            FinishedAt = DateTime.Now,
            Operation = _lastOperation,
            Destination = _destination.Text.Trim(),
            FileCount = completed,
            Bytes = bytes,
            Seconds = elapsed.TotalSeconds,
            Errors = failed
        });

        SetStatus(failed > 0
            ? $"Terminado con {failed} error(es). Revisa los detalles en la lista."
            : cancelled > 0
                ? "Transferencia cancelada."
                : "Transferencia completada correctamente.");

        var previousIcon = _tray.Icon;
        _tray.Icon = _trayIcon ?? Icons.AppIcon();
        if (previousIcon is not null && !ReferenceEquals(previousIcon, _tray.Icon) && !ReferenceEquals(previousIcon, _trayIcon))
            previousIcon.Dispose();
        _tray.Text = "XoPy";
        _trayPercent = -1;

        SyncRows();
        UpdateFooter();
        UpdateButtons();

        if (_settings.PlaySoundWhenDone)
        {
            try
            {
                if (failed > 0) SystemSounds.Hand.Play();
                else SystemSounds.Asterisk.Play();
            }
            catch { }
        }

        if (_settings.OpenDestinationWhenDone && failed == 0 && cancelled == 0 && _lastOperation is OperationType.Copy or OperationType.Move)
        {
            try
            {
                var folder = _destination.Text.Trim();
                if (Directory.Exists(folder)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            }
            catch { }
        }

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

    private void TimerTick()
    {
        if (_wasRunning) SyncRows();
        UpdateFooter();
        UpdateButtons();

        if (_engine.IsRunning)
        {
            var total = _engine.TotalBytes;
            var percent = total > 0 ? (int)Math.Clamp(_engine.DoneBytes * 100 / total, 0, 100) : 0;
            if (percent != _trayPercent)
            {
                _trayPercent = percent;
                var icon = Icons.ProgressIcon(percent, _settings.DarkTheme);
                var previous = _tray.Icon;
                _tray.Icon = icon;
                if (previous is not null && !ReferenceEquals(previous, _trayIcon)) previous.Dispose();
                _tray.Text = $"XoPy — {percent} %";
            }
            _trayPauseItem.Enabled = true;
        }
        else
        {
            _trayPauseItem.Enabled = false;
        }
    }

    private void UpdateButtons()
    {
        var running = _engine.IsRunning;
        _addFilesButton.Enabled = !running;
        _addFolderButton.Enabled = !running;
        _copyButton.Enabled = !running && _items.Count > 0;
        _moveButton.Enabled = !running && _items.Count > 0;
        _deleteButton.Enabled = !running && _items.Count > 0;
        _testButton.Enabled = !running && _items.Count > 0;
        _pauseButton.Enabled = running;
        _stopButton.Enabled = running;
        _browseButton.Enabled = !running;
        _favorites.Enabled = !running;
        _favoriteButton.Enabled = !running;
        _verifyCheck.Enabled = !running;
        _removeButton.Enabled = !running && _items.Count > 0;
        _clearButton.Enabled = !running && _items.Count > 0;
        _destination.ReadOnly = running;
        _pauseButton.BackColor = running && _engine.IsPaused ? _palette.Accent : _palette.Raised;
        _pauseButton.ForeColor = _pauseButton.BackColor == _palette.Accent ? Color.White : _palette.Text;
    }

    private void UpdateFooter()
    {
        var total = _items.Sum(item => Math.Max(0, item.SizeBytes));
        var done = _items.Sum(item => Math.Max(0, Math.Min(item.TransferredBytes, Math.Max(item.SizeBytes, item.TransferredBytes))));
        var percent = total > 0 ? Math.Clamp(done * 100d / total, 0, 100) : 0;
        if (!_engine.IsRunning && _items.Count > 0 && _items.All(item => item.State is ItemState.Completed or ItemState.Skipped))
            percent = 100;

        _progress.Value = percent;
        _progress.FillColor = _items.Any(item => item.State == ItemState.Failed) && !_engine.IsRunning
            ? _palette.Warning
            : _palette.Accent;

        var files = _items.Sum(item => item.FileCount);
        _summaryLabel.Text = _items.Count == 0
            ? "Sin archivos en la lista"
            : $"{files} archivos · {Format.Bytes(done)} de {Format.Bytes(total)} · {percent:0} %";

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
    }

    private void SetStatus(string message) => _statusLabel.Text = message;

    private void ShowError(string message)
    {
        SetStatus(message);
        MessageBox.Show(this, message, "XoPy", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    // ---------------------------------------------------------------- dibujado

    private void ListDrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var background = new SolidBrush(_palette.SurfaceAlt);
        e.Graphics.FillRectangle(background, e.Bounds);
        using var line = new Pen(_palette.Border);
        e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        TextRenderer.DrawText(e.Graphics, e.Header.Text, _headerFont,
            Rectangle.Inflate(e.Bounds, -10, 0), _palette.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void ListDrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.Item is null) return;
        var item = e.Item.Tag as TransferItem;
        var selected = e.Item.Selected;
        var background = selected
            ? _palette.Selection
            : (e.ItemIndex % 2 == 0 ? _palette.Surface : _palette.SurfaceAlt);
        using (var brush = new SolidBrush(background))
            e.Graphics.FillRectangle(brush, e.Bounds);

        if (item is null) return;

        var textColor = _palette.Text;
        var bounds = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 16), e.Bounds.Height);

        switch (e.ColumnIndex)
        {
            case 0:
            {
                var glyph = item.IsFolder ? Icons.Folder : Icons.File;
                var iconColor = item.IsFolder ? _palette.Accent : _palette.Muted;
                TextRenderer.DrawText(e.Graphics, glyph, _glyphFont,
                    new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 5, 16, 20), iconColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(e.Graphics, item.Name, _list.Font,
                    new Rectangle(e.Bounds.X + 32, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 44), e.Bounds.Height),
                    textColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                break;
            }
            case 1:
                TextRenderer.DrawText(e.Graphics, item.SizeText, _list.Font,
                    new Rectangle(e.Bounds.X, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 14), e.Bounds.Height),
                    _palette.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                break;
            case 2:
                TextRenderer.DrawText(e.Graphics, item.StateText, _list.Font, bounds,
                    StateColor(item), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                break;
            case 3:
            {
                var barBounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + (e.Bounds.Height - 10) / 2,
                    Math.Max(10, e.Bounds.Width - 76), 10);
                Ui.DrawProgress(e.Graphics, barBounds, item.Percent, _palette, StateColor(item));
                TextRenderer.DrawText(e.Graphics, $"{item.Percent:0} %", _percentFont,
                    new Rectangle(barBounds.Right + 6, e.Bounds.Y, 56, e.Bounds.Height), _palette.Muted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                break;
            }
        }
    }

    private Color StateColor(TransferItem item) => item.State switch
    {
        ItemState.Completed => _palette.Success,
        ItemState.Failed => _palette.Danger,
        ItemState.Cancelled => _palette.Warning,
        ItemState.Skipped => _palette.Muted,
        ItemState.Running or ItemState.Verifying => _palette.Accent,
        ItemState.Measuring => _palette.Warning,
        _ => _palette.Muted
    };

    private void ListColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (_engine.IsRunning) return;
        if (_sortColumn == e.Column) _sortAscending = !_sortAscending;
        else { _sortColumn = e.Column; _sortAscending = true; }

        Comparison<TransferItem> comparison = e.Column switch
        {
            0 => (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name),
            1 => (left, right) => left.SizeBytes.CompareTo(right.SizeBytes),
            2 => (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.StateText, right.StateText),
            _ => (left, right) => left.Percent.CompareTo(right.Percent)
        };
        if (!_sortAscending)
        {
            var ascending = comparison;
            comparison = (left, right) => -ascending(left, right);
        }

        _items.Sort(comparison);
        _list.Items.Clear();
        _rows.Clear();
        SyncRows();
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
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{item.SourcePath}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private void OpenSelectedFolder()
    {
        if (_list.SelectedItems.Count != 1) return;
        if (_list.SelectedItems[0].Tag is not TransferItem item) return;
        try
        {
            var folder = Directory.Exists(item.SourcePath) ? item.SourcePath : Path.GetDirectoryName(item.SourcePath);
            if (!string.IsNullOrEmpty(folder))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch { }
    }

    // ---------------------------------------------------------------- cuadros de diálogo

    private void BrowseDestination()
    {
        try
        {
            if (Directory.Exists(_destination.Text.Trim())) _pickDestination.SelectedPath = _destination.Text.Trim();
        }
        catch { }
        if (_pickDestination.ShowDialog(this) != DialogResult.OK) return;
        _destination.Text = _pickDestination.SelectedPath;
        _settings.Destination = _destination.Text;
        _store.Save(_settings);
    }

    private void ShowOptions()
    {
        using var dialog = new OptionsDialog(_settings, _palette);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        var themeChanged = dialog.Result.DarkTheme != _settings.DarkTheme;
        dialog.ApplyTo(_settings);
        _store.Save(_settings);

        try
        {
            ShellIntegration.Apply(_settings.ExplorerIntegration);
            ShellIntegration.SetAutoStart(_settings.StartWithWindows);
        }
        catch { }

        LoadSettingsIntoUi();
        UpdateButtons();
        UpdateFooter();
        SetStatus("Preferencias guardadas.");

        if (themeChanged)
        {
            MessageBox.Show(this, "El tema se aplicará la próxima vez que abras XoPy.", "XoPy",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ShowExplorerIntegration()
    {
        var message = "La integración añade XoPy al menú contextual del Explorador de Windows:\n\n" +
                      "· Copiar con XoPy\n· Mover con XoPy\n· Verificar con XoPy\n· Pegar con XoPy (en el fondo de una carpeta)\n\n" +
                      "Windows no permite que un programa normal intercepte Ctrl+C / Ctrl+V; por eso la integración usa el menú contextual, igual que TeraCopy.";
        if (MessageBox.Show(this, message + "\n\n¿Activar la integración con el Explorador?", "Integración con el Explorador",
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
        using var dialog = new HistoryDialog(_store.LoadHistory(), _palette);
        dialog.ShowDialog(this);
    }

    private void ShowAbout()
    {
        var version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "2.0.0";
        MessageBox.Show(this,
            $"XoPy {version}\n\nGestor de copias gratuito para Windows, inspirado en la interfaz de TeraCopy.\n\n" +
            "· Copia, mueve, elimina y verifica archivos o carpetas completas\n" +
            "· Verificación CRC32, MD5, SHA-1, SHA-256 y SHA-512\n" +
            "· Pausa, reanudación, límite de velocidad y transferencias paralelas\n" +
            "· Historial, favoritos e integración con el Explorador\n\n" +
            "Hecho por Quevedo-crack.",
            "Acerca de XoPy", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SaveQueueFile()
    {
        if (_saveQueue.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var snapshot = new QueueSnapshot
            {
                Destination = _destination.Text.Trim(),
                Operation = OperationType.Copy,
                Sources = _items.Select(item => item.SourcePath).ToList()
            };
            File.WriteAllText(_saveQueue.FileName, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            SetStatus("Cola guardada en " + _saveQueue.FileName);
        }
        catch (Exception ex)
        {
            ShowError("No se pudo guardar la cola: " + ex.Message);
        }
    }

    private void LoadQueueFile()
    {
        if (_engine.IsRunning) { SetStatus("Detén la transferencia antes de abrir otra cola."); return; }
        if (_loadQueue.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var snapshot = JsonSerializer.Deserialize<QueueSnapshot>(File.ReadAllText(_loadQueue.FileName));
            if (snapshot is null) { ShowError("El archivo no contiene una cola válida."); return; }

            _items.Clear();
            _rows.Clear();
            _list.Items.Clear();

            if (!string.IsNullOrWhiteSpace(snapshot.Destination) && Directory.Exists(snapshot.Destination))
                _destination.Text = snapshot.Destination;
            AddPaths(snapshot.Sources);
            SetStatus($"Cola cargada: {_items.Count} elemento(s).");
        }
        catch (Exception ex)
        {
            ShowError("No se pudo abrir la cola: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------- ventana y bandeja

    private void RestoreWindow()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        Show();
        Activate();
        BringToFront();
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
            _listMenu.Dispose();
            if (_trayIcon is not null) _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
