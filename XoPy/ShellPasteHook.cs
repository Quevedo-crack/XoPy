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
                // Actualiza el indicador sin bloquear el hook.
                _clipboardHasFiles = SafeContainsFileDropList();

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

    private static bool SafeContainsFileDropList()
    {
        try { return Clipboard.ContainsFileDropList(); }
        catch { return false; }
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
            // Sólo se consulta el indicador mantenido por el hilo STA: nada de COM ni
            // portapapeles aquí dentro (eso colgaba el Explorador y daba «Object reference»).
            if (!_clipboardHasFiles) return CallNextHookEx(_hook, code, wParam, lParam);

            var window = GetForegroundWindow();
            if (!IsExplorer(window)) return CallNextHookEx(_hook, code, wParam, lParam);

            _explorerWindow = window;

            // Se cancela el pegado nativo y el trabajo se hace en el hilo STA.
            if (!_work.IsAddingCompleted)
                _work.Add(ProcessPaste);

            return (IntPtr)1;
        }
        catch (Exception ex)
        {
            FileLog.Write("Error en el interceptor de pegado: " + ex.Message);
            return CallNextHookEx(_hook, code, wParam, lParam);
        }
    }

    private void ProcessPaste()
    {
        var handler = PasteRequested;
        if (handler is null) return;

        var files = ClipboardFilesCore();
        if (files.Count == 0) return;

        var destination = ExplorerDestinationCore(_explorerWindow);
        if (string.IsNullOrWhiteSpace(destination)) return;

        var move = ClipboardIsCutCore();
        handler(files, destination, move);
        FileLog.Write($"Pegado interceptado: {files.Count} elemento(s) a {destination} ({(move ? "mover" : "copiar")})");
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

    private static bool ClipboardIsCutCore()
    {
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null || !data.GetDataPresent("Preferred DropEffect")) return false;
            if (data.GetData("Preferred DropEffect") is not MemoryStream stream) return false;
            var buffer = new byte[4];
            if (stream.Read(buffer, 0, 4) < 4) return false;
            var effect = BitConverter.ToInt32(buffer, 0);
            return (effect & 2) == 2;
        }
        catch
        {
            return false;
        }
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
