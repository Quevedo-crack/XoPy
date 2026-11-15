namespace XoPy;

partial class MainForm
{
    private Button AddFilesButton = null!;
    private Button AddFolderButton = null!;
    private Button StartButton = null!;
    private Button PauseButton = null!;
    private Button StopButton = null!;
    private Button MoveUpButton = null!;
    private Button MoveDownButton = null!;
    private Button RemoveSelectedButton = null!;
    private Button ClearCompletedButton = null!;
    private Button RetryFailedButton = null!;
    private Button SaveQueueButton = null!;
    private Button LoadQueueButton = null!;
    private Button BrowseDestinationButton = null!;
    private Button OptionsButton = null!;
    private ListView QueueListView = null!;
    private ProgressBar OverallProgressBar = null!;
    private Label StatusLabel = null!;
    private NotifyIcon TrayIcon = null!;
    private TextBox DestinationTextBox = null!;
    private ComboBox OperationComboBox = null!;
    private ComboBox ConflictComboBox = null!;
    private CheckBox VerifyCheckBox = null!;
    private System.ComponentModel.IContainer? components;

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        var dark = Color.FromArgb(18, 24, 31);
        var panel = Color.FromArgb(27, 35, 44);
        var panelAlt = Color.FromArgb(36, 45, 56);
        var line = Color.FromArgb(56, 70, 84);
        var text = Color.FromArgb(235, 240, 245);
        var muted = Color.FromArgb(160, 170, 180);
        var accent = Color.FromArgb(31, 131, 224);
        var danger = Color.FromArgb(153, 59, 63);

        AddFilesButton = CreateButton("Añadir archivos", panelAlt, text);
        AddFolderButton = CreateButton("Añadir carpeta", panelAlt, text);
        StartButton = CreateButton("Iniciar", accent, Color.White);
        PauseButton = CreateButton("Pausar", panelAlt, text);
        StopButton = CreateButton("Cancelar", danger, Color.White);
        MoveUpButton = CreateButton("Subir", panelAlt, text);
        MoveDownButton = CreateButton("Bajar", panelAlt, text);
        RemoveSelectedButton = CreateButton("Quitar seleccionados", panelAlt, text);
        ClearCompletedButton = CreateButton("Limpiar finalizadas", panelAlt, text);
        RetryFailedButton = CreateButton("Reintentar fallidas", panelAlt, text);
        SaveQueueButton = CreateButton("Guardar cola", panelAlt, text);
        LoadQueueButton = CreateButton("Cargar cola", panelAlt, text);
        BrowseDestinationButton = CreateButton("Examinar…", panelAlt, text);
        OptionsButton = CreateButton("Opciones", panelAlt, text);

        DestinationTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 8, 8, 8),
            BackColor = Color.FromArgb(23, 30, 38),
            ForeColor = text,
            BorderStyle = BorderStyle.FixedSingle
        };

        OperationComboBox = CreateComboBox(110, panel, text);
        ConflictComboBox = CreateComboBox(150, panel, text);
        VerifyCheckBox = new CheckBox
        {
            Text = "Verificar con SHA-256",
            AutoSize = true,
            Checked = true,
            ForeColor = text,
            BackColor = panel,
            Margin = new Padding(14, 9, 8, 8)
        };

        QueueListView = new ListView
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 29, 37),
            ForeColor = text,
            BorderStyle = BorderStyle.None,
            FullRowSelect = true,
            HideSelection = false,
            GridLines = true,
            MultiSelect = true,
            View = View.Details,
            UseCompatibleStateImageBehavior = false
        };

        OverallProgressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous,
            Height = 12,
            Margin = new Padding(0, 8, 0, 2)
        };

        StatusLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Listo para transferir",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            ForeColor = muted,
            BackColor = panel,
            Padding = new Padding(10, 0, 0, 0)
        };

        TrayIcon = new NotifyIcon(components) { Text = "XoPy", Visible = true };

        AddFilesButton.Click += AddFilesButton_Click;
        AddFolderButton.Click += AddFolderButton_Click;
        BrowseDestinationButton.Click += BrowseDestinationButton_Click;
        StartButton.Click += StartButton_Click;
        PauseButton.Click += PauseButton_Click;
        StopButton.Click += StopButton_Click;
        MoveUpButton.Click += MoveUpButton_Click;
        MoveDownButton.Click += MoveDownButton_Click;
        RemoveSelectedButton.Click += RemoveSelectedButton_Click;
        ClearCompletedButton.Click += ClearCompletedButton_Click;
        RetryFailedButton.Click += RetryFailedButton_Click;
        SaveQueueButton.Click += SaveQueueButton_Click;
        LoadQueueButton.Click += LoadQueueButton_Click;
        OptionsButton.Click += ShowOptionsDialog;

        var menu = new MenuStrip
        {
            Dock = DockStyle.Fill,
            BackColor = dark,
            ForeColor = text,
            Renderer = new ToolStripProfessionalRenderer(new MenuColorTable())
        };

        var fileMenu = new ToolStripMenuItem("Archivo");
        fileMenu.DropDownItems.Add("Añadir archivos…", null, AddFilesButton_Click);
        fileMenu.DropDownItems.Add("Añadir carpeta…", null, AddFolderButton_Click);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("Guardar cola…", null, SaveQueueButton_Click);
        fileMenu.DropDownItems.Add("Cargar cola…", null, LoadQueueButton_Click);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("Opciones…", null, ShowOptionsDialog);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("Salir", null, (_, _) => Close());

        var transferMenu = new ToolStripMenuItem("Transferencia");
        transferMenu.DropDownItems.Add("Iniciar", null, StartButton_Click);
        transferMenu.DropDownItems.Add("Pausar / reanudar", null, PauseButton_Click);
        transferMenu.DropDownItems.Add("Cancelar", null, StopButton_Click);

        var queueMenu = new ToolStripMenuItem("Cola");
        queueMenu.DropDownItems.Add("Quitar seleccionados", null, RemoveSelectedButton_Click);
        queueMenu.DropDownItems.Add("Limpiar finalizadas", null, ClearCompletedButton_Click);
        queueMenu.DropDownItems.Add("Reintentar fallidas", null, RetryFailedButton_Click);
        queueMenu.DropDownItems.Add(new ToolStripSeparator());
        queueMenu.DropDownItems.Add("Subir", null, MoveUpButton_Click);
        queueMenu.DropDownItems.Add("Bajar", null, MoveDownButton_Click);

        var helpMenu = new ToolStripMenuItem("Ayuda");
        helpMenu.DropDownItems.Add("Acerca de XoPy", null, (_, _) => MessageBox.Show(this, "XoPy\n\nGestor gratuito y accesible de transferencias para Windows.", "Acerca de XoPy", MessageBoxButtons.OK, MessageBoxIcon.Information));

        menu.Items.AddRange(new ToolStripItem[] { fileMenu, transferMenu, queueMenu, helpMenu });

        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, BackColor = dark, WrapContents = false, Padding = new Padding(0, 6, 0, 4) };
        var title = new Label { Text = "XoPy", AutoSize = true, ForeColor = text, Font = new Font("Segoe UI", 18, FontStyle.Bold), Margin = new Padding(0, 0, 8, 0) };
        var subTitle = new Label { Text = "GESTOR DE TRANSFERENCIAS", AutoSize = true, ForeColor = muted, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(0, 12, 0, 0) };
        header.Controls.Add(title);
        header.Controls.Add(subTitle);

        var destinationCaption = new Label { Text = "DESTINO", AutoSize = true, ForeColor = muted, Font = new Font("Segoe UI", 8, FontStyle.Bold), Margin = new Padding(2, 0, 0, 4) };
        var destinationPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = panel, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(0, 0, 0, 8) };
        destinationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        destinationPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        destinationPanel.Controls.Add(DestinationTextBox, 0, 0);
        destinationPanel.Controls.Add(BrowseDestinationButton, 1, 0);

        var destinationBlock = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true, BackColor = dark, Margin = new Padding(0, 0, 0, 8) };
        destinationBlock.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        destinationBlock.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        destinationBlock.Controls.Add(destinationCaption, 0, 0);
        destinationBlock.Controls.Add(destinationPanel, 0, 1);

        var settings = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, BackColor = panel, Padding = new Padding(10, 6, 6, 6), Margin = new Padding(0, 0, 0, 8) };
        settings.Controls.Add(new Label { Text = "Operación:", AutoSize = true, ForeColor = muted, Margin = new Padding(0, 8, 4, 0) });
        settings.Controls.Add(OperationComboBox);
        settings.Controls.Add(new Label { Text = "Si existe:", AutoSize = true, ForeColor = muted, Margin = new Padding(12, 8, 4, 0) });
        settings.Controls.Add(ConflictComboBox);
        settings.Controls.Add(VerifyCheckBox);

        var primaryActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, BackColor = dark, Margin = new Padding(0, 0, 0, 6) };
        primaryActions.Controls.AddRange(new Control[] { AddFilesButton, AddFolderButton, StartButton, PauseButton, StopButton, OptionsButton });

        var queueActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, BackColor = dark, Margin = new Padding(0, 0, 0, 8) };
        queueActions.Controls.AddRange(new Control[] { MoveUpButton, MoveDownButton, RemoveSelectedButton, ClearCompletedButton, RetryFailedButton, SaveQueueButton, LoadQueueButton });

        var queueTitle = new Label { Text = "Cola de transferencias", AutoSize = true, ForeColor = text, Font = new Font("Segoe UI", 11, FontStyle.Bold), Margin = new Padding(0, 0, 0, 2) };
        var queueHint = new Label { Text = "Arrastra archivos o carpetas • Ordena por columnas • Reintenta fallidas sin restricciones", AutoSize = true, ForeColor = muted, Font = new Font("Segoe UI", 8), Margin = new Padding(0, 0, 0, 8) };
        var queueHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true, BackColor = panel, Padding = new Padding(12, 10, 12, 0) };
        queueHeader.Controls.Add(queueTitle, 0, 0);
        queueHeader.Controls.Add(queueHint, 0, 1);

        var queuePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = panel, Padding = new Padding(1), Margin = new Padding(0, 0, 0, 8) };
        queuePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        queuePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        queuePanel.Controls.Add(queueHeader, 0, 0);
        queuePanel.Controls.Add(QueueListView, 0, 1);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = panel, Padding = new Padding(10, 6, 10, 8), Margin = new Padding(0) };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        footer.Controls.Add(OverallProgressBar, 0, 0);
        footer.Controls.Add(StatusLabel, 0, 1);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, BackColor = dark, Padding = new Padding(18) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(menu, 0, 0);
        layout.Controls.Add(header, 0, 1);
        layout.Controls.Add(destinationBlock, 0, 2);
        layout.Controls.Add(settings, 0, 3);
        layout.Controls.Add(primaryActions, 0, 4);
        layout.Controls.Add(queueActions, 0, 5);
        layout.Controls.Add(queuePanel, 0, 6);
        layout.Controls.Add(footer, 0, 7);

        Text = "XoPy — Gestor de transferencias";
        BackColor = dark;
        ForeColor = text;
        Font = new Font("Segoe UI", 9);
        MinimumSize = new Size(960, 640);
        Size = new Size(1280, 820);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(layout);
    }

    private static Button CreateButton(string text, Color backColor, Color foreColor) => new()
    {
        Text = text,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        BackColor = backColor,
        ForeColor = foreColor,
        Font = new Font("Segoe UI", 9, FontStyle.Bold),
        Padding = new Padding(10, 6, 10, 6),
        Margin = new Padding(0, 0, 8, 6),
        Cursor = Cursors.Hand
    };

    private static ComboBox CreateComboBox(int width, Color backColor, Color foreColor) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = width,
        BackColor = backColor,
        ForeColor = foreColor,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(0, 4, 8, 4)
    };
}

internal sealed class MenuColorTable : ProfessionalColorTable
{
    public override Color MenuBorder => Color.FromArgb(56, 70, 84);
    public override Color MenuItemBorder => Color.FromArgb(56, 70, 84);
    public override Color MenuItemSelected => Color.FromArgb(31, 131, 224);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(31, 131, 224);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(31, 131, 224);
    public override Color MenuStripGradientBegin => Color.FromArgb(18, 24, 31);
    public override Color MenuStripGradientEnd => Color.FromArgb(18, 24, 31);
    public override Color ToolStripDropDownBackground => Color.FromArgb(27, 35, 44);
    public override Color ImageMarginGradientBegin => Color.FromArgb(27, 35, 44);
    public override Color ImageMarginGradientEnd => Color.FromArgb(27, 35, 44);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(27, 35, 44);
}
