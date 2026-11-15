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
        AddFilesButton = new Button { Text = "Añadir archivos", AutoSize = true };
        AddFolderButton = new Button { Text = "Añadir carpeta", AutoSize = true };
        StartButton = new Button { Text = "Iniciar", AutoSize = true };
        PauseButton = new Button { Text = "Pausar", AutoSize = true };
        StopButton = new Button { Text = "Cancelar", AutoSize = true };
        MoveUpButton = new Button { Text = "Subir", AutoSize = true };
        MoveDownButton = new Button { Text = "Bajar", AutoSize = true };
        SaveQueueButton = new Button { Text = "Guardar cola", AutoSize = true };
        LoadQueueButton = new Button { Text = "Cargar cola", AutoSize = true };
        BrowseDestinationButton = new Button { Text = "Examinar…", AutoSize = true };
        DestinationTextBox = new TextBox { Width = 330, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        OperationComboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
        ConflictComboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        VerifyCheckBox = new CheckBox { Text = "Verificar con SHA-256", AutoSize = true, Checked = true };
        QueueListView = new ListView { Dock = DockStyle.Fill };
        OverallProgressBar = new ProgressBar { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100 };
        StatusLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        TrayIcon = new NotifyIcon(components) { Text = "XoPy", Visible = true };

        AddFilesButton.Click += AddFilesButton_Click;
        AddFolderButton.Click += AddFolderButton_Click;
        BrowseDestinationButton.Click += BrowseDestinationButton_Click;
        StartButton.Click += StartButton_Click;
        PauseButton.Click += PauseButton_Click;
        StopButton.Click += StopButton_Click;
        MoveUpButton.Click += MoveUpButton_Click;
        MoveDownButton.Click += MoveDownButton_Click;
        SaveQueueButton.Click += SaveQueueButton_Click;
        LoadQueueButton.Click += LoadQueueButton_Click;

        var destinationPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        destinationPanel.Controls.Add(new Label { Text = "Destino:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
        destinationPanel.Controls.Add(DestinationTextBox);
        destinationPanel.Controls.Add(BrowseDestinationButton);
        destinationPanel.Controls.Add(new Label { Text = "Operación:", AutoSize = true, Padding = new Padding(8, 7, 0, 0) });
        destinationPanel.Controls.Add(OperationComboBox);
        destinationPanel.Controls.Add(new Label { Text = "Si existe:", AutoSize = true, Padding = new Padding(8, 7, 0, 0) });
        destinationPanel.Controls.Add(ConflictComboBox);
        destinationPanel.Controls.Add(VerifyCheckBox);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        buttons.Controls.AddRange(new Control[] { AddFilesButton, AddFolderButton, StartButton, PauseButton, StopButton, MoveUpButton, MoveDownButton, SaveQueueButton, LoadQueueButton });

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(destinationPanel, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        layout.Controls.Add(QueueListView, 0, 2);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Height = 48 };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        footer.Controls.Add(OverallProgressBar, 0, 0);
        footer.Controls.Add(StatusLabel, 0, 1);
        layout.Controls.Add(footer, 0, 3);

        Text = "XoPy — Gestor de copias";
        MinimumSize = new Size(900, 520);
        Size = new Size(1200, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(layout);
    }
}
