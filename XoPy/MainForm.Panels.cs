using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace XoPy;

public sealed partial class MainForm
{
    private readonly Font _headerFont = new("Segoe UI", 8.5f, FontStyle.Bold);
    private readonly Font _glyphFont = new("Segoe MDL2 Assets", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font _chevronFont = new("Segoe MDL2 Assets", 9f, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font _percentFont = new("Segoe UI", 8f, FontStyle.Bold);
    private readonly Font _smallFont = new("Segoe UI", 8.25f);
    private readonly Font _monoFont = new("Consolas", 8.25f);

    private Label _targetPathLabel = null!;
    private Panel _optionsScroll = null!;
    private bool _syncingOptions;
    private List<HistoryEntry> _history = new();

    // ---------------------------------------------------------------- pestaña: lista

    private Control BuildFilesPage() => BuildListShell();

    // ---------------------------------------------------------------- pestaña: destino

    private Control BuildTargetPage()
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = _palette.Surface,
            Padding = new Padding(16, 12, 16, 12)
        };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        var title = Ui.Label("Carpeta de destino", _palette, 11f, true);
        title.Anchor = AnchorStyles.Left;

        _targetPathLabel = new Label
        {
            Text = string.Empty,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = _palette.Text,
            BackColor = _palette.SurfaceAlt,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Padding = new Padding(10, 0, 10, 0),
            AutoEllipsis = true,
            Margin = new Padding(0, 2, 0, 4)
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = _palette.Surface,
            Margin = new Padding(0)
        };
        var change = Ui.ToolButton("Cambiar…", Icons.Folder, _palette);
        change.Size = new Size(112, 30);
        change.Click += (_, _) => BrowseDestination();
        var open = Ui.ToolButton("Abrir", Icons.Open, _palette);
        open.Size = new Size(96, 30);
        open.Click += (_, _) => OpenFolder(_destination.Text.Trim());
        var save = Ui.ToolButton("Añadir a favoritos", Icons.Star, _palette);
        save.Size = new Size(148, 30);
        save.Click += (_, _) => AddFavorite();
        actions.Controls.AddRange(new Control[] { change, open, save });

        _targetFreeLabel = Ui.Label("", _palette, 8.75f, false, _palette.Muted);
        _targetFreeLabel.Anchor = AnchorStyles.Left;

        var listTitle = Ui.Label("Favoritos y destinos recientes", _palette, 9f, true, _palette.Muted);
        listTitle.Anchor = AnchorStyles.Left;

        _targetList = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = _palette.SurfaceAlt,
            ForeColor = _palette.Text,
            Font = new Font("Segoe UI", 9f),
            IntegralHeight = false,
            Margin = new Padding(0)
        };
        _targetList.DoubleClick += (_, _) =>
        {
            if (_targetList.SelectedItem is not string path) return;
            if (!Directory.Exists(path)) return;
            _destination.Text = path;
            SetStatus("Destino: " + path);
        };

        var hint = Ui.Label("Consejo: doble clic en un favorito para usarlo como destino. También puedes arrastrar una carpeta sobre la ventana.",
            _palette, 8.5f, false, _palette.Muted);
        hint.Dock = DockStyle.Fill;
        hint.AutoSize = false;
        hint.TextAlign = ContentAlignment.MiddleLeft;
        hint.Margin = new Padding(0, 4, 0, 0);

        page.Controls.Add(title, 0, 0);
        page.Controls.Add(_targetPathLabel, 0, 1);
        page.Controls.Add(actions, 0, 2);
        page.Controls.Add(_targetFreeLabel, 0, 3);
        page.Controls.Add(_targetList, 0, 4);
        page.Controls.Add(hint, 0, 5);
        return page;
    }

    private void UpdateTargetTab()
    {
        if (_targetPathLabel is null) return;
        var destination = _destination.Text.Trim();
        _targetPathLabel.Text = destination.Length == 0 ? "(sin destino)" : destination;

        var free = string.Empty;
        try
        {
            if (destination.Length > 0)
            {
                var root = Path.GetPathRoot(Path.GetFullPath(destination));
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    if (drive.IsReady)
                        free = $"{drive.Name}  ·  {Format.Bytes(drive.AvailableFreeSpace)} libres de {Format.Bytes(drive.TotalSize)}";
                }
            }
        }
        catch { }
        _targetFreeLabel.Text = free;

        if (_targetList is null) return;
        var recent = _history.Where(entry => entry.Destination.Length > 0 && Directory.Exists(entry.Destination))
            .Select(entry => entry.Destination)
            .Where(path => !string.Equals(path, destination, StringComparison.OrdinalIgnoreCase));
        var paths = _settings.Favorites.Where(Directory.Exists)
            .Concat(recent)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(60)
            .ToList();

        if (_targetList.Items.Count == paths.Count && paths.Select((path, index) => _targetList.Items[index] as string == path).All(equal => equal))
            return;

        _targetList.BeginUpdate();
        _targetList.Items.Clear();
        foreach (var path in paths) _targetList.Items.Add(path);
        _targetList.EndUpdate();
    }

    // ---------------------------------------------------------------- pestaña: opciones

    private Control BuildOptionsPage()
    {
        _optionsScroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = _palette.Surface,
            Padding = new Padding(16, 12, 16, 12)
        };
        var scroll = _optionsScroll;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = _palette.Surface,
            Padding = new Padding(0)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var title = Ui.Label("Opciones de la transferencia", _palette, 11f, true);
        AddWideRow(grid, title, 30);

        _optConflict = new ChoiceButton(_palette, new[]
        {
            "Preguntar siempre",
            "Sobrescribir",
            "Omitir los existentes",
            "Cambiar el nombre",
            "Sobrescribir si el original es más reciente",
            "Sobrescribir si el original es más pequeño"
        }, (int)_settings.Conflict);
        AddRow(grid, "Si el destino ya existe:", _optConflict, 36, "Preguntar muestra un cuadro con Sobrescribir, Omitir, Cambiar nombre y la opción de aplicarlo a todos.");

        _optError = new ChoiceButton(_palette, new[]
        {
            "Preguntar siempre",
            "Reintentar automáticamente",
            "Omitir y continuar",
            "Detener la transferencia"
        }, (int)_settings.OnError);
        AddRow(grid, "Si se produce un error:", _optError, 36, "Reintentar vuelve a probar cada archivo antes de darlo por fallido.");

        _optVerify = new ChoiceButton(_palette, new[]
        {
            "Sin verificación",
            "CRC32 (rápido)",
            "MD5",
            "SHA-1",
            "SHA-256 (recomendado)",
            "SHA-512"
        }, (int)_settings.Verify);
        AddRow(grid, "Algoritmo de verificación:", _optVerify, 36, "El hash se calcula al copiar y se compara con el original.");

        _optParallel = new NumericUpDown
        {
            Minimum = 1, Maximum = 16, Value = Math.Clamp(_settings.Parallelism, 1, 16),
            Width = 90, Font = new Font("Segoe UI", 9f)
        };
        Ui.StyleNumeric(_optParallel, _palette);
        AddRow(grid, "Archivos a la vez:", _optParallel, 32, "Pensado para un Ryzen 7 y NVMe: 4 va muy bien; súbelo si copias a varios discos.");

        _optBuffer = new NumericUpDown
        {
            Minimum = 64, Maximum = 8192, Increment = 256, Value = Math.Clamp(_settings.BufferKiB, 64, 8192),
            Width = 110, Font = new Font("Segoe UI", 9f)
        };
        Ui.StyleNumeric(_optBuffer, _palette);
        AddRow(grid, "Búfer por archivo (KB):", _optBuffer, 32, "4096 KB (4 MB) es el valor recomendado con 24 GB de RAM; 1024 KB para equipos modestos.");

        _optRetries = new NumericUpDown
        {
            Minimum = 0, Maximum = 20, Value = Math.Clamp(_settings.RetryCount, 0, 20),
            Width = 90, Font = new Font("Segoe UI", 9f)
        };
        Ui.StyleNumeric(_optRetries, _palette);
        AddRow(grid, "Reintentos por archivo:", _optRetries, 32, null);

        _optSpeedLimit = new NumericUpDown
        {
            Minimum = 0, Maximum = 100000, Increment = 50, Value = Math.Clamp(_settings.SpeedLimitMiB, 0, 100000),
            Width = 110, Font = new Font("Segoe UI", 9f)
        };
        Ui.StyleNumeric(_optSpeedLimit, _palette);
        AddRow(grid, "Límite de velocidad (MB/s):", _optSpeedLimit, 32, "0 significa sin límite.");

        var toggles = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = _palette.Surface,
            Margin = new Padding(0, 6, 0, 0)
        };
        _optVerifyAfterCopy = OptionCheck("Verificar cada archivo después de copiarlo", _settings.VerifyAfterCopy);
        _optPreserveTimestamps = OptionCheck("Conservar las fechas de modificación", _settings.PreserveTimestamps);
        _optRecycleBin = OptionCheck("Enviar a la Papelera de reciclaje al eliminar", _settings.UseRecycleBin);
        _optShowHashes = OptionCheck("Mostrar los hashes en la lista", _settings.ShowHashes);
        _optLocked = OptionCheck("Copiar archivos bloqueados o en uso (permisos de copia de seguridad)", _settings.CopyLockedFiles);
        _optResume = OptionCheck("Reanudar copias parciales al reintentar", _settings.ResumePartials);
        _optChecksums = OptionCheck("Crear un archivo de checksums al terminar", _settings.WriteChecksumFile);
        toggles.Controls.AddRange(new Control[]
        {
            _optVerifyAfterCopy, _optPreserveTimestamps, _optRecycleBin, _optShowHashes,
            _optLocked, _optResume, _optChecksums
        });
        AddWideRow(grid, toggles, 160);

        _optEngine = new ChoiceButton(_palette, new[] { "Nativo (recomendado)", "Robocopy", "Administrado" },
            (int)_settings.Engine);
        _optEngine.SelectedIndexChanged += (_, _) => SaveQuickOptions();
        AddRow(grid, "Motor de copia:", _optEngine, 36,
            "Nativo usa CopyFileEx de Windows. Robocopy va mejor con miles de archivos, redes y rutas largas. Administrado es el más compatible.");

        _optSecurePasses = new NumericUpDown
        {
            Minimum = 0, Maximum = 7, Value = Math.Clamp(_settings.SecureDeletePasses, 0, 7),
            Width = 90, Font = new Font("Segoe UI", 9f)
        };
        Ui.StyleNumeric(_optSecurePasses, _palette);
        AddRow(grid, "Pasadas de borrado seguro:", _optSecurePasses, 32,
            "0 borra de forma normal. Con 1 o 3 se sobrescribe el contenido antes de eliminar (no funciona en unidades de red).");

        var scriptPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = _palette.Surface,
            Margin = new Padding(0)
        };
        scriptPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scriptPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        _optScript = new TextBox
        {
            Text = _settings.CompletionScript,
            Dock = DockStyle.Fill,
            BackColor = _palette.SurfaceAlt,
            ForeColor = _palette.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9f),
            Margin = new Padding(0, 0, 8, 0)
        };
        var pickScript = Ui.ToolButton("Examinar…", Icons.Folder, _palette);
        pickScript.Size = new Size(96, 28);
        pickScript.Click += (_, _) => PickCompletionScript();
        scriptPanel.Controls.Add(_optScript, 0, 0);
        scriptPanel.Controls.Add(pickScript, 1, 0);
        AddRow(grid, "Script al finalizar:", scriptPanel, 32,
            "Se ejecuta al terminar y recibe: carpeta de destino, número de errores y operación. Admite .ps1, .bat, .cmd o .exe.");

        var extraPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = _palette.Surface,
            Margin = new Padding(0)
        };
        extraPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        extraPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        _extraDestinations = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = _palette.SurfaceAlt,
            ForeColor = _palette.Text,
            Font = new Font("Segoe UI", 9f),
            IntegralHeight = false
        };
        var extraButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = _palette.Surface,
            Margin = new Padding(0, 4, 0, 0)
        };
        var addExtra = Ui.ToolButton("Añadir destino…", Icons.FolderAdd, _palette);
        addExtra.Size = new Size(144, 26);
        addExtra.Click += (_, _) => AddExtraDestination();
        var removeExtra = Ui.ToolButton("Quitar", Icons.Remove, _palette);
        removeExtra.Size = new Size(84, 26);
        removeExtra.Click += (_, _) => RemoveExtraDestination();
        extraButtons.Controls.AddRange(new Control[] { addExtra, removeExtra });
        extraPanel.Controls.Add(_extraDestinations, 0, 0);
        extraPanel.Controls.Add(extraButtons, 0, 1);
        AddWideRow(grid, extraPanel, 116);
        var extraHint = Ui.Label("Cada archivo se copia también en estos destinos (copia a varios discos a la vez). Los destinos adicionales deben existir.",
            _palette, 8f, false, _palette.Muted);
        extraHint.AutoSize = false;
        extraHint.Dock = DockStyle.Fill;
        extraHint.Margin = new Padding(0, 0, 0, 6);
        AddWideRow(grid, extraHint, 26);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = _palette.Surface,
            Margin = new Padding(0, 8, 0, 0)
        };
        var all = Ui.ToolButton("Todas las preferencias…", Icons.Settings, _palette, _palette.Accent);
        all.Size = new Size(196, 34);
        all.Click += (_, _) => ShowOptions();
        var save = Ui.ToolButton("Guardar", Icons.Save, _palette);
        save.Size = new Size(104, 34);
        save.Click += (_, _) => SaveQuickOptions();
        buttons.Controls.AddRange(new Control[] { all, save });
        AddWideRow(grid, buttons, 46);

        scroll.Controls.Add(grid);

        _optConflict.SelectedIndexChanged += (_, _) => SaveQuickOptions();
        _optError.SelectedIndexChanged += (_, _) => SaveQuickOptions();
        _optVerify.SelectedIndexChanged += (_, _) => SaveQuickOptions();
        _optParallel.ValueChanged += (_, _) => SaveQuickOptions();
        _optBuffer.ValueChanged += (_, _) => SaveQuickOptions();
        _optRetries.ValueChanged += (_, _) => SaveQuickOptions();
        _optSpeedLimit.ValueChanged += (_, _) => SaveQuickOptions();
        _optVerifyAfterCopy.CheckedChanged += (_, _) => SaveQuickOptions();
        _optPreserveTimestamps.CheckedChanged += (_, _) => SaveQuickOptions();
        _optRecycleBin.CheckedChanged += (_, _) => SaveQuickOptions();
        _optLocked.CheckedChanged += (_, _) => SaveQuickOptions();
        _optResume.CheckedChanged += (_, _) => SaveQuickOptions();
        _optChecksums.CheckedChanged += (_, _) => SaveQuickOptions();
        _optSecurePasses.ValueChanged += (_, _) => SaveQuickOptions();
        _optScript.Leave += (_, _) => SaveQuickOptions();
        _optShowHashes.CheckedChanged += (_, _) =>
        {
            _settings.ShowHashes = _optShowHashes.Checked;
            _store.Save(_settings);
            UpdateHashColumns();
        };

        RefreshExtraDestinations();
        return scroll;
    }

    private void PickCompletionScript()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Script al finalizar",
            Filter = "Scripts|*.ps1;*.bat;*.cmd;*.exe|Todos los archivos (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _optScript.Text = dialog.FileName;
        SaveQuickOptions();
    }

    private void AddExtraDestination()
    {
        using var dialog = new FolderBrowserDialog { Description = "Carpeta de destino adicional" };
        var current = _destination.Text.Trim();
        try { if (Directory.Exists(current)) dialog.SelectedPath = current; } catch { }
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var path = dialog.SelectedPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        var primary = _destination.Text.Trim();
        if (string.Equals(path, primary, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Esa carpeta ya es el destino principal.");
            return;
        }
        if (!_settings.ExtraDestinations.Contains(path, StringComparer.OrdinalIgnoreCase))
            _settings.ExtraDestinations.Add(path);
        _store.Save(_settings);
        RefreshExtraDestinations();
        SetStatus($"Destinos: {1 + _settings.ExtraDestinations.Count}. Cada archivo se copiará en todos.");
    }

    private void RemoveExtraDestination()
    {
        if (_extraDestinations.SelectedItem is not string path) return;
        _settings.ExtraDestinations.RemoveAll(candidate => string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase));
        _store.Save(_settings);
        RefreshExtraDestinations();
    }

    private CheckBox OptionCheck(string text, bool value) => new()
    {
        Text = text,
        Checked = value,
        AutoSize = true,
        ForeColor = _palette.Text,
        BackColor = Color.Transparent,
        Font = new Font("Segoe UI", 9f),
        Margin = new Padding(0, 2, 0, 2)
    };

    private static void AddWideRow(TableLayoutPanel grid, Control control, int height)
    {
        var index = grid.RowStyles.Count;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        grid.RowCount = index + 1;
        grid.Controls.Add(control, 0, index);
        grid.SetColumnSpan(control, 2);
    }

    private void AddRow(TableLayoutPanel grid, string label, Control control, int height, string? hint)
    {
        var index = grid.RowStyles.Count;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        grid.RowCount = index + 1;

        var text = Ui.Label(label, _palette, 9f);
        text.Anchor = AnchorStyles.Left;
        grid.Controls.Add(text, 0, index);
        grid.Controls.Add(control, 1, index);

        if (string.IsNullOrEmpty(hint)) return;
        var hintRow = grid.RowStyles.Count;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        grid.RowCount = hintRow + 1;
        var hintLabel = new Label
        {
            Text = hint,
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = _palette.Muted,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8f),
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(0, 0, 0, 4)
        };
        grid.Controls.Add(hintLabel, 0, hintRow);
        grid.SetColumnSpan(hintLabel, 2);
    }

    private void SyncQuickOptions()
    {
        if (_optConflict is null) return;
        _syncingOptions = true;
        try
        {
            _optConflict.SelectedIndex = (int)_settings.Conflict;
            _optError.SelectedIndex = (int)_settings.OnError;
            _optVerify.SelectedIndex = (int)_settings.Verify;
            _optParallel.Value = Math.Clamp(_settings.Parallelism, 1, 16);
            _optBuffer.Value = Math.Clamp(_settings.BufferKiB, 64, 8192);
            _optRetries.Value = Math.Clamp(_settings.RetryCount, 0, 20);
            _optSpeedLimit.Value = Math.Clamp(_settings.SpeedLimitMiB, 0, 100000);
            _optVerifyAfterCopy.Checked = _settings.VerifyAfterCopy;
            _optPreserveTimestamps.Checked = _settings.PreserveTimestamps;
            _optRecycleBin.Checked = _settings.UseRecycleBin;
            _optShowHashes.Checked = _settings.ShowHashes;
            if (_optEngine is not null) _optEngine.SelectedIndex = (int)_settings.Engine;
        }
        finally
        {
            _syncingOptions = false;
        }
    }

    private void SaveQuickOptions()
    {
        if (_syncingOptions || _optConflict is null) return;
        _settings.Conflict = (ConflictPolicy)_optConflict.SelectedIndex;
        _settings.OnError = (ErrorPolicy)_optError.SelectedIndex;
        _settings.Verify = VerifyFromIndex(_optVerify.SelectedIndex);
        _settings.Parallelism = (int)_optParallel.Value;
        _settings.BufferKiB = (int)_optBuffer.Value;
        _settings.RetryCount = (int)_optRetries.Value;
        _settings.SpeedLimitMiB = (int)_optSpeedLimit.Value;
        _settings.VerifyAfterCopy = _optVerifyAfterCopy.Checked;
        _settings.PreserveTimestamps = _optPreserveTimestamps.Checked;
        _settings.UseRecycleBin = _optRecycleBin.Checked;
        _settings.ShowHashes = _optShowHashes.Checked;
        _settings.CopyLockedFiles = _optLocked.Checked;
        _settings.Engine = (CopyEngineKind)Math.Clamp(_optEngine.SelectedIndex, 0, 2);
        _settings.ResumePartials = _optResume.Checked;
        _settings.WriteChecksumFile = _optChecksums.Checked;
        _settings.SecureDeletePasses = (int)_optSecurePasses.Value;
        _settings.CompletionScript = _optScript.Text.Trim();
        _store.Save(_settings);

        _verifyCheck.Checked = _settings.VerifyAfterCopy;
        _verifyCheck.Text = _settings.VerifyAfterCopy
            ? $"Verificar ({Format.Algorithm(_settings.Verify)})"
            : "Verificar";
        UpdateHashColumns();
    }

    private static VerifyAlgorithm VerifyFromIndex(int index) => index switch
    {
        1 => VerifyAlgorithm.Crc32,
        2 => VerifyAlgorithm.Md5,
        3 => VerifyAlgorithm.Sha1,
        4 => VerifyAlgorithm.Sha256,
        5 => VerifyAlgorithm.Sha512,
        _ => VerifyAlgorithm.None
    };

    // ---------------------------------------------------------------- pestaña: estado

    private Control BuildStatusPage()
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9,
            BackColor = _palette.Surface,
            Padding = new Padding(16, 10, 16, 8)
        };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        var title = Ui.Label("Estado de la transferencia", _palette, 11f, true);
        title.Anchor = AnchorStyles.Left;

        _stOperation = StatusLine();
        _stSource = StatusLine();
        _stTarget = StatusLine();
        _stTotals = StatusLine();

        var progressRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = _palette.Surface,
            RowCount = 1
        };
        progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        _stProgress = new ProgressStrip(_palette) { Dock = DockStyle.Fill, Height = 12, Margin = new Padding(0, 6, 8, 4) };
        _stPercent = new Label
        {
            Text = "0 %",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = _palette.Text,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold)
        };
        progressRow.Controls.Add(_stProgress, 0, 0);
        progressRow.Controls.Add(_stPercent, 1, 0);

        _stSpeed = StatusLine();
        _stEta = StatusLine();
        _stErrors = StatusLine();

        var speedRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = _palette.Surface,
            Margin = new Padding(0)
        };
        speedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        speedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        _stEta.TextAlign = ContentAlignment.MiddleRight;
        speedRow.Controls.Add(_stSpeed, 0, 0);
        speedRow.Controls.Add(_stEta, 1, 0);

        var categories = BuildCategoryGrid();

        var hint = Ui.Label("Los hashes de cada archivo están en la pestaña Lista de archivos. El registro completo se guarda en %LOCALAPPDATA%\\XoPy\\transfer.log.",
            _palette, 8.5f, false, _palette.Muted);
        hint.Dock = DockStyle.Fill;
        hint.AutoSize = false;
        hint.AutoEllipsis = true;
        hint.TextAlign = ContentAlignment.MiddleLeft;
        hint.Margin = new Padding(0, 4, 0, 0);

        page.Controls.Add(title, 0, 0);
        page.Controls.Add(_stOperation, 0, 1);
        page.Controls.Add(_stSource, 0, 2);
        page.Controls.Add(_stTarget, 0, 3);
        page.Controls.Add(_stTotals, 0, 4);
        page.Controls.Add(progressRow, 0, 5);
        page.Controls.Add(speedRow, 0, 6);
        page.Controls.Add(categories, 0, 7);
        page.Controls.Add(hint, 0, 8);
        return page;
    }

    private Control BuildCategoryGrid()
    {
        var names = new[] { "Correctos", "Verificados", "Fallidos", "Total" };
        var colors = new[]
        {
            Color.FromArgb(87, 211, 140),
            _palette.Accent,
            _palette.Warning,
            _palette.Text
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 5,
            BackColor = _palette.Surface,
            Margin = new Padding(0, 4, 0, 0)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
        for (var row = 0; row < 5; row++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

        var headers = new[] { "Categoría", "Archivos", "Bytes", "Tiempo" };
        for (var column = 0; column < 4; column++)
        {
            var header = Ui.Label(headers[column], _palette, 8.5f, true, _palette.Muted);
            header.AutoSize = false;
            header.Dock = DockStyle.Fill;
            header.TextAlign = column == 0 ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleRight;
            grid.Controls.Add(header, column, 0);
        }

        for (var row = 0; row < 4; row++)
        {
            var name = Ui.Label(names[row], _palette, 9f, row == 3, colors[row]);
            name.AutoSize = false;
            name.Dock = DockStyle.Fill;
            name.TextAlign = ContentAlignment.MiddleLeft;
            grid.Controls.Add(name, 0, row + 1);

            for (var column = 0; column < 3; column++)
            {
                var value = Ui.Label(row == 3 ? "0" : "—", _palette, 9f, row == 3, colors[row]);
                value.AutoSize = false;
                value.Dock = DockStyle.Fill;
                value.TextAlign = ContentAlignment.MiddleRight;
                if (row == 3) value.Text = "—";
                _categoryValues[row, column] = value;
                grid.Controls.Add(value, column + 1, row + 1);
            }
        }

        return grid;
    }

    private void UpdateTransferHeader(List<TransferItem> leaves, TransferItem? runningLeaf, long done, long total, double percent)
    {
        if (_hdrCopyState is null) return;

        var running = _engine.IsRunning;
        var stateText = running
            ? _lastOperation switch
            {
                OperationType.Move => "Moviendo",
                OperationType.Delete => "Eliminando",
                OperationType.Test => "Verificando",
                _ => "Copiando"
            }
            : _items.Count == 0 ? "Inactivo"
            : _items.Any(item => item.State == ItemState.Failed) ? "Con errores" : "Preparado";

        _hdrCopyState.Text = stateText;
        var current = runningLeaf?.RelativePath;
        _hdrCopyPath.Text = string.IsNullOrWhiteSpace(current)
            ? (running ? "Preparando la transferencia…" : _lastDestination is { Length: > 0 } last ? "Último destino: " + last : "—")
            : current;
        if (leaves.Count == 0)
        {
            _hdrCopyInfo.Text = string.Empty;
        }
        else if (running && _engine.IsPaused)
        {
            _hdrCopyInfo.Text = $"{leaves.Count(item => item.State == ItemState.Completed)}/{leaves.Count} · en pausa · {percent:0.0} %";
        }
        else if (running)
        {
            var remaining = _engine.Remaining;
            _hdrCopyInfo.Text = remaining > TimeSpan.Zero
                ? $"{leaves.Count(item => item.State == ItemState.Completed)}/{leaves.Count} · {Format.Bytes(done)} de {Format.Bytes(total)} · quedan {Format.Duration(remaining)}"
                : $"{leaves.Count(item => item.State == ItemState.Completed)}/{leaves.Count} · {Format.Bytes(done)} de {Format.Bytes(total)}";
        }
        else
        {
            _hdrCopyInfo.Text = $"{leaves.Count(item => item.State == ItemState.Completed)}/{leaves.Count} · {Format.Bytes(done)} de {Format.Bytes(total)}";
        }
        _hdrCopyBar.Value = percent;
        _hdrCopyPercent.Text = leaves.Count == 0 ? "—" : $"{percent:0.0} %";
        _hdrCopyBar.FillColor = _items.Any(item => item.State == ItemState.Failed) && !running ? _palette.Warning : _palette.Accent;

        _hdrTargetState.Text = "Destino";
        var destination = string.IsNullOrWhiteSpace(_destination.Text) ? "—" : _destination.Text.Trim();
        var extras = _settings.ExtraDestinations.Count;
        _hdrTargetPath.Text = extras > 0 ? $"{destination}  (+{extras})" : destination;

        if (running && _engine.IsPaused)
        {
            _hdrTargetInfo.Text = "En pausa · 0 B/s";
        }
        else if (running)
        {
            var speed = _engine.SpeedBytesPerSecond;
            var remaining = _engine.Remaining;
            _hdrTargetInfo.Text = speed > 0
                ? $"{Format.Speed(speed)}  ·  {Format.Duration(_engine.ElapsedIncludingPauses)}  ·  quedan {Format.Duration(remaining)}"
                : "Calculando la velocidad…";
        }
        else
        {
            _hdrTargetInfo.Text = leaves.Count > 0 ? $"{leaves.Count} archivo(s)" : string.Empty;
        }

        var filePercent = runningLeaf is { SizeBytes: > 0 }
            ? Math.Clamp(runningLeaf.TransferredBytes * 100d / runningLeaf.SizeBytes, 0, 100)
            : 0;
        _hdrTargetBar.Value = running ? filePercent : 0;
        _hdrTargetPercent.Text = running ? $"{filePercent:0.0} %" : "—";
    }

    private void UpdateCategories()
    {
        if (_categoryValues[0, 0] is null) return;

        var files = _items.Where(item => item.Kind == ItemKind.File).ToList();
        var verified = files.Count(item => item.State == ItemState.Completed && !string.IsNullOrWhiteSpace(item.HashTarget));
        var failed = files.Count(item => item.State == ItemState.Failed);
        var ok = files.Count(item => item.State == ItemState.Completed) - verified;
        var completed = files.Where(item => item.State == ItemState.Completed).ToList();
        var elapsed = _engine.IsRunning ? _engine.Elapsed : _sessionElapsed;
        var bytes = completed.Sum(item => Math.Max(0, item.SizeBytes));
        var time = elapsed > TimeSpan.Zero ? Format.Duration(elapsed) : "—";

        void Set(int row, int count, string size, string seconds)
        {
            _categoryValues[row, 0].Text = count.ToString();
            _categoryValues[row, 1].Text = size;
            _categoryValues[row, 2].Text = seconds;
        }

        Set(0, ok, Format.Bytes(completed.Where(item => string.IsNullOrWhiteSpace(item.HashTarget)).Sum(item => item.SizeBytes)), time);
        Set(1, verified, Format.Bytes(completed.Where(item => !string.IsNullOrWhiteSpace(item.HashTarget)).Sum(item => item.SizeBytes)), time);
        Set(2, failed, Format.Bytes(files.Where(item => item.State == ItemState.Failed).Sum(item => Math.Max(0, item.SizeBytes))), "—");
        Set(3, files.Count, Format.Bytes(files.Sum(item => Math.Max(0, item.SizeBytes))), elapsed > TimeSpan.Zero ? Format.Duration(elapsed) : "—");
        _ = bytes;
    }

    private Label StatusLine() => new()
    {
        Text = string.Empty,
        AutoSize = false,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = _palette.Muted,
        BackColor = Color.Transparent,
        Font = new Font("Segoe UI", 9f),
        AutoEllipsis = true,
        Margin = new Padding(0)
    };

    private void UpdateStatusTab(double percent, long done, long total, string current)
    {
        if (_stOperation is null) return;
        var running = _engine.IsRunning;
        var operationText = _lastOperation switch
        {
            OperationType.Move => "mover",
            OperationType.Delete => "eliminar",
            OperationType.Test => "test / verificar",
            _ => "copiar"
        };
        _stOperation.Text = "Operación: " + operationText;

        var first = _items.FirstOrDefault(item => item.Kind is ItemKind.File);
        _stSource.Text = "Origen: " + (first?.SourcePath ?? "—");
        _stTarget.Text = "Destino: " + (string.IsNullOrWhiteSpace(_destination.Text) ? "—" : _destination.Text.Trim());
        var errorCount = _items.Count(item => item.State == ItemState.Failed);
        _stTotals.Text = $"Total: {Format.Bytes(done)} de {Format.Bytes(total)}  ·  {percent:0.0} %  ·  {errorCount} error(es)";
        _stProgress.Value = percent;
        _stPercent.Text = $"{percent:0.0} %";
        _stProgress.FillColor = _items.Any(item => item.State == ItemState.Failed) && !running ? _palette.Warning : _palette.Accent;
        UpdateCategories();

        if (running)
        {
            var speed = _engine.SpeedBytesPerSecond;
            var remaining = speed > 1 && total > done ? TimeSpan.FromSeconds((total - done) / speed) : TimeSpan.Zero;
            _stSpeed.Text = $"Velocidad: {(speed > 0 ? Format.Speed(speed) : "calculando…")}"
                + (_engine.IsPaused ? "  ·  en pausa" : string.Empty);
            _stEta.Text = "Tiempo restante: " + (remaining > TimeSpan.Zero ? Format.Duration(remaining) : "—");
            _stErrors.Text = string.Empty;
        }
        else
        {
            _stSpeed.Text = _sessionElapsed.TotalSeconds > 0.2
                ? $"Última transferencia: {Format.Speed(_sessionBytes / Math.Max(0.1, _sessionElapsed.TotalSeconds))}  ·  {Format.Duration(_sessionElapsed)}"
                : "Velocidad: —";
            _stEta.Text = "Tiempo restante: —";
            _stErrors.Text = string.Empty;
        }
    }

    // ---------------------------------------------------------------- pestaña: registro

    private Control BuildLogPage()
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = _palette.Surface,
            Padding = new Padding(16, 12, 16, 12)
        };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var title = Ui.Label("Registro de transferencias", _palette, 11f, true);
        title.Anchor = AnchorStyles.Left;

        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = _palette.SurfaceAlt,
            ForeColor = _palette.Text,
            Font = _monoFont,
            Margin = new Padding(0)
        };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = _palette.Surface
        };
        var refresh = Ui.ToolButton("Actualizar", Icons.Refresh, _palette);
        refresh.Size = new Size(112, 32);
        refresh.Click += (_, _) => RefreshLog();
        var open = Ui.ToolButton("Abrir el archivo de registro", Icons.Open, _palette);
        open.Size = new Size(196, 32);
        open.Click += (_, _) => OpenLogFile();
        actions.Controls.AddRange(new Control[] { refresh, open });

        page.Controls.Add(title, 0, 0);
        page.Controls.Add(_logBox, 0, 1);
        page.Controls.Add(actions, 0, 2);
        return page;
    }

    private void RefreshLog()
    {
        if (_logBox is null) return;
        try { SetWindowTheme(_logBox.Handle, "DarkMode_Explorer", null); } catch { }
        var builder = new StringBuilder();
        builder.AppendLine("=== Historial de transferencias ===");
        if (_history.Count == 0) builder.AppendLine("(todavía no hay transferencias)");
        foreach (var entry in _history.Take(40)) builder.AppendLine(entry.Summary);
        builder.AppendLine();
        builder.AppendLine("=== Registro técnico (transfer.log) ===");
        builder.AppendLine(ReadLogTail());
        _logBox.Text = builder.ToString();
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private static string ReadLogTail()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XoPy", "transfer.log");
            if (!File.Exists(path)) return "(el registro todavía está vacío)";
            var info = new FileInfo(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var start = Math.Max(0, info.Length - 160 * 1024);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var text = reader.ReadToEnd();
            if (start > 0)
            {
                var newline = text.IndexOf('\n');
                if (newline >= 0) text = text[(newline + 1)..];
            }
            return text;
        }
        catch (Exception ex)
        {
            return "(no se pudo leer el registro: " + ex.Message + ")";
        }
    }

    private void OpenLogFile()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XoPy", "transfer.log");
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, string.Empty);
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError("No se pudo abrir el registro: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------- pestañas

    private void SelectTab(int index)
    {
        if (_pages.Length == 0) return;
        _tabIndex = Math.Clamp(index, 0, _pages.Length - 1);
        for (var i = 0; i < _pages.Length; i++) _pages[i].Visible = i == _tabIndex;

        var buttons = new[] { _tabFiles, _tabTarget, _tabOptions, _tabStatus, _tabLog };
        for (var i = 0; i < buttons.Length; i++)
        {
            var active = i == _tabIndex;
            buttons[i].ForeColor = active ? _palette.Text : _palette.Muted;
            buttons[i].BackColor = active ? _palette.Surface : _palette.Canvas;
            buttons[i].Font = new Font("Segoe UI", 9f, active ? FontStyle.Bold : FontStyle.Regular);
        }
        _tabFiles.Parent?.Invalidate();
        if (IsHandleCreated) BeginInvoke(new Action(ApplyDarkScrollbars));

        if (_tabIndex == 1) UpdateTargetTab();
        if (_tabIndex == 3) UpdateFooter();
        if (_tabIndex == 4) RefreshLog();
    }

    // ---------------------------------------------------------------- hashes y columnas

    private bool _updatingColumns;
    private bool _columnUpdatePending;

    private void ScheduleColumnUpdate()
    {
        if (!IsHandleCreated || _columnUpdatePending) return;
        _columnUpdatePending = true;
        BeginInvoke(new Action(() =>
        {
            _columnUpdatePending = false;
            if (!IsDisposed) UpdateHashColumns();
        }));
    }

    private void UpdateHashColumns()
    {
        if (_list is null || _list.Columns.Count < 7 || _updatingColumns) return;
        _updatingColumns = true;
        try
        {
            UpdateHashColumnsCore();
        }
        finally
        {
            _updatingColumns = false;
        }
        ResizeColumns();
    }

    private static void SetWidth(ColumnHeader column, int width)
    {
        if (column.Width != width) column.Width = width;
    }

    private void UpdateHashColumnsCore()
    {
        if (_list.Columns.Count < 7) return;
        var wideEnough = _list.ClientSize.Width >= 900;
        var show = wideEnough && _settings.ShowHashes && (_settings.VerifyAfterCopy || _lastOperation == OperationType.Test);
        // 0 = Nombre · 1 = Destino · 2 = Tamaño · 3 = Estado · 4 = Progreso · 5/6 = hashes
        SetWidth(_list.Columns[1], 236);
        SetWidth(_list.Columns[2], show ? 84 : 92);
        SetWidth(_list.Columns[3], show ? 108 : 128);
        SetWidth(_list.Columns[4], show ? 126 : 190);
        SetWidth(_list.Columns[5], show ? 138 : 0);
        SetWidth(_list.Columns[6], show ? 138 : 0);
    }

    private void ToggleHashes()
    {
        _settings.ShowHashes = !_settings.ShowHashes;
        if (_optShowHashes is not null) _optShowHashes.Checked = _settings.ShowHashes;
        _store.Save(_settings);
        UpdateHashColumns();
        SetStatus(_settings.ShowHashes ? "Hashes visibles en la lista." : "Hashes ocultos.");
    }

    // ---------------------------------------------------------------- trabajos

    private void UpdateJobs()
    {
        if (_jobsList is null) return;
        var lines = new List<string>();
        if (_engine.IsRunning)
        {
            var total = _engine.TotalBytes;
            var percent = total > 0 ? _engine.DoneBytes * 100d / total : 0;
            var verb = _lastOperation switch
            {
                OperationType.Move => "Moviendo",
                OperationType.Delete => "Eliminando",
                OperationType.Test => "Verificando",
                _ => "Copiando"
            };
            lines.Add($"{(_engine.IsPaused ? "❚❚" : "▶")} {verb}  ·  {percent:0.0} %  ·  {Format.Speed(_engine.SpeedBytesPerSecond)}");
        }
        else if (_items.Count > 0)
        {
            lines.Add($"Lista preparada  ·  {_items.Count} elemento(s)");
        }
        else
        {
            lines.Add("Sin trabajos en curso");
        }

        if (_history.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Historial");
            foreach (var entry in _history.Take(9))
            {
                var mark = entry.Errors == 0 ? "✓" : "!";
                lines.Add($"{mark} {entry.FinishedAt:dd/MM HH:mm}  {entry.OperationText}  {entry.ResultText}");
            }
        }

        var signature = string.Join("\n", lines);
        if (string.Equals(signature, _jobsSignature, StringComparison.Ordinal)) return;
        _jobsSignature = signature;

        _jobsList.BeginUpdate();
        _jobsList.Items.Clear();
        foreach (var line in lines) _jobsList.Items.Add(line);
        _jobsList.EndUpdate();
    }

    private string _jobsSignature = string.Empty;

    // ---------------------------------------------------------------- dibujado de la lista

    private void ListDrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using var background = new SolidBrush(_palette.SurfaceAlt);
        e.Graphics.FillRectangle(background, e.Bounds);
        using var line = new Pen(_palette.Border);
        e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        var headerText = e.Header?.Text ?? string.Empty;
        var alignment = e.Header?.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left;
        TextRenderer.DrawText(e.Graphics, headerText, _headerFont,
            Rectangle.Inflate(e.Bounds, -10, 0), _palette.Muted,
            alignment | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private void ListDrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.Item is null) return;
        var selected = e.Item.Selected;
        var background = selected
            ? _palette.Selection
            : (e.ItemIndex % 2 == 0 ? _palette.Surface : _palette.SurfaceAlt);
        using (var brush = new SolidBrush(background))
            e.Graphics.FillRectangle(brush, e.Bounds);

        if (e.Item.Tag is not TransferItem item) return;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        var top = e.Bounds.Y;
        var height = e.Bounds.Height;

        switch (e.ColumnIndex)
        {
            case 0:
            {
                var x = e.Bounds.Left + 8 + item.Depth * 18;
                if (item.Kind == ItemKind.Folder)
                {
                    TextRenderer.DrawText(e.Graphics, item.IsExpanded ? Icons.ChevronDown : Icons.ChevronRight,
                        _chevronFont, new Rectangle(x, top, 14, height), _palette.Muted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    x += 16;
                }
                var glyph = item.Kind is ItemKind.Folder or ItemKind.DirectoryEntry ? Icons.Folder : Icons.File;
                var glyphColor = item.Kind is ItemKind.Folder or ItemKind.DirectoryEntry
                    ? _palette.Warning
                    : (item.State == ItemState.Failed ? _palette.Danger : _palette.Muted);
                TextRenderer.DrawText(e.Graphics, glyph, _glyphFont,
                    new Rectangle(x, top, 18, height), glyphColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                x += 22;

                var nameColor = item.State switch
                {
                    ItemState.Failed => _palette.Danger,
                    ItemState.Completed => _palette.Text,
                    ItemState.Skipped => _palette.Muted,
                    _ => _palette.Text
                };
                if (item.Kind == ItemKind.DirectoryEntry) nameColor = _palette.Muted;
                var name = item.Kind == ItemKind.DirectoryEntry ? e.Item.Text + "  (carpeta vacía)" : e.Item.Text;
                TextRenderer.DrawText(e.Graphics, name, _list.Font,
                    new Rectangle(x, top, Math.Max(0, e.Bounds.Right - x - 8), height),
                    nameColor, flags);
                break;
            }
            case 1:
            {
                // Carpeta de destino real de este elemento, para que se vea a dónde va cada cosa.
                var destinationText = DestinationText(item);
                var hasCustomTarget =
                    (item.State is ItemState.Completed or ItemState.Running or ItemState.Verifying or ItemState.Paused)
                    && !string.IsNullOrWhiteSpace(item.DestinationPath);
                var color = item.State == ItemState.Failed ? _palette.Danger
                    : hasCustomTarget ? _palette.Text
                    : _palette.Muted;
                TextRenderer.DrawText(e.Graphics, destinationText, _list.Font,
                    new Rectangle(e.Bounds.Left + 4, top, Math.Max(0, e.Bounds.Width - 10), height),
                    color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                           | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
                break;
            }
            case 2:
                TextRenderer.DrawText(e.Graphics, item.SizeText, _list.Font,
                    new Rectangle(e.Bounds.Left, top, Math.Max(0, e.Bounds.Width - 12), height),
                    _palette.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                break;
            case 3:
            {
                var color = StateColor(item);
                var glyph = item.State switch
                {
                    ItemState.Completed => Icons.Check,
                    ItemState.Failed => Icons.Warning,
                    ItemState.Skipped => Icons.Blocked,
                    ItemState.Cancelled => Icons.Stop,
                    ItemState.Pending or ItemState.Measuring => Icons.Clock,
                    _ => string.Empty
                };
                var x = e.Bounds.Left + 6;
                if (glyph.Length > 0)
                {
                    TextRenderer.DrawText(e.Graphics, glyph, _glyphFont,
                        new Rectangle(x, top, 16, height), color,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    x += 20;
                }
                var state = item.StateText;
                if (item.Kind == ItemKind.Folder && item.State is ItemState.Pending or ItemState.Measuring)
                {
                    var count = Children(item).Count;
                    if (count > 0) state += $" · {count}";
                }
                TextRenderer.DrawText(e.Graphics, state, _list.Font,
                    new Rectangle(x, top, Math.Max(0, e.Bounds.Right - x - 6), height), color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                break;
            }
            case 4:
            {
                var percent = RowPercent(item);
                var bar = new Rectangle(e.Bounds.Left + 6, top + (height - 10) / 2, Math.Max(12, e.Bounds.Width - 68), 10);
                Ui.DrawProgress(e.Graphics, bar, percent, _palette, StateColor(item));
                TextRenderer.DrawText(e.Graphics, $"{percent:0} %", _percentFont,
                    new Rectangle(bar.Right + 6, top, 54, height), _palette.Muted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                break;
            }
            case 5:
                TextRenderer.DrawText(e.Graphics, item.HashSource ?? string.Empty, _monoFont,
                    Rectangle.Inflate(e.Bounds, -6, 0), _palette.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                break;
            case 6:
                TextRenderer.DrawText(e.Graphics, item.HashTarget ?? string.Empty, _monoFont,
                    Rectangle.Inflate(e.Bounds, -6, 0),
                    string.Equals(item.HashSource, item.HashTarget, StringComparison.OrdinalIgnoreCase) ? _palette.Success : _palette.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                break;
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

    private static double RowPercent(TransferItem item)
    {
        if (item.State == ItemState.Completed) return 100;
        if (item.SizeBytes <= 0) return 0;
        return Math.Clamp(item.TransferredBytes * 100d / item.SizeBytes, 0, 100);
    }

    private void ResizeColumns()
    {
        if (_list is null || _list.Columns.Count < 7 || _updatingColumns) return;
        _updatingColumns = true;
        try
        {
            var used = _list.Columns[1].Width + _list.Columns[2].Width + _list.Columns[3].Width
                       + _list.Columns[4].Width + _list.Columns[5].Width + _list.Columns[6].Width;
            var available = _list.ClientSize.Width - used - 4;
            if (available < 300)
            {
                // En ventanas pequeñas se reduce el resto para que quepa el nombre.
                SetWidth(_list.Columns[1], 168);
                SetWidth(_list.Columns[2], 74);
                SetWidth(_list.Columns[3], 112);
                SetWidth(_list.Columns[4], 124);
                used = _list.Columns[1].Width + _list.Columns[2].Width + _list.Columns[3].Width
                       + _list.Columns[4].Width + _list.Columns[5].Width + _list.Columns[6].Width;
                available = _list.ClientSize.Width - used - 4;
            }
            SetWidth(_list.Columns[0], Math.Max(150, available));
        }
        finally
        {
            _updatingColumns = false;
        }
    }
}
