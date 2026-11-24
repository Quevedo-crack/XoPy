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

    /// <summary>
    /// Aplica la integración con el Explorador. Con <paramref name="enabled"/> se deja XoPy
    /// como gestor de copias: NO se borra la extensión «Pegar» (eso la rompía y el Ctrl+V
    /// seguía siendo el de Windows, que es lo que el usuario notaba). Sin él se limpia todo.
    /// </summary>
    public static void Apply(bool enabled)
    {
        if (enabled)
        {
            // Se registra la ruta y se conserva la extensión que instala el instalador.
            try { RegisterAppPath(Application.ExecutablePath); } catch { }
            try { EnsurePasteExtension(); } catch (Exception ex) { FileLog.Write("No se pudo registrar el Pegar: " + ex.Message); }
            try { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { }
            return;
        }

        Cleanup();
    }

    /// <summary>
    /// Deja registrada la extensión «Pegar» apuntando a la DLL que hay junto al ejecutable.
    /// Es idempotente: si el instalador ya la registró bien, no cambia nada.
    /// </summary>
    public static void EnsurePasteExtension()
    {
        var dll = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? string.Empty, "XoPyShell.dll");
        if (!File.Exists(dll)) return;

        const string clsid = "{3B1E92A7-7AD6-4E71-A218-75449CE13061}";
        using (var key = Registry.CurrentUser.CreateSubKey(ClassesRoot + @"\CLSID\" + clsid))
            key?.SetValue(null, "XoPy Shell Extension");
        using (var key = Registry.CurrentUser.CreateSubKey(ClassesRoot + @"\CLSID\" + clsid + @"\InprocServer32"))
        {
            key?.SetValue(null, dll);
            key?.SetValue("ThreadingModel", "Apartment");
        }
        using (var key = Registry.CurrentUser.CreateSubKey(ClassesRoot + @"\Directory\Background\shellex\ContextMenuHandlers\XoPyPaste"))
            key?.SetValue(null, clsid);
        try
        {
            using var approved = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved");
            approved?.SetValue(clsid, "XoPy Pegar");
        }
        catch { }
    }

    /// <summary>¿Está activa la sustitución del copiador (extensión + DLL en su sitio)?</summary>
    public static bool IsPasteExtensionInstalled()
    {
        try
        {
            var dll = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? string.Empty, "XoPyShell.dll");
            if (!File.Exists(dll)) return false;
            using var key = Registry.CurrentUser.OpenSubKey(LegacyPasteClsid + @"\InprocServer32");
            var value = key?.GetValue(null) as string;
            return !string.IsNullOrEmpty(value) && File.Exists(value);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Borra la clave APPROVED y la DLL asociada al desinstalar la integración.</summary>
    public static void RemovePasteExtension()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(LegacyPasteClsid, false); } catch { }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved", true);
            key?.DeleteValue("{3B1E92A7-7AD6-4E71-A218-75449CE13061}", false);
        }
        catch { }
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\Background\shellex\ContextMenuHandlers\XoPyPaste", false);
        }
        catch { }
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
        // La clave CLSID de la extensión «Pegar» NO se borra aquí: es la que registra el
        // instalador y la que hace que Ctrl+V abra XoPy. Antes se borraba al arrancar y por
        // eso la integración no sustituía al copiador de Windows.
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved", true);
            key?.DeleteValue("{3B1E92A7-7AD6-4E71-A218-75449CE13061}", false);
        }
        catch { }
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
