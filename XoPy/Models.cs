using System.IO;
using System.Text.Json.Serialization;

namespace XoPy;

public enum OperationType { Copy = 0, Move = 1, Delete = 2, Test = 3 }

public enum ItemKind { File = 0, Folder = 1, DirectoryEntry = 2 }

/// <summary>Decisión del usuario cuando el destino no tiene espacio suficiente.</summary>
public enum FreeSpaceMode
{
    Ignore,
    FitOnly,
    Cancel
}

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

/// <summary>Motor que hace la copia de cada archivo.</summary>
public enum CopyEngineKind
{
    /// <summary>CopyFileExW de Windows: rápido, con pausa, saltar y límite de velocidad.</summary>
    Native = 0,
    /// <summary>Lectura/escritura propia con búfer configurable.</summary>
    Managed = 1,
    /// <summary>Robocopy de Windows: muy rápido con muchos archivos, redes y rutas largas.</summary>
    Robocopy = 2
}

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

    /// <summary>Nombre visible. Se puede cambiar desde la lista (el archivo del disco no se toca).</summary>
    private string? _displayName;

    [JsonIgnore]
    public string Name
    {
        get => string.IsNullOrEmpty(_displayName)
            ? Path.GetFileName(Path.TrimEndingDirectorySeparator(SourcePath))
            : _displayName!;
        set => _displayName = string.IsNullOrWhiteSpace(value) ? null : value;
    }

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
    public string ResultText => Errors == 0 ? "Correcto" : Format.Plural(Errors, "error", "errores");

    [JsonIgnore]
    public string Summary => $"{FinishedAt:yyyy-MM-dd HH:mm}  ·  {OperationText}  ·  {Format.Files(FileCount)}  ·  {Format.Bytes(Bytes)}  ·  {Format.Speed(BytesPerSecond)}  ·  {ResultText}";
}

public sealed class AppSettings
{
    /// <summary>Versión de esquema para migraciones de preferencias entre versiones.</summary>
    public int SettingsVersion { get; set; }
    public string Destination { get; set; } = string.Empty;
    public bool DarkTheme { get; set; } = true;
    public ConflictPolicy Conflict { get; set; } = ConflictPolicy.Ask;
    public ErrorPolicy OnError { get; set; } = ErrorPolicy.Retry;
    public VerifyAlgorithm Verify { get; set; } = VerifyAlgorithm.Sha256;
    public bool VerifyAfterCopy { get; set; }
    public int RetryCount { get; set; } = 3;
    public int Parallelism { get; set; } = 4;
    /// <summary>
    /// Copiar varios archivos a la vez. Apagado de fábrica: dentro de un mismo disco (y en
    /// la mayoría de discos duros y memorias USB) ir en paralelo hace que el disco pase el
    /// rato buscando en vez de copiando, y la copia tarda MÁS. Se puede encender en la
    /// barra o en Opciones para copiar entre dos discos rápidos distintos.
    /// </summary>
    public bool MultiThreadCopy { get; set; }
    public int BufferKiB { get; set; } = 4096;
    public int SpeedLimitMiB { get; set; }
    public bool PreserveTimestamps { get; set; } = true;
    public bool UseRecycleBin { get; set; } = true;
    /// <summary>
    /// Cerrar con la X deja XoPy junto al reloj en lugar de terminar el proceso, como hacen
    /// SuperCopier y TeraCopy. Está ACTIVADO por defecto: el usuario pidió expresamente que
    /// al cerrar no se cierre del todo, sino que quede esperando con su icono.
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>Mostrar la barra de menús superior (Archivo/Transferencia/Cola…).</summary>
    public bool ShowMenuBar { get; set; }


    /// <summary>Ya se explicó una vez que XoPy queda en la bandeja al cerrar.</summary>
    public bool TrayHintShown { get; set; }
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool PlaySoundWhenDone { get; set; }
    public bool OpenDestinationWhenDone { get; set; }
    // Falso por defecto: la ventana no debe desaparecer sola al terminar una copia (el
    // usuario la percibía como «se cierra y se vuelve a abrir»). Se activa en Preferencias.
    public bool CloseWhenDone { get; set; }
    public bool ShutdownWhenDone { get; set; }
    public bool ExplorerIntegration { get; set; }
    public bool ConfirmDelete { get; set; } = true;
    public bool ShowHashes { get; set; } = true;
    public bool CopyLockedFiles { get; set; } = true;
    public bool ResumePartials { get; set; } = true;
    public CopyEngineKind Engine { get; set; } = CopyEngineKind.Native;
    public int SecureDeletePasses { get; set; }
    public bool WriteChecksumFile { get; set; }
    public string CompletionScript { get; set; } = string.Empty;
    public List<string> ExtraDestinations { get; set; } = new();
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; } = int.MinValue;
    public bool WindowMaximized { get; set; }
    public bool CollapseJobs { get; set; } = true;
    /// <summary>Historial de listas de copia (columna de trabajos) visible.</summary>
    public bool ShowJobs { get; set; }
    // Falso: la ventana abre con la lista de archivos visible, como TeraCopy. La vista
    // compacta queda como opción («Ocultar»), no como estado inicial.
    /// <summary>Modo de ventana al abrir: 0 = reducida, 1 = completa, 2 = automático.</summary>
    /// <summary>0 = solo el progreso (reducida), 1 = completa, 2 = automático. De fábrica
    /// arranca REDUCIDA: es como el usuario quiere ver XoPy al abrirlo.</summary>
    public int WindowMode { get; set; } = 0;
    /// <summary>Arranca con la lista oculta (vista reducida). Encendido de fábrica.</summary>
    public bool CompactView { get; set; } = true;
    public bool ReplaceWindowsCopier { get; set; } = true;
    public bool AskWhenBusy { get; set; } = true;

    /// <summary>
    /// Falso (lo normal): al pegar con Ctrl+V o con «Pegar» del menú del Explorador, XoPy
    /// arranca DIRECTAMENTE con lo que haya en el portapapeles. Verdadero: antes de empezar
    /// pregunta en qué lista van los archivos. El usuario pidió que pegar sea inmediato.
    /// </summary>
    public bool AskWhenPasting { get; set; }

    /// <summary>
    /// La lista enseña la ruta completa de cada origen («C:\Users\...\foto.jpg») además del
    /// nombre. Viene de FlashCopyPro, donde cada fila muestra la ruta entera.
    /// </summary>
    public bool ShowFullPath { get; set; } = true;

    /// <summary>Lista de archivos a la vista. Se oculta con el botón «Ocultar lista».</summary>
    public bool ShowList { get; set; } = true;

    public List<string> Favorites { get; set; } = new();

    // Opciones con equivalente en TeraCopy (todas se aplican de forma conservadora).
    public bool ConfirmDragDrop { get; set; }
    public bool OnlyWhenScrollLockOff { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool CheckFreeSpace { get; set; } = true;
    public bool UseSystemWriteCache { get; set; } = true;
    public string FastDevices { get; set; } = string.Empty;
    public string SlowDevices { get; set; } = string.Empty;
    public string Language { get; set; } = "es";

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
    public List<string> ExtraDestinations { get; set; } = new();
    public bool VerifyAfterCopy { get; set; }
    public VerifyAlgorithm Verify { get; set; } = VerifyAlgorithm.Sha256;
    public DateTime SavedAt { get; set; } = DateTime.Now;
}

public sealed class StartupRequest
{
    public OperationType? Operation { get; set; }
    public List<string> Paths { get; set; } = new();
    public string? Destination { get; set; }
    public bool PasteFromClipboard { get; set; }
    public bool StartMinimized { get; set; }
    public bool NoShellHook { get; set; }
    /// <summary>Modo seguro (--safe): sin gancho de teclado ni integración con el Explorador.</summary>
    public bool SafeMode { get; set; }
    public bool AskDestination { get; set; }
    public bool Screenshot { get; set; }
    /// <summary>Ruta del PNG que se genera con --screenshot (solo para las capturas de CI).</summary>
    public string? ScreenshotPath { get; set; }

    /// <summary>
    /// Qué captura pedir: window (por defecto), tabs, menus, options, conflict o history.
    /// Lo usan los flujos de CI para revisar la interfaz en Windows.
    /// </summary>
    public string ScreenshotMode { get; set; } = "window";

    /// <summary>Cerrar al terminar la transferencia pedida por línea de comandos (--exit).</summary>
    public bool ExitWhenDone { get; set; }

    /// <summary>Prueba automática de la cola (--self-test-queue); no abre la ventana.</summary>
    public bool SelfTestQueue { get; set; }

    /// <summary>Prueba de repetición: copiar, recopiar y mover dos veces (--self-test-repeat).</summary>
    public bool SelfTestRepeat { get; set; }
    public ConflictPolicy? Conflict { get; set; }
    public VerifyAlgorithm? VerifyAlgorithm { get; set; }
    public bool VerifyAfterCopy { get; set; }
    public bool CopyLockedFiles { get; set; }
    public bool ResumePartials { get; set; }
    public bool WriteChecksumFile { get; set; }
    public int? SecureDeletePasses { get; set; }
    public string? CompletionScript { get; set; }
    public List<string> ExtraDestinations { get; set; } = new();
    public CopyEngineKind? Engine { get; set; }

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
                case "--no-shell-hook": request.NoShellHook = true; break;
                // Modo seguro: solo la ventana. Ni gancho de teclado, ni integración con el
                // Explorador, ni arranque en bandeja. Es la vía de escape cuando algo de la
                // integración deja el equipo atascado.
                case "--safe":
                case "--modo-seguro":
                    request.SafeMode = true;
                    request.NoShellHook = true;
                    request.StartMinimized = false;
                    break;
                case "--tray": request.StartMinimized = true; break;
                case "--exit": request.ExitWhenDone = true; break;
                case "--self-test-repeat":
                    request.SelfTestRepeat = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        request.ScreenshotPath = Normalize(args[++i]);
                    break;
                case "--self-test-queue":
                    request.SelfTestQueue = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        request.ScreenshotPath = Normalize(args[++i]);
                    break;
                case "--ask-destination": request.AskDestination = true; break;
                case "--locked": request.CopyLockedFiles = true; break;
                case "--no-resume": request.ResumePartials = false; break;
                case "--checksums": request.WriteChecksumFile = true; break;
                case "--to2":
                    if (i + 1 < args.Length) request.ExtraDestinations.Add(Normalize(args[++i]));
                    break;
                case "--script":
                    if (i + 1 < args.Length) request.CompletionScript = Normalize(args[++i]);
                    break;
                case "--secure":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var passes)) request.SecureDeletePasses = passes;
                    break;
                // --replace lo envía el menú contextual del Explorador («Pegar con XoPy») y
                // significa «sustituye al copiador de Windows», NO «sobrescribe sin
                // preguntar». Antes no se reconocía y la acción se ignoraba en silencio: el
                // usuario pulsaba «Pegar con XoPy» y no pasaba nada. Se acepta y se deja el
                // conflicto en «preguntar», que es lo que pide el usuario.
                case "--replace": break;
                case "--overwrite": request.Conflict = ConflictPolicy.Overwrite; break;
                case "--skip": request.Conflict = ConflictPolicy.Skip; break;
                case "--rename": request.Conflict = ConflictPolicy.Rename; break;
                case "--verify": request.VerifyAfterCopy = true; break;
                case "--engine":
                    if (i + 1 < args.Length && Enum.TryParse<CopyEngineKind>(args[++i], true, out var engine))
                        request.Engine = engine;
                    break;
                case "--algo":
                    if (i + 1 < args.Length && Enum.TryParse<VerifyAlgorithm>(args[++i], true, out var algorithm))
                        request.VerifyAlgorithm = algorithm;
                    break;
                // «--screenshot» a secas es la foto de la VENTANA PRINCIPAL (la del README).
                // Estaba pegada al mismo bloque que «--screenshot-advanced», así que la
                // captura de la ventana salía siendo el cuadro de Preferencias: por eso
                // xopy-preview.png, xopy-advanced.png y xopy-verify.png eran la misma imagen.
                case "--screenshot":
                    request.Screenshot = true;
                    request.ScreenshotMode = "window";
                    if (i + 1 < args.Length) request.ScreenshotPath = Normalize(args[++i]);
                    break;
                case "--screenshot-advanced":
                    request.Screenshot = true;
                    request.ScreenshotMode = "advanced";
                    if (i + 1 < args.Length) request.ScreenshotPath = Normalize(args[++i]);
                    break;
                // Foto de la VISTA REDUCIDA, que es con la que ahora arranca XoPy: así cada
                // compilación enseña exactamente lo que ve el usuario al abrir el programa.
                case "--screenshot-compact":
                    request.Screenshot = true;
                    request.ScreenshotMode = "compact";
                    if (i + 1 < args.Length) request.ScreenshotPath = Normalize(args[++i]);
                    break;
                case "--screenshot-options":
                case "--screenshot-conflict":
                case "--screenshot-tabs":
                case "--screenshot-menus":
                case "--screenshot-history":
                    // El argumento siguiente es la ruta del PNG: se guarda para escribir ahí
                    // y NO se trata como archivo que copiar.
                    request.Screenshot = true;
                    request.ScreenshotMode = arg switch
                    {
                        "--screenshot-tabs" => "tabs",
                        "--screenshot-menus" => "menus",
                        "--screenshot-options" => "options",
                        "--screenshot-conflict" => "conflict",
                        "--screenshot-history" => "history",
                        _ => "window"
                    };
                    // Solo se consume el argumento siguiente si es la ruta del PNG. Antes se
                    // hacía «i++» también cuando no había ruta y se saltaba el primer archivo.
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal)
                        && (args[i + 1].EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                            || !File.Exists(args[i + 1])))
                        request.ScreenshotPath = Normalize(args[++i]);
                    break;
                case "--to":
                    if (i + 1 < args.Length) request.Destination = Normalize(args[++i]);
                    break;
                // LISTA DE ARCHIVOS EN UN ARCHIVO DE TEXTO (UTF-8, una ruta por línea). La
                // manda la extensión del Explorador al soltar una carpeta con miles de
                // archivos: en la línea de comandos no caben y Windows la cortaría.
                case "--lista":
                case "--list-file":
                    if (i + 1 < args.Length)
                    {
                        var listPath = Normalize(args[++i]);
                        try
                        {
                            foreach (var line in File.ReadAllLines(listPath))
                            {
                                var entry = Normalize(line);
                                if (entry.Length > 0) request.Paths.Add(entry);
                            }
                        }
                        catch { }
                        try { File.Delete(listPath); } catch { }
                    }
                    break;
                default:
                    if (arg.StartsWith("--to=", StringComparison.OrdinalIgnoreCase)) request.Destination = Normalize(arg[5..]);
                    else if (arg.StartsWith("--screenshot", StringComparison.OrdinalIgnoreCase)) request.Screenshot = true;
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

/// <summary>
/// Arreglos de rutas escritas a mano o pegadas. Una carpeta de destino perfectamente
/// válida se daba por «no válida» solo porque traía comillas de un «Copiar como ruta de
/// acceso» del Explorador, un espacio al final, barras de Linux o un %VARIABLE% sin
/// resolver. Aquí se limpia todo eso ANTES de decidir si la ruta sirve o no.
/// </summary>
public static class PathTools
{
    /// <summary>Caracteres que Windows no admite en un nombre de archivo o carpeta.</summary>
    private static readonly char[] Forbidden = { '<', '>', '"', '|', '?', '*' };

    /// <summary>
    /// Deja la ruta como Windows la quiere, sin cambiar a qué carpeta apunta: quita
    /// comillas y espacios sobrantes, resuelve %VARIABLES%, cambia / por \, junta las
    /// barras repetidas y quita los puntos y espacios del final de cada tramo (Windows
    /// los rechaza con «nombre no válido», error 123).
    /// </summary>
    public static string CleanFolder(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Trim();

        // «Copiar como ruta de acceso» del Explorador entrega la ruta ENTRE COMILLAS.
        while (text.Length > 1 && text[0] == '"' && text[^1] == '"') text = text[1..^1].Trim();
        text = text.Trim('\u200b', '\u202a', '\u202c', '\ufeff');

        try { if (text.Contains('%')) text = Environment.ExpandEnvironmentVariables(text); }
        catch { }

        text = text.Replace('/', '\\');

        // Barras repetidas en medio (C:\fotos\\2025) fuera; el \\servidor\recurso del
        // principio se respeta.
        var prefix = text.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\" : string.Empty;
        var body = prefix.Length > 0 ? text[2..] : text;
        while (body.Contains(@"\\", StringComparison.Ordinal)) body = body.Replace(@"\\", @"\");

        // Puntos y espacios al final de cada tramo: Windows no los admite.
        var parts = body.Split('\\');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i == 0 && parts[i].EndsWith(':')) continue;   // la letra de unidad se deja igual
            parts[i] = parts[i].TrimEnd(' ', '.');
        }
        body = string.Join('\\', parts);
        text = prefix + body;

        // «D:» a secas es la carpeta actual de esa unidad, no su raíz: se deja explícito.
        if (text.Length == 2 && char.IsLetter(text[0]) && text[1] == ':') text += "\\";
        if (text.Length > 3) text = text.TrimEnd('\\');
        return text;
    }

    /// <summary>
    /// Primer carácter prohibido de la ruta (sin contar los dos puntos de la unidad), o
    /// null si no hay ninguno. Sirve para decir QUÉ sobra en vez de «ruta no válida».
    /// </summary>
    public static char? FirstForbiddenChar(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var start = path.Length > 1 && path[1] == ':' ? 2 : 0;
        for (var i = start; i < path.Length; i++)
        {
            var c = path[i];
            if (c == ':' || Array.IndexOf(Forbidden, c) >= 0) return c;
            if (c < 32) return ' ';
        }
        return null;
    }

    /// <summary>¿Esta ruta se puede usar como carpeta de destino sin que Windows proteste?</summary>
    public static bool LooksUsable(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (FirstForbiddenChar(path) is not null) return false;
        try
        {
            _ = Path.GetFullPath(path);
            return true;
        }
        catch { return false; }
    }
}

public static class Format
{
    /// <summary>«1 archivo» / «4 archivos». Se acabaron los «archivo(s)» de la interfaz.</summary>
    public static string Files(int count) => count == 1 ? "1 archivo" : $"{count} archivos";

    /// <summary>Concuerda cualquier palabra: Plural(1, "copiado") → «1 copiado».</summary>
    public static string Plural(int count, string singular, string? plural = null)
        => count == 1 ? $"{count} {singular}" : $"{count} {plural ?? singular + "s"}";

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

/// <summary>Una lista de copia independiente: sus propios archivos y su propio destino,
/// pero compartiendo la misma ventana y pestaña que el resto de listas activas.</summary>
/// <summary>Cómo se ve una lista de copia en el panel de trabajos.</summary>
public enum ListStage
{
    /// <summary>Preparada, todavía no se ha pulsado Copiar.</summary>
    Preparada = 0,
    /// <summary>Se está copiando ahora mismo.</summary>
    Activa = 1,
    /// <summary>Copia empezada y puesta en pausa.</summary>
    Pausada = 2,
    /// <summary>Esperando a que termine la copia que está en marcha.</summary>
    EnEspera = 3,
    /// <summary>Ya terminó: es una copia antigua.</summary>
    Terminada = 4,
    /// <summary>Terminó, pero con archivos que fallaron.</summary>
    ConErrores = 5
}

public sealed class TransferList
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = "Lista de copia";
    public string Destination { get; set; } = string.Empty;
    public List<TransferItem> Items { get; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>Esta lista está esperando turno: se copiará al terminar la que va delante.</summary>
    public bool Queued { get; set; }

    /// <summary>Operación con la que se lanzará cuando le llegue el turno.</summary>
    public OperationType QueuedOperation { get; set; } = OperationType.Copy;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Lista de copia" : Name;

    /// <summary>
    /// Estado de la lista para poder distinguir de un vistazo, en el panel de trabajos, las
    /// copias ACTIVAS de las que están EN PAUSA y de las ANTIGUAS, que es lo que pedía el
    /// usuario. <paramref name="running"/> indica si el motor está trabajando en esta lista.
    /// </summary>
    public ListStage Stage(bool running, bool paused)
    {
        if (running) return paused ? ListStage.Pausada : ListStage.Activa;
        if (Queued) return ListStage.EnEspera;
        if (Items.Count == 0) return ListStage.Preparada;
        if (Items.Any(item => item.State == ItemState.Failed)) return ListStage.ConErrores;
        if (Items.All(item => item.State is ItemState.Completed or ItemState.Skipped)) return ListStage.Terminada;
        if (Items.Any(item => item.State is ItemState.Completed or ItemState.Paused)) return ListStage.Pausada;
        return ListStage.Preparada;
    }

    public static string StageText(ListStage stage) => stage switch
    {
        ListStage.Activa => "Copiando ahora",
        ListStage.Pausada => "En pausa / a medias",
        ListStage.EnEspera => "Esperando turno",
        ListStage.Terminada => "Terminada",
        ListStage.ConErrores => "Terminada con errores",
        _ => "Preparada"
    };

    public static string StageGlyph(ListStage stage) => stage switch
    {
        ListStage.Activa => "\u25B6",        // ▶
        ListStage.Pausada => "\u275A\u275A", // ❚❚
        ListStage.EnEspera => "\u2026",       // …
        ListStage.Terminada => "\u2713",      // ✓
        ListStage.ConErrores => "!",
        _ => "\u25CF"                         // ●
    };

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var pending = Items.Count(item => item.State == ItemState.Pending);
            var running = Items.Count(item => item.State == ItemState.Running || item.State == ItemState.Paused);
            var done = Items.Count(item => item.State == ItemState.Completed);
            var failed = Items.Count(item => item.State == ItemState.Failed);
            return $"{done}/{Items.Count}" + (failed > 0 ? $" · {Format.Plural(failed, "error", "errores")}" : string.Empty)
                + (running > 0 ? $" · {running} en curso" : string.Empty)
                + (pending > 0 ? $" · {Format.Plural(pending, "pendiente")}" : string.Empty);
        }
    }
}
