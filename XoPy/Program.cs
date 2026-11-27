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

            // MODO OSCURO DE WINDOWS PARA LAS PARTES QUE PINTA EL SISTEMA. Las barras de
            // desplazamiento, los menús nativos y los bordes internos los dibuja Windows,
            // y salen BLANCOS mientras la aplicación no diga que prefiere el modo oscuro.
            // Es la franja blanca que asomaba a la derecha de las opciones avanzadas.
            // La función no tiene nombre público (solo número de orden), así que puede no
            // existir en Windows antiguos: por eso va envuelta en su try.
            // Solo si el usuario tiene puesto el tema oscuro (el de fábrica): con el tema
            // claro, unas barras negras quedarían igual de mal que las blancas de ahora.
            Log("2b. Modo oscuro del sistema");
            try
            {
                var darkTheme = true;
                try { darkTheme = new SettingsStore().Load().DarkTheme; } catch { }
                if (darkTheme) NativeTheme.PreferDarkMode();
            }
            catch { }

            Log("3. SetUnhandledExceptionMode");
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Log("4. ThreadException handler");
            Application.ThreadException += (_, e) =>
            {
                Log("6. ThreadException: " + e.Exception.GetType().Name + " - " + e.Exception.Message);
                try { File.AppendAllText(_logPath, e.Exception.ToString() + "\n"); } catch { }
                try { FileLog.Write("FALLO en la ventana: " + e.Exception); } catch { }
                // NADA DE CUADROS MODALES AQUÍ. Un cuadro de error se queda esperando una
                // respuesta y, mientras tanto, la ventana no atiende a nadie: el motor de
                // copia, que necesita la ventana para preguntar y para pintar el avance, se
                // queda clavado y la copia parece «lentísima» o parada del todo (pasaba con
                // dos cuadros apilados detrás de la ventana). El fallo se anota en el
                // registro y se avisa en la barra de estado, sin detener la copia.
                try { MainForm.Current?.ReportBackgroundError(e.Exception); }
                catch { }
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
                // Se espera un poco al servidor: si la primera instancia aún está arrancando,
                // el envío inmediato fallaba y esta segunda instancia abría OTRA ventana. Eso
                // es lo que hacía que la ventana «volviera a abrirse» tras cerrarla: al pulsar
                // el icono o al reabrir se lanzaba un segundo proceso con su propia ventana.
                var delivered = false;
                for (var attempt = 0; attempt < 8 && !delivered; attempt++)
                {
                    delivered = IpcClient.Send(request);
                    if (!delivered) Thread.Sleep(250);
                }
                if (delivered)
                {
                    Log("12. IPC confirmado por la instancia abierta, salir");
                    try { mutex.Dispose(); } catch { }
                    return;
                }

                // La instancia anterior NO ha confirmado: puede estar colgada, sin interfaz o
                // ser un proceso zombi que se quedó con el mutex. Salir aquí dejaba al usuario
                // abriendo XoPy una y otra vez SIN QUE APARECIERA NADA. Se continúa y se abre
                // esta ventana: es preferible una ventana de más que ninguna.
                Log("12b. La instancia anterior no responde: se abre esta ventana igualmente");
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
