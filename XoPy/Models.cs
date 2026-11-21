using System.Text.Json.Serialization;

namespace XoPy;

public enum OperationType { Copy = 0, Move = 1, Delete = 2, Test = 3 }

public enum ItemKind { File = 0, Folder = 1, DirectoryEntry = 2 }

public enum ItemState { Pending = 0, Measuring = 1, Running = 2, Paused = 3, Verifying = 4, Completed = 5, Failed = 6, Skipped = 7, Cancelled = 8 }

public enum ConflictPolicy
{
    Ask = 0,
    Overwrite = 1,
    Skip = 2,
    Rename = 3,
    OverwriteOlder = 4,
    OverwriteSmaller = 5
}

public enum ErrorPolicy { Ask = 0, Retry = 1, Skip = 2, Abort = 3 }

public enum VerifyAlgorithm { None = 0, Crc32 = 1, Md5 = 2, Sha1 = 3, Sha256 = 4, Sha512 = 5 }

public sealed class TransferItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ParentId { get; set; }
    public int Depth { get; set; }
    public ItemKind Kind { get; set; } = ItemKind.File;
    public string SourcePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public OperationType Operation { get; set; }
    public long SizeBytes { get; set; }
    public long TransferredBytes { get; set; }
    public ItemState State { get; set; } = ItemState.Pending;
    public string? Error { get; set; }
    public string? CurrentFile { get; set; }
    public string? HashSource { get; set; }
    public string? HashTarget { get; set; }
    public bool SizeKnown { get; set; } = true;
    public bool IsExpanded { get; set; } = true;

    [JsonIgnore]
    public bool IsFolder => Kind == ItemKind.Folder;

    [JsonIgnore]
    public string Name => Path.GetFileName(Path.TrimEndingDirectorySeparator(SourcePath));

    [JsonIgnore]
    public double Percent
    {
        get
        {
            if (State == ItemState.Completed) return 100;
            if (Kind == ItemKind.Folder) return 0;
            if (SizeBytes <= 0) return 0;
            return Math.Clamp(TransferredBytes * 100d / SizeBytes, 0, 100);
        }
    }

    [JsonIgnore]
    public string StateText => State switch
    {
        ItemState.Pending => Kind == ItemKind.Folder ? "En cola" : "Pendiente",
        ItemState.Measuring => "Analizando…",
        ItemState.Running => Operation switch
        {
            OperationType.Move => "Moviendo",
            OperationType.Delete => "Eliminando",
            OperationType.Test => "Verificando",
            _ => "Copiando"
        },
        ItemState.Paused => "En pausa",
        ItemState.Verifying => "Verificando",
        ItemState.Completed => Operation switch
        {
            OperationType.Move => "Movido",
            OperationType.Delete => "Eliminado",
            OperationType.Test => "Correcto",
            _ => "Copiado"
        },
        ItemState.Failed => "Error",
        ItemState.Skipped => "Omitido",
        ItemState.Cancelled => "Cancelado",
        _ => "Pendiente"
    };

    [JsonIgnore]
    public string SizeText => Kind == ItemKind.Folder
        ? (SizeKnown ? Format.Bytes(SizeBytes) : "—")
        : Format.Bytes(SizeBytes);

    public TransferItem CloneShallow() => (TransferItem)MemberwiseClone();
}

public sealed class HistoryEntry
{
    public DateTime FinishedAt { get; set; } = DateTime.Now;
    public OperationType Operation { get; set; }
    public string Destination { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public long Bytes { get; set; }
    public double Seconds { get; set; }
    public int Errors { get; set; }

    [JsonIgnore]
    public double BytesPerSecond => Seconds > 0.05 ? Bytes / Seconds : 0;

    [JsonIgnore]
    public string OperationText => Operation switch
    {
        OperationType.Move => "Mover",
        OperationType.Delete => "Eliminar",
        OperationType.Test => "Verificar",
        _ => "Copiar"
    };

    [JsonIgnore]
    public string ResultText => Errors == 0 ? "Correcto" : $"{Errors} error(es)";

    [JsonIgnore]
    public string Summary => $"{FinishedAt:yyyy-MM-dd HH:mm}  ·  {OperationText}  ·  {FileCount} archivos  ·  {Format.Bytes(Bytes)}  ·  {Format.Speed(BytesPerSecond)}  ·  {ResultText}";
}

public sealed class AppSettings
{
    public string Destination { get; set; } = string.Empty;
    public bool DarkTheme { get; set; } = true;
    public ConflictPolicy Conflict { get; set; } = ConflictPolicy.Ask;
    public ErrorPolicy OnError { get; set; } = ErrorPolicy.Retry;
    public VerifyAlgorithm Verify { get; set; } = VerifyAlgorithm.Sha256;
    public bool VerifyAfterCopy { get; set; }
    public int RetryCount { get; set; } = 3;
    public int Parallelism { get; set; } = 2;
    public int BufferKiB { get; set; } = 1024;
    public int SpeedLimitMiB { get; set; }
    public bool PreserveTimestamps { get; set; } = true;
    public bool UseRecycleBin { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool PlaySoundWhenDone { get; set; }
    public bool OpenDestinationWhenDone { get; set; }
    public bool CloseWhenDone { get; set; }
    public bool ShutdownWhenDone { get; set; }
    public bool ExplorerIntegration { get; set; }
    public bool ConfirmDelete { get; set; } = true;
    public bool ShowHashes { get; set; } = true;
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; } = int.MinValue;
    public bool WindowMaximized { get; set; }
    public List<string> Favorites { get; set; } = new();

    public static string DefaultDestination()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads)) return downloads;
        return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }
}

public sealed class QueueSnapshot
{
    public string Destination { get; set; } = string.Empty;
    public OperationType Operation { get; set; }
    public List<string> Sources { get; set; } = new();
}

public sealed class StartupRequest
{
    public OperationType? Operation { get; set; }
    public List<string> Paths { get; set; } = new();
    public string? Destination { get; set; }
    public bool PasteFromClipboard { get; set; }
    public bool StartMinimized { get; set; }
    public bool AskDestination { get; set; }

    public static StartupRequest Parse(string[] args)
    {
        var request = new StartupRequest();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--copy": request.Operation = OperationType.Copy; break;
                case "--move": request.Operation = OperationType.Move; break;
                case "--delete": request.Operation = OperationType.Delete; break;
                case "--test": request.Operation = OperationType.Test; break;
                case "--paste": request.PasteFromClipboard = true; break;
                case "--tray": request.StartMinimized = true; break;
                case "--ask-destination": request.AskDestination = true; break;
                case "--screenshot":
                case "--screenshot-options":
                case "--screenshot-conflict":
                    i++;
                    break;
                case "--to":
                    if (i + 1 < args.Length) request.Destination = Normalize(args[++i]);
                    break;
                default:
                    if (arg.StartsWith("--to=", StringComparison.OrdinalIgnoreCase)) request.Destination = Normalize(arg[5..]);
                    else if (arg.StartsWith("--screenshot", StringComparison.OrdinalIgnoreCase)) break;
                    else if (!arg.StartsWith("-", StringComparison.Ordinal)) request.Paths.Add(Normalize(arg));
                    break;
            }
        }
        return request;
    }

    private static string Normalize(string value)
    {
        var text = value.Trim();
        if (text.Length > 1 && text.StartsWith('"') && text.EndsWith('"')) text = text[1..^1];
        return text;
    }

    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this);

    public static StartupRequest? FromJson(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<StartupRequest>(json); }
        catch { return null; }
    }
}

public static class Format
{
    public static string Bytes(long value)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return unit == 0 ? $"{value} B" : $"{size:0.0} {units[unit]}";
    }

    public static string Speed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return "—";
        return $"{Bytes((long)bytesPerSecond)}/s";
    }

    public static string Duration(TimeSpan span)
    {
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
        return $"{span.Minutes:00}:{span.Seconds:00}";
    }

    public static string Algorithm(VerifyAlgorithm algorithm) => algorithm switch
    {
        VerifyAlgorithm.Crc32 => "CRC32",
        VerifyAlgorithm.Md5 => "MD5",
        VerifyAlgorithm.Sha1 => "SHA-1",
        VerifyAlgorithm.Sha256 => "SHA-256",
        VerifyAlgorithm.Sha512 => "SHA-512",
        _ => "Sin verificación"
    };
}
