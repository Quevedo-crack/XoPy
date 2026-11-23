using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace XoPy;

internal static class Program
{
    private static string _logPath = @"C:\XoPy-startup.log";

    private static void Log(string step)
    {
        try
        {
            File.AppendAllText(_logPath, $"{step}\n");
        }
        catch { }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Log("1. Inicio Main");

        try
        {
            Log("2. ApplicationConfiguration.Initialize");
            ApplicationConfiguration.Initialize();

            Log("3. SetUnhandledExceptionMode");
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Log("4. ThreadException handler");
            Application.ThreadException += (_, e) =>
            {
                Log($"6. ThreadException: {e.Exception.GetType().Name} - {e.Exception.Message}");
                try { File.AppendAllText(_logPath, e.Exception.ToString() + "\n"); } catch { }
                try { MessageBox.Show("XoPy error: " + e.Exception.Message, "XoPy", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
            };

            Log("5. UnhandledException handler");
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                Log($"7. UnhandledException: {e.ExceptionObject?.GetType().Name ?? "null"}");
                try { File.AppendAllText(_logPath, e.ExceptionObject?.ToString() ?? "null exception object\n"); } catch { }
            };

            Log("8. StartupRequest.Parse");
            var request = StartupRequest.Parse(args);

            Log("9. Crear Mutex");
            var mutex = new Mutex(true, Ipc.MutexName, out var isFirstInstance);

            Log("10. Verificar primera instancia");
            if (!isFirstInstance)
            {
                Log("11. No es primera instancia, intentar IPC");
                if (IpcClient.Send(request))
                {
                    Log("12. IPC enviado, salir");
                    mutex.Dispose();
                    return;
                }
                try { mutex.Dispose(); } catch { }
            }

            Log("13. Crear MainForm");
            using var form = new MainForm(request);

            Log("14. Iniciar IpcServer");
            IpcServer.Start(form);

            Log("15. Application.Run");
            Application.Run(form);

            Log("16. Application.Run terminó");
            GC.KeepAlive(mutex);
        }
        catch (Exception ex)
        {
            Log($"FATAL: {ex.GetType().FullName} - {ex.Message}");
            Log($"FATAL StackTrace: {ex.StackTrace}");
            try
            {
                MessageBox.Show("XoPy no pudo iniciar: " + ex.Message, "XoPy",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
