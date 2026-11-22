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

        if (string.IsNullOrWhiteSpace(settings.Destination) || !Directory.Exists(settings.Destination))
            settings.Destination = AppSettings.DefaultDestination();

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
