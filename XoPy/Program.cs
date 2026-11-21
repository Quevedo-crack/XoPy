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
        using var form = new MainForm(request);
        var timer = new System.Windows.Forms.Timer { Interval = 1500 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                bitmap.Save(path, ImageFormat.Png);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(path + ".error.txt", ex.ToString()); } catch { }
            }
            form.Close();
        };
        form.Shown += (_, _) => timer.Start();
        Application.Run(form);
        timer.Dispose();
    }

    private static void ShowFatal(Exception? exception)
    {
        if (exception is null) return;
        try
        {
            MessageBox.Show("XoPy ha encontrado un problema:\n\n" + exception.Message,
                "XoPy", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }
}
