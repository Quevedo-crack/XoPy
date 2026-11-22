using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace XoPy;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatal(e.ExceptionObject as Exception);

        var request = StartupRequest.Parse(args);

        var screenshotPath = FindOption(args, "--screenshot");
        if (screenshotPath is not null)
        {
            RunScreenshot(request, screenshotPath);
            return;
        }

        var tabsShot = FindOption(args, "--screenshot-tabs");
        if (tabsShot is not null)
        {
            RunTabScreenshots(request, tabsShot);
            return;
        }

        var menusShot = FindOption(args, "--screenshot-menus");
        if (menusShot is not null)
        {
            RunMenuScreenshots(menusShot);
            return;
        }

        var optionsShot = FindOption(args, "--screenshot-options");
        if (optionsShot is not null)
        {
            RunDialogScreenshot(optionsShot, DialogShot.Options);
            return;
        }

        var conflictShot = FindOption(args, "--screenshot-conflict");
        if (conflictShot is not null)
        {
            RunDialogScreenshot(conflictShot, DialogShot.Conflict);
            return;
        }

        var historyShot = FindOption(args, "--screenshot-history");
        if (historyShot is not null)
        {
            RunHistoryScreenshot(historyShot);
            return;
        }

        var queueTest = FindOption(args, "--self-test-queue");
        if (queueTest is not null)
        {
            using var testForm = new MainForm(new StartupRequest());
            var report = testForm.QueueSelfTest();
            File.WriteAllText(queueTest, report);
            Environment.Exit(report.StartsWith("OK", StringComparison.Ordinal) ? 0 : 1);
        }

        var multi = args.Any(arg => string.Equals(arg, "--multi", StringComparison.OrdinalIgnoreCase));
        if (multi)
        {
            using var separate = new MainForm(request);
            Application.Run(separate);
            return;
        }

        var mutex = new Mutex(true, Ipc.MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            if (IpcClient.Send(request))
            {
                mutex.Dispose();
                return;
            }

            try { mutex.Dispose(); } catch { }
        }

        using var form = new MainForm(request);
        IpcServer.Start(form);
        Application.Run(form);
        GC.KeepAlive(mutex);
    }

        var screenshotPath = FindOption(args, "--screenshot");
        if (screenshotPath is not null)
        {
            RunScreenshot(request, screenshotPath);
            return;
        }

        var tabsShot = FindOption(args, "--screenshot-tabs");
        if (tabsShot is not null)
        {
            RunTabScreenshots(request, tabsShot);
            return;
        }

        var menusShot = FindOption(args, "--screenshot-menus");
        if (menusShot is not null)
        {
            RunMenuScreenshots(menusShot);
            return;
        }

        var optionsShot = FindOption(args, "--screenshot-options");
        if (optionsShot is not null)
        {
            RunDialogScreenshot(optionsShot, DialogShot.Options);
            return;
        }

        var conflictShot = FindOption(args, "--screenshot-conflict");
        if (conflictShot is not null)
        {
            RunDialogScreenshot(conflictShot, DialogShot.Conflict);
            return;
        }

        var historyShot = FindOption(args, "--screenshot-history");
        if (historyShot is not null)
        {
            RunHistoryScreenshot(historyShot);
            return;
        }

        var queueTest = FindOption(args, "--self-test-queue");
        if (queueTest is not null)
        {
            using var testForm = new MainForm(new StartupRequest());
            var report = testForm.QueueSelfTest();
            File.WriteAllText(queueTest, report);
            Environment.Exit(report.StartsWith("OK", StringComparison.Ordinal) ? 0 : 1);
        }

        var multi = args.Any(arg => string.Equals(arg, "--multi", StringComparison.OrdinalIgnoreCase));
        if (multi)
        {
            using var separate = new MainForm(request);
            Application.Run(separate);
            return;
        }

        var mutex = new Mutex(true, Ipc.MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            if (IpcClient.Send(request))
            {
                mutex.Dispose();
                return;
            }

            try { mutex.Dispose(); } catch { }
        }

        using var form = new MainForm(request);
        IpcServer.Start(form);
        Application.Run(form);
        GC.KeepAlive(mutex);
    }

    private static string? FindOption(string[] args, string option)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static void RunScreenshot(StartupRequest request, string path)
    {
        try
        {
            var log = path + ".log";
            _diagnosticFile = path + ".error.txt";
            File.WriteAllText(log, $"inicio {DateTime.Now:O}{Environment.NewLine}");
            using var form = new MainForm(request);
            File.AppendAllText(log, $"ventana creada {form.Size}{Environment.NewLine}");
            form.Show();
            File.AppendAllText(log, $"ventana mostrada {form.ClientSize}{Environment.NewLine}");

            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(40);
            }

            using var bitmap = new Bitmap(Math.Max(200, form.Width), Math.Max(150, form.Height));
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            bitmap.Save(path, ImageFormat.Png);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(path + ".error.txt", ex.ToString()); } catch { }
            Environment.Exit(2);
        }
    }

    private static void RunMenuScreenshots(string prefix)
    {
        try
        {
            using var form = new MainForm(new StartupRequest());
            form.Show();
            Pump(2500);

            var dropDown = form.OpenTransferMenu();
            Pump(500);
            if (dropDown is not null)
            {
                SaveControl(dropDown, prefix + "-menu.png");
                dropDown.Close();
            }
            Pump(200);

            using var context = form.BuildMainMenuForShot();
            context.Show(form, new Point(40, 40));
            Pump(500);
            SaveControl(context, prefix + "-context.png");
            context.Close();
            Pump(100);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(prefix + ".error.txt", ex.ToString()); } catch { }
            Environment.Exit(2);
        }
    }

    private static void SaveControl(Control control, string path)
    {
        using var bitmap = new Bitmap(Math.Max(120, control.Width), Math.Max(60, control.Height));
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void RunTabScreenshots(StartupRequest request, string prefix)
    {
        try
        {
            using var form = new MainForm(request);
            form.Show();
            Pump(5000);
            for (var index = 0; index < form.TabCount; index++)
            {
                form.ShowTab(index);
                Pump(400);
                Save(form, $"{prefix}-{index}.png");
            }
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(prefix + ".error.txt", ex.ToString()); } catch { }
            Environment.Exit(2);
        }
    }

    private enum DialogShot { Options, Conflict }

    private static void RunDialogScreenshot(string path, DialogShot kind)
    {
        try
        {
            var palette = Palette.For(true);
            Form dialog = kind == DialogShot.Options
                ? new OptionsDialog(new SettingsStore().Load(), palette)
                : new PromptDialog(palette, "Ya existe un archivo",
                    "El archivo de destino ya existe. ¿Qué quieres hacer con \"informe.pdf\"?",
                    "Origen: C:\\Datos\\informe.pdf (10,0 KB, 21/11/2026 10:12)\n" +
                    "Destino: D:\\Copias\\informe.pdf (8,4 KB, 03/09/2026 18:40)",
                    new[] { "Sobrescribir", "Omitir", "Cambiar nombre", "Cancelar" },
                    "Aplicar a todos los conflictos");

            dialog.Show();
            Pump(600);

            if (kind == DialogShot.Options && dialog is OptionsDialog optionsDialog)
            {
                var baseName = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path;
                for (var index = 0; index < optionsDialog.TabCount; index++)
                {
                    optionsDialog.SelectTab(index);
                    Pump(250);
                    Save(dialog, $"{baseName}-{index}.png");
                }
                Save(dialog, path);
            }
            else
            {
                Save(dialog, path);
            }
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(path + ".error.txt", ex.ToString()); } catch { }
            Environment.Exit(2);
        }
    }

    private static void RunHistoryScreenshot(string path)
    {
        try
        {
            var palette = Palette.For(true);
            var entries = new List<HistoryEntry>
            {
                new() { FinishedAt = DateTime.Now.AddMinutes(-2), Operation = OperationType.Copy, Destination = @"D:\Backup\Videos\Drone", FileCount = 142, Bytes = 12_884_901_888, Seconds = 64, Errors = 0 },
                new() { FinishedAt = DateTime.Now.AddMinutes(-18), Operation = OperationType.Move, Destination = @"E:\Series", FileCount = 1352, Bytes = 1_073_741_824, Seconds = 210, Errors = 0 },
                new() { FinishedAt = DateTime.Now.AddHours(-3), Operation = OperationType.Test, Destination = @"C:\Backup\Fotos", FileCount = 36, Bytes = 7_340_032, Seconds = 12, Errors = 1 },
                new() { FinishedAt = DateTime.Now.AddDays(-1), Operation = OperationType.Delete, Destination = @"C:\Temp", FileCount = 80, Bytes = 512_000_000, Seconds = 8, Errors = 0 }
            };

            using var dialog = new HistoryDialog(entries, palette);
            dialog.Show();
            Pump(600);
            Save(dialog, path);
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(path + ".error.txt", ex.ToString()); } catch { }
            Environment.Exit(2);
        }
    }

    private static void Pump(int milliseconds)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(30);
        }
    }

    private static void Save(Form form, string path)
    {
        using var bitmap = new Bitmap(Math.Max(200, form.Width), Math.Max(150, form.Height));
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static string? _diagnosticFile;

    private static void ShowFatal(Exception? exception)
    {
        if (exception is null) return;
        var file = _diagnosticFile;
        if (!string.IsNullOrEmpty(file))
        {
            try { File.WriteAllText(file, exception.ToString()); } catch { }
            Environment.Exit(3);
        }
        try
        {
            MessageBox.Show("XoPy ha encontrado un problema:\n\n" + exception.Message,
                "XoPy", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }
}
