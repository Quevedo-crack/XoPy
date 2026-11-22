using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace XoPy;

/// <summary>
/// Integración con Windows. XoPy ya NO añade entradas al menú contextual del Explorador
/// («Copiar con XoPy», «Mover con XoPy», «Pegar con XoPy», etc.): el usuario pidió que solo
/// aparezca «Pegar» y que lo haga XoPy directamente. La sustitución del copiador se hace con
/// Ctrl+V (ver <see cref="ShellPasteHook"/>), sin tocar el menú contextual.
/// Esta clase solo se encarga de limpiar las claves antiguas de versiones anteriores,
/// quitar los accesos de «Enviar a» y registrar la ruta de la aplicación.
/// </summary>
public static class ShellIntegration
{
    private const string ClassesRoot = @"Software\Classes";
    private const string AppPathKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths\XoPy.exe";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    /// <summary>Claves de menú contextual de versiones anteriores, para borrarlas al actualizar.</summary>
    private static readonly string[] LegacyVerbKeys =
    {
        @"*\shell\XoPyCopy",
        @"*\shell\XoPyCopyTo",
        @"*\shell\XoPyMove",
        @"*\shell\XoPyMoveTo",
        @"*\shell\XoPyVerify",
        @"Directory\shell\XoPyCopy",
        @"Directory\shell\XoPyCopyTo",
        @"Directory\shell\XoPyMove",
        @"Directory\shell\XoPyMoveTo",
        @"Directory\shell\XoPyVerify",
        @"Directory\Background\shell\XoPyPaste",
        @"Directory\Background\shell\XoPyOpen",
        @"Directory\Background\shellex\ContextMenuHandlers\XoPyPaste",
        @"Directory\shellex\ContextMenuHandlers\XoPyPaste",
        @"*\shellex\ContextMenuHandlers\XoPyPaste"
    };

    private const string LegacyPasteClsid = @"Software\Classes\CLSID\{3B1E92A7-7AD6-4E71-A218-75449CE13061}";

    /// <summary>Quedan claves antiguas del menú contextual (de una versión anterior).</summary>
    public static bool HasLegacyEntries()
    {
        try
        {
            foreach (var verb in LegacyVerbKeys)
            {
                using var key = Registry.CurrentUser.OpenSubKey(ClassesRoot + @"\" + verb);
                if (key is not null) return true;
            }
            using var clsid = Registry.CurrentUser.OpenSubKey(LegacyPasteClsid);
            if (clsid is not null) return true;
        }
        catch { }
        return false;
    }

    /// <summary>Indica si queda una integración antigua registrada.</summary>
    public static bool IsEnabled() => HasLegacyEntries();

    /// <summary>Usa atajos de teclado y un acceso estándar de «Enviar a», sin modificar el Pegar nativo.</summary>
    public static void Apply(bool enabled)
    {
        Cleanup();
        if (enabled)
        {
            try { CreateSendToShortcut(); } catch (Exception ex) { FileLog.Write("No se pudo crear el acceso de Enviar a: " + ex.Message); }
        }
    }

    /// <summary>Borra las integraciones antiguas y registra la ruta de la aplicación.</summary>
    public static void Cleanup()
    {
        var exe = Application.ExecutablePath;
        try { RegisterAppPath(exe); } catch { }
        try { RemoveSendToShortcuts(); } catch { }
        try { Unregister(); } catch { }
        try { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { }
    }

    private static void CreateSendToShortcut()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "SendTo");
        Directory.CreateDirectory(folder);
        var shortcutPath = Path.Combine(folder, "XoPy.lnk");
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) throw new InvalidOperationException("Windows Script Host no está disponible.");
        dynamic? shell = Activator.CreateInstance(shellType);
        if (shell is null) throw new InvalidOperationException("No se pudo crear el acceso directo.");
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = Application.ExecutablePath;
        shortcut.Arguments = "--ask-destination";
        shortcut.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath) ?? string.Empty;
        shortcut.Description = "Copiar elementos con XoPy a una carpeta elegida";
        shortcut.Save();
        try { Marshal.FinalReleaseComObject(shortcut); } catch { }
        try { Marshal.FinalReleaseComObject(shell); } catch { }
    }

    /// <summary>XoPy no usa «Enviar a»: se sustituye al copiador de Windows con Ctrl+V.</summary>
    public static void RemoveSendToShortcuts()
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "SendTo");
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder, "*.lnk"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Contains("XoPy", StringComparison.OrdinalIgnoreCase)) File.Delete(file);
            }
        }
        catch { }
    }

    /// <summary>Borra todas las claves de menú contextual de versiones anteriores.</summary>
    public static void Unregister()
    {
        foreach (var verb in LegacyVerbKeys)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(ClassesRoot + "\\" + verb, false); }
            catch { }
        }
        try { Registry.CurrentUser.DeleteSubKeyTree(LegacyPasteClsid, false); } catch { }
    }

    public static void SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;
            if (enabled) key.SetValue("XoPy", "\"" + Application.ExecutablePath + "\" --tray");
            else key.DeleteValue("XoPy", false);
        }
        catch { }
    }

    private static void RegisterAppPath(string exe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(AppPathKey);
        if (key is null) return;
        key.SetValue(null, exe);
        key.SetValue("Path", Path.GetDirectoryName(exe) ?? string.Empty);
    }

    /// <summary>Ruta del ejecutable actual, útil para diagnósticos de la integración.</summary>
    public static string ExecutablePath => Application.ExecutablePath;
}
