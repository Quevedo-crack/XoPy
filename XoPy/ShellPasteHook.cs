using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace XoPy;

/// <summary>
/// Sustituye al copiador de Windows: escucha Ctrl+V en el Explorador y el escritorio.
/// Cuando hay archivos en el portapapeles se cancela el pegado de Windows y se hace con XoPy.
/// Si el portapapeles viene de un «Cortar» (Ctrl+X), se mueve y se borra el origen.
/// </summary>
public sealed class ShellPasteHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;

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
    private IntPtr _hook;

    public bool Enabled { get; set; }
    public event Action<IReadOnlyList<string>, string, bool>? PasteRequested;

    public bool IsInstalled => _hook != IntPtr.Zero;

    public ShellPasteHook()
    {
        _callback = OnKeyboard;
    }

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            FileLog.Write("No se pudo instalar el interceptor del teclado: " + Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        try { UnhookWindowsHookEx(_hook); } catch { }
        _hook = IntPtr.Zero;
    }

    private IntPtr OnKeyboard(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || !Enabled || wParam != (IntPtr)WmKeyDown) return CallNextHookEx(_hook, code, wParam, lParam);

        var key = Marshal.ReadInt32(lParam);
        var control = (GetAsyncKeyState(0x11) & 0x8000) != 0;
        var shift = (GetAsyncKeyState(0x10) & 0x8000) != 0;
        if (key != 0x56 || !control || shift) return CallNextHookEx(_hook, code, wParam, lParam);

        try
        {
            var window = GetForegroundWindow();
            if (!IsExplorer(window)) return CallNextHookEx(_hook, code, wParam, lParam);

            var files = ClipboardFiles();
            if (files.Count == 0) return CallNextHookEx(_hook, code, wParam, lParam);

            var destination = ExplorerDestination(window);
            if (string.IsNullOrWhiteSpace(destination)) return CallNextHookEx(_hook, code, wParam, lParam);

            var move = ClipboardIsCut();
            var handler = PasteRequested;
            if (handler is null) return CallNextHookEx(_hook, code, wParam, lParam);

            // Se cancela el pegado de Windows y se hace con XoPy.
            handler(files, destination, move);
            FileLog.Write($"Pegado interceptado: {files.Count} elemento(s) a {destination} ({(move ? "mover" : "copiar")})");
            return (IntPtr)1;
        }
        catch (Exception ex)
        {
            FileLog.Write("Error en el interceptor de pegado: " + ex.Message);
            return CallNextHookEx(_hook, code, wParam, lParam);
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

    private static List<string> ClipboardFiles()
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

    private static bool ClipboardIsCut()
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
    private static string? ExplorerDestination(IntPtr window)
    {
        var builder = new StringBuilder(256);
        GetClassName(window, builder, builder.Capacity);
        var className = builder.ToString();

        if (string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase))
            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return null;
            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null) return null;
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
        catch (Exception ex)
        {
            FileLog.Write("No se pudo saber la carpeta del Explorador: " + ex.Message);
        }
        return null;
    }
}
