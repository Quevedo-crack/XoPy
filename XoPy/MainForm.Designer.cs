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
        var background = Color.FromArgb(22, 27, 34);
        var surface = Color.FromArgb(32, 39, 48);
        var surfaceAlt = Color.FromArgb(43, 52, 63);
        var foreground = Color.FromArgb(235, 239, 244);
        var muted = Color.FromArgb(158, 169, 181);
        var accent = Color.FromArgb(35, 136, 220);

        AddFilesButton = CreateButton("Añadir archivos", surfaceAlt, foreground);
        AddFolderButton = CreateButton("Añadir carpeta", surfaceAlt, foreground);
        StartButton = CreateButton("Iniciar", accent, Color.White);
        PauseButton = CreateButton("Pausar", surfaceAlt, foreground);
        StopButton = CreateButton("Cancelar", Color.FromArgb(117, 55, 59), foreground);
        MoveUpButton = CreateButton("Subir", surfaceAlt, foreground);
        MoveDownButton = CreateButton("Bajar", surfaceAlt, foreground);
        RemoveSelectedButton = CreateButton("Quitar seleccionados", surfaceAlt, foreground);
        ClearCompletedButton = CreateButton("Limpiar finalizadas", surfaceAlt, foreground);
        RetryFailedButton = CreateButton("Reintentar fallidas", surfaceAlt, foreground);
        SaveQueueButton = CreateButton("Guardar cola", surfaceAlt, foreground);
        LoadQueueButton = CreateButton("Cargar cola", surfaceAlt, foreground);
        BrowseDestinationButton = CreateButton("Examinar…", surfaceAlt, foreground);
        DestinationTextBox = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(6, 8, 8, 8), BackColor = Color.FromArgb(25, 31, 39), ForeColor = foreground, BorderStyle = BorderStyle.FixedSingle };
        OperationComboBox = CreateComboBox(105, surface, foreground);
        ConflictComboBox = CreateComboBox(145, surface, foreground);
        VerifyCheckBox = new CheckBox { Text = "Verificar con SHA-256", AutoSize = true, Checked = true, ForeColor = foreground, BackColor = surface, Margin = new Padding(12, 9, 8, 8) };
        QueueListView = new ListView { Dock = DockStyle.Fill, BackColor = Color.FromArgb(25, 31, 39), ForeColor = foreground, BorderStyle = BorderStyle.None, UseCompatibleStateImageBehavior = false, FullRowSelect = true, MultiSelect = true, HideSelection = false, GridLines = true, View = View.Details };
        OverallProgressBar = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous, Height = 12, Margin = new Padding(0, 6, 0, 4) };
        StatusLabel = new Label { Dock = DockStyle.Fill, Text = "Listo para transferir", TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, ForeColor = muted, BackColor = surface, Padding = new Padding(10, 0, 0, 0) };
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

        var menu = new MenuStrip { Dock = DockStyle.Fill, BackColor = background, ForeColor = foreground, Padding = new Padding(0, 4, 0, 6) };
        var fileMenu = new ToolStripMenuItem("Archivo");
        fileMenu.DropDownItems.Add("Añadir archivos…", null, AddFilesButton_Click);
        fileMenu.DropDownItems.Add("Añadir carpeta…", null, AddFolderButton_Click);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("Guardar cola…", null, SaveQueueButton_Click);
        fileMenu.DropDownItems.Add("Cargar cola…", null, LoadQueueButton_Click);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("Salir", null, (_, _) => FindForm()?.Close());

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
        helpMenu.DropDownItems.Add("Acerca de XoPy", null, (_, _) => MessageBox.Show("XoPy — Gestor de transferencias\n\nAplicación personal de código abierto para Windows.", "Acerca de XoPy", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.AddRange(new ToolStripItem[] { fileMenu, transferMenu, queueMenu, helpMenu });

        var title = new Label { Text = "XoPy", AutoSize = true, ForeColor = foreground, Font = new Font("Segoe UI", 18, FontStyle.Bold), Margin = new Padding(0, 0, 12, 0) };
        var subtitle = new Label { Text = "GESTOR DE TRANSFERENCIAS", AutoSize = true, ForeColor = muted, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(0, 12, 0, 0) };
        var brand = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, BackColor = background, Padding = new Padding(0, 4, 0, 8) };
        brand.Controls.Add(title);
        brand.Controls.Add(subtitle);

        var destinationTitle = new Label { Text = "DESTINO", AutoSize = true, ForeColor = muted, Font = new Font("Segoe UI", 8, FontStyle.Bold), Margin = new Padding(2, 0, 0, 4) };
        var destinationRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = surface, Padding = new Padding(10, 4, 10, 4), Margin = new Padding(0, 0, 0, 8) };
        destinationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        destinationRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        destinationRow.Controls.Add(DestinationTextBox, 0, 0);
        destinationRow.Controls.Add(BrowseDestinationButton, 1, 0);
        var destinationBlock = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true, BackColor = background, Margin = new Padding(0, 0, 0, 8) };
        destinationBlock.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        destinationBlock.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        destinationBlock.Controls.Add(destinationTitle, 0, 0);
        destinationBlock.Controls.Add(destinationRow, 0, 1);

        var settings = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, BackColor = surface, Padding = new Padding(10, 5, 6, 2), Margin = new Padding(0, 0, 0, 8) };
        settings.Controls.Add(new Label { Text = "Operación:", AutoSize = true, ForeColor = muted, Margin = new Padding(0, 8, 4, 0) });
        settings.Controls.Add(OperationComboBox);
        settings.Controls.Add(new Label { Text = "Si el destino existe:", AutoSize = true, ForeColor = muted, Margin = new Padding(12, 8, 4, 0) });
        settings.Controls.Add(ConflictComboBox);
        settings.Controls.Add(VerifyCheckBox);

        var primaryActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, BackColor = background, Margin = new Padding(0, 0, 0, 6) };
        primaryActions.Controls.AddRange(new Control[] { AddFilesButton, AddFolderButton, StartButton, PauseButton, StopButton });
        var queueActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, BackColor = background, Margin = new Padding(0, 0, 0, 8) };
        queueActions.Controls.AddRange(new Control[] { MoveUpButton, MoveDownButton, RemoveSelectedButton, ClearCompletedButton, RetryFailedButton, SaveQueueButton, LoadQueueButton });

        var queueTitle = new Label { Text = "Cola de transferencias", AutoSize = true, ForeColor = foreground, Font = new Font("Segoe UI", 11, FontStyle.Bold), Margin = new Padding(0, 0, 0, 2) };
        var queueHint = new Label { Text = "Arrastra archivos o carpetas · Supr para quitar la selección · selecciona una cabecera para ordenar", AutoSize = true, ForeColor = muted, Font = new Font("Segoe UI", 8), Margin = new Padding(0, 0, 0, 8) };
        var queueHeading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true, BackColor = surface, Padding = new Padding(12, 10, 12, 0) };
        queueHeading.Controls.Add(queueTitle, 0, 0);
        queueHeading.Controls.Add(queueHint, 0, 1);
        var queueCard = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = surface, Padding = new Padding(1), Margin = new Padding(0, 0, 0, 8) };
        queueCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        queueCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        queueCard.Controls.Add(queueHeading, 0, 0);
        queueCard.Controls.Add(QueueListView, 0, 1);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = surface, Padding = new Padding(10, 4, 10, 6), Margin = new Padding(0) };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        footer.Controls.Add(OverallProgressBar, 0, 0);
        footer.Controls.Add(StatusLabel, 0, 1);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, BackColor = background, Padding = new Padding(18) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(menu, 0, 0);
        layout.Controls.Add(brand, 0, 1);
        layout.Controls.Add(destinationBlock, 0, 2);
        layout.Controls.Add(settings, 0, 3);
        layout.Controls.Add(primaryActions, 0, 4);
        layout.Controls.Add(queueActions, 0, 5);
        layout.Controls.Add(queueCard, 0, 6);
        layout.Controls.Add(footer, 0, 7);

        Text = "XoPy — Gestor de transferencias";
        BackColor = background;
        ForeColor = foreground;
        Font = new Font("Segoe UI", 9);
        MinimumSize = new Size(960, 640);
        Size = new Size(1280, 820);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
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
