using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace XoPy;

internal static class Program
{
    private static string _logPath;

    static Program()
    {
        // Usar LocalAppData que SIEMPRE es escribible, no C:\ que puede fallar por permisos
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logDir = Path.Combine(appData, "XoPy");
        try { Directory.CreateDirectory(logDir); } catch { }
        _logPath = Path.Combine(logDir, "startup.log");
    }

    internal static void Log(string step)
    {
        try
        {
            File.AppendAllText(_logPath, step + "\n");
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
                Log("6. ThreadException: " + e.Exception.GetType().Name + " - " + e.Exception.Message);
                try { File.AppendAllText(_logPath, e.Exception.ToString() + "\n"); } catch { }
                try { MessageBox.Show("XoPy error: " + e.Exception.Message, "XoPy", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
            };

            Log("5. UnhandledException handler");
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                Log("7. UnhandledException: " + (e.ExceptionObject?.GetType().Name ?? "null"));
                try { File.AppendAllText(_logPath, e.ExceptionObject?.ToString() ?? "null exception object\n"); } catch { }
            };

            // El instalador llama con --install-hooks: deja XoPy como copiador por defecto
            // (Ctrl+V interceptado) y guarda el arranque con Windows. Así la sustitución del
            // copiador queda activa desde la instalación, sin pasos manuales.
            if (args.Any(arg => string.Equals(arg, "--install-hooks", StringComparison.OrdinalIgnoreCase)))
            {
                ConfigureDefaultCopier();
                if (args.Length == 1) return;
            }

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
            Log("FATAL: " + ex.GetType().FullName + " - " + ex.Message);
            Log("FATAL StackTrace: " + ex.StackTrace);
            try
            {
                MessageBox.Show("XoPy no pudo iniciar: " + ex.Message, "XoPy",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }

    /// <summary>
    /// Deja XoPy configurado como copiador predeterminado: activa la sustitución del
    /// copiador de Windows, el arranque con el equipo (para que el interceptor de Ctrl+V
    /// exista) y aplica los cambios en el registro. Se invoca desde el instalador.
    /// </summary>
    private static void ConfigureDefaultCopier()
    {
        try
        {
            var store = new SettingsStore();
            var settings = store.Load();
            settings.ReplaceWindowsCopier = true;
            settings.StartWithWindows = true;
            settings.MinimizeToTray = true;
            store.Save(settings);

            try { ShellIntegration.Apply(true); } catch { }
            try { ShellIntegration.SetAutoStart(true); } catch { }

            // Se registra en el log para poder diagnosticarlo desde el equipo del usuario.
            try
            {
                Directory.CreateDirectory(store.DataFolder);
                File.AppendAllText(Path.Combine(store.DataFolder, "install-hooks.log"),
                    $"{DateTime.Now:O} copiador predeterminado configurado{Environment.NewLine}");
            }
            catch { }
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(_logPath, "ConfigureDefaultCopier: " + ex + "\n"); } catch { }
        }
    }
}
