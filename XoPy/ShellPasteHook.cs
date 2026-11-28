using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace XoPy;

/// <summary>
/// Intercepta los atajos Ctrl+V y Mayús+Insert en el Explorador y el escritorio.
/// Cuando hay archivos en el portapapeles se cancela el pegado nativo y se hace con XoPy.
/// Si el portapapeles viene de un «Cortar» (Ctrl+X), se mueve y se borra el origen.
///
/// Importante: un hook de teclado de bajo nivel se ejecuta en el hilo de entrada de
/// Windows y debe responder en microsegundos. Por eso el portapapeles NO se lee dentro
/// del hook: un hilo STA en segundo plano mantiene un indicador actualizado y el hook
/// solo consulta ese indicador. El trabajo real se encola en ese mismo hilo STA.
/// </summary>
public sealed class ShellPasteHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int VkInsert = 0x2D;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder text, int count);

    // CF_HDROP: el formato con el que el Explorador pone archivos en el portapapeles.
    private const uint CfHdrop = 15;

    /// <summary>
    /// Comprueba si el portapapeles tiene una lista de archivos. A diferencia de
    /// <c>Clipboard.ContainsFileDropList()</c> esto NO usa COM/OLE: es una consulta directa
    /// a user32 que solo mira la lista de formatos registrados, no pide los datos al dueño
    /// del portapapeles. Por eso se puede llamar desde el gancho sin bloquear nada.
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    private static readonly string[] ExplorerClasses =
    {
        "CabinetWClass", "ExploreWClass", "Progman", "WorkerW", "ApplicationFrameWindow", "MultitaskingViewFrame"
    };

    private readonly HookProc _callback;
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _worker;
    private IntPtr _hook;
    private volatile bool _clipboardHasFiles;
    private volatile bool _disposed;
    private IntPtr _explorerWindow;

    public bool Enabled { get; set; }
    public event Action<IReadOnlyList<string>, string, bool>? PasteRequested;

    public bool IsInstalled => _hook != IntPtr.Zero;

    public ShellPasteHook()
    {
        _callback = OnKeyboard;

        // Hilo STA dedicado: mantiene el indicador del portapapeles al día y ejecuta
        // el trabajo pesado (leer archivos, consultar la carpeta del Explorador).
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "XoPy-PasteHook"
        };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    private void WorkerLoop()
    {
        while (!_disposed)
        {
            try
            {
                // Respaldo del indicador. Si la lectura falla (el Explorador tiene tomado el
                // portapapeles) NO se pone en falso: perder el indicador en ese instante era
                // lo que hacía que la segunda copia la acabara haciendo el copiador de
                // Windows. Solo se baja cuando se confirma que ya no hay archivos.
                var probe = ProbeClipboard();
                if (probe is not null) _clipboardHasFiles = probe.Value;

                if (_work.TryTake(out var action, 200))
                {
                    try { action(); }
                    catch (Exception ex) { FileLog.Write("Error en el trabajo de pegado: " + ex.Message); }
                }
            }
            catch (ObjectDisposedException) { return; }
            catch { Thread.Sleep(150); }
        }
    }

    /// <summary>
    /// true/false si se pudo leer el portapapeles; null si la lectura falló (otro proceso lo
    /// tiene tomado). En ese caso se conserva el valor anterior en vez de darlo por vacío.
    /// </summary>
    private static bool? ProbeClipboard()
    {
        try { return Clipboard.ContainsFileDropList(); }
        catch { return null; }
    }

    /// <summary>
    /// Lee los archivos del portapapeles reintentando: justo después de un Ctrl+C el
    /// Explorador todavía lo tiene abierto y la primera lectura devuelve vacío. Sin estos
    /// reintentos la copia se perdía y parecía que Ctrl+V no había hecho nada.
    /// </summary>
    private static List<string> ClipboardFilesWithRetry()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var files = ClipboardFilesCore();
            if (files.Count > 0) return files;
            Thread.Sleep(40);
        }
        return new List<string>();
    }

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        // Para un hook de bajo nivel (WH_KEYBOARD_LL) el módulo debe ser IntPtr.Zero:
        // pasar el módulo del ejecutable hace que Windows rechace la instalación.
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, IntPtr.Zero, 0);
        if (_hook == IntPtr.Zero)
        {
            // Reintento con el módulo propio por si el sistema lo exigiera.
            try { _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(null), 0); } catch { }
        }
        if (_hook == IntPtr.Zero)
            FileLog.Write("No se pudo instalar el interceptor del teclado: " + Marshal.GetLastWin32Error());
        else
            FileLog.Write("Interceptor de pegado instalado (Ctrl+V / Mayús+Insert en el Explorador).");
    }

    public void Dispose()
    {
        _disposed = true;
        try { _work.CompleteAdding(); } catch { }
        if (_hook != IntPtr.Zero)
        {
            try { UnhookWindowsHookEx(_hook); } catch { }
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr OnKeyboard(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || !Enabled || wParam != (IntPtr)WmKeyDown) return CallNextHookEx(_hook, code, wParam, lParam);

        var key = Marshal.ReadInt32(lParam);
        var control = (GetAsyncKeyState(0x11) & 0x8000) != 0;
        var shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
        var ctrlV = key == 0x56 && control && !shift;
        var shiftInsert = key == VkInsert && shift && !control;
        if (!ctrlV && !shiftInsert) return CallNextHookEx(_hook, code, wParam, lParam);

        try
        {
            // IsClipboardFormatAvailable es una consulta directa a user32: NO usa COM, no
            // pide los datos al dueño del portapapeles y vuelve al instante, así que es
            // segura dentro del gancho. El indicador del hilo STA queda como respaldo.
            //
            // Esto arregla el fallo de «la segunda copia la hace Windows»: el sondeo del
            // hilo STA se actualiza como mucho cada 200 ms y falla mientras el Explorador
            // tiene tomado el portapapeles (justo después de un Ctrl+C). En ese hueco el
            // indicador valía «false», el gancho dejaba pasar la pulsación y pegaba Windows.
            var hasFiles = false;
            try { hasFiles = IsClipboardFormatAvailable(CfHdrop); } catch { }
            if (!hasFiles && !_clipboardHasFiles) return CallNextHookEx(_hook, code, wParam, lParam);

            var window = GetForegroundWindow();
            if (!IsExplorer(window)) return CallNextHookEx(_hook, code, wParam, lParam);

            _explorerWindow = window;

            // ──────────────────────────────────────────────────────────────────────────
            // AQUÍ DENTRO NO SE PUEDE HACER NADA LENTO. Este método es la rutina de un
            // gancho global de teclado: Windows la ejecuta DENTRO de la cola de entrada
            // del sistema. Si tarda, TODO el equipo se queda «pensando» (ratón y teclado
            // incluidos) y los dobles clic no llegan a procesarse.
            //
            // En 2.5.7 se leía el portapapeles aquí (COM/OLE) para detectar «cortar». Eso
            // podía bloquear el sistema entero, porque el dueño del portapapeles es el
            // propio Explorador y la llamada se queda esperando a que conteste… mientras
            // su respuesta espera a que este gancho termine. Interbloqueo.
            //
            // Lo único que se hace: encolar el trabajo y cancelar el pegado nativo. La
            // lectura del portapapeles (archivos y efecto cortar) la hace el hilo STA.
            // ──────────────────────────────────────────────────────────────────────────
            if (!_work.IsAddingCompleted)
                _work.Add(() => ProcessPaste(_explorerWindow));

            return (IntPtr)1;
        }
        catch
        {
            // Ni siquiera se escribe en el registro: tocar disco dentro del gancho es
            // exactamente lo que no se puede hacer. El error se verá en el hilo STA.
            return CallNextHookEx(_hook, code, wParam, lParam);
        }
    }

    /// <summary>
    /// Trabajo real del pegado, SIEMPRE en el hilo STA propio (nunca en el gancho).
    /// Aquí sí se puede hablar con el portapapeles: cancelar la pulsación no borra su
    /// contenido, así que tanto los archivos como el efecto «cortar» siguen disponibles.
    /// </summary>
    private void ProcessPaste(IntPtr explorerWindow)
    {
        try
        {
            var handler = PasteRequested;
            if (handler is null) return;

            var files = ClipboardFilesWithRetry();
            if (files.Count == 0)
            {
                FileLog.Write("Pegado interceptado pero el portapapeles no devolvió archivos.");
                return;
            }
            var move = ClipboardIsCutCore();

            // Si no se consigue saber en qué carpeta está el Explorador, NO se tira el
            // pegado: la pulsación ya se ha cancelado, así que perderlo dejaría al usuario
            // con la sensación de que Ctrl+V no hizo nada. Se avisa a XoPy con el destino
            // vacío y la ventana preguntará a dónde copiar.
            var destination = ExplorerDestinationCore(explorerWindow);
            if (string.IsNullOrWhiteSpace(destination))
                FileLog.Write("Pegado interceptado sin carpeta de destino: XoPy la preguntará.");

            handler(files, destination ?? string.Empty, move);
            FileLog.Write($"Pegado interceptado: {files.Count} elemento(s) a {destination ?? "(sin destino)"} ({(move ? "mover" : "copiar")})");
        }
        catch (Exception ex)
        {
            FileLog.Write("Error al procesar el pegado: " + ex.Message);
        }
    }

    private static bool IsExplorer(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        var builder = new StringBuilder(256);
        if (GetClassName(window, builder, builder.Capacity) == 0) return false;
        var name = builder.ToString();
        return ExplorerClasses.Any(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Lee los archivos del portapapeles. Debe llamarse en un hilo STA.</summary>
    private static List<string> ClipboardFilesCore()
    {
        var result = new List<string>();
        try
        {
            if (!Clipboard.ContainsFileDropList()) return result;
            foreach (var path in Clipboard.GetFileDropList())
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (File.Exists(path) || Directory.Exists(path)) result.Add(path);
            }
        }
        catch { }
        return result;
    }

    public static bool ClipboardIsCut()
    {
        try
        {
            if (Application.MessageLoop || Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                return ClipboardIsCutCore();
            var cut = false;
            var thread = new Thread(() => { try { cut = ClipboardIsCutCore(); } catch { } });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join(1000);
            return cut;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// ¿El portapapeles está en modo «cortar»? El Explorador lo marca con el formato
    /// «Preferred DropEffect» (DROPEFFECT_MOVE = 2).
    /// </summary>
    /// <remarks>
    /// Antes solo se aceptaba que llegara como <c>MemoryStream</c>; en la práctica Windows
    /// puede entregarlo también como <c>byte[]</c>, y además <c>stream.Read</c> consume el
    /// cursor del flujo (la segunda lectura devolvía 0). Resultado: cortar nunca se detectaba
    /// y Ctrl+V copiaba en vez de mover, o no hacía nada. Ahora se aceptan las dos formas y
    /// se lee sin consumir.
    /// </remarks>
    private static bool ClipboardIsCutCore()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null) return false;

            byte[]? raw = null;
            // Se prueban las dos formas de pedir el mismo formato: por nombre y por formato.
            // GetData solo acepta un nombre de formato (string) o un Type: se pide primero
            // sin conversión y después con ella, que es como lo entrega el Explorador.
            var format = DataFormats.GetFormat("Preferred DropEffect");
            var name = format?.Name ?? "Preferred DropEffect";
            object? value = SafeGet(() => data.GetData(name, false));
            value ??= SafeGet(() => data.GetData(name));
            value ??= SafeGet(() => data.GetData("Preferred DropEffect", true));

            switch (value)
            {
                case byte[] bytes:
                    raw = bytes;
                    break;
                case MemoryStream memory:
                    raw = memory.ToArray();
                    break;
                case Stream stream:
                    using (var copy = new MemoryStream())
                    {
                        stream.CopyTo(copy);
                        raw = copy.ToArray();
                    }
                    break;
            }

            if (raw is null || raw.Length < 4) return false;
            var effect = BitConverter.ToInt32(raw, 0);
            // DROPEFFECT_MOVE (2) = cortar; DROPEFFECT_COPY (1) = copiar.
            return (effect & 2) == 2;
        }
        catch
        {
            return false;
        }
    }

    private static object? SafeGet(Func<object?> read)
    {
        try { return read(); } catch { return null; }
    }

    /// <summary>Carpeta de la ventana del Explorador que tiene el foco (o el escritorio).</summary>
    /// <summary>Carpeta de la primera ventana del Explorador abierta (no el escritorio).</summary>
    private static string? FirstExplorerFolder()
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return null;
            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null) return null;
            try
            {
                foreach (dynamic candidate in shell.Windows())
                {
                    try
                    {
                        string? path = candidate.Document?.Folder?.Self?.Path;
                        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists((string)path)) return path;
                    }
                    catch { }
                }
            }
            finally
            {
                try { Marshal.FinalReleaseComObject(shell); } catch { }
            }
        }
        catch { }
        return null;
    }

    private static string? ExplorerDestinationCore(IntPtr window)
    {
        var builder = new StringBuilder(256);
        GetClassName(window, builder, builder.Capacity);
        var className = builder.ToString();

        // Pegar sobre el escritorio: el destino ES el escritorio. Antes se prefería «una
        // carpeta abierta en el Explorador», que normalmente es la de ORIGEN: por eso al
        // pegar en el escritorio salía «origen y destino son el mismo archivo» y encima
        // usaba la carpeta de copia como destino.
        if (string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase))
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return null;
            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null) return null;
            try
            {
                foreach (dynamic candidate in shell.Windows())
                {
                    try
                    {
                        if ((long)candidate.HWND != window.ToInt64()) continue;
                        string? path = candidate.Document?.Folder?.Self?.Path;
                        if (!string.IsNullOrWhiteSpace(path)) return path;
                    }
                    catch { }
                }
            }
            finally
            {
                try { Marshal.FinalReleaseComObject(shell); } catch { }
            }
        }
        catch (Exception ex)
        {
            FileLog.Write("No se pudo saber la carpeta del Explorador: " + ex.Message);
        }
        return null;
    }
}
