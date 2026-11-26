using System.Text.Json;

namespace XoPy;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string DataFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XoPy");
    public string SettingsFile => Path.Combine(DataFolder, "settings.json");
    public string HistoryFile => Path.Combine(DataFolder, "history.json");
    public string LastQueueFile => Path.Combine(DataFolder, "ultima-cola.xopyq");

    public AppSettings Load()
    {
        AppSettings settings = new();
        var migrated = false;
        try
        {
            if (File.Exists(SettingsFile))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile));
                if (loaded is not null) settings = loaded;
            }
        }
        catch
        {
            // Un archivo de configuración dañado no debe impedir que la aplicación arranque.
        }

        if (settings.SettingsVersion < 1)
        {
            // En 2.4.0 se podía quedar oculta en bandeja al cerrar o arrancar; al actualizar,
            // la primera apertura vuelve a ser visible y el cierre normal termina la aplicación.
            settings.MinimizeToTray = false;
            settings.StartMinimized = false;
            settings.CollapseJobs = true;
            settings.SettingsVersion = 1;
            migrated = true;
        }

        if (settings.SettingsVersion < 2)
        {
            // 2.5.6: la ventana se ocultaba sola al terminar cada copia y el usuario lo vivía
            // como que «se cierra y se vuelve a abrir». Se deja visible por defecto; quien
            // la quiera ocultar lo marca en Preferencias.
            settings.CloseWhenDone = false;
            settings.SettingsVersion = 2;
            migrated = true;
        }

        if (settings.SettingsVersion < 3)
        {
            // 2.5.6: el historial de listas arranca a la vista y se puede ocultar con un botón;
            // el usuario lo pedía «ocultable y mostrable» sin depender del menú.
            settings.ShowJobs = true;
            settings.CollapseJobs = false;
            settings.SettingsVersion = 3;
            migrated = true;
        }

        if (settings.SettingsVersion < 4)
        {
            // Fusión con FlashCopyPro: pegar empieza SOLO, sin cuadro intermedio, y la lista
            // arranca visible enseñando la ruta completa de cada archivo.
            settings.AskWhenPasting = false;
            settings.ShowFullPath = true;
            settings.ShowList = true;
            settings.SettingsVersion = 4;
            migrated = true;
        }

        if (settings.SettingsVersion < 5)
        {
            // La copia se había vuelto mucho más lenta: el multihilo venía encendido y
            // copiar cuatro archivos a la vez en el mismo disco (o en un disco duro
            // normal) es más lento que copiarlos de uno en uno. Vuelve a apagarse.
            settings.MultiThreadCopy = false;
            settings.SettingsVersion = 5;
            migrated = true;
        }

        if (settings.SettingsVersion < 6)
        {
            // Cerrar la ventana NO debe cerrar XoPy: se queda junto al reloj, como
            // TeraCopy o SuperCopier. Venía apagado en algunas instalaciones y la X
            // mataba el programa entero (y con él el interceptor de Ctrl+V).
            settings.MinimizeToTray = true;
            // La ventana se abre en su tamaño normal, nunca maximizada de salida.
            settings.WindowMaximized = false;
            settings.SettingsVersion = 6;
            migrated = true;
        }

        settings.Favorites ??= new List<string>();
        settings.ExtraDestinations ??= new List<string>();
        settings.ExtraDestinations = settings.ExtraDestinations
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (settings.Favorites.Count == 0)
        {
            settings.Favorites.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            settings.Favorites.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            settings.Favorites.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            settings.Favorites.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            settings.Favorites.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        }

        // El destino NO se rellena solo. Siempre queda vacío al arrancar: así XoPy pregunta
        // a dónde copiar en lugar de usar una carpeta heredada (el escritorio, «Descargas»…,
        // que era justo lo que hacía copiar al sitio equivocado sin preguntar).
        settings.Destination = string.Empty;

        settings.Parallelism = Math.Clamp(settings.Parallelism, 1, 16);
        settings.BufferKiB = Math.Clamp(settings.BufferKiB, 64, 8192);
        settings.SpeedLimitMiB = Math.Clamp(settings.SpeedLimitMiB, 0, 100000);
        settings.RetryCount = Math.Clamp(settings.RetryCount, 0, 20);
        settings.SecureDeletePasses = Math.Clamp(settings.SecureDeletePasses, 0, 7);
        settings.CompletionScript ??= string.Empty;
        if (migrated) Save(settings);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Silencioso: guardar preferencias nunca debe bloquear la interfaz.
        }
    }

    public List<HistoryEntry> LoadHistory()
    {
        try
        {
            if (!File.Exists(HistoryFile)) return new List<HistoryEntry>();
            return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(HistoryFile)) ?? new List<HistoryEntry>();
        }
        catch
        {
            return new List<HistoryEntry>();
        }
    }

    public void SaveHistory(IEnumerable<HistoryEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(HistoryFile, JsonSerializer.Serialize(entries.Take(500).ToList(), JsonOptions));
        }
        catch
        {
            // El historial es opcional.
        }
    }

    public void AppendHistory(HistoryEntry entry)
    {
        try
        {
            var history = LoadHistory();
            history.Insert(0, entry);
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(HistoryFile, JsonSerializer.Serialize(history.Take(300).ToList(), JsonOptions));
        }
        catch
        {
            // El historial es opcional.
        }
    }
}
