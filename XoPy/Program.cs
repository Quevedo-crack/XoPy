using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace XoPy;

internal static class Program
{
    private static string? _diagnosticFile;

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            _diagnosticFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "XoPy", "startup-error.txt");

            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ShowFatal(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatal(e.ExceptionObject as Exception);

            var request = StartupRequest.Parse(args);

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
        catch (Exception ex)
        {
            try
            {
                var dir = Path.GetDirectoryName(_diagnosticFile ?? string.Empty);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_diagnosticFile ?? string.Empty,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  ERROR AL INICIAR XoPy{Environment.NewLine}" +
                    $"Excepción: {ex.GetType().FullName}{Environment.NewLine}" +
                    $"Mensaje: {ex.Message}{Environment.NewLine}" +
                    $"StackTrace: {ex.StackTrace}{Environment.NewLine}");
            }
            catch { }

            try
            {
                MessageBox.Show("XoPy no pudo iniciar: " + ex.Message,
                    "XoPy", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }

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
