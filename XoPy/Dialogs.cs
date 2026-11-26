using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace XoPy;

internal sealed class PromptDialog : Form
{
    /// <summary>
    /// Último repaso antes de que la ventana se vea: la barra de título se pinta de oscuro
    /// (Windows la hace blanca por su cuenta) y cualquier control que se haya quedado con
    /// los colores del sistema recibe los de la paleta. Es lo que quita las barras blancas.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.DarkFrame(this, _palette!);
        Ui.PaintAll(this, _palette!);
    }

    /// <summary>
    /// Ya con la ventana en pantalla: a estas alturas TODOS los controles tienen su
    /// identificador de Windows, así que es cuando se pueden oscurecer sus barras de
    /// desplazamiento nativas (en Preferencias salían blancas).
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Ui.PaintAll(this, _palette!);
        Ui.DarkScrollbars(this, _palette!);
    }

    private readonly CheckBox? _checkBox;
    private readonly Palette _palette;
    public int SelectedIndex { get; private set; }
    public bool CheckboxChecked => _checkBox?.Checked ?? false;

    public PromptDialog(Palette palette, string title, string message, string? detail, string[] buttons, string? checkboxText)
    {
        _palette = palette;
        Text = title;
        BackColor = palette.Canvas;
        ForeColor = palette.Text;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(640, 0);
        MaximumSize = new Size(640, 1400);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            BackColor = palette.Canvas,
            Padding = new Padding(20, 18, 20, 14),
            Width = 620,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            RowCount = 5
        };
        for (var row = 0; row < 5; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = new Label
        {
            Text = title,
            AutoSize = true,
            ForeColor = palette.Text,
            Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8)
        };

        var body = new Label
        {
            Text = message,
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            ForeColor = palette.Text,
            Margin = new Padding(0, 0, 0, 8)
        };

        var detailText = (detail ?? string.Empty).Replace("\r\n", "\n").Replace("\n", "\r\n");
        var detailFont = new Font("Segoe UI", 8.25f);
        // El alto se MIDE con el texto real. Antes eran 76 px fijos: el mensaje salía
        // recortado y, encima, con la barra de desplazamiento bajada, así que se leía por la
        // mitad («…bloqueado, activa Copiar archivos bloqueados en Opciones»).
        var detailHeight = 76;
        try
        {
            var measured = TextRenderer.MeasureText(detailText, detailFont, new Size(548, 0),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            detailHeight = Math.Clamp(measured.Height + 16, 48, 260);
        }
        catch { }
        var detailBox = new TextBox
        {
            Text = detailText,
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.None,
            Size = new Size(560, detailHeight),
            TabStop = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = palette.SurfaceAlt,
            ForeColor = palette.Muted,
            Font = detailFont,
            Margin = new Padding(0, 0, 0, 10)
        };
        if (string.IsNullOrEmpty(detail)) detailBox.Visible = false;

        if (!string.IsNullOrEmpty(checkboxText))
        {
            _checkBox = new CheckBox
            {
                Text = checkboxText,
                AutoSize = true,
                ForeColor = palette.Text,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 10)
            };
        }

        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = palette.Canvas,
            WrapContents = false
        };
        for (var i = buttons.Length - 1; i >= 0; i--)
        {
            var index = i;
            var button = Ui.DialogButton(buttons[i], palette, i == 0);
            button.Click += (_, _) =>
            {
                SelectedIndex = index;
                DialogResult = DialogResult.OK;
                Close();
            };
            buttonRow.Controls.Add(button);
        }

        Control checkControl = _checkBox is null
            ? new Panel { Height = 0, BackColor = Color.Transparent }
            : _checkBox;

        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(body, 0, 1);
        layout.Controls.Add(detailBox, 0, 2);
        layout.Controls.Add(checkControl, 0, 3);
        layout.Controls.Add(buttonRow, 0, 4);
        Controls.Add(layout);

        // Siempre empezando por la PRIMERA línea del detalle.
        Shown += (_, _) =>
        {
            try
            {
                detailBox.SelectionStart = 0;
                detailBox.SelectionLength = 0;
                NativeTheme.ScrollToTop(detailBox);
            }
            catch { }
            ActiveControl = null;
        };
        AcceptButton = buttonRow.Controls.Count > 0 ? buttonRow.Controls[buttonRow.Controls.Count - 1] as Button : null;
        CancelButton = buttonRow.Controls.Count > 0 ? buttonRow.Controls[0] as Button : null;
    }

    public static int Ask(IWin32Window owner, Palette palette, string title, string message, string? detail, string[] buttons)
        => Ask(owner, palette, title, message, detail, buttons, null, out _);

    public static int Ask(IWin32Window owner, Palette palette, string title, string message, string? detail, string[] buttons,
        string? checkboxText, out bool checkedState)
    {
        using var dialog = new PromptDialog(palette, title, message, detail, buttons, checkboxText);
        var result = dialog.ShowDialog(owner);
        checkedState = dialog.CheckboxChecked;
        return result == DialogResult.OK ? dialog.SelectedIndex : Math.Max(0, buttons.Length - 1);
    }
}

internal sealed class OptionsDialog : Form
{
    /// <summary>
    /// Último repaso antes de que la ventana se vea: la barra de título se pinta de oscuro
    /// (Windows la hace blanca por su cuenta) y cualquier control que se haya quedado con
    /// los colores del sistema recibe los de la paleta. Es lo que quita las barras blancas.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.DarkFrame(this, _palette!);
        Ui.PaintAll(this, _palette!);
    }

    /// <summary>
    /// Ya con la ventana en pantalla: a estas alturas TODOS los controles tienen su
    /// identificador de Windows, así que es cuando se pueden oscurecer sus barras de
    /// desplazamiento nativas (en Preferencias salían blancas).
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Ui.PaintAll(this, _palette!);
        Ui.DarkScrollbars(this, _palette!);
    }

    private readonly Palette _palette;
    private readonly TabControl _tabs;
    private Button? _advancedBack;
    private bool _advancedShown;
    private FlowLayoutPanel? _advancedRow;
    private readonly TextBox _destination;
    private readonly ChoiceButton _theme;
    private readonly CheckBox _confirmDelete;
    private readonly CheckBox _minimizeToTray;
    private readonly CheckBox _startWithWindows;
    private readonly CheckBox _startMinimized;
    private readonly CheckBox _multiThread;
    private readonly CheckBox _showMenuBar;
    private readonly CheckBox _showJobs;
    private readonly NumericUpDown _parallel;
    private readonly NumericUpDown _buffer;
    private readonly NumericUpDown _speed;
    private readonly CheckBox _preserveTimestamps;
    private readonly CheckBox _recycleBin;
    private readonly ChoiceButton _verifyAlgorithm;
    private readonly CheckBox _verifyAfterCopy;
    private readonly ChoiceButton _conflict;
    private readonly ChoiceButton _onError;
    private readonly NumericUpDown _retries;
    private readonly CheckBox _replaceCopier;
    private readonly CheckBox _askWhenBusy;
    private readonly CheckBox _askWhenPasting;
    private readonly CheckBox _showFullPath;
    private readonly CheckBox _sound;
    private readonly CheckBox _openDestination;
    private readonly CheckBox _closeWhenDone;
    private readonly CheckBox _shutdown;
    private readonly ListBox _favorites;
    private readonly CheckBox _copyLocked;
    private readonly ChoiceButton _engine;
    private readonly CheckBox _resumePartials;
    private readonly CheckBox _writeChecksums;
    private readonly NumericUpDown _securePasses;
    private readonly TextBox _completionScript;
    private readonly ListBox _extraDestinations;
    // Opciones del estilo TeraCopy (una sola pantalla, todo en español).
    private readonly CheckBox _registerCopyManager;
    private readonly CheckBox _confirmDragDrop;
    private readonly CheckBox _defaultCopyManager;
    private readonly CheckBox _onlyWhenDropOff;
    private readonly CheckBox _addExplorerMenu;
    private readonly CheckBox _verifyWithChecksum;
    private readonly CheckBox _alwaysOnTop;
    private readonly CheckBox _checkFreeSpace;
    private readonly CheckBox _systemWriteCache;
    private readonly CheckBox _notifyDone;
    private readonly CheckBox _showHashes;
    private readonly NumericUpDown _maxJobs;
    private readonly TextBox _fastDevices;
    private readonly TextBox _slowDevices;
    private readonly ChoiceButton _language;
    private readonly ChoiceButton _windowMode;

    public AppSettings Result { get; }

    public OptionsDialog(AppSettings settings, Palette palette)
    {
        _palette = palette;
        Result = CloneSettings(settings);
        _favorites = new ListBox();
        _extraDestinations = new ListBox();

        Text = "Preferencias";
        BackColor = palette.Canvas;
        ForeColor = palette.Text;
        Font = new Font("Segoe UI", 9f);
        // Ventana redimensionable, como las Preferencias de TeraCopy 4.
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = true;
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        ClientSize = new Size(Math.Min(1000, Math.Max(820, workArea.Width - 120)),
            Math.Min(740, Math.Max(520, workArea.Height - 40)));
        MinimumSize = new Size(840, 560);


        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            // FRANJA BLANCA DE LAS PESTAÑAS. Con anchos fijos sobraba sitio a la derecha de
            // cada renglón y ese hueco lo pintaba Windows de blanco (se veía una banda clara
            // encima del cuadro oscuro). «FillToRight» estira las pestañas hasta llenar el
            // renglón entero, así que no queda ni un pixel sin pintar por nosotros.
            SizeMode = TabSizeMode.FillToRight,
            ItemSize = new Size(112, 30),
            Padding = new Point(12, 6),
            DrawMode = TabDrawMode.OwnerDrawFixed,
            // EN VARIAS FILAS. Con ocho pestañas en una sola fila no cabían y Windows ponía
            // dos flechitas para ir pasándolas de una en una: no se veía dónde estaba cada
            // cosa. Ahora se ven TODAS a la vez, repartidas en dos renglones.
            Multiline = true,
            Font = new Font("Segoe UI", 9f)
        };
        _tabs.DrawItem += DrawTab;
        _tabs.HandleCreated += (_, _) => FitTabWidths();
        // La vista principal es de una sola pantalla (estilo TeraCopy); las pestañas siguen
        // existiendo solo para las opciones avanzadas, que se abren desde un botón.
        _tabs.Visible = false;

        _destination = TextBox(settings.Destination);
        _theme = new ChoiceButton(palette, new[] { "Oscuro", "Claro" }, settings.DarkTheme ? 0 : 1);
        _confirmDelete = Check("Pedir confirmación antes de eliminar", settings.ConfirmDelete);
        _minimizeToTray = Check("Minimizar en el área de notificación al cerrar la ventana", settings.MinimizeToTray);
        _startWithWindows = Check("Iniciar XoPy al encender el equipo", settings.StartWithWindows);
        // Se aclara el alcance: abrir XoPy a mano SIEMPRE muestra la ventana. Esta casilla
        // solo afecta al arranque automático con Windows.
        _startMinimized = Check("Al iniciar con Windows, arrancar en el área de notificación", settings.StartMinimized);

        _multiThread = Check("Copiar varios archivos a la vez (solo ayuda entre discos distintos)", settings.MultiThreadCopy);
        _showMenuBar = Check("Mostrar la barra de menús superior", settings.ShowMenuBar);
        _showJobs = Check("Mostrar el historial de listas de copia (se puede ocultar con el botón « de la lista)",
            settings.ShowJobs);
        _parallel = Numeric(settings.Parallelism, 1, 16, 1);
        _buffer = Numeric(settings.BufferKiB, 64, 8192, 64);
        _speed = Numeric(settings.SpeedLimitMiB, 0, 100000, 10);
        _preserveTimestamps = Check("Conservar las fechas de modificación", settings.PreserveTimestamps);
        _recycleBin = Check("Enviar a la Papelera de reciclaje al eliminar", settings.UseRecycleBin);
        _copyLocked = Check("Copiar archivos bloqueados o en uso", settings.CopyLockedFiles);
        _engine = new ChoiceButton(palette, new[] { "Nativo — CopyFileEx (recomendado)", "Robocopy — muchos archivos y redes", "Administrado — máxima compatibilidad" }, (int)settings.Engine);
        _engine.SelectedIndexChanged += (_, _) => { };
        _resumePartials = Check("Reanudar copias parciales al reintentar", settings.ResumePartials);
        _writeChecksums = Check("Crear un archivo de checksums al terminar", settings.WriteChecksumFile);
        _securePasses = Numeric(settings.SecureDeletePasses, 0, 7, 1);
        _completionScript = TextBox(settings.CompletionScript);
        _extraDestinations.Items.AddRange(settings.ExtraDestinations.Cast<object>().ToArray());

        _verifyAlgorithm = new ChoiceButton(palette, new[]
        {
            "Sin verificación", "CRC32", "MD5", "SHA-1", "SHA-256", "SHA-512"
        }, (int)settings.Verify);
        _verifyAfterCopy = Check("Verificar cada archivo después de copiarlo", settings.VerifyAfterCopy);

        _conflict = new ChoiceButton(palette, new[]
        {
            "Preguntar", "Sobrescribir siempre", "Omitir", "Cambiar nombre",
            "Sobrescribir si es más antiguo", "Sobrescribir si es más pequeño"
        }, (int)settings.Conflict);
        _onError = new ChoiceButton(palette,
            new[] { "Preguntar", "Reintentar automáticamente", "Omitir el archivo", "Detener la transferencia" },
            (int)settings.OnError);
        _retries = Numeric(settings.RetryCount, 0, 20, 1);

        _replaceCopier = Check("Sustituir el copiador de Windows: Ctrl+V y Mayús+Insert en el Explorador abren XoPy",
            settings.ReplaceWindowsCopier);
        _askWhenBusy = Check("Preguntar qué hacer si ya hay una transferencia en curso", settings.AskWhenBusy);
        // De fábrica DESMARCADA: pegar arranca directamente con lo que hay en el
        // portapapeles, sin cuadros por el medio.
        _askWhenPasting = Check("Al pegar, preguntar en qué lista van los archivos", settings.AskWhenPasting);
        _showFullPath = Check("Mostrar la ruta completa de cada archivo en la lista", settings.ShowFullPath);

        _sound = Check("Reproducir un sonido al terminar", settings.PlaySoundWhenDone);
        _openDestination = Check("Abrir la carpeta de destino al terminar", settings.OpenDestinationWhenDone);
        _closeWhenDone = Check("Cerrar XoPy al terminar", settings.CloseWhenDone);
        _shutdown = Check("Apagar el equipo al terminar", settings.ShutdownWhenDone);

        _favorites.Items.AddRange(settings.Favorites.Cast<object>().ToArray());

        // --- Opciones de la pantalla principal (equivalentes a las de TeraCopy, en español) ---
        _registerCopyManager = Check("Registrar XoPy como gestor de copia", settings.ReplaceWindowsCopier);
        _confirmDragDrop = Check("Confirmar al arrastrar y soltar", settings.ConfirmDragDrop);
        _defaultCopyManager = Check("Usar XoPy como gestor de copias predeterminado", settings.ReplaceWindowsCopier);
        _onlyWhenDropOff = Check("Solo cuando BLOQ DESPL está apagado", settings.OnlyWhenScrollLockOff);
        _addExplorerMenu = Check("Añadir al menú contextual del Explorador", settings.ExplorerIntegration);
        _verifyWithChecksum = Check("Verificar cada archivo contra una suma de comprobación", settings.VerifyAfterCopy);
        _alwaysOnTop = Check("Siempre visible", settings.AlwaysOnTop);
        _checkFreeSpace = Check("Comprobar el espacio libre", settings.CheckFreeSpace);
        _systemWriteCache = Check("Usar caché de escritura del sistema", settings.UseSystemWriteCache);
        _notifyDone = Check("Notificar al terminar", settings.PlaySoundWhenDone || settings.OpenDestinationWhenDone);
        _showHashes = Check("Mostrar las columnas de suma de comprobación", settings.ShowHashes);
        _maxJobs = Numeric(settings.Parallelism, 1, 32, 1);
        _maxJobs.Width = 62;
        _fastDevices = TextBox(settings.FastDevices);
        _fastDevices.PlaceholderText = @"Ejemplo: C:, C:\Ruta, \\Servidor\recurso";
        _slowDevices = TextBox(settings.SlowDevices);
        _slowDevices.PlaceholderText = @"Ejemplo: D:, \\PC-lento\datos";
        _language = new ChoiceButton(palette, new[] { "Español", "English" }, settings.Language == "en" ? 1 : 0);
        _language.Width = 170;
        // Modo de ventana al abrir. «Solo el progreso» esconde la lista de archivos y las
        // pestañas (quedan la barra de botones, el progreso y el pie), «Completa» enseña la
        // ventana entera y «Automático» usa la reducida solo al abrirse para copiar algo.
        // Se vuelve a la vista completa con el botón «Mostrar lista» o con F9.
        _windowMode = new ChoiceButton(palette, new[] { "Solo el progreso", "Completa", "Automático" },
            Math.Clamp(settings.WindowMode, 0, 2));
        _windowMode.Width = 170;

        BuildPages();
        _tabs.SelectedIndex = 0;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = palette.Surface,
            Padding = new Padding(14, 11, 14, 11)
        };
        var cancel = Ui.DialogButton("Cancelar", palette);
        cancel.DialogResult = DialogResult.Cancel;
        var ok = Ui.DialogButton("Guardar", palette, true);
        ok.Click += (_, _) =>
        {
            CaptureValues();
            DialogResult = DialogResult.OK;
            Close();
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = palette.Canvas,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));   // pestañas: solo avanzadas
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.Controls.Add(BuildMainSettings(), 0, 0);
        root.Controls.Add(_tabs, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    /// <summary>
    /// Con el escalado DPI del sistema, el tamaño fijado en el constructor se multiplica y
    /// el diálogo acababa más alto que la pantalla (los botones quedaban bajo la barra de
    /// tareas). Ya cargado, se recorta al área de trabajo real.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var area = Screen.FromControl(this).WorkingArea;
        var maxWidth = area.Width - 24;
        var maxHeight = area.Height - 24;
        if (Width > maxWidth || Height > maxHeight)
        {
            Width = Math.Min(Width, maxWidth);
            Height = Math.Min(Height, maxHeight);
        }
        Location = new Point(
            area.X + Math.Max(0, (area.Width - Width) / 2),
            area.Y + Math.Max(0, (area.Height - Height) / 2));
    }

    /// <summary>
    /// Pantalla principal de Preferencias, con la misma distribución que TeraCopy 4
    /// (Integración con Shell · Ventana · Al finalizar · Cola · Idioma · Dispositivos)
    /// pero con todos los textos en español.
    /// </summary>
    private Control BuildMainSettings()
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = _palette.Canvas,
            Padding = new Padding(16, 14, 16, 8)
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // ---------------- columna izquierda ----------------
        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = _palette.Canvas,
            Margin = new Padding(0, 0, 14, 0)
        };
        left.Controls.Add(SectionTitle("Integración con el Explorador"));
        left.Controls.Add(Stack(_registerCopyManager, Indent(_defaultCopyManager)));
        left.Controls.Add(Hint("Añade XoPy a los comandos de copiar y pegar del Explorador. " +
                               "Con el gestor predeterminado, Ctrl+V abre XoPy en lugar de la copia de Windows."));
        left.Controls.Add(Stack(_confirmDragDrop, _onlyWhenDropOff));
        left.Controls.Add(Stack(_addExplorerMenu));
        left.Controls.Add(Stack(_verifyWithChecksum));
        left.Controls.Add(Spacer(14));
        left.Controls.Add(SectionTitle("Cola"));
        left.Controls.Add(Stack(Inline("Trabajos activos como máximo:", _maxJobs)));
        left.Controls.Add(Spacer(10));
        left.Controls.Add(SectionTitle("Dispositivos rápidos"));
        left.Controls.Add(_fastDevices);
        left.Controls.Add(Hint("Ejemplo: C:, C:\\Ruta, \\\\Servidor\\recurso. Varios, separados por punto y coma."));
        left.Controls.Add(Spacer(10));
        left.Controls.Add(SectionTitle("Dispositivos lentos"));
        left.Controls.Add(_slowDevices);
        left.Controls.Add(Hint("Ejemplo: D:, \\\\PC-lento\\datos. Se procesan después que los rápidos."));

        // ---------------- columna derecha ----------------
        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = _palette.Canvas,
            Margin = new Padding(0)
        };
        right.Controls.Add(SectionTitle("Ventana"));
        right.Controls.Add(Inline("Al abrir:", _windowMode));
        right.Controls.Add(Stack(_alwaysOnTop));
        right.Controls.Add(Inline("Idioma:", _language));
        right.Controls.Add(Inline("Tema:", _theme));
        right.Controls.Add(Spacer(12));
        right.Controls.Add(SectionTitle("Al finalizar"));
        right.Controls.Add(Stack(_notifyDone));
        right.Controls.Add(Stack(_sound));
        right.Controls.Add(Stack(_openDestination));
        right.Controls.Add(Spacer(12));
        right.Controls.Add(SectionTitle("Comportamiento"));
        right.Controls.Add(Stack(_checkFreeSpace));
        right.Controls.Add(Stack(_systemWriteCache));
        right.Controls.Add(Stack(_showHashes));
        right.Controls.Add(Stack(_showFullPath));
        right.Controls.Add(Stack(_askWhenPasting));
        right.Controls.Add(Stack(_minimizeToTray, _startWithWindows));

        var columns = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = _palette.Canvas
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));

        // Cada columna en un panel con scroll: en pantallas bajas (como la del CI, 577 px)
        // todo el contenido sigue siendo accesible sin tapar los botones inferiores.
        var leftScroll = new Panel { Dock = DockStyle.Fill, BackColor = _palette.Canvas, AutoScroll = true };
        left.AutoScroll = false;
        left.Dock = DockStyle.Top;
        left.AutoSize = true;
        left.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        leftScroll.Controls.Add(left);

        var rightScroll = new Panel { Dock = DockStyle.Fill, BackColor = _palette.Canvas, AutoScroll = true };
        right.AutoScroll = false;
        right.Dock = DockStyle.Top;
        right.AutoSize = true;
        right.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        rightScroll.Controls.Add(right);

        columns.Controls.Add(leftScroll, 0, 0);
        columns.Controls.Add(rightScroll, 1, 0);

        var advanced = Ui.DialogButton("Opciones avanzadas…", _palette);
        advanced.Size = new Size(180, 30);
        advanced.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        advanced.Margin = new Padding(0, 8, 0, 0);
        advanced.Click += (_, _) => ShowAdvanced();

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = _palette.Canvas,
            Margin = new Padding(0)
        };
        bottom.Controls.Add(advanced);
        _advancedRow = bottom;

        page.RowStyles.Clear();
        page.RowCount = 2;
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        page.Controls.Add(columns, 0, 0);
        page.SetColumnSpan(columns, 2);
        page.Controls.Add(bottom, 0, 1);
        page.SetColumnSpan(bottom, 2);
        return page;
    }

    /// <summary>Muestra las pestañas de opciones avanzadas (destino, motor, verificación…).</summary>
    private void ShowAdvanced()
    {
        if (_advancedShown) return;
        _advancedShown = true;

        var host = _tabs.Parent as TableLayoutPanel;
        if (host is not null)
        {
            // Se oculta la pantalla principal y se muestra la de pestañas avanzadas.
            host.RowStyles[0].SizeType = SizeType.Absolute;
            host.RowStyles[0].Height = 0;
            host.RowStyles[1].SizeType = SizeType.Percent;
            host.RowStyles[1].Height = 100;
            var main = host.GetControlFromPosition(0, 0);
            if (main is not null) main.Visible = false;
        }
        _tabs.Visible = true;
        _tabs.BringToFront();
        FitTabWidths();

        // «Volver» va en la fila del botón Guardar/Cancelar, NO dentro del TabControl:
        // un TabControl solo admite TabPage y añadirle un Button lanzaba
        // «No se puede agregar 'Button' a 'TabControl'» al pulsar Opciones avanzadas.
        var back = Ui.DialogButton("← Volver a las opciones", _palette);
        back.Size = new Size(210, 30);
        back.Margin = new Padding(0, 0, 8, 0);
        back.Click += (_, _) => CloseAdvanced();
        _advancedBack = back;
        if (_advancedRow is not null)
        {
            _advancedRow.Controls.Add(back);
            _advancedRow.Controls.SetChildIndex(back, 0);
        }
    }

    private void CloseAdvanced()
    {
        _tabs.Visible = false;
        var host = _tabs.Parent as TableLayoutPanel;
        if (host is not null)
        {
            host.RowStyles[0].SizeType = SizeType.Percent;
            host.RowStyles[0].Height = 100;
            host.RowStyles[1].SizeType = SizeType.Absolute;
            host.RowStyles[1].Height = 0;
            var main = host.GetControlFromPosition(0, 0);
            if (main is not null) main.Visible = true;
        }
        if (_advancedBack is not null) _advancedBack.Visible = false;
        _advancedShown = false;
    }

    private Label SectionTitle(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = _palette.Text,
        Font = new Font("Segoe UI", 9.25f, FontStyle.Bold),
        BackColor = Color.Transparent,
        Margin = new Padding(0, 2, 0, 6)
    };

    /// <summary>Texto de presentación de una pestaña: dice para qué sirve lo que hay debajo.</summary>
    private Label Intro(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(560, 0),
        ForeColor = _palette.Muted,
        Font = new Font("Segoe UI", 8.75f),
        BackColor = Color.Transparent,
        Margin = new Padding(0, 0, 0, 10)
    };

    private Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(430, 0),
        ForeColor = _palette.Muted,
        Font = new Font("Segoe UI", 8.25f),
        BackColor = Color.Transparent,
        Margin = new Padding(0, 1, 0, 8)
    };

    private static Control Spacer(int height) => new Panel
    {
        Height = height,
        Width = 1,
        BackColor = Color.Transparent,
        Margin = new Padding(0)
    };

    private Control Indent(Control control)
    {
        control.Margin = new Padding(22, 2, 0, 4);
        return control;
    }

    private static FlowLayoutPanel Stack(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 4)
        };
        foreach (var control in controls)
        {
            control.Margin = new Padding(0, 2, 0, 2);
            panel.Controls.Add(control);
        }
        return panel;
    }

    private FlowLayoutPanel Inline(string labelText, Control control)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 4, 0, 6)
        };
        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            ForeColor = _palette.Text,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 8, 10, 0)
        };
        control.Margin = new Padding(0, 2, 0, 2);
        panel.Controls.Add(label);
        panel.Controls.Add(control);
        return panel;
    }

    private void BuildPages()
    {
        // La pestaña «General» ya no existe: sus opciones están en la pantalla principal.
        // Un control de WinForms solo puede tener UN padre: los que ya están en la vista
        // principal (tema, sonido al terminar y abrir el destino) no se pueden repetir aquí
        // porque al añadirlos otra vez desaparecen de una de las dos pantallas. Es lo que
        // dejaba «Tema de la interfaz» como un rótulo suelto sin su desplegable.
        // La caja del destino con su botón «Examinar…»: escribir una ruta a mano era la
        // única forma de rellenarla y la pestaña se veía medio vacía.
        var destinationRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = _palette.Canvas,
            Margin = new Padding(0)
        };
        destinationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        destinationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        _destination.Dock = DockStyle.Fill;
        _destination.Margin = new Padding(0, 2, 8, 2);
        var browseDestination = Ui.DialogButton("Examinar…", _palette);
        browseDestination.Dock = DockStyle.Fill;
        browseDestination.Margin = new Padding(0, 2, 0, 2);
        browseDestination.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Carpeta de destino predeterminada" };
            if (!string.IsNullOrWhiteSpace(_destination.Text) && Directory.Exists(_destination.Text.Trim()))
                dialog.SelectedPath = _destination.Text.Trim();
            if (dialog.ShowDialog(this) == DialogResult.OK) _destination.Text = dialog.SelectedPath;
        };
        destinationRow.Controls.Add(_destination, 0, 0);
        destinationRow.Controls.Add(browseDestination, 1, 0);

        _tabs.TabPages.Add(Page("Destino predeterminado", 3,
            ("", Intro("La carpeta que XoPy propone al copiar cuando todavía no se ha elegido ninguna. " +
                       "Si se deja vacía, XoPy pregunta a dónde copiar cada vez."), 0),
            ("Carpeta de destino predeterminada", destinationRow, 48),
            ("", _confirmDelete, 40),
            ("", _startMinimized, 40)));

        var extraPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = _palette.Canvas
        };
        extraPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        extraPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        _extraDestinations.Dock = DockStyle.Fill;
        _extraDestinations.BorderStyle = BorderStyle.FixedSingle;
        _extraDestinations.BackColor = _palette.Surface;
        _extraDestinations.ForeColor = _palette.Text;
        _extraDestinations.IntegralHeight = false;
        var extraButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = _palette.Canvas
        };
        var addExtra = Ui.DialogButton("Añadir destino…", _palette);
        addExtra.Size = new Size(140, 28);
        addExtra.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Destino adicional" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var path = dialog.SelectedPath;
            if (string.Equals(path, _destination.Text.Trim(), StringComparison.OrdinalIgnoreCase)) return;
            if (!_extraDestinations.Items.Cast<object>().Any(entry => string.Equals(entry.ToString(), path, StringComparison.OrdinalIgnoreCase)))
                _extraDestinations.Items.Add(path);
        };
        var removeExtra = Ui.DialogButton("Quitar", _palette);
        removeExtra.Size = new Size(90, 28);
        removeExtra.Click += (_, _) =>
        {
            if (_extraDestinations.SelectedIndex >= 0) _extraDestinations.Items.RemoveAt(_extraDestinations.SelectedIndex);
        };
        extraButtons.Controls.AddRange(new Control[] { addExtra, removeExtra });
        extraPanel.Controls.Add(_extraDestinations, 0, 0);
        extraPanel.Controls.Add(extraButtons, 0, 1);

        _tabs.TabPages.Add(Page("Transferencia", 11,
            ("", Intro("Cómo copia XoPy: cuántos archivos a la vez, qué motor usa y qué hace con las fechas, " +
                       "la papelera y los archivos en uso. Lo de fábrica va bien para casi todo."), 0),
            ("", _multiThread, 32),
            ("Hilos en paralelo (solo con multihilo)", _parallel, 44),
            ("Búfer por archivo (KiB)", _buffer, 44),
            ("Límite de velocidad (MiB/s, 0 = sin límite)", _speed, 44),
            ("", _preserveTimestamps, 36),
            ("", _recycleBin, 36),
            ("", _copyLocked, 32),
            // La casilla de la barra de menús existía pero no se añadía a ninguna página:
            // no había forma de mostrarla ni ocultarla desde Preferencias.
            ("", _showMenuBar, 32),
            ("", _showJobs, 32),
            ("Motor de copia", _engine, 40),
            ("", _resumePartials, 32),
            ("", _writeChecksums, 32),
            ("Pasadas de borrado seguro (0 = normal)", _securePasses, 40),
            ("Script al finalizar", _completionScript, 40)));

        _tabs.TabPages.Add(Page("Destinos", 3,
            ("Carpetas que reciben una copia de todo", extraPanel, 260),
            ("", new Label
            {
                Text = "Cada destino adicional recibe una copia completa de lo que se copie o mueva. " +
                       "El script final recibe la carpeta de destino, los errores y la operación.",
                AutoSize = true,
                ForeColor = _palette.Muted,
                Font = new Font("Segoe UI", 8.5f),
                BackColor = Color.Transparent
            }, 44)));

        var verifyPage = Page("Verificación", 3,
            ("Algoritmo", _verifyAlgorithm, 48),
            ("", _verifyAfterCopy, 40),
            ("", new Label
            {
                Text = "La verificación compara el archivo copiado con el original. Con SHA-256 puedes " +
                       "estar seguro de que la copia es idéntica bit a bit, a costa de leer los archivos dos " +
                       "veces. Desactívala para copiar a máxima velocidad.",
                AutoSize = true,
                ForeColor = _palette.Muted,
                Font = new Font("Segoe UI", 8.5f),
                BackColor = Color.Transparent
            }, 110));
        _tabs.TabPages.Add(verifyPage);

        _tabs.TabPages.Add(Page("Conflictos", 3,
            ("", Intro("Qué hacer cuando el archivo ya está en el destino o cuando algo falla. " +
                       "Con «Preguntar», XoPy enseña los dos archivos y decides tú."), 0),
            ("Si el archivo ya existe", _conflict, 48),
            ("Si aparece un error", _onError, 48),
            ("Reintentos por archivo", _retries, 48)));

        _tabs.TabPages.Add(Page("Integración", 3,
            ("", _replaceCopier, 60),
            ("", new Label
            {
                Text = "Ctrl+V y Mayús+Insert en el Explorador y el escritorio se transfieren con XoPy. " +
                       "«Enviar a → XoPy» abre el selector de destino. Si el portapapeles contiene elementos " +
                       "cortados, XoPy los mueve y elimina el origen al finalizar." + Environment.NewLine + Environment.NewLine +
                       "El pegado nativo del menú contextual de Windows se conserva: Windows no ofrece una " +
                       "forma compatible de reemplazarlo globalmente. XoPy no añade otra opción «Pegar» al " +
                       "menú contextual; así se evita el duplicado y no se reinicia Explorer.", 
                AutoSize = true,
                ForeColor = _palette.Muted,
                Font = new Font("Segoe UI", 8.5f),
                BackColor = Color.Transparent
            }, 160)));

        // «Reproducir un sonido» y «Abrir la carpeta de destino» están en la pantalla
        // principal de Preferencias (sección «Al finalizar»): repetirlos aquí los borraba
        // de allí y dejaba esta página con filas vacías.
        _tabs.TabPages.Add(Page("Al terminar", 3,
            ("", Intro("Qué pasa cuando la copia acaba y qué hacer si se pide otra copia mientras hay una en marcha."), 0),
            ("", _closeWhenDone, 42),
            ("", _shutdown, 42),
            ("", _askWhenBusy, 42)));

        _tabs.TabPages.Add(BuildFavoritesPage());
    }

    private TabPage BuildFavoritesPage()
    {
        var page = new TabPage("Favoritos") { BackColor = _palette.Canvas, Padding = new Padding(16) };
        _favorites.Dock = DockStyle.Fill;
        _favorites.BorderStyle = BorderStyle.FixedSingle;
        _favorites.BackColor = _palette.Surface;
        _favorites.ForeColor = _palette.Text;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 150,
            FlowDirection = FlowDirection.TopDown,
            BackColor = _palette.Canvas,
            Padding = new Padding(10, 0, 0, 0)
        };
        var add = Ui.DialogButton("Añadir carpeta", _palette);
        add.Size = new Size(140, 30);
        add.Margin = new Padding(0, 0, 0, 6);
        add.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Carpeta favorita" };
            if (dialog.ShowDialog(this) == DialogResult.OK && !_favorites.Items.Contains(dialog.SelectedPath))
                _favorites.Items.Add(dialog.SelectedPath);
        };
        var remove = Ui.DialogButton("Quitar", _palette);
        remove.Size = new Size(140, 30);
        remove.Margin = new Padding(0);
        remove.Click += (_, _) =>
        {
            if (_favorites.SelectedIndex >= 0) _favorites.Items.RemoveAt(_favorites.SelectedIndex);
        };
        buttons.Controls.Add(add);
        buttons.Controls.Add(remove);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = _palette.Canvas };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(_favorites, 0, 0);
        layout.Controls.Add(buttons, 1, 0);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage Page(string title, int rows, params (string Label, Control Control, int Height)[] items)
    {
        var page = new TabPage(title) { BackColor = _palette.Canvas, Padding = new Padding(14, 12, 14, 12), AutoScroll = true };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = _palette.Canvas,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
            Padding = new Padding(0)
        };
        // Columnas: rótulo estrecho + control que aprovecha todo el ancho restante.
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 236));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var descriptions = new List<Label>();
        layout.RowCount = items.Length;
        var row = 0;
        foreach (var (label, control, height) in items)
        {
            // Aviso temprano: si el control ya tiene padre, al añadirlo aquí se lo quita a la
            // otra pantalla y esa fila queda con el rótulo y sin control (el fallo del «Tema
            // de la interfaz»). Queda registrado en el log en vez de pasar inadvertido.
            if (control.Parent is not null)
                FileLog.Write($"Preferencias: «{label}» reutiliza un control que ya está en otra pantalla.");

            // Las descripciones largas crecen solas: fila automática y ajuste de línea real,
            // en lugar de un alto fijo que recortaba el texto a media frase.
            var isDescription = control is Label && string.IsNullOrEmpty(label);
            layout.RowStyles.Add(isDescription
                ? new RowStyle(SizeType.AutoSize)
                : new RowStyle(SizeType.Absolute, height));
            var text = new Label
            {
                Text = label,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                UseMnemonic = false,
                ForeColor = _palette.Muted,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.75f),
                Margin = new Padding(0, 0, 16, 0)
            };
            if (control is Label description)
            {
                description.AutoSize = true;
                description.Dock = DockStyle.None;
                description.TextAlign = ContentAlignment.TopLeft;
                description.AutoEllipsis = false;
                description.UseMnemonic = false;
                description.MaximumSize = new Size(520, 0);
                description.Margin = new Padding(0, 8, 8, 8);
                descriptions.Add(description);
            }
            else if (control is CheckBox)
            {
                control.Anchor = AnchorStyles.Left;
                control.Margin = new Padding(0, 0, 8, 0);
            }
            else
            {
                // Combos, cajas de texto, numéricos y paneles: ocupan el ancho útil.
                control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                control.Margin = new Padding(0, 2, 8, 2);
            }
            if (isDescription)
            {
                // Los textos explicativos ocupan las DOS columnas: empiezan en el margen
                // izquierdo, como un párrafo, en vez de quedar colgados bajo los controles.
                layout.Controls.Add(control, 0, row);
                layout.SetColumnSpan(control, 2);
            }
            else
            {
                layout.Controls.Add(text, 0, row);
                layout.Controls.Add(control, 1, row);
            }
            row++;
        }

        // El ancho de las descripciones sigue al ancho real de la pestaña para que el
        // texto se reparta en varias líneas sin huecos ni cortes.
        page.Resize += (_, _) =>
        {
            // ColumnStyle.Width es float: sin el cast, WinForms no puede restar int - float.
            var available = Math.Max(280, page.ClientSize.Width - (int)layout.ColumnStyles[0].Width - 40);
            foreach (var description in descriptions) description.MaximumSize = new Size(available, 0);
        };

        page.Controls.Add(layout);
        return page;
    }

    /// <summary>
    /// Ajusta el ancho de TODAS las pestañas al título más largo. Con un ancho fijo de 92 px
    /// se cortaban «Transferencia», «Verificación» e «Integración» y el diálogo parecía roto.
    /// </summary>
    private void FitTabWidths()
    {
        if (_tabs.TabPages.Count == 0) return;
        var widest = 0;
        foreach (TabPage tab in _tabs.TabPages)
        {
            var measured = TextRenderer.MeasureText(tab.Text, _tabs.Font,
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix);
            widest = Math.Max(widest, measured.Width);
        }
        // Ancho mínimo por pestaña; «FillToRight» reparte el sobrante entre las del renglón.
        // Se limita para que en dos renglones quepan todas sin que Windows saque flechitas.
        var width = Math.Max(84, Math.Min(widest + 34, Math.Max(84, (_tabs.ClientSize.Width - 8) / 4)));
        _tabs.ItemSize = new Size(width, 30);
    }

    private void DrawTab(object? sender, DrawItemEventArgs e)
    {
        var selected = e.Index == _tabs.SelectedIndex;
        using var background = new SolidBrush(selected ? _palette.Surface : _palette.Canvas);
        e.Graphics.FillRectangle(background, e.Bounds);
        using var line = new Pen(_palette.Border);
        e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        if (selected)
        {
            using var accent = new SolidBrush(_palette.Accent);
            e.Graphics.FillRectangle(accent, e.Bounds.Left + 6, e.Bounds.Bottom - 3, e.Bounds.Width - 12, 3);
        }
        TextRenderer.DrawText(e.Graphics, _tabs.TabPages[e.Index].Text, _tabs.Font,
            Rectangle.Inflate(e.Bounds, -6, -2), selected ? _palette.Text : _palette.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }

    private TextBox TextBox(string value) => new()
    {
        Text = value,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = _palette.Surface,
        ForeColor = _palette.Text,
        Width = 340
    };

    private CheckBox Check(string text, bool value) => new()
    {
        Text = text,
        Checked = value,
        AutoSize = true,
        ForeColor = _palette.Text,
        BackColor = Color.Transparent
    };

    private NumericUpDown Numeric(int value, int min, int max, int step)
    {
        var numeric = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Increment = step,
            Value = Math.Clamp(value, min, max),
            Width = 120
        };
        Ui.StyleNumeric(numeric, _palette);
        return numeric;
    }

    public int TabCount => _tabs.TabCount;

    public void SelectTab(int index)
    {
        if (index >= 0 && index < _tabs.TabCount) _tabs.SelectedIndex = index;
    }

    private void CaptureValues()
    {
        Result.Destination = _destination.Text.Trim();
        Result.DarkTheme = _theme.SelectedIndex == 0;
        Result.ConfirmDelete = _confirmDelete.Checked;
        Result.MinimizeToTray = _minimizeToTray.Checked;
        Result.StartWithWindows = _startWithWindows.Checked;
        Result.StartMinimized = _startMinimized.Checked;
        Result.MultiThreadCopy = _multiThread.Checked;
        Result.ShowMenuBar = _showMenuBar.Checked;
        Result.ShowJobs = _showJobs.Checked;
        Result.Parallelism = (int)_parallel.Value;
        Result.BufferKiB = (int)_buffer.Value;
        Result.SpeedLimitMiB = (int)_speed.Value;
        Result.PreserveTimestamps = _preserveTimestamps.Checked;
        Result.UseRecycleBin = _recycleBin.Checked;
        Result.CopyLockedFiles = _copyLocked.Checked;
        Result.Engine = (CopyEngineKind)Math.Clamp(_engine.SelectedIndex, 0, 2);
        Result.ResumePartials = _resumePartials.Checked;
        Result.WriteChecksumFile = _writeChecksums.Checked;
        Result.SecureDeletePasses = (int)_securePasses.Value;
        Result.CompletionScript = _completionScript.Text.Trim();
        Result.ExtraDestinations = _extraDestinations.Items.Cast<object>()
            .Select(entry => entry.ToString() ?? string.Empty)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Result.Verify = (VerifyAlgorithm)_verifyAlgorithm.SelectedIndex;
        Result.VerifyAfterCopy = _verifyAfterCopy.Checked;
        Result.Conflict = (ConflictPolicy)_conflict.SelectedIndex;
        Result.OnError = (ErrorPolicy)_onError.SelectedIndex;
        Result.RetryCount = (int)_retries.Value;
        Result.ExplorerIntegration = false;
        Result.ReplaceWindowsCopier = _replaceCopier.Checked;
        Result.AskWhenBusy = _askWhenBusy.Checked;
        Result.AskWhenPasting = _askWhenPasting.Checked;
        Result.ShowFullPath = _showFullPath.Checked;
        Result.PlaySoundWhenDone = _sound.Checked;
        Result.OpenDestinationWhenDone = _openDestination.Checked;
        Result.CloseWhenDone = _closeWhenDone.Checked;
        Result.ShutdownWhenDone = _shutdown.Checked;
        Result.Favorites = _favorites.Items.Cast<object>().Select(entry => entry.ToString() ?? string.Empty)
            .Where(path => path.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Pantalla principal estilo TeraCopy.
        Result.ConfirmDragDrop = _confirmDragDrop.Checked;
        Result.OnlyWhenScrollLockOff = _onlyWhenDropOff.Checked;
        Result.ExplorerIntegration = _addExplorerMenu.Checked;
        Result.VerifyAfterCopy = _verifyWithChecksum.Checked;
        Result.AlwaysOnTop = _alwaysOnTop.Checked;
        Result.CheckFreeSpace = _checkFreeSpace.Checked;
        Result.UseSystemWriteCache = _systemWriteCache.Checked;
        Result.Parallelism = (int)_maxJobs.Value;
        Result.FastDevices = _fastDevices.Text.Trim();
        Result.SlowDevices = _slowDevices.Text.Trim();
        Result.Language = _language.SelectedIndex == 1 ? "en" : "es";
        Result.WindowMode = _windowMode.SelectedIndex;
        // El gestor predeterminado y el registro del gestor de copia son la misma casilla
        // de integración: si cualquiera está marcada, el copiador queda sustituido.
        Result.ReplaceWindowsCopier = _registerCopyManager.Checked || _defaultCopyManager.Checked;
        // «Notificar al terminar» avisa con el sonido; el resto de acciones se eligen aparte.
        if (_notifyDone.Checked) Result.PlaySoundWhenDone = true;
    }

    public void ApplyTo(AppSettings target)
    {
        target.Destination = Result.Destination;
        target.DarkTheme = Result.DarkTheme;
        target.ConfirmDelete = Result.ConfirmDelete;
        target.MinimizeToTray = Result.MinimizeToTray;
        target.StartWithWindows = Result.StartWithWindows;
        target.StartMinimized = Result.StartMinimized;
        target.Parallelism = Result.Parallelism;
        target.MultiThreadCopy = Result.MultiThreadCopy;
        target.ShowMenuBar = Result.ShowMenuBar;
        target.ShowJobs = Result.ShowJobs;
        target.CollapseJobs = !Result.ShowJobs;
        target.BufferKiB = Result.BufferKiB;
        target.SpeedLimitMiB = Result.SpeedLimitMiB;
        target.PreserveTimestamps = Result.PreserveTimestamps;
        target.UseRecycleBin = Result.UseRecycleBin;
        target.CopyLockedFiles = Result.CopyLockedFiles;
        target.Engine = Result.Engine;
        target.ResumePartials = Result.ResumePartials;
        target.WriteChecksumFile = Result.WriteChecksumFile;
        target.SecureDeletePasses = Result.SecureDeletePasses;
        target.CompletionScript = Result.CompletionScript;
        target.ExtraDestinations = Result.ExtraDestinations.ToList();
        target.Verify = Result.Verify;
        target.VerifyAfterCopy = Result.VerifyAfterCopy;
        target.Conflict = Result.Conflict;
        target.OnError = Result.OnError;
        target.RetryCount = Result.RetryCount;
        target.ExplorerIntegration = false;
        target.ReplaceWindowsCopier = Result.ReplaceWindowsCopier;
        target.AskWhenBusy = Result.AskWhenBusy;
        target.AskWhenPasting = Result.AskWhenPasting;
        target.ShowFullPath = Result.ShowFullPath;
        target.PlaySoundWhenDone = Result.PlaySoundWhenDone;
        target.OpenDestinationWhenDone = Result.OpenDestinationWhenDone;
        target.CloseWhenDone = Result.CloseWhenDone;
        target.ShutdownWhenDone = Result.ShutdownWhenDone;
        target.Favorites = Result.Favorites.ToList();
        target.ConfirmDragDrop = Result.ConfirmDragDrop;
        target.OnlyWhenScrollLockOff = Result.OnlyWhenScrollLockOff;
        target.AlwaysOnTop = Result.AlwaysOnTop;
        target.CheckFreeSpace = Result.CheckFreeSpace;
        target.UseSystemWriteCache = Result.UseSystemWriteCache;
        target.FastDevices = Result.FastDevices;
        target.SlowDevices = Result.SlowDevices;
        target.Language = Result.Language;
        target.ShowHashes = Result.ShowHashes;
    }

    public static AppSettings CloneSettings(AppSettings source) => new()
    {
        Destination = source.Destination,
        DarkTheme = source.DarkTheme,
        ConfirmDelete = source.ConfirmDelete,
        MinimizeToTray = source.MinimizeToTray,
        StartWithWindows = source.StartWithWindows,
        StartMinimized = source.StartMinimized,
        Parallelism = source.Parallelism,
        MultiThreadCopy = source.MultiThreadCopy,
        ShowMenuBar = source.ShowMenuBar,
        ShowJobs = source.ShowJobs,
        BufferKiB = source.BufferKiB,
        SpeedLimitMiB = source.SpeedLimitMiB,
        PreserveTimestamps = source.PreserveTimestamps,
        UseRecycleBin = source.UseRecycleBin,
        CopyLockedFiles = source.CopyLockedFiles,
        Engine = source.Engine,
        ResumePartials = source.ResumePartials,
        WriteChecksumFile = source.WriteChecksumFile,
        SecureDeletePasses = source.SecureDeletePasses,
        CompletionScript = source.CompletionScript,
        ExtraDestinations = source.ExtraDestinations.ToList(),
        Verify = source.Verify,
        VerifyAfterCopy = source.VerifyAfterCopy,
        Conflict = source.Conflict,
        OnError = source.OnError,
        RetryCount = source.RetryCount,
        ExplorerIntegration = source.ExplorerIntegration,
        ReplaceWindowsCopier = source.ReplaceWindowsCopier,
        AskWhenBusy = source.AskWhenBusy,
        AskWhenPasting = source.AskWhenPasting,
        ShowFullPath = source.ShowFullPath,
        ShowList = source.ShowList,
        PlaySoundWhenDone = source.PlaySoundWhenDone,
        OpenDestinationWhenDone = source.OpenDestinationWhenDone,
        CloseWhenDone = source.CloseWhenDone,
        ShutdownWhenDone = source.ShutdownWhenDone,
        Favorites = source.Favorites.ToList(),
        ConfirmDragDrop = source.ConfirmDragDrop,
        OnlyWhenScrollLockOff = source.OnlyWhenScrollLockOff,
        AlwaysOnTop = source.AlwaysOnTop,
        CheckFreeSpace = source.CheckFreeSpace,
        UseSystemWriteCache = source.UseSystemWriteCache,
        FastDevices = source.FastDevices,
        SlowDevices = source.SlowDevices,
        Language = source.Language,
        ShowHashes = source.ShowHashes
    };
}

internal sealed class HistoryDialog : Form
{
    /// <summary>
    /// Último repaso antes de que la ventana se vea: la barra de título se pinta de oscuro
    /// (Windows la hace blanca por su cuenta) y cualquier control que se haya quedado con
    /// los colores del sistema recibe los de la paleta. Es lo que quita las barras blancas.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.DarkFrame(this, _palette!);
        Ui.PaintAll(this, _palette!);
    }

    /// <summary>
    /// Ya con la ventana en pantalla: a estas alturas TODOS los controles tienen su
    /// identificador de Windows, así que es cuando se pueden oscurecer sus barras de
    /// desplazamiento nativas (en Preferencias salían blancas).
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Ui.PaintAll(this, _palette!);
        Ui.DarkScrollbars(this, _palette!);
    }

    private readonly List<HistoryEntry> _entries;
    private readonly ListView _list;
    private readonly Label _empty;
    private readonly Palette _palette;
    private readonly Font _headerFont = new("Segoe UI", 9f, FontStyle.Bold);

    public bool Modified { get; private set; }
    public IReadOnlyList<HistoryEntry> Entries => _entries;

    public HistoryDialog(List<HistoryEntry> entries, Palette palette)
    {
        _entries = entries.ToList();
        _palette = palette;

        Text = "Historial de transferencias";
        BackColor = palette.Canvas;
        ForeColor = palette.Text;
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(880, 520);
        MinimumSize = new Size(700, 420);
        ShowInTaskbar = false;
        KeyPreview = true;

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            BorderStyle = BorderStyle.None,
            BackColor = palette.Surface,
            ForeColor = palette.Text,
            Font = new Font("Segoe UI", 9f),
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        _list.OwnerDraw = true;
        _empty = new Label
        {
            Text = "Todavía no hay transferencias en el historial.\r\n\r\n"
                   + "Aquí se irán guardando la fecha, el destino, el tamaño y la velocidad de cada copia.",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = palette.Muted,
            BackColor = palette.Surface,
            Font = new Font("Segoe UI", 9.5f),
            Visible = false
        };
        _list.Columns.Add("Fecha", 122);
        _list.Columns.Add("Operación", 88);
        _list.Columns.Add("Destino", 300); // se ajusta al ancho para no dejar hueco en la cabecera
        _list.Columns.Add("Archivos", 88, HorizontalAlignment.Right);
        _list.Columns.Add("Tamaño", 84, HorizontalAlignment.Right);
        _list.Columns.Add("Velocidad", 92, HorizontalAlignment.Right);
        _list.Columns.Add("Resultado", 100);
        _list.DrawColumnHeader += (_, e) =>
        {
            using var background = new SolidBrush(palette.SurfaceAlt);
            e.Graphics.FillRectangle(background, e.Bounds);
            using var line = new Pen(palette.Border);
            e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            var headerText = e.Header?.Text ?? string.Empty;
            var alignment = e.Header?.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left;
            TextRenderer.DrawText(e.Graphics, headerText, _headerFont,
                Rectangle.Inflate(e.Bounds, -10, 0), palette.Muted,
                alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
        // Las filas se pintan a mano: con DrawDefault el ListView usaba los colores del
        // sistema y las filas seleccionadas salían en azul claro sobre fondo oscuro.
        _list.DrawItem += (_, e) =>
        {
            var background = e.Item.Selected
                ? palette.Selection
                : (e.ItemIndex % 2 == 0 ? palette.Surface : palette.SurfaceAlt);
            using var brush = new SolidBrush(background);
            e.Graphics.FillRectangle(brush, e.Bounds);
        };
        _list.DrawSubItem += (_, e) =>
        {
            if (e.Item is null) return;
            var entry = e.Item.Tag as HistoryEntry;
            var color = e.ColumnIndex == 6 && entry is not null
                ? (entry.Errors > 0 ? palette.Danger : palette.Success)
                : e.ColumnIndex is 0 or 2 ? palette.Text : palette.Muted;
            var align = _list.Columns[e.ColumnIndex].TextAlign == HorizontalAlignment.Right
                ? TextFormatFlags.Right
                : TextFormatFlags.Left;
            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? string.Empty, _list.Font,
                Rectangle.Inflate(e.Bounds, -8, 0), color,
                align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
        _list.Resize += (_, _) => FitDestinationColumn();
        // LA BARRA BLANCA del historial: el ListView nativo pinta con los colores del sistema
        // tanto la barra de desplazamiento como la franja que queda a la derecha de la última
        // columna. SetWindowTheme oscurece la barra; la franja sobrante la tapa _scrollCover.
        _list.HandleCreated += (_, _) =>
        {
            try { SetWindowTheme(_list.Handle, "DarkMode_Explorer", null); } catch { }
            foreach (Control child in _list.Controls)
            {
                try { if (child.IsHandleCreated) SetWindowTheme(child.Handle, "DarkMode_Explorer", null); } catch { }
            }
        };
        _list.Paint += (_, e) =>
        {
            var width = SystemInformation.VerticalScrollBarWidth + 4;
            var right = _list.ClientSize.Width - width;
            if (right <= 0) return;
            var header = _list.Items.Count > 0 ? Math.Max(1, _list.Items[0].Bounds.Top) : _list.ClientSize.Height;
            using var brush = new SolidBrush(palette.Surface);
            e.Graphics.FillRectangle(brush, new Rectangle(right, 0, width, header));
        };
        try
        {
            typeof(ListView).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(_list, true);
        }
        catch { }
        FillList();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = palette.Surface,
            Padding = new Padding(12, 10, 12, 10)
        };

        var close = Ui.DialogButton("Cerrar", palette, true);
        close.DialogResult = DialogResult.OK;

        var export = Ui.DialogButton("Exportar CSV", palette);
        export.Click += (_, _) => ExportCsv(palette);

        var exportHtml = Ui.DialogButton("Exportar HTML", palette);
        exportHtml.Click += (_, _) => ExportHtml();

        var clear = Ui.DialogButton("Vaciar historial", palette);
        clear.Click += (_, _) =>
        {
            if (_entries.Count == 0) return;
            if (MessageBox.Show(this, $"¿Borrar las {_entries.Count} entradas del historial?\n\nNo se toca ningún archivo: solo el registro.",
                    "XoPy", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _entries.Clear();
            Modified = true;
            FillList();
        };

        var remove = Ui.DialogButton("Borrar entrada", palette);
        remove.Click += (_, _) => RemoveSelected();

        buttons.Controls.Add(close);
        buttons.Controls.Add(exportHtml);
        buttons.Controls.Add(export);
        buttons.Controls.Add(clear);
        buttons.Controls.Add(remove);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = palette.Canvas,
            Padding = new Padding(12),
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));

        // Panel con la lista y, encima, el aviso de «sin entradas»: una ventana negra y vacía
        // no dice si está cargando, si falló o si simplemente no hay nada.
        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = palette.Surface, Margin = new Padding(0) };
        _empty.Dock = DockStyle.Fill;
        listHost.Controls.Add(_empty);
        listHost.Controls.Add(_list);
        _list.BringToFront();

        root.Controls.Add(listHost, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
        Shown += (_, _) => FitDestinationColumn();

        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Delete) return;
            RemoveSelected();
            e.Handled = true;
        };
    }

    private void FitDestinationColumn()
    {
        const int fixedWidth = 122 + 88 + 88 + 84 + 92 + 100;
        // Se reserva el ancho de la barra vertical: si las columnas llenan el ancho exacto,
        // el ListView saca además la barra HORIZONTAL, que es la raya blanca de abajo.
        var reserve = SystemInformation.VerticalScrollBarWidth + 6;
        var width = Math.Max(140, _list.ClientSize.Width - fixedWidth - reserve);
        if (_list.Columns[2].Width != width) _list.Columns[2].Width = width;
    }

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string appName, string? idList);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _headerFont.Dispose();
        base.Dispose(disposing);
    }

    private void FillList()
    {
        // Con el historial vacío se esconde la lista entera y se enseña el aviso: así no queda
        // un rectángulo negro con una barra de desplazamiento blanca y nada dentro.
        var vacio = _entries.Count == 0;
        _empty.Visible = vacio;
        _list.Visible = !vacio;
        if (vacio) _empty.BringToFront();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var entry in _entries)
        {
            var row = new ListViewItem(new[]
            {
                entry.FinishedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                entry.OperationText,
                entry.Destination,
                entry.FileCount.ToString(),
                Format.Bytes(entry.Bytes),
                Format.Speed(entry.BytesPerSecond),
                entry.ResultText
            });
            row.Tag = entry;
            _list.Items.Add(row);
        }
        _list.EndUpdate();
    }

    private void RemoveSelected()
    {
        var selected = _list.SelectedItems.Cast<ListViewItem>()
            .Select(row => row.Tag as HistoryEntry)
            .Where(entry => entry is not null)
            .Cast<HistoryEntry>()
            .ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Selecciona una o varias entradas del historial para borrarlas.",
                "XoPy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(this, $"¿Borrar {selected.Count} entrada(s) del historial?\n\nNo se toca ningún archivo: solo el registro.",
                "XoPy", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        foreach (var entry in selected) _entries.Remove(entry);
        Modified = true;
        FillList();
    }

    private void ExportCsv(Palette palette)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Exportar historial",
            Filter = "CSV (*.csv)|*.csv",
            FileName = "xopy-historial.csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var builder = new StringBuilder();
            builder.AppendLine("Fecha;Operacion;Destino;Archivos;Bytes;Segundos;Errores");
            foreach (var entry in _entries)
                builder.AppendLine($"{entry.FinishedAt:yyyy-MM-dd HH:mm:ss};{entry.OperationText};{entry.Destination};{entry.FileCount};{entry.Bytes};{entry.Seconds:0.0};{entry.Errors}");
            File.WriteAllText(dialog.FileName, builder.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "No se pudo exportar: " + ex.Message, "XoPy", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportHtml()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Exportar historial",
            Filter = "Página HTML (*.html)|*.html",
            FileName = $"xopy-historial-{DateTime.Now:yyyyMMdd-HHmm}.html"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>Historial de XoPy</title><style>")
                .AppendLine("body{font-family:'Segoe UI',sans-serif;background:#16181d;color:#e6e8ee;margin:28px}")
                .AppendLine("h1{font-size:20px}table{border-collapse:collapse;width:100%;font-size:13px}")
                .AppendLine("th,td{border-bottom:1px solid #2b2f38;padding:6px 8px;text-align:left}")
                .AppendLine("th{color:#9aa3b2}.bad{color:#ff7a7a}.ok{color:#57d38c}")
                .AppendLine("</style></head><body><h1>Historial de transferencias</h1><table>")
                .AppendLine("<tr><th>Fecha</th><th>Operación</th><th>Destino</th><th>Archivos</th><th>Tamaño</th><th>Velocidad</th><th>Resultado</th></tr>");
            foreach (var entry in _entries)
            {
                var css = entry.Errors > 0 ? "bad" : "ok";
                html.AppendLine($"<tr><td>{entry.FinishedAt:yyyy-MM-dd HH:mm}</td><td>{entry.OperationText}</td><td>{System.Net.WebUtility.HtmlEncode(entry.Destination)}</td><td>{entry.FileCount}</td><td>{Format.Bytes(entry.Bytes)}</td><td>{Format.Speed(entry.BytesPerSecond)}</td><td class=\"{css}\">{entry.ResultText}</td></tr>");
            }
            html.AppendLine("</table></body></html>");
            File.WriteAllText(dialog.FileName, html.ToString(), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "No se pudo exportar: " + ex.Message, "XoPy", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

/// <summary>
/// Ventana que sale cuando el archivo ya existe en el destino. Antes era un cuadro de texto
/// gris con dos rutas larguísimas pegadas y costaba leerlo. Ahora compara los dos archivos
/// lado a lado —tamaño y fecha— y marca cuál es más nuevo y cuál más grande, para poder
/// decidir de un vistazo sin leer ninguna ruta completa.
/// </summary>
internal sealed class ConflictDialog : Form
{
    /// <summary>
    /// Último repaso antes de que la ventana se vea: la barra de título se pinta de oscuro
    /// (Windows la hace blanca por su cuenta) y cualquier control que se haya quedado con
    /// los colores del sistema recibe los de la paleta. Es lo que quita las barras blancas.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Ui.DarkFrame(this, _palette!);
        Ui.PaintAll(this, _palette!);
    }

    /// <summary>
    /// Ya con la ventana en pantalla: a estas alturas TODOS los controles tienen su
    /// identificador de Windows, así que es cuando se pueden oscurecer sus barras de
    /// desplazamiento nativas (en Preferencias salían blancas).
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Ui.PaintAll(this, _palette!);
        Ui.DarkScrollbars(this, _palette!);
    }

    /// <summary>0 Sobrescribir · 1 Omitir · 2 Cambiar nombre · 3 Cancelar.</summary>
    public int Choice { get; private set; } = 3;

    public bool ApplyToAll => _applyAll.Checked;

    private readonly CheckBox _applyAll;
    private readonly Palette _palette;
    // Un solo ToolTip para todo el cuadro: antes se creaba uno por botón y no se liberaba.
    private readonly ToolTip _tips = new();

    public ConflictDialog(Palette palette, ConflictPrompt prompt)
    {
        _palette = palette;
        var name = Path.GetFileName(prompt.TargetPath);

        Text = "Ya existe un archivo con ese nombre";
        BackColor = palette.Canvas;
        ForeColor = palette.Text;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        // Ancho suficiente para las cuatro respuestas en una sola fila.
        ClientSize = new Size(700, 356);
        KeyPreview = true;

        var newer = prompt.SourceModified > prompt.TargetModified ? 1
            : prompt.SourceModified < prompt.TargetModified ? 2 : 0;
        var bigger = prompt.SourceSize > prompt.TargetSize ? 1
            : prompt.SourceSize < prompt.TargetSize ? 2 : 0;
        var identical = prompt.SourceSize == prompt.TargetSize
            && Math.Abs((prompt.SourceModified - prompt.TargetModified).TotalSeconds) < 2;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = palette.Canvas,
            Padding = new Padding(18, 14, 18, 12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        // Título: el NOMBRE del archivo, que es lo que de verdad hace falta saber.
        var heading = new Label
        {
            Text = name,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = palette.Text,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            AutoEllipsis = true,
            UseMnemonic = false
        };

        var subtitle = new Label
        {
            Text = identical
                ? "Ya hay un archivo igual en la carpeta de destino (mismo tamaño y misma fecha)."
                : "Ya hay un archivo con ese nombre en la carpeta de destino.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = palette.Muted,
            Font = new Font("Segoe UI", 9f),
            AutoEllipsis = true,
            UseMnemonic = false
        };

        // Comparación lado a lado.
        var compare = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = palette.Canvas,
            Margin = new Padding(0, 6, 0, 6)
        };
        compare.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        compare.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        compare.Controls.Add(Card(palette, _tips, "El que vas a copiar", prompt.SourcePath,
            prompt.SourceSize, prompt.SourceModified, newer == 1, bigger == 1), 0, 0);
        compare.Controls.Add(Card(palette, _tips, "El que ya está en el destino", prompt.TargetPath,
            prompt.TargetSize, prompt.TargetModified, newer == 2, bigger == 2), 1, 0);

        _applyAll = new CheckBox
        {
            Text = "Hacer lo mismo con el resto de archivos repetidos de esta copia",
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = palette.Text,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9f)
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = palette.Canvas,
            Margin = new Padding(0, 4, 0, 0)
        };

        // Las cuatro respuestas, todas con el mismo aspecto. Lo que las distingue es el
        // TEXTO y el globo de ayuda, no el color: así ninguna parece «la buena» de entrada.
        Button Make(string text, string tip, int choice)
        {
            // Todos los botones con EL MISMO estilo: ninguno resaltado, ninguno de color.
            var button = Ui.DialogButton(text, palette);
            // El ancho se MIDE con la fuente del botón: con 128 fijos, «Quedarme con los
            // dos» se quedaba en «Quedarme con los».
            button.Width = Math.Max(118, TextRenderer.MeasureText(text, button.Font).Width + 26);
            button.Margin = new Padding(8, 0, 0, 0);
            _tips.SetToolTip(button, tip);
            button.Click += (_, _) => { Choice = choice; DialogResult = DialogResult.OK; Close(); };
            return button;
        }

        // Orden de izquierda a derecha en pantalla: Cancelar · Omitir · Copiar las dos ·
        // Reemplazar. El botón resaltado es el que menos daño hace.
        var replace = Make("Reemplazar", "BORRA el archivo del destino y pone el nuevo en su sitio. Lo que había se pierde.", 0);
        var skip = Make("No copiar este", "Deja el destino como está y pasa al siguiente archivo.", 1);
        var both = Make("Quedarme con los dos", "Copia el nuevo con un nombre distinto; no se pierde ninguno.", 2);
        var cancel = Make("Cancelar la copia", "Detiene la transferencia completa.", 3);

        buttons.Controls.Add(replace);
        buttons.Controls.Add(skip);
        buttons.Controls.Add(both);
        buttons.Controls.Add(cancel);

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(subtitle, 0, 1);
        root.Controls.Add(compare, 0, 2);
        root.Controls.Add(_applyAll, 0, 3);
        root.Controls.Add(buttons, 0, 4);
        Controls.Add(root);

        AcceptButton = both;
        CancelButton = cancel;
        // Nadie empieza con el foco puesto: un botón enfocado se ve distinto de los demás y
        // eso es, otra vez, resaltar una respuesta. Se contesta con el ratón o con Tab.
        Shown += (_, _) => ActiveControl = null;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            Choice = 3;
            DialogResult = DialogResult.Cancel;
            Close();
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tips.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>Tarjeta con los datos de uno de los dos archivos.</summary>
    private static Control Card(Palette palette, ToolTip tips, string title, string path, long size, DateTime modified,
        bool isNewer, bool isBigger)
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = palette.Surface,
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0, 0, 8, 0)
        };
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        card.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Label Line(string text, float points, Color color, bool bold = false) => new()
        {
            Text = text,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = color,
            Font = new Font("Segoe UI", points, bold ? FontStyle.Bold : FontStyle.Regular),
            AutoEllipsis = true,
            UseMnemonic = false,
            Margin = new Padding(0)
        };

        var folder = string.Empty;
        try { folder = Path.GetDirectoryName(path) ?? string.Empty; } catch { }

        var sizeLine = Format.Bytes(size) + (isBigger ? "   ·   el más grande" : string.Empty);
        var dateLine = modified.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
                       + (isNewer ? "   ·   el más nuevo" : string.Empty);

        card.Controls.Add(Line(title.ToUpperInvariant(), 7.5f, palette.Muted, true), 0, 0);
        card.Controls.Add(Line(sizeLine, 12f, isBigger ? palette.Accent : palette.Text, true), 0, 1);
        card.Controls.Add(Line(dateLine, 9f, isNewer ? palette.Accent : palette.Text), 0, 2);
        card.Controls.Add(Line("Carpeta", 7.5f, palette.Muted, true), 0, 3);

        var folderLabel = Line(folder, 8.25f, palette.Muted);
        folderLabel.TextAlign = ContentAlignment.TopLeft;
        // El globo con la ruta se cuelga del cuadro (antes se creaba un ToolTip suelto que
        // nadie liberaba y se quedaba flotando en medio de la ventana).
        tips.SetToolTip(folderLabel, path);
        card.Controls.Add(folderLabel, 0, 4);
        return card;
    }
}
