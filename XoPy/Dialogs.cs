using System.Drawing;
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
    private readonly CheckBox _explorer;
    private readonly CheckBox _sound;
    private readonly CheckBox _openDestination;
    private readonly CheckBox _closeWhenDone;
    private readonly CheckBox _shutdown;
    private readonly ListBox _favorites;

    public AppSettings Result { get; }

    public OptionsDialog(AppSettings settings, Palette palette)
    {
        _palette = palette;
        Result = CloneSettings(settings);
        _favorites = new ListBox();

        Text = "Preferencias de XoPy";
        BackColor = palette.Canvas;
        ForeColor = palette.Text;
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(760, 560);

        _tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(100, 28),
            Padding = new Point(12, 6),
            DrawMode = TabDrawMode.OwnerDrawFixed,
            Font = new Font("Segoe UI", 9f)
        };
        _tabs.DrawItem += DrawTab;

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

        _explorer = Check("Añadir XoPy al menú contextual del Explorador", settings.ExplorerIntegration);

        _sound = Check("Reproducir un sonido al terminar", settings.PlaySoundWhenDone);
        _openDestination = Check("Abrir la carpeta de destino al terminar", settings.OpenDestinationWhenDone);
        _closeWhenDone = Check("Cerrar XoPy al terminar", settings.CloseWhenDone);
        _shutdown = Check("Apagar el equipo al terminar", settings.ShutdownWhenDone);

        _favorites.Items.AddRange(settings.Favorites.Cast<object>().ToArray());

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
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.Controls.Add(_tabs, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void BuildPages()
    {
        _tabs.TabPages.Add(Page("General", 6,
            ("Carpeta de destino predeterminada", _destination, 48),
            ("Tema de la interfaz", _theme, 48),
            ("", _confirmDelete, 40),
            ("", _minimizeToTray, 40),
            ("", _startWithWindows, 40),
            ("", _startMinimized, 40)));

        _tabs.TabPages.Add(Page("Transferencia", 5,
            ("Transferencias simultáneas", _parallel, 48),
            ("Búfer por archivo (KiB)", _buffer, 48),
            ("Límite de velocidad (MiB/s, 0 = sin límite)", _speed, 48),
            ("", _preserveTimestamps, 40),
            ("", _recycleBin, 40)));

        var verifyPage = Page("Verificación", 3,
            ("Algoritmo", _verifyAlgorithm, 48),
            ("", _verifyAfterCopy, 40),
            ("", new Label
            {
                Text = "La verificación compara el archivo copiado con el original. Con SHA-256\n" +
                       "puedes estar seguro de que la copia es idéntica bit a bit, a costa de\n" +
                       "leer los archivos dos veces. Desactívala para copiar a máxima velocidad.",
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

        _tabs.TabPages.Add(Page("Integración", 2,
            ("", _explorer, 40),
            ("", new Label
            {
                Text = "El Explorador de Windows no permite interceptar Ctrl+C / Ctrl+V desde\n" +
                       "un programa normal. XoPy se integra con estas acciones del menú\n" +
                       "contextual: Copiar con XoPy, Mover con XoPy, Verificar con XoPy y\n" +
                       "Pegar con XoPy en el fondo de una carpeta.",
                AutoSize = true,
                ForeColor = _palette.Muted,
                Font = new Font("Segoe UI", 8.5f),
                BackColor = Color.Transparent
            }, 120)));

        _tabs.TabPages.Add(Page("Al terminar", 4,
            ("", _sound, 42),
            ("", _openDestination, 42),
            ("", _closeWhenDone, 42),
            ("", _shutdown, 42)));

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
        var page = new TabPage(title) { BackColor = _palette.Canvas, Padding = new Padding(18), AutoScroll = true };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = _palette.Canvas,
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.RowCount = items.Length + 1;
        var row = 0;
        foreach (var (label, control, height) in items)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            var text = new Label
            {
                Text = label,
                AutoSize = true,
                ForeColor = _palette.Muted,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 0, 14, 0)
            };
            control.Anchor = control is Label ? AnchorStyles.Left : AnchorStyles.Left;
            control.Margin = new Padding(0, 0, 0, 0);
            layout.Controls.Add(text, 0, row);
            layout.Controls.Add(control, 1, row);
            row++;
        }
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);
        return page;
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

    private NumericUpDown Numeric(int value, int min, int max, int step) => new()
    {
        Minimum = min,
        Maximum = max,
        Increment = step,
        Value = Math.Clamp(value, min, max),
        Width = 120,
        BackColor = _palette.Surface,
        ForeColor = _palette.Text,
        BorderStyle = BorderStyle.FixedSingle
    };

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
        Result.Verify = (VerifyAlgorithm)_verifyAlgorithm.SelectedIndex;
        Result.VerifyAfterCopy = _verifyAfterCopy.Checked;
        Result.Conflict = (ConflictPolicy)_conflict.SelectedIndex;
        Result.OnError = (ErrorPolicy)_onError.SelectedIndex;
        Result.RetryCount = (int)_retries.Value;
        Result.ExplorerIntegration = _explorer.Checked;
        Result.PlaySoundWhenDone = _sound.Checked;
        Result.OpenDestinationWhenDone = _openDestination.Checked;
        Result.CloseWhenDone = _closeWhenDone.Checked;
        Result.ShutdownWhenDone = _shutdown.Checked;
        Result.Favorites = _favorites.Items.Cast<object>().Select(entry => entry.ToString() ?? string.Empty)
            .Where(path => path.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
        target.BufferKiB = Result.BufferKiB;
        target.SpeedLimitMiB = Result.SpeedLimitMiB;
        target.PreserveTimestamps = Result.PreserveTimestamps;
        target.UseRecycleBin = Result.UseRecycleBin;
        target.Verify = Result.Verify;
        target.VerifyAfterCopy = Result.VerifyAfterCopy;
        target.Conflict = Result.Conflict;
        target.OnError = Result.OnError;
        target.RetryCount = Result.RetryCount;
        target.ExplorerIntegration = Result.ExplorerIntegration;
        target.PlaySoundWhenDone = Result.PlaySoundWhenDone;
        target.OpenDestinationWhenDone = Result.OpenDestinationWhenDone;
        target.CloseWhenDone = Result.CloseWhenDone;
        target.ShutdownWhenDone = Result.ShutdownWhenDone;
        target.Favorites = Result.Favorites.ToList();
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
        BufferKiB = source.BufferKiB,
        SpeedLimitMiB = source.SpeedLimitMiB,
        PreserveTimestamps = source.PreserveTimestamps,
        UseRecycleBin = source.UseRecycleBin,
        Verify = source.Verify,
        VerifyAfterCopy = source.VerifyAfterCopy,
        Conflict = source.Conflict,
        OnError = source.OnError,
        RetryCount = source.RetryCount,
        ExplorerIntegration = source.ExplorerIntegration,
        PlaySoundWhenDone = source.PlaySoundWhenDone,
        OpenDestinationWhenDone = source.OpenDestinationWhenDone,
        CloseWhenDone = source.CloseWhenDone,
        ShutdownWhenDone = source.ShutdownWhenDone,
        Favorites = source.Favorites.ToList()
    };
}

internal sealed class HistoryDialog : Form
{
    public HistoryDialog(List<HistoryEntry> entries, Palette palette)
    {
        Text = "Historial de transferencias";
        BackColor = palette.Canvas;
        ForeColor = palette.Text;
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(840, 480);
        MinimumSize = new Size(680, 380);
        ShowInTaskbar = false;

        var list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            BorderStyle = BorderStyle.None,
            BackColor = palette.Surface,
            ForeColor = palette.Text,
            Font = new Font("Segoe UI", 9f),
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        list.Columns.Add("Fecha", 130);
        list.Columns.Add("Operación", 90);
        list.Columns.Add("Destino", 250);
        list.Columns.Add("Archivos", 80, HorizontalAlignment.Right);
        list.Columns.Add("Tamaño", 90, HorizontalAlignment.Right);
        list.Columns.Add("Velocidad", 100, HorizontalAlignment.Right);
        list.Columns.Add("Resultado", 110);

        foreach (var entry in entries)
        {
            list.Items.Add(new ListViewItem(new[]
            {
                entry.FinishedAt.ToString("g"),
                entry.OperationText,
                entry.Destination,
                entry.FileCount.ToString(),
                Format.Bytes(entry.Bytes),
                Format.Speed(entry.BytesPerSecond),
                entry.ResultText
            }));
        }

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
        export.Click += (_, _) =>
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
                foreach (var entry in entries)
                    builder.AppendLine($"{entry.FinishedAt:yyyy-MM-dd HH:mm:ss};{entry.OperationText};{entry.Destination};{entry.FileCount};{entry.Bytes};{entry.Seconds:0.0};{entry.Errors}");
                File.WriteAllText(dialog.FileName, builder.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo exportar: " + ex.Message, "XoPy", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        buttons.Controls.Add(close);
        buttons.Controls.Add(export);

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
        root.Controls.Add(list, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
    }
}
