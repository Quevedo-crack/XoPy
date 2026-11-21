using Microsoft.Win32;
using System.Windows.Forms;

namespace XoPy;

public static class ShellIntegration
{
    private const string ClassesRoot = @"Software\Classes";
    private const string AppPathKey = @"Software\Microsoft\Windows\CurrentVersion\App Paths\XoPy.exe";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static readonly string[] VerbKeys =
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
        @"Directory\Background\shell\XoPyOpen"
    };

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ClassesRoot + @"\*\shell\XoPyCopy");
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(bool enabled)
    {
        var exe = Application.ExecutablePath;
        try { RegisterAppPath(exe); } catch { }
        try
        {
            if (enabled) Register(exe);
            else Unregister();
        }
        catch
        {
            // Si una política del sistema bloquea el registro, la aplicación sigue funcionando.
        }
    }

    public static void Register(string exe)
    {
        var quoted = "\"" + exe + "\"";

        WriteVerb(@"*\shell\XoPyCopy", "Copiar con XoPy", quoted + " --copy \"%1\"", exe);
        WriteVerb(@"*\shell\XoPyCopyTo", "Copiar a… con XoPy", quoted + " --copy --ask-destination \"%1\"", exe);
        WriteVerb(@"*\shell\XoPyMove", "Mover con XoPy", quoted + " --move \"%1\"", exe);
        WriteVerb(@"*\shell\XoPyMoveTo", "Mover a… con XoPy", quoted + " --move --ask-destination \"%1\"", exe);
        WriteVerb(@"*\shell\XoPyVerify", "Verificar con XoPy", quoted + " --test \"%1\"", exe);

        WriteVerb(@"Directory\shell\XoPyCopy", "Copiar con XoPy", quoted + " --copy \"%1\"", exe);
        WriteVerb(@"Directory\shell\XoPyCopyTo", "Copiar a… con XoPy", quoted + " --copy --ask-destination \"%1\"", exe);
        WriteVerb(@"Directory\shell\XoPyMove", "Mover con XoPy", quoted + " --move \"%1\"", exe);
        WriteVerb(@"Directory\shell\XoPyMoveTo", "Mover a… con XoPy", quoted + " --move --ask-destination \"%1\"", exe);
        WriteVerb(@"Directory\shell\XoPyVerify", "Verificar con XoPy", quoted + " --test \"%1\"", exe);

        WriteVerb(@"Directory\Background\shell\XoPyPaste", "Pegar con XoPy", quoted + " --paste --to \"%V\"", exe);
        WriteVerb(@"Directory\Background\shell\XoPyOpen", "Abrir XoPy aquí", quoted + " --ask-destination", exe);
    }

    public static void Unregister()
    {
        foreach (var verb in VerbKeys)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(ClassesRoot + "\\" + verb, false); }
            catch { }
        }
    }

    private static void WriteVerb(string path, string label, string command, string exe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(ClassesRoot + "\\" + path);
        if (key is null) return;
        key.SetValue(null, label);
        key.SetValue("Icon", "\"" + exe + "\",0");
        key.SetValue("MultiSelectModel", "Player");
        using var commandKey = key.CreateSubKey("command");
        commandKey?.SetValue(null, command);

        foreach (var oldValue in new[] { "AppliesTo", "Position" })
        {
            try { key.DeleteValue(oldValue, false); } catch { }
        }
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
