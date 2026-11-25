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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string path);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr module);

    private delegate int RegisterDelegate();

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
            // Se limpian las entradas de menú de versiones antiguas (el workflow de CI las
            // crea a propósito y comprueba que desaparecen), pero Unregister ya NO borra el
            // CLSID de la extensión «Pegar»: esa clave es la que hace funcionar Ctrl+V.
            try { RegisterAppPath(Application.ExecutablePath); } catch { }
            try { Unregister(); } catch { }
            // «Enviar a → XoPy»: se crea y se deja. Antes el arranque llamaba a Cleanup(),
            // que borraba el acceso, y por eso «Enviar a» no existía nunca.
            try { CreateSendToShortcut(); } catch (Exception ex) { FileLog.Write("No se pudo crear Enviar a: " + ex.Message); }
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

        // Se llama a DllRegisterServer de la propia DLL (registro COM oficial) y ADEMÁS se
        // escriben las claves a mano: el registro COM por sí solo no basta porque no cubre
        // todos los contextos del Explorador.
        RegisterComServer(dll);

        const string clsid = "{3B1E92A7-7AD6-4E71-A218-75449CE13061}";
        using (var key = Registry.CurrentUser.CreateSubKey(ClassesRoot + @"\CLSID\" + clsid))
            key?.SetValue(null, "XoPy Shell Extension");
        using (var key = Registry.CurrentUser.CreateSubKey(ClassesRoot + @"\CLSID\" + clsid + @"\InprocServer32"))
        {
            key?.SetValue(null, dll);
            key?.SetValue("ThreadingModel", "Apartment");
        }
        // «Pegar» debe salir con el clic derecho EN TODOS los contextos donde Windows lo
        // ofrece: dentro de una carpeta (fondo), sobre una carpeta, y en el escritorio.
        // Antes solo se registraba el fondo de carpeta: por eso al pegar en el escritorio o
        // con una carpeta seleccionada seguía copiando el de Windows.
        foreach (var context in new[]
                 {
                     @"Directory\Background\shellex\ContextMenuHandlers\XoPyPaste",
                     @"Directory\shellex\ContextMenuHandlers\XoPyPaste",
                     @"DesktopBackground\shellex\ContextMenuHandlers\XoPyPaste"
                 })
        {
            using var handlers = Registry.CurrentUser.CreateSubKey(ClassesRoot + @"\" + context);
            handlers?.SetValue(null, clsid);
        }
        try
        {
            using var approved = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved");
            approved?.SetValue(clsid, "XoPy Pegar");
        }
        catch { }

        // Sin este aviso el Explorador sigue con la caché antigua y no muestra «Pegar»
        // hasta reiniciarlo a mano.
        try { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } catch { }
        try { SHChangeNotify(0x00000004, 0, IntPtr.Zero, IntPtr.Zero); } catch { }
    }

    /// <summary>Ejecuta DllRegisterServer de la extensión; devuelve si lo consiguió.</summary>
    private static bool RegisterComServer(string dll)
    {
        var module = IntPtr.Zero;
        try
        {
            module = LoadLibrary(dll);
            if (module == IntPtr.Zero) return false;
            var address = GetProcAddress(module, "DllRegisterServer");
            if (address == IntPtr.Zero) return false;
            var register = Marshal.GetDelegateForFunctionPointer<RegisterDelegate>(address);
            return register() == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (module != IntPtr.Zero) FreeLibrary(module);
        }
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
        // instalador y la que hace que Ctrl+V abra XoPy.
        // TAMPOCO se borra «Approved»: sin esa aprobación el Explorador NO muestra la entrada
        // «Pegar con XoPy» del menú contextual (por eso el pegado del menú no funcionaba y
        // acababa copiando Windows). Solo se quita al desinstalar (RemovePasteExtension).
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
