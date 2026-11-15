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
    private TabControl WorkspaceTabs = null!;
    private Label QueueCountLabel = null!;
    private Label ActiveCountLabel = null!;
    private Label CompletedCountLabel = null!;
    private Label FailedCountLabel = null!;
    private Label QueueSummaryLabel = null!;
    private Label ActiveSummaryLabel = null!;
    private Label CompletedSummaryLabel = null!;
    private Label FailedSummaryLabel = null!;
    private System.ComponentModel.IContainer? components;

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        var canvas = Color.FromArgb(21, 23, 27);
        var surface = Color.FromArgb(29, 32, 37);
        var raised = Color.FromArgb(38, 42, 48);
        var border = Color.FromArgb(54, 59, 67);
        var foreground = Color.FromArgb(229, 232, 236);
        var secondary = Color.FromArgb(151, 158, 168);
        var blue = Color.FromArgb(35, 136, 226);
        var green = Color.FromArgb(62, 177, 115);
        var orange = Color.FromArgb(226, 167, 58);

        AddFilesButton = CreateButton("＋  Añadir archivos", raised, foreground, border);
        AddFolderButton = CreateButton("▣  Añadir carpeta", raised, foreground, border);
        StartButton = CreateButton("▶  Iniciar copia", blue, Color.White, blue);
        PauseButton = CreateButton("Ⅱ  Pausar", raised, foreground, border);
        StopButton = CreateButton("■  Detener", raised, foreground, border);
        MoveUpButton = CreateButton("↑", raised, foreground, border);
        MoveDownButton = CreateButton("↓", raised, foreground, border);
        RemoveSelectedButton = CreateButton("Quitar", raised, foreground, border);
        ClearCompletedButton = CreateButton("Limpiar", raised, foreground, border);
        RetryFailedButton = CreateButton("Reintentar", raised, foreground, border);
        SaveQueueButton = CreateButton("Guardar cola", raised, foreground, border);
        LoadQueueButton = CreateButton("Abrir cola", raised, foreground, border);
        BrowseDestinationButton = CreateButton("Examinar…", raised, foreground, border);
        OptionsButton = CreateButton("⚙  Preferencias", raised, foreground, border);

        DestinationTextBox = new TextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 22, 26), ForeColor = foreground, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 5, 8, 5) };
        OperationComboBox = CreateComboBox(150, raised, foreground);
        ConflictComboBox = CreateComboBox(170, raised, foreground);
        VerifyCheckBox = new CheckBox { Text = "Verificar archivos con SHA-256", AutoSize = true, Checked = true, ForeColor = foreground, BackColor = surface, Margin = new Padding(0, 8, 8, 8) };

        QueueListView = new ListView
        {
            Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 26, 30), ForeColor = foreground,
            BorderStyle = BorderStyle.None, FullRowSelect = true, HideSelection = false,
            GridLines = false, MultiSelect = true, View = View.Details,
            UseCompatibleStateImageBehavior = false, OwnerDraw = true, HeaderStyle = ColumnHeaderStyle.Clickable
        };
        QueueListView.DrawColumnHeader += (_, e) =>
        {
            using var brush = new SolidBrush(raised);
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, QueueListView.Font, Rectangle.Inflate(e.Bounds, -9, 0), secondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        QueueListView.DrawItem += (_, e) => { if (QueueListView.View != View.Details) e.DrawDefault = true; };
        QueueListView.DrawSubItem += (_, e) =>
        {
            var selected = e.Item.Selected;
            var rowColor = selected ? Color.FromArgb(24, 70, 111) : (e.ItemIndex % 2 == 0 ? Color.FromArgb(24, 26, 30) : Color.FromArgb(27, 29, 33));
            using var brush = new SolidBrush(rowColor);
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, QueueListView.Font, Rectangle.Inflate(e.Bounds, -9, 0), selected ? Color.White : foreground, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };

        OverallProgressBar = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous, Height = 8, Margin = new Padding(0, 3, 0, 3) };
        StatusLabel = new Label { Dock = DockStyle.Fill, Text = "Listo para transferir", TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, ForeColor = secondary, BackColor = surface, Padding = new Padding(10, 0, 0, 0) };
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

        var menu = new MenuStrip { Dock = DockStyle.Fill, BackColor = canvas, ForeColor = foreground, Renderer = new ToolStripProfessionalRenderer(new XoPyMenuColorTable()) };
        var appMenu = new ToolStripMenuItem("XoPy");
        appMenu.DropDownItems.Add("Añadir archivos…", null, AddFilesButton_Click);
        appMenu.DropDownItems.Add("Añadir carpeta…", null, AddFolderButton_Click);
        appMenu.DropDownItems.Add(new ToolStripSeparator());
        appMenu.DropDownItems.Add("Abrir cola…", null, LoadQueueButton_Click);
        appMenu.DropDownItems.Add("Guardar cola…", null, SaveQueueButton_Click);
        appMenu.DropDownItems.Add(new ToolStripSeparator());
        appMenu.DropDownItems.Add("Historial", null, ShowHistoryDialog);
        appMenu.DropDownItems.Add("Preferencias…", null, ShowOptionsDialog);
        appMenu.DropDownItems.Add(new ToolStripSeparator());
        appMenu.DropDownItems.Add("Salir", null, (_, _) => Close());
        var transferMenu = new ToolStripMenuItem("Transferencia");
        transferMenu.DropDownItems.Add("Iniciar", null, StartButton_Click);
        transferMenu.DropDownItems.Add("Pausar / reanudar", null, PauseButton_Click);
        transferMenu.DropDownItems.Add("Detener", null, StopButton_Click);
        var queueMenu = new ToolStripMenuItem("Cola");
        queueMenu.DropDownItems.Add("Reintentar fallidas", null, RetryFailedButton_Click);
        queueMenu.DropDownItems.Add("Quitar seleccionadas", null, RemoveSelectedButton_Click);
        queueMenu.DropDownItems.Add("Limpiar completadas", null, ClearCompletedButton_Click);
        var helpMenu = new ToolStripMenuItem("Ayuda");
        helpMenu.DropDownItems.Add("Acerca de XoPy", null, (_, _) => MessageBox.Show(this, "XoPy\nGestor de transferencias para Windows.", "Acerca de XoPy", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.AddRange(new ToolStripItem[] { appMenu, transferMenu, queueMenu, helpMenu });

        QueueCountLabel = CreateSidebarCount(foreground);
        ActiveCountLabel = CreateSidebarCount(blue);
        CompletedCountLabel = CreateSidebarCount(green);
        FailedCountLabel = CreateSidebarCount(orange);
        QueueSummaryLabel = CreateSummaryValue(foreground);
        ActiveSummaryLabel = CreateSummaryValue(blue);
        CompletedSummaryLabel = CreateSummaryValue(green);
        FailedSummaryLabel = CreateSummaryValue(orange);

        var brand = new Label { Text = "XoPy", AutoSize = true, ForeColor = foreground, Font = new Font("Segoe UI", 19, FontStyle.Bold), Margin = new Padding(0, 1, 0, 0) };
        var brandTag = new Label { Text = "FILE TRANSFER", AutoSize = true, ForeColor = secondary, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 0) };
        var brandRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, BackColor = canvas, Margin = new Padding(0, 0, 0, 15) };
        brandRow.Controls.Add(brand);
        brandRow.Controls.Add(brandTag);

        var sideMenu = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7, BackColor = canvas, Padding = new Padding(0, 5, 0, 0) };
        sideMenu.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sideMenu.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddSidebarRow(sideMenu, 0, "▤   Todas las transferencias", QueueCountLabel, foreground);
        AddSidebarRow(sideMenu, 1, "◷   En curso", ActiveCountLabel, foreground);
        AddSidebarRow(sideMenu, 2, "✓   Completadas", CompletedCountLabel, foreground);
        AddSidebarRow(sideMenu, 3, "!   Con errores", FailedCountLabel, foreground);
        sideMenu.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        var sectionLabel = new Label { Text = "ESPACIO DE TRABAJO", AutoSize = true, ForeColor = secondary, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Margin = new Padding(9, 8, 0, 4) };
        sideMenu.Controls.Add(sectionLabel, 0, 4);
        sideMenu.SetColumnSpan(sectionLabel, 2);
        AddSidebarNavigation(sideMenu, 5, "▧   Historial", (_, _) => ShowHistoryDialog(this, EventArgs.Empty), foreground);
        AddSidebarNavigation(sideMenu, 6, "⚙   Preferencias", ShowOptionsDialog, foreground);

        var sideFooter = new Label { Text = "Transferencias locales\nTodas las opciones disponibles", AutoSize = true, ForeColor = secondary, Font = new Font("Segoe UI", 8), Dock = DockStyle.Fill, Padding = new Padding(8, 8, 0, 2) };
        var sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = canvas, Padding = new Padding(13, 16, 11, 12) };
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.Controls.Add(brandRow, 0, 0);
        sidebar.Controls.Add(sideMenu, 0, 1);
        sidebar.Controls.Add(sideFooter, 0, 2);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, BackColor = canvas, Margin = new Padding(0, 4, 0, 7) };
        toolbar.Controls.AddRange(new Control[] { AddFilesButton, AddFolderButton, StartButton, PauseButton, StopButton });
        var toolbarRight = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = canvas };
        toolbarRight.Controls.Add(OptionsButton);
        var toolbarRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = canvas };
        toolbarRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbarRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbarRow.Controls.Add(toolbar, 0, 0);
        toolbarRow.Controls.Add(toolbarRight, 1, 0);

        var pageTitle = new Label { Text = "Transferencias", AutoSize = true, ForeColor = foreground, Font = new Font("Segoe UI", 17, FontStyle.Bold), Margin = new Padding(0, 0, 0, 1) };
        var subtitle = new Label { Text = "Administra, supervisa y verifica tus copias", AutoSize = true, ForeColor = secondary, Font = new Font("Segoe UI", 9), Margin = new Padding(1, 0, 0, 12) };
        var titleBlock = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, AutoSize = true, BackColor = canvas, Margin = new Padding(0, 10, 0, 2) };
        titleBlock.Controls.Add(pageTitle, 0, 0);
        titleBlock.Controls.Add(subtitle, 0, 1);

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = canvas, Margin = new Padding(0, 0, 0, 12), Height = 66 };
        for (var i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        cards.Controls.Add(CreateSummaryCard("EN COLA", "Pendientes", QueueSummaryLabel, foreground, surface), 0, 0);
        cards.Controls.Add(CreateSummaryCard("ACTIVAS", "Transfiriendo", ActiveSummaryLabel, blue, surface), 1, 0);
        cards.Controls.Add(CreateSummaryCard("COMPLETADAS", "Finalizadas", CompletedSummaryLabel, green, surface), 2, 0);
        cards.Controls.Add(CreateSummaryCard("ATENCIÓN", "Fallidas", FailedSummaryLabel, orange, surface), 3, 0);

        WorkspaceTabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(15, 6), Font = new Font("Segoe UI", 9), BackColor = surface, ForeColor = foreground };
        var listPage = CreateTab("Lista");
        var targetPage = CreateTab("Destino");
        var optionsPage = CreateTab("Opciones");
        var statusPage = CreateTab("Estado");
        var logPage = CreateTab("Registro");

        var listHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = surface, Padding = new Padding(12, 8, 12, 4) };
        listHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        listHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        listHeader.Controls.Add(new Label { Text = "Cola de archivos", AutoSize = true, ForeColor = foreground, Font = new Font("Segoe UI", 10, FontStyle.Bold), Anchor = AnchorStyles.Left }, 0, 0);
        var listTools = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true, BackColor = surface };
        listTools.Controls.AddRange(new Control[] { LoadQueueButton, SaveQueueButton, RetryFailedButton, ClearCompletedButton, RemoveSelectedButton, MoveDownButton, MoveUpButton });
        listHeader.Controls.Add(listTools, 1, 0);
        var listLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = surface, Padding = new Padding(1) };
        listLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        listLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        listLayout.Controls.Add(listHeader, 0, 0);
        listLayout.Controls.Add(QueueListView, 0, 1);
        listPage.Controls.Add(listLayout);

        var destinationLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = surface };
        destinationLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        destinationLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        destinationLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        destinationLayout.Controls.Add(CreatePageHeading("Destino de copia", "Elige dónde se guardarán los archivos. Cada carpeta conserva su nombre y estructura."), 0, 0);
        var destinationFields = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 2, BackColor = surface, Padding = new Padding(20, 12, 20, 12), AutoSize = true };
        destinationFields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        destinationFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        destinationFields.Controls.Add(CreateFieldLabel("Carpeta destino", secondary), 0, 0);
        destinationFields.Controls.Add(DestinationTextBox, 1, 0);
        destinationFields.Controls.Add(new Label { Text = "Las carpetas seleccionadas se copiarán dentro de una carpeta con el mismo nombre.", AutoSize = true, ForeColor = secondary, Margin = new Padding(0, 5, 0, 3) }, 1, 1);
        destinationLayout.Controls.Add(destinationFields, 0, 1);
        var browseRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = surface, Padding = new Padding(20, 0, 20, 12) };
        browseRow.Controls.Add(BrowseDestinationButton);
        destinationLayout.Controls.Add(browseRow, 0, 2);
        targetPage.Controls.Add(destinationLayout);

        var optionsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = surface };
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        optionsLayout.Controls.Add(CreatePageHeading("Opciones de transferencia", "Ajustes disponibles para todos los usuarios; no hay funciones bloqueadas."), 0, 0);
        var optionsFields = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 4, BackColor = surface, Padding = new Padding(20, 10, 20, 16), AutoSize = true };
        optionsFields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        optionsFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        optionsFields.Controls.Add(CreateFieldLabel("Acción", secondary), 0, 0);
        optionsFields.Controls.Add(OperationComboBox, 1, 0);
        optionsFields.Controls.Add(CreateFieldLabel("Si el archivo ya existe", secondary), 0, 1);
        optionsFields.Controls.Add(ConflictComboBox, 1, 1);
        optionsFields.Controls.Add(CreateFieldLabel("Integridad", secondary), 0, 2);
        optionsFields.Controls.Add(VerifyCheckBox, 1, 2);
        optionsFields.Controls.Add(CreateFieldLabel("Límite de velocidad", secondary), 0, 3);
        optionsFields.Controls.Add(new Label { Text = "Configúralo en Preferencias (0 = sin límite)", AutoSize = true, ForeColor = secondary, Margin = new Padding(0, 8, 0, 8) }, 1, 3);
        optionsLayout.Controls.Add(optionsFields, 0, 1);
        optionsPage.Controls.Add(optionsLayout);

        var statusLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = surface };
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        statusLayout.Controls.Add(CreatePageHeading("Estado de la transferencia", "Supervisa el avance global de la cola."), 0, 0);
        var progressPanel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2, BackColor = surface, Padding = new Padding(22), AutoSize = true };
        progressPanel.Controls.Add(new Label { Text = "PROGRESO GLOBAL", AutoSize = true, ForeColor = secondary, Font = new Font("Segoe UI", 8, FontStyle.Bold), Margin = new Padding(0, 0, 0, 7) }, 0, 0);
        var statusProgress = new ProgressBar { Dock = DockStyle.Top, Minimum = 0, Maximum = 100, Height = 12, Style = ProgressBarStyle.Continuous };
        progressPanel.Controls.Add(statusProgress, 0, 1);
        statusPage.Controls.Add(statusLayout);
        statusLayout.Controls.Add(progressPanel, 0, 1);
        OverallProgressBar.ValueChanged += (_, _) => statusProgress.Value = OverallProgressBar.Value;

        var logLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = surface };
        logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        logLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        logLayout.Controls.Add(CreatePageHeading("Registro e historial", "Consulta trabajos anteriores, su duración y velocidad media."), 0, 0);
        var historyRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = surface, Padding = new Padding(20) };
        var historyButton = CreateButton("Abrir historial de transferencias", raised, foreground, border);
        historyButton.Click += ShowHistoryDialog;
        historyRow.Controls.Add(historyButton);
        logLayout.Controls.Add(historyRow, 0, 1);
        logPage.Controls.Add(logLayout);

        WorkspaceTabs.TabPages.AddRange(new[] { listPage, targetPage, optionsPage, statusPage, logPage });
        WorkspaceTabs.SelectedIndex = 0;

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = surface, Padding = new Padding(10, 4, 10, 6) };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        footer.Controls.Add(OverallProgressBar, 0, 0);
        footer.Controls.Add(StatusLabel, 0, 1);

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = canvas, Padding = new Padding(16, 12, 16, 12) };
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.Controls.Add(toolbarRow, 0, 0);
        main.Controls.Add(titleBlock, 0, 1);
        main.Controls.Add(cards, 0, 2);
        main.Controls.Add(WorkspaceTabs, 0, 3);
        main.Controls.Add(footer, 0, 4);

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = canvas };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.Controls.Add(menu, 0, 0);
        shell.SetColumnSpan(menu, 2);
        shell.Controls.Add(sidebar, 0, 1);
        shell.Controls.Add(main, 1, 1);

        Text = "XoPy — Gestor de transferencias";
        BackColor = canvas;
        ForeColor = foreground;
        Font = new Font("Segoe UI", 9);
        MinimumSize = new Size(1080, 700);
        Size = new Size(1420, 900);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        Controls.Add(shell);
    }

    private static Button CreateButton(string text, Color backColor, Color foreColor, Color borderColor) => new()
    {
        Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = backColor, ForeColor = foreColor,
        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Padding = new Padding(9, 5, 9, 5),
        Margin = new Padding(0, 0, 7, 5), Cursor = Cursors.Hand,
        FlatAppearance = { BorderColor = borderColor, BorderSize = 1, MouseOverBackColor = Color.FromArgb(Math.Min(255, backColor.R + 12), Math.Min(255, backColor.G + 12), Math.Min(255, backColor.B + 12)) }
    };

    private static ComboBox CreateComboBox(int width, Color backColor, Color foreColor) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Width = width, BackColor = backColor, ForeColor = foreColor,
        FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 4, 8, 8)
    };

    private static TabPage CreateTab(string text) => new(text) { BackColor = Color.FromArgb(29, 32, 37), Padding = new Padding(0) };

    private static Control CreatePageHeading(string title, string description)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2, AutoSize = true, BackColor = Color.FromArgb(29, 32, 37), Padding = new Padding(20, 17, 15, 11) };
        layout.Controls.Add(new Label { Text = title, AutoSize = true, ForeColor = Color.FromArgb(229, 232, 236), Font = new Font("Segoe UI", 12, FontStyle.Bold), Margin = new Padding(0, 0, 0, 3) }, 0, 0);
        layout.Controls.Add(new Label { Text = description, AutoSize = true, ForeColor = Color.FromArgb(151, 158, 168), Font = new Font("Segoe UI", 8.5f), Margin = new Padding(0) }, 0, 1);
        return layout;
    }

    private static Label CreateFieldLabel(string text, Color color) => new()
    {
        Text = text, AutoSize = true, ForeColor = color, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
        Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 12, 8)
    };

    private static Label CreateSidebarCount(Color color) => new()
    {
        Text = "0", AutoSize = true, ForeColor = color, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
        Anchor = AnchorStyles.Right, Margin = new Padding(0, 8, 8, 8)
    };

    private static Label CreateSummaryValue(Color color) => new()
    {
        Text = "0", AutoSize = true, ForeColor = color, Font = new Font("Segoe UI", 14, FontStyle.Bold),
        Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, 12, 0)
    };

    private static void AddSidebarRow(TableLayoutPanel panel, int row, string text, Label count, Color foreground)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.Controls.Add(new Label { Text = text, AutoSize = true, ForeColor = foreground, Font = new Font("Segoe UI", 8.5f), Anchor = AnchorStyles.Left, Margin = new Padding(9, 8, 0, 6) }, 0, row);
        panel.Controls.Add(count, 1, row);
    }

    private static void AddSidebarNavigation(TableLayoutPanel panel, int row, string text, EventHandler handler, Color foreground)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        var button = new Button { Text = text, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(21, 23, 27), ForeColor = foreground, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), Margin = new Padding(0, 1, 0, 1), Cursor = Cursors.Hand };
        button.FlatAppearance.BorderSize = 0;
        button.Click += handler;
        panel.Controls.Add(button, 0, row);
        panel.SetColumnSpan(button, 2);
    }

    private static Panel CreateSummaryCard(string heading, string caption, Label value, Color accent, Color surface)
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = surface, Margin = new Padding(0, 0, 9, 0), Padding = new Padding(10) };
        var stripe = new Panel { Dock = DockStyle.Left, Width = 3, BackColor = accent };
        var title = new Label { Text = heading, AutoSize = true, ForeColor = Color.FromArgb(151, 158, 168), Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Location = new Point(13, 9) };
        var description = new Label { Text = caption, AutoSize = true, ForeColor = Color.FromArgb(151, 158, 168), Font = new Font("Segoe UI", 8), Location = new Point(13, 30) };
        value.Location = new Point(122, 18);
        card.Controls.Add(stripe);
        card.Controls.Add(title);
        card.Controls.Add(description);
        card.Controls.Add(value);
        return card;
    }
}

internal sealed class XoPyMenuColorTable : ProfessionalColorTable
{
    public override Color MenuBorder => Color.FromArgb(54, 59, 67);
    public override Color MenuItemBorder => Color.FromArgb(54, 59, 67);
    public override Color MenuItemSelected => Color.FromArgb(43, 48, 56);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(43, 48, 56);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(43, 48, 56);
    public override Color MenuStripGradientBegin => Color.FromArgb(21, 23, 27);
    public override Color MenuStripGradientEnd => Color.FromArgb(21, 23, 27);
    public override Color ToolStripDropDownBackground => Color.FromArgb(29, 32, 37);
    public override Color ImageMarginGradientBegin => Color.FromArgb(29, 32, 37);
    public override Color ImageMarginGradientEnd => Color.FromArgb(29, 32, 37);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(29, 32, 37);
}
