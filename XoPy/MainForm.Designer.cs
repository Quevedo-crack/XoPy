namespace XoPy;

partial class MainForm
{
    private System.Windows.Forms.Button AddFilesButton;
    private System.Windows.Forms.Button AddFolderButton;
    private System.Windows.Forms.Button StartButton;
    private System.Windows.Forms.Button PauseButton;
    private System.Windows.Forms.Button StopButton;
    private System.Windows.Forms.Button MoveUpButton;
    private System.Windows.Forms.Button MoveDownButton;
    private System.Windows.Forms.Button SaveQueueButton;
    private System.Windows.Forms.Button LoadQueueButton;
    private System.Windows.Forms.ListView QueueListView;
    private System.Windows.Forms.ProgressBar OverallProgressBar;
    private System.Windows.Forms.Label StatusLabel;
    private System.Windows.Forms.NotifyIcon TrayIcon;

    private System.ComponentModel.IContainer components = null;

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.AddFilesButton = new System.Windows.Forms.Button();
        this.AddFolderButton = new System.Windows.Forms.Button();
        this.StartButton = new System.Windows.Forms.Button();
        this.PauseButton = new System.Windows.Forms.Button();
        this.StopButton = new System.Windows.Forms.Button();
        this.MoveUpButton = new System.Windows.Forms.Button();
        this.MoveDownButton = new System.Windows.Forms.Button();
        this.SaveQueueButton = new System.Windows.Forms.Button();
        this.LoadQueueButton = new System.Windows.Forms.Button();
        this.QueueListView = new System.Windows.Forms.ListView();
        this.OverallProgressBar = new System.Windows.Forms.ProgressBar();
        this.StatusLabel = new System.Windows.Forms.Label();
        this.TrayIcon = new System.Windows.Forms.NotifyIcon(this.components);

        this.SuspendLayout();

        this.AddFilesButton.Text = "Añadir archivos";
        this.AddFilesButton.Location = new System.Drawing.Point(12, 12);
        this.AddFilesButton.Size = new System.Drawing.Size(120, 32);
        this.AddFilesButton.Click += new System.EventHandler(this.AddFilesButton_Click);

        this.AddFolderButton.Text = "Añadir carpeta";
        this.AddFolderButton.Location = new System.Drawing.Point(138, 12);
        this.AddFolderButton.Size = new System.Drawing.Size(120, 32);
        this.AddFolderButton.Click += new System.EventHandler(this.AddFolderButton_Click);

        this.StartButton.Text = "Iniciar";
        this.StartButton.Location = new System.Drawing.Point(264, 12);
        this.StartButton.Size = new System.Drawing.Size(100, 32);
        this.StartButton.Click += new System.EventHandler(this.StartButton_Click);

        this.PauseButton.Text = "Pausar";
        this.PauseButton.Location = new System.Drawing.Point(370, 12);
        this.PauseButton.Size = new System.Drawing.Size(100, 32);
        this.PauseButton.Click += new System.EventHandler(this.PauseButton_Click);

        this.StopButton.Text = "Cancelar";
        this.StopButton.Location = new System.Drawing.Point(476, 12);
        this.StopButton.Size = new System.Drawing.Size(100, 32);
        this.StopButton.Click += new System.EventHandler(this.StopButton_Click);

        this.MoveUpButton.Text = "Subir";
        this.MoveUpButton.Location = new System.Drawing.Point(582, 12);
        this.MoveUpButton.Size = new System.Drawing.Size(90, 32);
        this.MoveUpButton.Click += new System.EventHandler(this.MoveUpButton_Click);

        this.MoveDownButton.Text = "Bajar";
        this.MoveDownButton.Location = new System.Drawing.Point(678, 12);
        this.MoveDownButton.Size = new System.Drawing.Size(90, 32);
        this.MoveDownButton.Click += new System.EventHandler(this.MoveDownButton_Click);

        this.SaveQueueButton.Text = "Guardar cola";
        this.SaveQueueButton.Location = new System.Drawing.Point(774, 12);
        this.SaveQueueButton.Size = new System.Drawing.Size(110, 32);
        this.SaveQueueButton.Click += new System.EventHandler(this.SaveQueueButton_Click);

        this.LoadQueueButton.Text = "Cargar cola";
        this.LoadQueueButton.Location = new System.Drawing.Point(890, 12);
        this.LoadQueueButton.Size = new System.Drawing.Size(110, 32);
        this.LoadQueueButton.Click += new System.EventHandler(this.LoadQueueButton_Click);

        this.QueueListView.Location = new System.Drawing.Point(12, 60);
        this.QueueListView.Size = new System.Drawing.Size(988, 440);

        this.OverallProgressBar.Location = new System.Drawing.Point(12, 510);
        this.OverallProgressBar.Size = new System.Drawing.Size(988, 25);

        this.StatusLabel.Location = new System.Drawing.Point(12, 545);
        this.StatusLabel.Size = new System.Drawing.Size(988, 18);
        this.StatusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;

        this.TrayIcon.Text = "XoPy";
        this.TrayIcon.Visible = true;

        this.Text = "XoPy";
        this.MinimumSize = new System.Drawing.Size(1020, 620);
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        this.Controls.Add(this.AddFilesButton);
        this.Controls.Add(this.AddFolderButton);
        this.Controls.Add(this.StartButton);
        this.Controls.Add(this.PauseButton);
        this.Controls.Add(this.StopButton);
        this.Controls.Add(this.MoveUpButton);
        this.Controls.Add(this.MoveDownButton);
        this.Controls.Add(this.SaveQueueButton);
        this.Controls.Add(this.LoadQueueButton);
        this.Controls.Add(this.QueueListView);
        this.Controls.Add(this.OverallProgressBar);
        this.Controls.Add(this.StatusLabel);

        this.ResumeLayout(false);
    }
}
