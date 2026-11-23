using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace XoPy;

internal sealed class PromptDialog : Form
{
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

        var detailBox = new TextBox
        {
            Text = (detail ?? string.Empty).Replace("\r\n", "\n").Replace("\n", "\r\n"),
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.None,
            Size = new Size(560, 76),
            TabStop = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = palette.SurfaceAlt,
            ForeColor = palette.Muted,
            Font = new Font("Segoe UI", 8.25f),
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

        Shown += (_, _) => { detailBox.Select(0, 0); ActiveControl = null; };
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
    private readonly Palette _palette;
    private readonly TabControl _tabs;
    private readonly TextBox _destination;
    private readonly ChoiceButton _theme;
    private readonly CheckBox _confirmDelete;
    private readonly CheckBox _minimizeToTray;
    private readonly CheckBox _startWithWindows;
    private readonly CheckBox _startMinimized;
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
        // Debe caber entera dentro del área de trabajo: si es más alta que la pantalla,
        // los botones Guardar/Cancelar quedan debajo de la barra de tareas.
        ClientSize = new Size(Math.Min(1000, Math.Max(820, workArea.Width - 120)),
            Math.Min(740, Math.Max(520, workArea.Height - 40)));
        MinimumSize = new Size(840, 560);

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(112, 30),
            Padding = new Point(12, 6),
            DrawMode = TabDrawMode.OwnerDrawFixed,
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
        _startMinimized = Check("Arrancar minimizado en el área de notificación", settings.StartMinimized);

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
        var tabs = _tabs;
        tabs.Visible = true;
        tabs.BringToFront();
        var host = tabs.Parent as TableLayoutPanel;
        if (host is not null)
        {
            host.RowStyles[0].Height = 0;
            host.RowStyles[1].Height = 100;
            host.RowStyles[1].SizeType = SizeType.Percent;
            var main = host.GetControlFromPosition(0, 0);
            if (main is not null) main.Visible = false;
        }
        FitTabWidths();

        // Al cerrar las pestañas se vuelve a la pantalla principal.
        var back = Ui.DialogButton("← Volver", _palette);
        back.Size = new Size(110, 28);
        back.Location = new Point(14, 8);
        back.Click += (_, _) =>
        {
            tabs.Visible = false;
            if (host is not null)
            {
                host.RowStyles[0].SizeType = SizeType.Percent;
                host.RowStyles[0].Height = 100;
                host.RowStyles[1].SizeType = SizeType.Absolute;
                host.RowStyles[1].Height = 0;
                var main = host.GetControlFromPosition(0, 0);
                if (main is not null) main.Visible = true;
            }
            back.Visible = false;
        };
        tabs.Controls.Add(back);
        back.Parent = tabs;
        back.BringToFront();
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
        _tabs.TabPages.Add(Page("Destino predeterminado", 4,
            ("Carpeta de destino predeterminada", _destination, 48),
            ("Tema de la interfaz", _theme, 48),
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
            ("Máximo de hilos (modo multihilo)", _parallel, 44),
            ("Búfer por archivo (KiB)", _buffer, 44),
            ("Límite de velocidad (MiB/s, 0 = sin límite)", _speed, 44),
            ("", _preserveTimestamps, 36),
            ("", _recycleBin, 36),
            ("", _copyLocked, 32),
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

        _tabs.TabPages.Add(Page("Al terminar", 5,
            ("", _sound, 42),
            ("", _openDestination, 42),
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
            layout.Controls.Add(text, 0, row);
            layout.Controls.Add(control, 1, row);
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
        var width = Math.Max(84, widest + 34);
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
    private readonly List<HistoryEntry> _entries;
    private readonly ListView _list;
    private readonly Font _headerFont = new("Segoe UI", 9f, FontStyle.Bold);

    public bool Modified { get; private set; }
    public IReadOnlyList<HistoryEntry> Entries => _entries;

    public HistoryDialog(List<HistoryEntry> entries, Palette palette)
    {
        _entries = entries.ToList();

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
        _list.DrawItem += (_, e) => e.DrawDefault = true;
        _list.Resize += (_, _) => FitDestinationColumn();
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
        root.Controls.Add(_list, 0, 0);
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
        var width = Math.Max(140, _list.ClientSize.Width - fixedWidth - 4);
        if (_list.Columns[2].Width != width) _list.Columns[2].Width = width;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _headerFont.Dispose();
        base.Dispose(disposing);
    }

    private void FillList()
    {
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
