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
    /// <summary>
    /// La ventana principal, para que el enganche de fallos de <see cref="Program"/> pueda
    /// avisar sin abrir un cuadro modal (un modal deja la copia esperando).
    /// </summary>
    public static MainForm? Current { get; private set; }

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
    private ToolStripButton _multiThreadButton = null!;
    private Button _removeButton = null!;
    private Button _clearButton = null!;
    private Button _upButton = null!;
    private Button _downButton = null!;
    private ListView _list = null!;
    private Label _countLabel = null!;
    private Button _sortButton = null!;
    // Vista de la lista: «En cola» (lo que falta por copiar) o «Finalizados» (lo ya copiado).
    // Por defecto se ve SOLO lo pendiente, para que ordenar por tamaño no mezcle lo terminado.
    private bool _showFinished;
    private Button _viewQueueButton = null!;
    private Button _viewDoneButton = null!;
    private PillLabel _hdrCopyState = null!, _hdrTargetState = null!;
    private Label _hdrCopyPath = null!, _hdrCopyInfo = null!, _hdrCopyPercent = null!;
    private Label _hdrTargetPath = null!, _hdrTargetInfo = null!, _hdrTargetPercent = null!;
    private ProgressStrip _hdrCopyBar = null!, _hdrFileBar = null!;
    private SpeedGraph _footerGraph = null!;
    private TableLayoutPanel? _footerLayout;
    private TableLayoutPanel? _statusRow;
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
    private ListHeaderTail? _headerTail;
    private Control[] _pages = Array.Empty<Control>();
    private int _tabIndex;

    private ListBox _jobsList = null!;
    private ListBox _targetList = null!;
    private Label _targetFreeLabel = null!;
    private TextBox _logBox = null!;
    private Label _stOperation = null!;
    private Label _stSpeed = null!;
    private Label _stEta = null!;
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
    private bool _shown;
    /// <summary>La interfaz está construida y los controles existen (fin de OnShown).</summary>
    private volatile bool _uiReady;
    private int _trayPercent = -1;
    private TableLayoutPanel _contentGrid = null!;
    private Control _jobsPanel = null!;
    private Button? _jobsToggleButton;
    private ToolStripMenuItem? _fullPathMenuItem;
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
    /// <summary>Cuántas copias había en marcha la última vez que se pintó el icono de la bandeja.</summary>
    private int _trayJobs;
    private DateTime _completedAt = DateTime.MinValue;
    private OperationType _lastOperation = OperationType.Copy;
    private string _lastDestination = string.Empty;
    private long _sessionBytes;
    private double _lastPercent;
    private TimeSpan _sessionElapsed;

    // ---------------------------------------------------------------- listas de copia múltiples en una misma pestaña

    private readonly List<TransferList> _lists = new();
    private TransferList? _activeList;
    /// <summary>Lista sobre la que está trabajando el motor ahora mismo (null si está parado).</summary>
    private TransferList? _runningList;

    public MainForm(StartupRequest request)
    {
        _startupRequest = request;
        _settings = _store.Load();
        _history = _store.LoadHistory();
        _palette = Palette.For(_settings.DarkTheme);
        Current = this;
    }

    /// <summary>
    /// Avisa de un fallo interno SIN abrir un cuadro modal: lo anota en el registro y lo
    /// enseña en la barra de estado. Un modal aquí deja la ventana sin atender y la copia
    /// en curso se queda parada esperando a que alguien pulse «Aceptar».
    /// </summary>
    public void ReportBackgroundError(Exception error)
    {
        try
        {
            if (IsDisposed || !IsHandleCreated) return;
            void Show()
            {
                try
                {
                    SetStatus("Ha fallado algo dentro de XoPy (" + error.GetType().Name + "). " +
                              "La copia sigue; el detalle está en el registro: Ayuda → Ver registro.");
                }
                catch { }
            }
            if (InvokeRequired) BeginInvoke(new Action(Show));
            else Show();
        }
        catch { }
    }

    // ---------------------------------------------------------------- interfaz

    private void BuildUi()
    {
        SuspendLayout();
        // Como SuperCopier: el título dice para qué sirve el programa.
        Text = "XoPy — reemplazo del copiador de Windows";
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
            LayoutRootRows();
            ResizeColumns();
            SelectTab(0);
            ApplyDarkScrollbars();
            // Último repaso: cualquier control que se haya quedado con los colores de
            // Windows (ese gris casi blanco de fábrica) se pinta con la paleta, y las
            // barras de desplazamiento nativas se oscurecen. Es lo que acaba con las
            // «barras blancas» sueltas que veía el usuario.
            Ui.PaintAll(this, _palette);
            Ui.DarkScrollbars(this, _palette);
        };
    }

    private void ToggleCompactView() => ApplyCompactView(!_compactView, persist: true);

    // Alturas fijas de las franjas de la ventana (en píxeles de cliente).
    private const int RowMenuHeight = 24;
    private const int RowToolbarHeight = 48;
    // Cabecera: línea del archivo (24) + su barra (12) + línea del total (24) + su barra
    // (16) + 8 de relleno. Ni un píxel de más: lo que sobra se ve como franja vacía.
    private const int RowHeaderHeight = 84;
    private const int RowTabsHeight = 34;
    // Pie: línea del archivo en curso (17) + gráfico de velocidad (35) + línea de estado
    // (28) + relleno (16). La barra de progreso ya no está aquí.
    private const int RowFooterNormal = 96;
    // Pie de la vista reducida: NADA. El progreso, la velocidad y el tiempo restante están
    // en la cabecera, que siempre se ve; dejar una franja aquí era hueco desperdiciado.
    private const int RowFooterCompact = 0;
    private const int RowContentMin = 110;

    /// <summary>
    /// Cuánto hay que agrandar las medidas fijas. Todas las alturas de arriba están
    /// escritas en píxeles «al 100 %», pero las letras y los iconos los agranda Windows
    /// cuando el usuario pone el tamaño del texto al 125 % o al 150 %: sin este factor,
    /// la barra de 48 px seguía midiendo 48 con botones de 60 y el texto salía cortado
    /// («Actualiz», «Abrir el archivo de»), y la ventana reducida se quedaba corta.
    /// </summary>
    private float UiScale => DeviceDpi > 0 ? DeviceDpi / 96f : 1f;

    /// <summary>Convierte una medida «al 100 %» en píxeles reales de esta pantalla.</summary>
    private int Px(int logical) => (int)Math.Round(logical * UiScale);

    /// <summary>
    /// Reparte el alto de la ventana a mano. Hacía falta: con la fila del contenido en
    /// «porcentaje», TableLayoutPanel le daba el alto que PEDÍA la pestaña más grande
    /// (la de Estado) y se lo quitaba a las demás franjas — por eso desaparecía la barra de
    /// pestañas y el pie salía cortado por la mitad. Aquí se calcula y se fija.
    /// </summary>
    private void LayoutRootRows()
    {
        if (_rootLayout is null || _rootLayout.RowStyles.Count < 6) return;

        var menu = _settings.ShowMenuBar ? Px(RowMenuHeight) : 0;
        var tabs = _compactView ? 0 : Px(RowTabsHeight);
        var footer = _compactView ? RowFooterCompact : Px(RowFooterNormal);
        var available = _rootLayout.ClientSize.Height;
        if (available <= 0) available = ClientSize.Height;
        var content = available - menu - Px(RowToolbarHeight) - Px(RowHeaderHeight) - tabs - footer;
        if (content < Px(RowContentMin)) content = Px(RowContentMin);
        // LISTA OCULTA: la franja del contenido se va a cero de verdad. Antes la vista
        // reducida seguía reservando sitio para la lista, así que el botón decía «Ocultar»
        // y la lista no se iba a ninguna parte.
        if (_compactView) content = 0;

        var changed = false;
        void Set(int index, float height)
        {
            var style = _rootLayout.RowStyles[index];
            if (style.SizeType == SizeType.Absolute && Math.Abs(style.Height - height) < 0.5f) return;
            style.SizeType = SizeType.Absolute;
            style.Height = height;
            changed = true;
        }

        _rootLayout.SuspendLayout();
        Set(0, menu);
        Set(1, Px(RowToolbarHeight));
        Set(2, Px(RowHeaderHeight));
        Set(3, tabs);
        Set(4, content);
        Set(5, footer);
        // Solo se vuelve a repartir si de verdad ha cambiado algo: esto se llama en cada
        // repaso del temporizador de tamaño y un PerformLayout por tick parpadea.
        _rootLayout.ResumeLayout(changed);
    }

    private void ApplyCompactView(bool compact, bool persist)
    {
        _compactView = compact;
        if (_rootLayout is not null)
        {
            _rootLayout.SuspendLayout();
            // En reducido se ocultan las pestañas (como TeraCopy: solo la lista y el progreso);
            // la lista SÍ queda a la vista, que es donde va la barra de cada archivo.
            if (_tabBar is not null) _tabBar.Visible = !compact;
            // En reducido el historial de listas se repliega y la lista ocupa todo el ancho.
            // Antes esto peleaba con SetJobsVisible: la vista reducida ponía la columna a 0 y
            // el ajuste guardado la devolvía a 228 justo después, así que al abrir salía media
            // ventana reducida (sin pestañas) y media completa (con el panel TRABAJOS).
            ApplyJobsLayout();
            // VISTA REDUCIDA: el pie se queda SOLO con la barra de progreso y la línea de
            // estado. El gráfico de velocidad y la línea «quedan N archivos» repiten lo que
            // ya dice la cabecera (porcentaje, velocidad y tiempo restante) y era otra de las
            // cosas repetidas que señaló el usuario.
            if (_footerLayout is not null && _footerLayout.RowStyles.Count >= 4)
            {
                _footerLayout.RowStyles[0].Height = compact ? 0 : Px(17);
                _footerLayout.RowStyles[1].Height = compact ? 0 : Px(35);
                _footerLayout.RowStyles[2].Height = 0;
                // En reducido la línea de estado se va del todo: «Transferencia en pausa» ya
                // se ve en el botón (que pasa a decir «Reanudar») y en la cabecera, y esa
                // franja era el hueco desperdiciado de la ventana pequeña.
                _footerLayout.RowStyles[3].Height = compact ? 0 : Px(28);
                if (_footerLayout.Parent is not null) _footerLayout.Visible = !compact;
                if (_currentLabel is not null) _currentLabel.Visible = !compact;
                if (_footerGraph is not null) _footerGraph.Visible = !compact;
                if (_statusRow is not null) _statusRow.Visible = !compact;
                // La velocidad y el tiempo restante salen ya en la segunda línea de la
                // cabecera: en reducido se ocultan aquí para no decirlo dos veces.
                if (_speedLabel is not null) _speedLabel.Visible = !compact;
            }
            if (_transferHeader is not null && _transferHeader.RowStyles.Count >= 4)
            {
                // Las dos líneas con sus dos barras se ven igual en reducido: en ese modo
                // son lo único que queda, así que tienen que estar completas. El orden es
                // el de BuildTransferHeader: Total + su barra, y debajo el archivo con la
                // suya (si estos altos no coinciden con los de allí, una de las dos barras
                // se queda a medio pintar).
                _transferHeader.RowStyles[0].Height = Px(24);
                _transferHeader.RowStyles[1].Height = Px(14);
                _transferHeader.RowStyles[2].Height = Px(24);
                _transferHeader.RowStyles[3].Height = Px(14);
            }
            if (compact && _tabIndex != 0) SelectTab(0);
            // La lista (con sus pestañas y el panel de trabajos) se esconde ENTERA, y con
            // ella la propia lista: la barra de desplazamiento de un ListView es una
            // ventana de Windows que se queda pintada si solo se encoge su contenedor, y
            // eso es la «franja blanca» que quedaba debajo al ocultar la lista.
            if (_contentGrid is not null) _contentGrid.Visible = !compact;
            if (_tabsHost is not null) _tabsHost.Visible = !compact;
            if (_list is not null) _list.Visible = !compact;
            // Mientras la lista está escondida no se repasa (sería trabajo invisible): al
            // volver a enseñarla hay que ponerla al día de una vez.
            if (!compact && _uiReady) { try { SyncRows(); } catch { } }
            UpdateToolbarForCompact(compact);
            if (_compactButton is not null)
            {
                // CON TEXTO y con el fondo encendido cuando la lista está escondida: este
                // botón es el ÚNICO camino de vuelta y tiene que cantar. Esconder algo sin
                // dejar a la vista cómo recuperarlo ya dejó al usuario atrapado una vez.
                _compactButton.Image = Icons.Glyph(compact ? Icons.ChevronRight : Icons.ChevronDown,
                    compact ? Color.White : _palette.Text, 18);
                _compactButton.Text = compact ? "Mostrar lista" : "Ocultar lista";
                _compactButton.BackColor = compact ? _palette.Accent : _palette.Surface;
                _compactButton.ForeColor = compact ? Color.White : _palette.Text;
                _compactButton.Font = compact ? _barFontBold : _barFont;
                _compactButton.ToolTipText = compact
                    ? "Volver a mostrar la lista de archivos"
                    : "Ocultar la lista de archivos y dejar solo los controles y el progreso";
                _compactButton.AccessibleName = _compactButton.ToolTipText;
            }
            _rootLayout.ResumeLayout(true);
            LayoutRootRows();
            // Repintado completo: sin esto Windows deja a medias lo que había donde estaba
            // la lista (ese resto claro que se veía al reducir la ventana).
            try { _rootLayout.Invalidate(true); Invalidate(true); Update(); }
            catch { }
        }

        // EL TAMAÑO DE ANTES SE APUNTA SIEMPRE, aunque el cambio no sea definitivo: al
        // pegar con Ctrl+V la ventana se recoge sin «persist», y sin esto, al volver a
        // enseñar la lista, XoPy no sabía a qué tamaño devolverla.
        if (compact && _expandedBounds.Width == 0 && WindowState != FormWindowState.Minimized)
        {
            _expandedWasMaximized = WindowState == FormWindowState.Maximized;
            _expandedBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        }

        // La ventana NUNCA se ajusta sola: se recortaba. Como en TeraCopy, es redimensionable
        // a mano en los dos modos y el tamaño mínimo se calcula para que quepa todo lo visible.
        AutoSize = false;
        // Con la lista oculta solo quedan la barra y la cabecera: el mínimo es ese alto
        // exacto, para que no quede ni un dedo de ventana vacía por debajo.
        MinimumSize = compact ? new Size(560, CompactWindowHeight()) : new Size(820, 560);
        // …Y EL MÁXIMO, EL MISMO. La vista reducida es una banda: si se puede estirar hacia
        // abajo, debajo de la cabecera queda un rectángulo negro enorme con nada dentro
        // (lo que el usuario fotografió). Se bloquea el alto —el ancho se sigue pudiendo
        // cambiar— y se quita el botón de maximizar, que hacía lo mismo de un clic.
        if (compact)
        {
            MaximumSize = CompactMaximum();
            if (MaximizeBox) MaximizeBox = false;
        }
        else
        {
            if (MaximumSize != Size.Empty) MaximumSize = Size.Empty;
            if (!MaximizeBox) MaximizeBox = true;
        }
        if (persist)
        {
            // LA VENTANA NO SE MUEVE DE SITIO. Solo cambia de alto, y crece o encoge hacia
            // abajo desde la esquina en la que está: antes se recolocaba sola por la
            // pantalla cada vez que se mostraba o se escondía la lista.
            if (compact)
            {

                // MAXIMIZADA NO SE QUEDA MAXIMIZADA. Con la lista escondida, una ventana a
                // pantalla completa deja debajo de la cabecera un enorme rectángulo vacío
                // (lo que el usuario señaló en la captura): la vista reducida es una banda
                // y punto. Se sale de maximizado conservando el ancho que tenía.
                if (WindowState == FormWindowState.Maximized)
                {
                    // LA BANDA SE QUEDA DONDE ESTABA LA VENTANA GRANDE. Antes se usaban las
                    // coordenadas guardadas de la ventana «normal» (RestoreBounds), que
                    // podían ser de otra pantalla, de una resolución anterior o estar
                    // directamente fuera del escritorio: la ventana desaparecía y no había
                    // manera de recuperarla. Ahora se parte del rectángulo que ocupa la
                    // ventana maximizada y se recorta al área visible.
                    var screen = Screen.FromControl(this).WorkingArea;
                    var wide = Math.Clamp(Width, 560, Math.Max(560, screen.Width));
                    var band = new Rectangle(screen.X + Math.Max(0, (screen.Width - wide) / 2),
                        screen.Y, wide, CompactWindowHeight());
                    MinimumSize = new Size(560, CompactWindowHeight());
                    MaximumSize = CompactMaximum();
                    WindowState = FormWindowState.Normal;
                    Bounds = ClampToWorkArea(band);
                }
                else if (WindowState == FormWindowState.Normal)
                {
                    // La altura es EXACTAMENTE la de lo que se sigue viendo (menú, barra y
                    // cabecera de progreso). Ni un píxel más: el alto mínimo de 300 que
                    // había antes dejaba una franja negra vacía debajo de todo, que es el
                    // hueco que el usuario señala una y otra vez en la ventana reducida.
                    var height = CompactWindowHeight();
                    // El mínimo se ajusta ANTES de mover la ventana: si no, Math.Max con el
                    // mínimo anterior volvía a estirarla y reaparecía el hueco.
                    MinimumSize = new Size(560, height);
                    MaximumSize = CompactMaximum();
                    // Una sola asignación de Bounds: cambiar Height y Width por separado
                    // provocaba dos recolocaciones y el típico salto de la ventana.
                    Bounds = ClampToWorkArea(new Rectangle(Left, Top, Math.Max(Width, 560), height));
                }
            }
            else if (_expandedBounds.Width > 0)
            {
                AutoSize = false;
                if (_expandedWasMaximized)
                {
                    if (WindowState != FormWindowState.Maximized) WindowState = FormWindowState.Maximized;
                }
                else if (WindowState == FormWindowState.Normal)
                {
                    // Vuelve al alto que tenía, EN EL MISMO SITIO en el que está ahora: si el
                    // usuario movió la ventana con la lista escondida, no se la devolvemos de
                    // un salto a las coordenadas antiguas.
                    Bounds = ClampToWorkArea(new Rectangle(Left, Top,
                        Math.Max(MinimumSize.Width, _expandedBounds.Width),
                        Math.Max(MinimumSize.Height, _expandedBounds.Height)));
                }
                _expandedBounds = Rectangle.Empty;
                _expandedWasMaximized = false;
            }
            _settings.CompactView = compact;
            _settings.ShowList = !compact;
            _store.Save(_settings);
            // Último repaso: pase lo que pase, la ventana TIENE que seguir viéndose.
            RescueOffScreen();
            SetStatus(compact
                ? "Lista oculta. Pulsa «Mostrar lista» en la barra para recuperarla."
                : "Lista visible.");
        }
    }

    /// <summary>
    /// Tamaño MÁXIMO de la vista reducida: alto el de la banda y ancho «el que quieras».
    /// OJO CON EL CERO: en WinForms un MaximumSize con ancho 0 NO significa «sin límite»
    /// —eso solo vale cuando los dos valores son cero—, sino un ancho máximo de cero, y
    /// Windows dejaba la ventana en los 136 px que mide un marco con sus botones. Por eso
    /// aquí se pone un ancho enorme en vez de un cero.
    /// </summary>
    private Size CompactMaximum()
    {
        var wide = 4096;
        try { wide = Math.Max(wide, SystemInformation.VirtualScreen.Width * 2); }
        catch { }
        return new Size(wide, CompactWindowHeight());
    }

    /// <summary>
    /// Mete un rectángulo DENTRO de la pantalla en la que cae. Es la red de seguridad que
    /// impide que la ventana acabe fuera del escritorio (y, por tanto, imposible de
    /// recuperar): pasaba al esconder la lista con la ventana maximizada.
    /// </summary>
    private static Rectangle ClampToWorkArea(Rectangle bounds)
    {
        try
        {
            var area = Screen.FromRectangle(bounds).WorkingArea;
            var width = Math.Min(bounds.Width, area.Width);
            var height = Math.Min(bounds.Height, area.Height);
            var x = Math.Clamp(bounds.X, area.Left, Math.Max(area.Left, area.Right - width));
            var y = Math.Clamp(bounds.Y, area.Top, Math.Max(area.Top, area.Bottom - height));
            return new Rectangle(x, y, Math.Max(200, width), Math.Max(80, height));
        }
        catch { return bounds; }
    }

    /// <summary>
    /// Alto EXACTO de la ventana reducida: lo que miden las franjas que se siguen viendo
    /// más el marco y la barra de título. Sin huecos.
    /// </summary>
    private int CompactWindowHeight()
    {
        var client = (_settings.ShowMenuBar ? Px(RowMenuHeight) : 0) + Px(RowToolbarHeight) + Px(RowHeaderHeight) + 4;
        return client + (Height - ClientSize.Height is var chrome && chrome > 0 && chrome < 120
            ? chrome
            : SystemInformation.FrameBorderSize.Height * 2 + SystemInformation.CaptionHeight);
    }

    /// <summary>Muestra u oculta la barra de menús superior y devuelve el alto a la lista.</summary>
    private void ApplyMenuBarSetting()
    {
        if (_menu is null || _rootLayout is null) return;
        _menu.Visible = _settings.ShowMenuBar;
        LayoutRootRows();
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
        queue.DropDownItems.Add(MenuItem("Llevar al principio  (Alt+Inicio)", (_, _) => MoveSelectedToEdge(top: true)));
        queue.DropDownItems.Add(MenuItem("Llevar al final  (Alt+Fin)", (_, _) => MoveSelectedToEdge(top: false)));
        queue.DropDownItems.Add(new ToolStripSeparator());
        // UN SOLO sitio donde se escriben las opciones de ordenar (ver FillSortItems): el
        // menú Cola y el botón «Ordenar» de la lista enseñaban dos listas distintas, con
        // entradas repetidas y casi iguales entre sí.
        var sortMenu = SubMenu("Ordenar");
        FillSortItems(sortMenu.DropDownItems);
        queue.DropDownItems.Add(sortMenu);

        var viewMenu = SubMenu("Ver lista");
        viewMenu.DropDownItems.Add(MenuItem("Expandir todo", (_, _) => ExpandAll(true)));
        viewMenu.DropDownItems.Add(MenuItem("Contraer todo", (_, _) => ExpandAll(false)));
        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        viewMenu.DropDownItems.Add(MenuItem("Ocultar o mostrar la lista de archivos (F9)", (_, _) => ToggleCompactView(), Keys.F9));
        viewMenu.DropDownItems.Add(MenuItem("Mostrar la ruta completa de cada archivo", (_, _) => ToggleFullPath()));
        // Antes había dos entradas («historial de listas» y «columna de trabajos») que
        // llamaban a lo mismo con nombres distintos. Una sola, con atajo.
        viewMenu.DropDownItems.Add(MenuItem("Ocultar o mostrar la columna de listas (F8)", (_, _) => ToggleJobsPanel(), Keys.F8));
        queue.DropDownItems.Add(viewMenu);
        queue.DropDownItems.Add(new ToolStripSeparator());
        queue.DropDownItems.Add(MenuItem("Quitar seleccionados", (_, _) => RemoveSelected()));
        queue.DropDownItems.Add(MenuItem("Limpiar la lista", (_, _) => ClearList()));

        var tools = new ToolStripMenuItem("Herramientas");
        tools.DropDownItems.Add(MenuItem("Preferencias…", (_, _) => ShowOptions()));
        tools.DropDownItems.Add(MenuItem("Integración con el Explorador…", (_, _) => ShowExplorerIntegration()));
        tools.DropDownItems.Add(MenuItem("Abrir el registro de transferencias", (_, _) => OpenLogFile()));
        tools.DropDownItems.Add(MenuItem("Guardar informe para enviar…", (_, _) => ExportDiagnosticReport()));
        tools.DropDownItems.Add(MenuItem("Reiniciar XoPy como administrador", (_, _) => RestartElevated()));
        // La protección contra ransomware de Windows («Acceso controlado a carpetas») es la
        // causa del «Acceso denegado (error 5)» al copiar al Escritorio o a Documentos.
        tools.DropDownItems.Add(MenuItem("Permitir XoPy en el acceso controlado a carpetas…", (_, _) => AllowControlledFolderAccess()));
        // El Explorador guarda en memoria la DLL del menú: tras actualizar XoPy sigue
        // enseñando el texto de la versión anterior hasta que se reinicia.
        tools.DropDownItems.Add(MenuItem("Refrescar el menú del Explorador…", (_, _) => RepairExplorerMenu()));
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
        // Relleno de 6 arriba y abajo: con 8, los botones de 36 px pedían 52 y la franja mide
        // 48, así que Windows les recortaba la base (se veía media fila de iconos).
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = _palette.Surface,
            Padding = new Padding(10, 6, 10, 6),
            RowCount = 1
        };
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // Izquierda: las acciones, que se recogen en el desplegable « » si la ventana se
        // estrecha. Derecha: los botones de ESTADO (multihilo y ocultar lista) en una
        // columna que se mide sola, así que NUNCA se recogen. Esto importa: el botón de
        // «Mostrar lista» es el camino de vuelta y no puede esconderse en un menú.
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

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
        // en Preferencias). CON TEXTO: en modo solo icono no había forma de saber si estaba
        // activado sin acertar con el color del rayo, que es justo lo que el usuario pedía
        // ver de un vistazo. El estado se nota tres veces: texto, color de fondo y rayo.
        _multiThreadButton = BarButton(MultiThreadLabel(), Icons.Lightning,
            (_, _) => ToggleMultiThread());
        _multiThreadButton.AutoSize = false;
        _multiThreadButton.Size = new Size(BarLabelWidth(MultiThreadButtonWidth, "Multihilo · 16", "Multihilo: no"), 36);
        _multiThreadButton.ToolTipText = MultiThreadTooltip();

        // «Verificar» y el resto de opciones viven en Preferencias: aquí solo lo esencial.
        // El botón de ocultar la lista TAMBIÉN lleva texto: es el único camino de vuelta
        // cuando la lista está escondida y tiene que verse desde el otro lado de la sala.
        _compactButton = BarButton("Ocultar lista", Icons.ChevronDown, (_, _) => ToggleCompactView());
        _compactButton.AutoSize = false;
        _compactButton.Size = new Size(BarLabelWidth(ListToggleButtonWidth, "Ocultar lista", "Mostrar lista"), 36);
        _compactButton.Margin = new Padding(2, 0, 4, 0);
        _compactButton.ToolTipText = "Ocultar la lista de archivos y dejar solo los controles y el progreso";
        // Único acceso a Preferencias (el menú «⋯» tenía la misma opción y sobraba).
        _optionsButton = BarButton("Opciones", Icons.Settings, (_, _) => ShowOptions(), false, true);
        _optionsButton.ToolTipText = "Preferencias…";
        _optionsButton.Margin = new Padding(6, 0, 4, 0);
        // El selector de listas y los botones de «nueva / borrar lista» ya NO viven en esta
        // barra: estaban creados pero nunca se añadían, así que no se veían. Todo lo de las
        // listas de copia está ahora en el panel TRABAJOS, que es donde se distinguen por
        // color el estado de cada una.
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

        // Los controles de la transferencia siguen con las acciones: si no caben, se
        // recogen en el desplegable, pero mientras haya sitio van en su orden natural.
        left.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripSeparator(),
            _pauseButton, _skipButton, _stopButton
        });

        // Barra derecha: siempre visible, nunca se recoge.
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
        right.Items.AddRange(new ToolStripItem[] { _multiThreadButton, _compactButton, _optionsButton });

        bar.Controls.Add(left, 0, 0);
        bar.Controls.Add(right, 1, 0);
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(_palette.Border);
            e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
        };
        return bar;
    }

    /// <summary>Ancho fijo del botón de multihilo: su texto cambia y el botón no debe bailar.</summary>
    private const int MultiThreadButtonWidth = 128;

    /// <summary>Ancho fijo del botón que oculta y muestra la lista.</summary>
    private const int ListToggleButtonWidth = 124;

    /// <summary>
    /// Ancho de un botón de la barra cuyo texto CAMBIA (multihilo, ocultar/mostrar lista):
    /// se mide el más largo de los textos posibles, para que el botón no baile al cambiar
    /// y para que con la letra de Windows al 125 % siga cabiendo entero. Los anchos fijos
    /// de antes cortaban el texto en cuanto se agrandaba la letra del sistema.
    /// </summary>
    private static int BarLabelWidth(int minimum, params string[] labels)
    {
        var width = minimum;
        try
        {
            using var font = new Font("Segoe UI", 8.75f, FontStyle.Bold);
            foreach (var label in labels)
                width = Math.Max(width, TextRenderer.MeasureText(label, font).Width + 46);
        }
        catch { }
        return width;
    }

    private ToolStripButton BarButton(string text, string glyph, EventHandler click, bool accent = false, bool iconOnly = false)
    {
        var foreground = accent ? Color.White : _palette.Text;
        var font = new Font("Segoe UI", 8.75f, FontStyle.Bold);
        // Holgura justa: con +58 los tres botones de la transferencia no cabían junto a los
        // de estado y «Detener» acababa escondido en el desplegable « ». Con +46 el texto
        // sigue sin tocar el borde (los iconos miden 18 px y el relleno es de 8).
        var width = iconOnly ? 38 : TextRenderer.MeasureText(text, font).Width + 46;
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
        // 46, no 36: el botón «Historial» mide 38 de alto (Ui.ToolButton) y con 36 se veía
        // cortado por abajo, como en la captura del informe.
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

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

        // Crear y cerrar listas de copia se hace aquí, junto a las listas: antes estos dos
        // botones estaban en la barra de arriba y no llegaban a verse nunca.
        var newListButton = Ui.IconButton(Icons.Add, _palette, "Nueva lista de copia");
        newListButton.Size = new Size(22, 22);
        newListButton.Margin = new Padding(4, 2, 0, 0);
        newListButton.Click += (_, _) =>
        {
            var dest = _destination.Text.Trim();
            if (!Directory.Exists(dest) && _activeList is not null && Directory.Exists(_activeList.Destination))
                dest = _activeList.Destination;
            AddNewList(dest);
        };

        var closeListButton = Ui.IconButton(Icons.ClearList, _palette,
            "Cerrar esta lista de copia (no borra ningún archivo del disco).\n"
            + "Con la tecla Mayús pulsada se cierra directamente, sin preguntar.");
        closeListButton.Size = new Size(22, 22);
        closeListButton.Margin = new Padding(2, 2, 0, 0);
        // Mayús = «ya sé lo que hago»: cierra la lista sin el cuadro de confirmación. Es
        // para limpiar varias listas seguidas sin tener que contestar una y otra vez.
        closeListButton.Click += (_, _) => DeleteList(_activeList, ask: ModifierKeys != Keys.Shift);

        _jobsToggleButton = Ui.IconButton("\u00AB", _palette, "Ocultar la columna de listas (F8)");
        _jobsToggleButton.Image = null;
        _jobsToggleButton.Text = "\u00AB";
        _jobsToggleButton.Size = new Size(22, 22);
        _jobsToggleButton.Margin = new Padding(2, 2, 0, 0);
        _jobsToggleButton.Click += (_, _) => SetJobsVisible(!_jobsVisible, persist: true);
        headerRow.Controls.Add(title);
        headerRow.Controls.Add(newListButton);
        headerRow.Controls.Add(closeListButton);
        headerRow.Controls.Add(_jobsToggleButton);

        _jobsList = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = _palette.Surface,
            ForeColor = _palette.Text,
            Font = new Font("Segoe UI", 8.75f),
            IntegralHeight = false,
            Margin = new Padding(0),
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 19
        };
        // Cada lista se pinta con el color de su estado: azul la que copia ahora, naranja la
        // que está en pausa, verde las terminadas y gris las que esperan turno.
        _jobsList.DrawItem += JobsDrawItem;
        // Un clic cambia a esa lista de copia; el doble clic abre el historial.
        _jobsList.Click += (_, _) => ActivateSelectedJob();
        _jobsList.DoubleClick += (_, _) => ShowHistory();
        // Supr cierra la lista marcada (preguntando); Mayús+Supr la cierra sin preguntar.
        _jobsList.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Delete) return;
            e.Handled = true;
            DeleteList(SelectedJobList(), ask: !e.Shift);
        };
        // Menú del botón derecho sobre el panel: lo que se puede hacer con una lista, dicho
        // con todas las letras y con el atajo al lado.
        var jobsMenu = new ContextMenuStrip();
        Ui.StyleMenu(jobsMenu, _palette);
        jobsMenu.Items.Add(MenuItem("Abrir esta lista", (_, _) => ActivateSelectedJob()));
        jobsMenu.Items.Add(new ToolStripSeparator());
        jobsMenu.Items.Add(MenuItem("Nueva lista de copia", (_, _) => AddNewList(_destination.Text.Trim())));
        jobsMenu.Items.Add(MenuItem("Cerrar esta lista…  (Supr)", (_, _) => DeleteList(SelectedJobList(), ask: true)));
        jobsMenu.Items.Add(MenuItem("Cerrar sin preguntar  (Mayús+Supr)", (_, _) => DeleteList(SelectedJobList(), ask: false)));
        jobsMenu.Opening += (_, e) =>
        {
            // El botón derecho no marca la línea en un ListBox: hay que marcarla a mano o el
            // menú actuaría sobre otra lista distinta de la que se ha señalado.
            var point = _jobsList.PointToClient(Cursor.Position);
            var index = _jobsList.IndexFromPoint(point);
            if (index >= 0 && index < _jobsList.Items.Count) _jobsList.SelectedIndex = index;
            if (SelectedJobList() is null) e.Cancel = true;
        };
        _jobsList.ContextMenuStrip = jobsMenu;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = _palette.Canvas,
            Padding = new Padding(0, 6, 0, 0)
        };
        var historyButton = Ui.ToolButton("Historial", Icons.History, _palette);
        // El ancho lo pone el texto; el alto es el de la fila menos el hueco de arriba.
        Ui.Fit(historyButton, 120, 36);
        historyButton.Height = 36;
        historyButton.Margin = new Padding(0);
        historyButton.Click += (_, _) => ShowHistory();
        buttons.Controls.Add(historyButton);

        panel.Controls.Add(headerRow, 0, 0);
        panel.Controls.Add(_jobsList, 0, 1);
        panel.Controls.Add(buttons, 0, 2);
        return panel;
    }

    private Font? _jobsBoldFont;

    private void JobsDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (_jobsList is null || e.Index < 0 || e.Index >= _jobsList.Items.Count) return;
        var value = _jobsList.Items[e.Index];
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        using (var background = new SolidBrush(selected ? _palette.Selection : _palette.Surface))
            e.Graphics.FillRectangle(background, e.Bounds);

        var text = value?.ToString() ?? string.Empty;
        var color = _palette.Text;
        var font = _jobsList.Font;
        if (value is JobLine line)
        {
            text = line.Title;
            color = line.IsDetail ? _palette.Muted : StageColor(line.Stage);
            // La lista que se está viendo va en negrita. La fuente se crea una sola vez: en
            // un DrawItem se repinta decenas de veces por segundo y crearla aquí agotaba
            // los recursos de GDI.
            if (!line.IsDetail && line.List is not null && line.List == _activeList)
                font = _jobsBoldFont ??= new Font(_jobsList.Font, FontStyle.Bold);
        }
        TextRenderer.DrawText(e.Graphics, text, font, Rectangle.Inflate(e.Bounds, -4, 0), color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    /// <summary>La lista de copia señalada en el panel de trabajos, si hay alguna.</summary>
    private TransferList? SelectedJobList()
        => _jobsList?.SelectedItem is JobLine line ? line.List : null;

    /// <summary>Al pulsar una línea del panel de trabajos se pasa a esa lista de copia.</summary>
    private void ActivateSelectedJob()
    {
        if (_jobsList?.SelectedItem is not JobLine line || line.List is null) return;
        if (line.List == _activeList) return;
        SwitchToList(line.List);
        RebuildRows();
        UpdateFooter();
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
            RowCount = 3,
            BackColor = _palette.Surface,
            Padding = new Padding(10, 5, 10, 3),
            Margin = new Padding(0, 0, 0, 0)
        };
        _transferHeader = host;
        // DOS BARRAS, cada una debajo de su línea y sin lugar a dudas:
        //   línea 1 «Total …»     + barra del TOTAL de la copia (la grande, arriba)
        //   línea 2 «Copiando …»  + barra del ARCHIVO que se está copiando ahora
        // El porcentaje de cada línea es el de SU barra. El total va PRIMERO: es el dato
        // que se mira de reojo, y así coincide con el orden de FlashCopy Pro.
        host.RowCount = 4;
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));

        host.Controls.Add(BuildHeaderLine("Total", out _hdrTargetState, out _hdrTargetPath, out _hdrTargetInfo, out _hdrTargetPercent), 0, 0);
        host.Controls.Add(BuildHeaderBar(out _hdrCopyBar, 14), 0, 1);
        host.Controls.Add(BuildHeaderLine("Copiando", out _hdrCopyState, out _hdrCopyPath, out _hdrCopyInfo, out _hdrCopyPercent), 0, 2);
        host.Controls.Add(BuildHeaderBar(out _hdrFileBar, 12), 0, 3);
        return host;
    }

    private Control BuildHeaderBar(out ProgressStrip bar, int height)
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = _palette.Surface, Margin = new Padding(0, 1, 0, 2), Padding = new Padding(0) };
        // Las barras de 4 px no se veían y parecían una raya del borde.
        bar = new ProgressStrip(_palette) { Dock = DockStyle.Fill, Height = height, MinimumSize = new Size(0, 8) };
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
        // AL TAMAÑO DEL TEXTO, no a un ancho fijo. En la línea «Total» va todo lo que hay
        // que saber —«1.234 de 3.222 archivos · 2,3 GB de 2,9 GB · 88 MB/s · quedan 6 s»— y
        // con 320 px fijos se cortaba por la mitad. Lo que sobra se lo queda la ruta.
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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
            // AutoSize: la columna se mide por el texto (ver ColumnStyles), así nunca se
            // corta «quedan 6 s» ni los megabytes que faltan.
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = _palette.Muted,
            Font = new Font("Segoe UI", 8.25f),
            UseMnemonic = false,
            Margin = new Padding(0, 4, 8, 0)
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
        FillSortItems(menu.Items);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    /// <summary>
    /// LAS OPCIONES DE ORDENAR, EN UN SOLO SITIO. Antes había dos listas distintas (el
    /// menú Cola y el botón «Ordenar» de la lista) con entradas repetidas y otras que
    /// hacían casi lo mismo con nombres diferentes: «Tamaño, dentro de cada carpeta»,
    /// «Tamaño de TODOS los archivos», «Tamaño de carpeta por el total de su contenido»…
    /// Ahora son nueve líneas claras y cada una hace algo distinto. La primera es la que
    /// pedía el usuario: los archivos MÁS GRANDES primero, uno a uno, sin importar de qué
    /// carpeta salgan, que es lo que marca el orden real de la copia.
    /// </summary>
    private void FillSortItems(ToolStripItemCollection items)
    {
        void Add(string text, Action action)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (_, _) => action();
            items.Add(item);
        }
        void Separator() => items.Add(new ToolStripSeparator());

        Add("Archivos más grandes primero", () => SortAllFilesBySize(invert: true));
        Add("Archivos más pequeños primero", () => SortAllFilesBySize(invert: false));
        Add("Tamaño total de cada carpeta (las mayores primero)", () => SortQueue(QueueSortField.FolderSize, invert: true));
        Separator();
        Add("Nombre (A → Z)", () => SortQueue(QueueSortField.Name));
        Add("Fecha de modificación (más nuevos primero)", () => SortQueue(QueueSortField.Modified, invert: true));
        Add("Tipo de archivo", () => SortQueue(QueueSortField.Type));
        Add("Carpeta de destino", () => SortQueue(QueueSortField.Destination));
        Add("Carpeta de origen", () => SortQueue(QueueSortField.Source));
        Add("Estado de la copia", () => SortQueue(QueueSortField.State));
        Separator();
        Add("Invertir el orden actual", InvertCurrentSort);
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
        // La fila de la barra de progreso se queda en 0: LA BARRA DEL TOTAL ES LA DE LA
        // CABECERA, y solo hay una. Tener otra aquí abajo era la «segunda barra de progreso
        // total» que no había manera de distinguir de la primera.
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 17));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
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
        // Sigue existiendo (hay código que le pone el valor), pero no se ve: la única barra
        // de progreso total de la ventana es la de la cabecera.
        _progress = new ProgressStrip(_palette) { Dock = DockStyle.Fill, Height = 10, Margin = new Padding(0, 0, 0, 1), Visible = false };

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
        _statusRow = statusRow;

        footer.Controls.Add(_currentLabel, 0, 0);
        footer.Controls.Add(_footerGraph, 0, 1);
        footer.Controls.Add(_progress, 0, 2);
        footer.Controls.Add(statusRow, 0, 3);
        _footerLayout = footer;
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

        // Tres columnas: las dos pestañitas de la lista (ancho justo), el recuento (lo que
        // sobre, con puntos suspensivos si no cabe) y los botones de la derecha (ancho justo).
        // Con dos columnas, el recuento vivía dentro del FlowLayoutPanel de las pestañitas y
        // se cortaba a media palabra («4 archivo(s) · 22.0» en la captura).
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            BackColor = _palette.SurfaceAlt,
            RowCount = 1
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Dos vistas, como pidió el usuario: la lista por defecto enseña SOLO lo que falta por
        // copiar y lo terminado se va a «Finalizados». Así, al ordenar por tamaño, no se
        // mezclan los archivos ya copiados con los que quedan.
        _viewQueueButton = ViewTab("En cola", () => SetQueueView(false));
        _viewDoneButton = ViewTab("Finalizados", () => SetQueueView(true));

        // Etiqueta de UNA línea: el Label normal partía «4 archivo(s) · 22.0 KB» en dos
        // renglones dentro de una franja de 36 px.
        _countLabel = Ui.OneLine(string.Empty, _palette, 8.5f, false, _palette.Muted);
        _countLabel.Dock = DockStyle.Fill;
        _countLabel.TextAlign = ContentAlignment.MiddleLeft;
        _countLabel.Margin = new Padding(10, 0, 6, 0);

        var viewRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = _palette.SurfaceAlt,
            Padding = new Padding(6, 4, 0, 0),
            Margin = new Padding(0)
        };
        viewRow.Controls.Add(_viewQueueButton);
        viewRow.Controls.Add(_viewDoneButton);

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
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = _palette.SurfaceAlt,
            Padding = new Padding(0, 2, 4, 0)
        };
        // Icono nativo de «Segoe MDL2 Assets»: U+21C5 no existe en esa fuente y salía
        // rasterizado como un amasijo de puntos.
        _sortButton = Ui.IconButton(Icons.SortAsc, _palette,
            "Ordenar la lista por nombre, tamaño, fecha de modificación, destino u origen");
        _sortButton.Click += (_, _) => ShowSortMenu(_sortButton);

        // Único interruptor que queda a la vista cuando la columna de listas se esconde: por eso
        // al ocultarla se le pone texto («Listas») en vez de dejar solo el chevrón.
        _collapseButton = Ui.IconButton("\u00AB", _palette, "Ocultar la columna de listas (F8)");
        // « y » no están en la fuente de iconos: se pintan como TEXTO. Si además se deja la
        // imagen del glifo, WinForms superpone las dos cosas y queda un borrón.
        _collapseButton.Image = null;
        _collapseButton.Text = "\u00AB";
        _collapseButton.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        _collapseButton.Click += (_, _) => ToggleJobsPanel();
        tools.Controls.Add(_collapseButton);
        tools.Controls.Add(_clearButton);
        tools.Controls.Add(_removeButton);
        tools.Controls.Add(_downButton);
        tools.Controls.Add(_upButton);
        tools.Controls.Add(_sortButton);

        header.Controls.Add(viewRow, 0, 0);
        header.Controls.Add(_countLabel, 1, 0);
        header.Controls.Add(tools, 2, 0);

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
            // RENOMBRAR SOLO CUANDO SE PIDE. Con esto en true, un segundo clic sobre una fila
            // ya marcada abría la cajita de edición él solo: bastaba volver a pulsar un
            // archivo (o empezar a arrastrarlo despacio) para que XoPy diera por renombrado
            // algo que nadie había tocado. Ahora se enciende en F2 / «Renombrar» y se apaga
            // en cuanto se termina.
            LabelEdit = false,
            GridLines = false
        };
        _list.Columns.Add(_settings.ShowFullPath ? "Archivo y ruta" : "Nombre", 380);
        // «Subcarpeta», no «Destino»: la carpeta de destino sale una sola vez, en la cabecera.
        _list.Columns.Add("Subcarpeta", 150);
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

        // La franja de cabecera que sobra a la derecha de la última columna la pinta
        // Windows de blanco; este enganche la repinta siempre (defecto de la barra blanca).
        _headerTail = new ListHeaderTail(_list, _palette.SurfaceAlt, _palette.Border);

        _list.DrawColumnHeader += ListDrawColumnHeader;
        _list.DrawItem += (_, e) => { if (_list.View != View.Details) e.DrawDefault = true; };
        _list.DrawSubItem += ListDrawSubItem;
        // Las columnas se ajustan al ancho de la ventana, no al de la lista:
        // así la aparición de la barra de desplazamiento no provoca un bucle de redimensionado.
        Resize += (_, _) => ScheduleColumnUpdate();
        Shown += (_, _) => ScheduleColumnUpdate();
        // Cualquier cambio de ancho de la LISTA (no solo de la ventana) vuelve a repartir
        // las columnas: plegar la columna de listas, mostrar u ocultar la barra de menús o
        // que aparezca la barra de desplazamiento la ensanchan o la estrechan, y el hueco
        // sobrante a la derecha de la última columna se pintaba de blanco.
        _list.ClientSizeChanged += (_, _) => ScheduleColumnUpdate();
        // Y si el usuario arrastra el borde de una columna a mano, el hueco que deje se
        // reparte igual: antes quedaba a la derecha una franja sin columna (blanca).
        _list.ColumnWidthChanged += (_, _) => ScheduleColumnUpdate();
        _list.KeyDown += ListKeyDown;
        _list.DoubleClick += ListDoubleClick;
        _list.AfterLabelEdit += ListAfterLabelEdit;
        _list.BeforeLabelEdit += (_, e) => { if (!_renameRequested) e.CancelEdit = true; };
        _list.MouseDown += ListMouseDown;
        _list.ItemDrag += ListItemDrag;
        _list.DragEnter += ListDragEnter;
        _list.DragOver += ListDragEnter;
        _list.DragDrop += ListDragDrop;
        _list.SelectedIndexChanged += (_, _) =>
        {
            UpdateButtons();
            ShowSelectedError();
            UpdateHighlightChain();
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

    /// <summary>Pestañita plana de la cabecera de la lista («En cola» / «Finalizados»).</summary>
    private Button ViewTab(string text, Action click)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            // 98 px: con 116 las dos pestañitas se comían el sitio del recuento, que salía
            // recortado («4 archivo(s) · …»).
            Size = new Size(98, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = _palette.Raised,
            ForeColor = _palette.Muted,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Margin = new Padding(0, 0, 6, 0),
            TabStop = false,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = _palette.Border;
        button.Click += (_, _) => click();
        return button;
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
        _listMenu.Items.Add("Llevar al principio de la lista", null, (_, _) => MoveSelectedToEdge(top: true));
        _listMenu.Items.Add("Llevar al final de la lista", null, (_, _) => MoveSelectedToEdge(top: false));

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
        // Ordenar desde el propio menú del botón derecho, con LAS MISMAS opciones que el
        // menú Cola y el botón «Ordenar» de la lista (ver FillSortItems).
        var sortItem = new ToolStripMenuItem("Ordenar la lista");
        FillSortItems(sortItem.DropDownItems);
        _listMenu.Items.Add(sortItem);
        _fullPathMenuItem = new ToolStripMenuItem("Mostrar la ruta completa")
        {
            CheckOnClick = true,
            Checked = _settings.ShowFullPath
        };
        _fullPathMenuItem.Click += (_, _) => ToggleFullPath();
        _listMenu.Items.Add(_fullPathMenuItem);
        _listMenu.Items.Add("Mostrar los hashes", null, (_, _) => ToggleHashes());
    }

    /// <summary>
    /// Enseña u oculta la ruta completa del origen delante del nombre de cada elemento de
    /// primer nivel. Encendido de fábrica: el usuario quería ver de dónde sale cada archivo
    /// sin tener que pasar el ratón por encima.
    /// </summary>
    private void ToggleFullPath()
    {
        _settings.ShowFullPath = !_settings.ShowFullPath;
        _store.Save(_settings);
        if (_fullPathMenuItem is not null) _fullPathMenuItem.Checked = _settings.ShowFullPath;
        if (_list is not null)
        {
            _list.Columns[0].Text = _settings.ShowFullPath ? "Archivo y ruta" : "Nombre";
            _list.Invalidate();
        }
        SetStatus(_settings.ShowFullPath
            ? "La lista muestra la ruta completa de cada origen."
            : "La lista muestra solo el nombre.");
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
        foreach (var control in new Control?[] { _list, _logBox, _targetList, _jobsList, _optionsScroll, _statusScroll })
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

        // NUNCA se abre maximizada. El usuario lo pidió expresamente: XoPy arranca en su
        // ventana normal (el tamaño que tenía la última vez, si cabe en la pantalla) y
        // maximiza quien quiera, cuando quiera.
        WindowState = FormWindowState.Normal;
        if (_compactView)
        {
            // VISTA REDUCIDA AL ARRANCAR: el alto tiene que ser EXACTAMENTE el de lo que se
            // ve (barra + cabecera). ApplyCompactView solo encogía la ventana cuando el
            // usuario pulsaba el botón («persist»), así que al abrir el programa la ventana
            // conservaba su alto grande y quedaba media pantalla negra debajo: ese es el
            // hueco que el usuario señala en cada captura.
            var compactHeight = CompactWindowHeight();
            var compactWidth = Math.Max(560, Math.Min(
                _settings.WindowWidth > 400 ? _settings.WindowWidth : Width, workingArea.Width));
            MinimumSize = new Size(560, compactHeight);
            try { Size = new Size(compactWidth, compactHeight); }
            catch { }
        }
        else if (_settings.WindowWidth > 400 && _settings.WindowHeight > 300)
        {
            var width = Math.Min(_settings.WindowWidth, workingArea.Width);
            var height = Math.Min(_settings.WindowHeight, workingArea.Height);
            try { Size = new Size(width, height); }
            catch { }
        }
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
        var finished = false;
        var folder = Path.Combine(Path.GetTempPath(), "xopy-colatest");

        // Red de seguridad: si algún paso se queda colgado (un escaneo de carpeta esperando
        // al hilo de la interfaz, por ejemplo), a los 45 s se guarda lo conseguido hasta ese
        // momento y se sale con error. Antes la prueba moría sin dejar informe y no había
        // manera de saber en qué línea se había parado.
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(45000);
            if (finished) return;
            try
            {
                var partial = report.ToList();
                partial.Add("error=la prueba se quedó colgada");
                partial.Add("resultado=FALLO");
                File.WriteAllLines(reportPath, partial);
            }
            catch { }
            Environment.Exit(1);
        })
        { IsBackground = true };
        watchdog.Start();

        try
        {
            Directory.CreateDirectory(folder);
            for (var i = 1; i <= 3; i++)
                File.WriteAllBytes(Path.Combine(folder, $"archivo{i}.bin"), new byte[i * 1024]);

            // XoPy ARRANCA EN VISTA REDUCIDA, y en ese modo la lista y sus botones (↑ ↓
            // quitar, ordenar) están escondidos. «PerformClick» no hace nada sobre un botón
            // que no se ve, así que la prueba daba «el boton subir no movio nada» aunque el
            // reordenado funcionase. Lo primero que hace la prueba es, por tanto, lo mismo
            // que haría el usuario: mostrar la lista.
            ApplyCompactView(false, persist: false);

            // ── Rutas de destino que antes daban «Carpeta de destino no válida» ───────
            var sucia = "  \"" + folder + "\\destino \"  ";
            var limpia = PathTools.CleanFolder(sucia);
            report.Add("ruta_limpia=" + limpia);
            if (!string.Equals(limpia, Path.Combine(folder, "destino"), StringComparison.OrdinalIgnoreCase))
            { report.Add("error=no se limpiaron las comillas ni el espacio final del destino"); ok = false; }
            if (!string.Equals(PathTools.CleanFolder("C:/fotos//2025"), @"C:\fotos\2025", StringComparison.OrdinalIgnoreCase))
            { report.Add("error=no se normalizaron las barras del destino"); ok = false; }
            if (PathTools.FirstForbiddenChar(@"C:\fotos|raras") is null)
            { report.Add("error=no se detectó un carácter prohibido en la ruta"); ok = false; }
            if (PathTools.FirstForbiddenChar(@"C:\fotos\2025") is not null)
            { report.Add("error=se dio por mala una ruta correcta"); ok = false; }

            // ── Línea de comandos ROTA POR LAS COMILLAS ───────────────────────────────
            // Pegar en la raíz de un disco mandaba «--to "E:\"»: la barra final escapa la
            // comilla de cierre y Windows entrega un único argumento con todo pegado
            // («E:" --lista C:\...\xopXXXX.tmp»). Salía «Carpeta de destino no válida» con
            // la orden entera dentro. Se comprueba que XoPy la desenreda.
            var listaFalsa = Path.Combine(folder, "lista-prueba.txt");
            File.WriteAllLines(listaFalsa, new[] { Path.Combine(folder, "archivo1.bin") });
            var rescatada = StartupRequest.Parse(new[] { "--copy", "--to", "E:\" --lista " + listaFalsa });
            report.Add($"rescate_comillas={rescatada.Destination}|archivos={rescatada.Paths.Count}");
            if (!string.Equals(rescatada.Destination, "E:\\", StringComparison.OrdinalIgnoreCase)
                || rescatada.Paths.Count != 1)
            { report.Add("error=no se rescató la carpeta de una línea de comandos mal entrecomillada"); ok = false; }

            EnsureDefaultList();
            _settings.Destination = Path.Combine(folder, "destino");
            Directory.CreateDirectory(_settings.Destination);
            _destination.Text = _settings.Destination;

            var paths = Enumerable.Range(1, 3).Select(i => Path.Combine(folder, $"archivo{i}.bin")).ToList();
            WaitForQueueTask(AddPathsAsync(paths, askDestination: false));

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

            // ── «Los más grandes primero» CRUZANDO CARPETAS ──────────────────────────
            // Un archivo grande metido en una carpeta tiene que adelantar a los sueltos más
            // pequeños. Antes no pasaba: al estar la carpeta plegada, la lista volvía a
            // juntar su contenido y deshacía el orden por tamaño (y la copia iba igual de
            // mal ordenada que la lista).
            var pesada = Path.Combine(folder, "pesada");
            Directory.CreateDirectory(pesada);
            File.WriteAllBytes(Path.Combine(pesada, "grande.bin"), new byte[9 * 1024]);
            ClearList();
            WaitForQueueTask(AddPathsAsync(new[] { pesada, Path.Combine(folder, "archivo1.bin") },
                askDestination: false));
            SortAllFilesBySize(invert: true);
            var conCarpeta = _list.Items.Cast<ListViewItem>()
                .Select(row => (row.Tag as TransferItem)?.Name ?? "?")
                .Where(name => name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)).ToList();
            report.Add("tamano_cruzando_carpetas=" + string.Join(",", conCarpeta));
            if (conCarpeta.FirstOrDefault() != "grande.bin")
            { report.Add("error=el archivo grande de dentro de una carpeta no quedó el primero"); ok = false; }
            ClearList();
            WaitForQueueTask(AddPathsAsync(
                Enumerable.Range(1, 3).Select(i => Path.Combine(folder, $"archivo{i}.bin")).ToList(),
                askDestination: false));

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
            WaitForQueueTask(AddPathsAsync(new[]
            {
                Path.Combine(rootA, "a1.bin"), Path.Combine(rootA, "a2.bin"), Path.Combine(rootB, "b1.bin")
            }, askDestination: false));

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
            WaitForQueueTask(AddPathsAsync(new[]
            {
                Path.Combine(rootA, "a1.bin"), Path.Combine(rootB, "b1.bin"), Path.Combine(rootA, "a2.bin")
            }, askDestination: false));
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
            WaitForQueueTask(AddPathsAsync(new[] { Path.Combine(rootA, "a1.bin"), Path.Combine(rootA, "a2.bin") },
                askDestination: false));
            {
                var beforeEdit = _items.Count(item => item.Kind == ItemKind.File);
                var victim = _list.Items.Cast<ListViewItem>().Select(row => row.Tag as TransferItem)
                    .OfType<TransferItem>().FirstOrDefault();
                if (victim is not null) RemoveItems(new[] { victim });
                var afterRemove = _items.Count(item => item.Kind == ItemKind.File);
                report.Add($"quitar_copiando={beforeEdit}->{afterRemove}");
                if (afterRemove != beforeEdit - 1)
                { report.Add("error=no se pudo quitar una fila durante la copia"); ok = false; }

                WaitForQueueTask(AddPathsAsync(new[] { Path.Combine(rootB, "b1.bin") }, askDestination: false));
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

            // Prueba del ÁRBOL: carpetas con archivos dentro, orden libre entre carpetas,
            // cabeceras repetidas y plegado. No toca el disco (son rutas inventadas).
            var arbol = QueueSelfTest();
            report.Add("arbol=" + arbol.Replace("\r", string.Empty).Replace("\n", " | "));
            if (!arbol.StartsWith("OK", StringComparison.Ordinal)) ok = false;
        }
        catch (Exception ex)
        {
            report.Add("excepcion=" + ex.GetType().Name + ": " + ex.Message);
            ok = false;
        }

        finished = true;
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
            WaitForQueueTask(AddPathsAsync(files, askDestination: false));

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
            WaitForQueueTask(AddPathsAsync(Directory.GetFiles(source2, "*.bin"), askDestination: false));
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

    /// <summary>
    /// Espera a una tarea de la lista SIN congelar la ventana. Añadir una carpeta lanza un
    /// escaneo en segundo plano que, al terminar, necesita volver al hilo de la interfaz;
    /// si aquí se bloquease ese hilo con GetResult(), el escaneo no podría continuar nunca
    /// y la prueba se quedaba colgada hasta que el CI la mataba (sin dejar informe).
    /// </summary>
    private static void WaitForQueueTask(Task task, int milliseconds = 20000)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (!task.IsCompleted && Environment.TickCount64 < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        if (!task.IsCompleted) throw new TimeoutException("El análisis de la lista no terminó a tiempo.");
        task.GetAwaiter().GetResult();
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

                case "compact":
                    // La ventana pequeña, tal cual la ve el usuario al abrir XoPy.
                    ApplyCompactView(true, persist: false);
                    // Y con su alto real: si la ventana se queda con el alto grande, la
                    // foto enseña el hueco negro que precisamente hay que vigilar.
                    try { Bounds = new Rectangle(Left, Top, Math.Max(Width, 560), CompactWindowHeight()); }
                    catch { }
                    Application.DoEvents();
                    Update();
                    SaveWindowPng(target);
                    break;

                case "advanced":
                    // Regresión del informe: «Opciones avanzadas» lanzaba
                    // «No se puede agregar 'Button' a 'TabControl'». Se abre de verdad y se
                    // pulsa el botón; si vuelve a fallar, la captura lo muestra.
                    CaptureAdvancedPng(target);
                    break;

                case "conflict":
                    // La ventana REAL de conflicto (ConflictDialog), no el cuadro de texto
                    // antiguo: así la captura enseña lo que de verdad ve el usuario.
                    CaptureDialogPng(() => new ConflictDialog(_palette, new ConflictPrompt(
                        @"C:\xopytest\informe.pdf", @"C:\xopytest\destino\informe.pdf",
                        10_240, 7_168,
                        new DateTime(2026, 11, 20, 18, 42, 0), new DateTime(2025, 4, 3, 9, 15, 0))), target);
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
        // Modo seguro: ni se toca el registro ni la integración con el Explorador.
        if (_startupRequest.SafeMode)
        {
            FileLog.Write("Modo seguro: no se aplica la integración con el Explorador.");
            SetStatus("Modo seguro: XoPy funciona sin integración con el Explorador ni interceptor de Ctrl+V.");
            return;
        }
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
    /// <summary>Marca de «gancho de teclado puesto». Si sobrevive a la sesión es que XoPy
    /// no cerró bien con el gancho activo (se colgó o lo mataron).</summary>
    private string HookGuardPath => Path.Combine(_store.DataFolder, "gancho.marca");

    /// <summary>
    /// Instala el interceptor de Ctrl+V con un fusible: un gancho global de teclado que se
    /// porte mal puede dejar TODO el equipo sin responder, y entonces el usuario no puede
    /// ni abrir XoPy para desactivarlo. Si dos arranques seguidos terminan sin retirarlo,
    /// no se vuelve a poner y se avisa; se puede reactivar desde Preferencias.
    /// </summary>
    private void InstallShellHookIfNeeded()
    {
        if (_shellHookInstalled || _pasteHook is null) return;
        if (_startupRequest.NoShellHook || _startupRequest.SafeMode)
        {
            FileLog.Write("Modo seguro: no se instala el interceptor de Ctrl+V.");
            return;
        }
        try
        {
            var fallos = 0;
            if (File.Exists(HookGuardPath) && int.TryParse(File.ReadAllText(HookGuardPath).Trim(), out var leidos))
                fallos = leidos;
            if (fallos >= 2)
            {
                FileLog.Write($"Interceptor de Ctrl+V desactivado: {fallos} arranques sin cierre limpio.");
                SetStatus("El interceptor de Ctrl+V se ha desactivado solo porque la sesión anterior no cerró bien. " +
                          "Puedes volver a activarlo en Preferencias → Integración con el Explorador.");
                _settings.ReplaceWindowsCopier = false;
                try { _store.Save(_settings); } catch { }
                try { File.Delete(HookGuardPath); } catch { }
                return;
            }
            Directory.CreateDirectory(_store.DataFolder);
            File.WriteAllText(HookGuardPath, (fallos + 1).ToString());

            _pasteHook.Install();
            _shellHookInstalled = true;
        }
        catch (Exception ex)
        {
            FileLog.Write("No se pudo instalar el hook de pegado: " + ex.Message);
        }
    }

    /// <summary>Cierre limpio: se borra la marca, así el fusible no salta sin motivo.</summary>
    private void ClearHookGuard()
    {
        try { if (File.Exists(HookGuardPath)) File.Delete(HookGuardPath); } catch { }
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

    /// <summary>
    /// Texto del icono del área de notificación. Se muestra «(reemplazo del copiador de
    /// Windows)», igual que SuperCopier, para que se sepa de un vistazo qué hace XoPy.
    /// </summary>
    internal const string TrayBaseText = "XoPy (reemplazo del copiador de Windows)";

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
        // Igual que SuperCopier: el icono junto al reloj dice para qué sirve el programa.
        // NotifyIcon.Text admite como mucho 63 caracteres.
        _tray.Text = TrayBaseText;
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
            // Modo de ventana de Preferencias: 0 reducida, 1 completa (por defecto) y
            // 2 automático. «Automático» usa la reducida SOLO cuando XoPy se abre para copiar
            // algo (pegado, menú contextual, --to): ahí interesa una ventanita de progreso.
            // Abierto a mano se ve la ventana entera, porque la vista reducida esconde las
            // pestañas y no habría forma de llegar a Destino, Opciones, Estado ni Registro.
            var startingTransfer = _startupRequest.Paths.Count > 0 || _startupRequest.Operation is not null;
            var compactOnStart = _settings.WindowMode switch
            {
                0 => true,
                2 => startingTransfer,
                _ => false
            };
            // En modo captura nunca se usa la vista reducida: las imágenes de documentación
            // tienen que enseñar la ventana completa, con sus pestañas. La excepción es
            // «--screenshot-compact», que existe justamente para fotografiar la ventana
            // pequeña con la que XoPy arranca.
            if (_startupRequest.Screenshot)
                compactOnStart = string.Equals(_startupRequest.ScreenshotMode, "compact", StringComparison.Ordinal);
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
        if (!isStartup)
        {
            // FORMATO MÍNIMO AL PEGAR. Lo que llega de fuera (Ctrl+V, el menú del
            // Explorador, arrastrar y soltar) es una copia, y para una copia el usuario
            // pidió la ventanita de progreso. Si XoPy ya estaba abierto con la lista
            // desplegada, se recoge ANTES de enseñarla: así no se ve primero la ventana
            // grande y después el salto a la pequeña (el parpadeo del informe).
            var traeTrabajo = request.Paths.Count > 0 || request.PasteFromClipboard
                              || request.Operation is not null;
            var quiereReducida = _settings.WindowMode switch
            {
                0 => true,
                2 => traeTrabajo,
                _ => false
            };
            if (quiereReducida && traeTrabajo && !_compactView) ApplyCompactView(true, persist: false);
            RestoreWindow();
        }

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
            // Mismo cuidado que arriba: con la ventana a medio construir esto era un
            // «Object reference not set» y la pantalla se llenaba de cuadros de error
            // encima de una copia que ya estaba corriendo.
            if (_destination is not null) _destination.Text = request.Destination!;
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
            else if (request.AskDestination || string.IsNullOrWhiteSpace(_destination?.Text?.Trim()))
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
    /// <summary>
    /// Cierra una lista de copia. Con <paramref name="ask"/> en falso no sale el cuadro de
    /// confirmación: es lo que hace la tecla Mayús para limpiar varias listas seguidas.
    /// Cerrar una lista NUNCA borra archivos del disco.
    /// </summary>
    private void DeleteList(TransferList? list, bool ask = true)
    {
        if (list is null) return;
        // Solo se bloquea si la lista que se quiere cerrar es LA QUE SE ESTÁ COPIANDO: las
        // demás se pueden cerrar aunque el motor esté trabajando en otra.
        if (_engine.IsRunning && list == _runningList)
        {
            SetStatus("Esta lista se está copiando ahora mismo: detén la copia para cerrarla.");
            return;
        }

        if (ask)
        {
            var answer = MessageBox.Show(this,
                $"¿Cerrar la lista «{list.DisplayName}»?\n\n" +
                "Esto sólo la quita del panel de la izquierda. No borra ningún archivo del disco.\n\n" +
                "Atajo: con la tecla Mayús pulsada se cierra sin preguntar.",
                "XoPy", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
        }

        var name = list.DisplayName;
        var wasActive = list == _activeList;
        _lists.Remove(list);
        if (_lists.Count == 0)
        {
            _activeList = null;
            EnsureDefaultList();
            ResetVisualOrder();
            RebuildRows();
            UpdateFooter();
        }
        else if (wasActive)
        {
            SwitchToList(_lists[0]);
        }
        _jobsSignature = string.Empty;
        UpdateJobs();
        SetStatus($"Lista «{name}» cerrada. No se ha borrado ningún archivo del disco.");
    }

    /// <summary>
    /// Cambia la lista que se está viendo. Se puede hacer CON LA COPIA EN MARCHA: el motor
    /// sigue trabajando sobre su lista (_runningList) y la ventana simplemente enseña otra.
    /// Antes se forzaba a volver a la lista activa, así que era imposible preparar una copia
    /// nueva mientras había una en curso.
    /// </summary>
    private void SwitchToList(TransferList? list)
    {
        if (list is null) return;

        _activeList = list;
        _items = list.Items;
        ResetVisualOrder();

        // La caja del destino enseña SIEMPRE el destino de esta lista, aunque esté vacío.
        // Si no, al pasar a una lista sin destino se quedaba a la vista el de la lista
        // anterior y la copia salía hacia esa otra carpeta sin preguntar nada.
        var listDestination = list.Destination ?? string.Empty;
        if (!string.Equals(_destination.Text, listDestination, StringComparison.Ordinal))
            _destination.Text = listDestination;
        list.UpdatedAt = DateTime.Now;
        RefreshListSelector();
        UpdateTargetTab();
        UpdateButtons();
        RebuildRows();
        UpdateJobs();

        var running = _engine.IsRunning && list == _runningList;
        var files = list.Items.Count(item => item.Kind != ItemKind.Folder);
        SetStatus(running
            ? $"Lista «{list.DisplayName}» · copiando ahora ({Format.Files(files)})"
            : _engine.IsRunning
                ? $"Lista «{list.DisplayName}» · la copia en marcha es otra lista"
                : $"Lista «{list.DisplayName}» · {Format.Files(files)}");
    }

    private bool _refreshingSelector;

    /// <summary>
    /// Mantiene al día el panel TRABAJOS, que es donde se eligen las listas de copia.
    /// </summary>
    private void RefreshListSelector()
    {
        if (_refreshingSelector) return; // evita la reentrada SwitchToList → refresco → …
        _refreshingSelector = true;
        try
        {
            if (_activeList is null || !_lists.Contains(_activeList))
            {
                if (_lists.Count > 0) _activeList = _lists[0];
            }
            UpdateJobs();
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
    /// <summary>
    /// Decide SOLA dónde van los archivos que llegan de fuera (pegar con Ctrl+V o con
    /// «Pegar» del menú del Explorador) y no enseña ningún cuadro: es el comportamiento de
    /// fábrica que pidió el usuario — pulsar Pegar y que empiece con lo que hay.
    ///   · lista activa vacía          → se usa esa misma lista
    ///   · mismo destino que la activa → se añaden al final y sigue la copia
    ///   · destino distinto            → lista NUEVA, para no mezclar dos destinos
    /// Quien prefiera que pregunte lo marca en Preferencias («Preguntar al pegar…»).
    /// </summary>
    private (TransferList List, bool ChangeDestination)? AutoListDecision(string detectedDestination)
    {
        EnsureDefaultList();
        var active = _activeList;
        if (active is null || active.Items.Count == 0)
        {
            if (active is null) return null;
            return (active, true);
        }

        var sameDestination = active.Destination.Length > 0
            && string.Equals(active.Destination, detectedDestination, StringComparison.OrdinalIgnoreCase);
        if (sameDestination) return (active, false);

        // SIN DESTINO CONOCIDO (archivos arrastrados a la ventana): van a la lista que se
        // está mirando, que es donde el usuario los ha soltado. Antes se creaba una lista
        // nueva en silencio y los archivos «desaparecían» de la vista.
        if (string.IsNullOrWhiteSpace(detectedDestination)) return (active, false);

        var fresh = new TransferList
        {
            Name = $"Lista de copia {_lists.Count + 1}",
            Destination = detectedDestination
        };
        _lists.Add(fresh);
        return (fresh, true);
    }

    private (TransferList List, bool ChangeDestination)? AskListDecision(
        IReadOnlyList<string> files, string detectedDestination, bool move)
    {
        var dlg = new Form
        {
            Text = "Añadir archivos a la copia",
            BackColor = _palette.Canvas,
            ForeColor = _palette.Text,
            StartPosition = FormStartPosition.CenterParent,
            // Redimensionable: las rutas largas se leen enteras ensanchando el cuadro, que es
            // mejor que una barra de desplazamiento horizontal (la que salía blanca abajo).
            FormBorderStyle = FormBorderStyle.Sizable,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            MinimumSize = new Size(520, 210),
            // El tamaño DEFINITIVO se calcula abajo con las opciones que haya: este cuadro
            // enseña dos o tres líneas y antes ocupaba media pantalla.
            ClientSize = new Size(560, 240),
        };

        var verb = move ? "mover" : "copiar";
        var destinoTexto = string.IsNullOrWhiteSpace(detectedDestination)
            ? "sin detectar — se preguntará al empezar"
            : detectedDestination;
        var message = new Label
        {
            Text = $"{Format.Files(files.Count)} {Format.Plural(files.Count, "listo", "listos")} para {verb}.\nDestino detectado: {destinoTexto}",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 14, 16, 0),
            ForeColor = _palette.Text,
            BackColor = _palette.Canvas,
            Font = new Font("Segoe UI", 9.5f),
        };

        // Una columna que ocupa TODO el ancho y crece hacia abajo. Antes era un
        // FlowLayoutPanel con desplazamiento en las dos direcciones: las líneas largas
        // sacaban una barra horizontal que el tema oscuro pintaba de blanco (la franja
        // blanca del fondo del cuadro) y además cortaban el texto por la derecha.
        var options = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            Padding = new Padding(12, 8, 12, 8),
            BackColor = _palette.Canvas,
            AutoSize = false,
        };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var optionTips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 400 };

        var defaultDestination = detectedDestination;
        var sameDestination = _activeList is not null
            && _activeList.Destination.Length > 0
            && string.Equals(_activeList.Destination, detectedDestination, StringComparison.OrdinalIgnoreCase);

        TransferList? chosen = null;
        Action? RefreshAddButton = null;

        // Las casillas se crean SIN marcar y se añaden al cuadro; la marca se pone al final,
        // en una sola de ellas. Marcarlas al construirlas (antes de tener padre) saltaba el
        // «solo una a la vez» de Windows y salían DOS opciones marcadas a la vez.
        var choices = new List<RadioButton>();

        RadioButton AddOption(TransferList? list, string text, bool bold = false)
        {
            var rb = new RadioButton
            {
                Text = text,
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = 30,
                Margin = new Padding(4, 3, 4, 3),
                ForeColor = _palette.Text,
                BackColor = _palette.Canvas,
                Font = new Font("Segoe UI", 9f, bold ? FontStyle.Bold : FontStyle.Regular),
                // El texto que no quepa se corta con puntos suspensivos y se lee entero en
                // el globo de ayuda: así el cuadro nunca saca barra de desplazamiento lateral.
                AutoEllipsis = true,
                UseMnemonic = false
            };
            optionTips.SetToolTip(rb, text);
            rb.CheckedChanged += (_, _) =>
            {
                if (!rb.Checked) return;
                chosen = list;
                RefreshAddButton?.Invoke();
            };
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            options.Controls.Add(rb, 0, choices.Count);
            choices.Add(rb);
            return rb;
        }

        // LISTA NUEVA primero y marcada por defecto: es lo que menos sorprende y lo que el
        // usuario pide cuando arrastra algo mientras ya hay una copia en marcha.
        var nueva = new TransferList { Name = $"Lista de copia {_lists.Count + 1}", Destination = detectedDestination };
        var newOption = AddOption(nueva, $"Abrir en una lista NUEVA (destino: {destinoTexto})", bold: true);
        chosen = nueva;
        RadioButton? preferred = null;

        // Enumerable.Reverse: List<T>.Reverse() invierte la lista EN EL SITIO y devuelve void
        // (reordenaría de verdad las listas del usuario).
        foreach (var list in Enumerable.Reverse(_lists))
        {
            var same = string.Equals(list.Destination, detectedDestination, StringComparison.OrdinalIgnoreCase);
            // Cada lista dice EN QUÉ ESTADO está: copiando ahora, en pausa, esperando turno o
            // ya terminada. Así se distingue a simple vista dónde se están metiendo los
            // archivos, que es justo lo que pedía el usuario.
            var running = _engine.IsRunning && list == _runningList;
            var estado = TransferList.StageText(list.Stage(running, running && _engine.IsPaused));
            var option = AddOption(list,
                $"Añadir a «{list.DisplayName}»  [{estado}]  ·  destino: {list.Destination}"
                + (same ? "  · mismo destino" : string.Empty));
            if (list == _activeList && sameDestination) preferred = option;
            if (!same)
            {
                AddOption(list,
                    $"Añadir a «{list.DisplayName}» cambiando su destino a: {detectedDestination}");
            }
        }

        // UNA SOLA MARCA, y siempre después de que todas tengan padre.
        foreach (var rb in choices) rb.Checked = false;
        (preferred ?? newOption).Checked = true;

        // VENTANA A MEDIDA: alto = texto de arriba + una fila por opción + los botones.
        // Nada de 400 px fijos para enseñar dos líneas.
        var optionsHeight = Math.Min(choices.Count * 36 + 16, 340);
        dlg.ClientSize = new Size(560, 56 + optionsHeight + 52);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(12, 8, 12, 8),
            BackColor = _palette.Surface,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var cancel = Ui.DialogButton("Cancelar", _palette);
        cancel.Width = 110;
        cancel.DialogResult = DialogResult.Cancel;
        var add = Ui.DialogButton($"Añadir a «{(chosen?.DisplayName ?? "la lista")}»", _palette, primary: true);
        add.Width = 230;
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

        // Si XoPy estaba en la bandeja hay que sacarlo: un cuadro de diálogo colgando de una
        // ventana oculta es justo lo que hace que parezca que el pegado «no hizo nada».
        if (!Visible || WindowState == FormWindowState.Minimized)
        {
            try { RestoreWindow(); } catch { }
        }

        // Este cuadro se construye a mano (no hereda de los de Dialogs.cs): hay que pedirle
        // a Windows la barra de título oscura y repasar los colores igual que en los demás.
        dlg.HandleCreated += (_, _) => Ui.DarkFrame(dlg, _palette);
        dlg.Shown += (_, _) =>
        {
            Ui.PaintAll(dlg, _palette);
            Ui.DarkScrollbars(dlg, _palette);
        };
        using (dlg)
        using (optionTips)
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return null;
        }
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

        // PEGAR EMPIEZA SOLO. De fábrica no se enseña ningún cuadro al recibir archivos de
        // fuera (pegado, menú del Explorador, segunda instancia): XoPy elige la lista por su
        // cuenta y arranca. Ese «paso intermedio» era justo lo que el usuario no quería.
        // Quien prefiera decidir cada vez marca «Preguntar al pegar…» en Preferencias.
        var activeListEmpty = _activeList is null || _activeList.Items.Count == 0;
        // En modo captura no se abre ningún cuadro: bloquearía la generación de la imagen.
        // En modo captura no se toca la lista activa: la imagen tiene que salir con los
        // archivos que se le han pasado, no con una lista nueva vacía.
        if (!activeListEmpty && !_startupRequest.Screenshot && (_lists.Count >= 1 || validDetected))
        {
            var decision = _settings.AskWhenPasting
                ? AskListDecision(files, detectedDestination!, move)
                : AutoListDecision(detectedDestination ?? string.Empty);
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

        SwitchToList(target);
        if (changeDestination && !string.IsNullOrEmpty(resolvedDestination) && target is not null)
        {
            target.Destination = resolvedDestination;
            _destination.Text = resolvedDestination;
            UpdateTargetTab();
        }

        // El destino ya lo eligió el Explorer/--to: no se vuelve a preguntar. Pero si los
        // archivos llegan ARRASTRADOS a la ventana no hay destino ninguno, y entonces sí hay
        // que preguntar a dónde van antes de empezar a copiar (defecto: se copiaba a la
        // carpeta de la lista anterior sin decir nada).
        var needsFolder = !validDetected
            && target is not null
            && string.IsNullOrWhiteSpace(target.Destination)
            && !_startupRequest.Screenshot;
        await AddPathsAsync(files, askDestination: false);
        if (needsFolder && _activeList is { } pending && string.IsNullOrWhiteSpace(pending.Destination))
        {
            PromptDestinationForList(pending, sayNothingIfCancelled: true);
            if (string.IsNullOrWhiteSpace(pending.Destination))
            {
                SetStatus("Archivos añadidos. Elige la carpeta de destino y pulsa Copiar.");
                return;
            }
        }

        // En modo captura solo se rellena la lista para la imagen: no se abre el selector de
        // carpeta ni se arranca una copia (eso bloqueaba la captura y salía vacía).
        if (_startupRequest.Screenshot) return;

        var operation = move ? OperationType.Move : OperationType.Copy;
        if (!_engine.IsRunning)
        {
            StartTransfer(operation);
        }
        else if (target is not null && target != _runningList)
        {
            // Ya hay una copia en marcha y esto es una lista NUEVA: se pone a esperar turno y
            // arranca sola al terminar la de delante. Sin preguntar otra vez: la elección ya
            // se hizo en el cuadro anterior.
            target.Queued = true;
            target.QueuedOperation = operation;
            UpdateJobs();
            SetStatus($"«{target.DisplayName}» esperará turno: empezará al terminar la copia actual.");
        }
        else
        {
            _followUpOperation = operation;
            SetStatus("Se copiarán en cuanto termine la transferencia en curso.");
        }
    }

    private void PromptDestinationAndStart(OperationType operation)
    {
        // Si la ventana está en la bandeja, se saca antes de preguntar la carpeta.
        if (!Visible || WindowState == FormWindowState.Minimized)
        {
            try { RestoreWindow(); } catch { }
        }

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

        // Igual que al arrastrar: si la lista activa ya tiene algo hay que decidir dónde van
        // los archivos nuevos. De fábrica lo decide XoPy solo (sin cuadro) y la copia
        // arranca; solo pregunta si está marcado «Preguntar al pegar…» en Preferencias.
        if (!string.IsNullOrWhiteSpace(destination) && Directory.Exists(destination)
            && _activeList is not null && _activeList.Items.Count > 0)
        {
            var decision = _settings.AskWhenPasting
                ? AskListDecision(files, destination, move)
                : AutoListDecision(destination);
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
