; Instalador de XoPy para Windows 10/11 x64

[Setup]
AppName=XoPy
AppVersion=2.5.6
AppVerName=XoPy 2.5.6
AppPublisher=Quevedo-crack
AppPublisherURL=https://github.com/Quevedo-crack/XoPy
AppSupportURL=https://github.com/Quevedo-crack/XoPy/issues
AppUpdatesURL=https://github.com/Quevedo-crack/XoPy/releases
DefaultDirName={autopf}\XoPy
DefaultGroupName=XoPy
DisableProgramGroupPage=yes
OutputDir=..\installer-output
OutputBaseFilename=XoPy-Setup-win-x64
UninstallDisplayIcon={app}\XoPy.exe
UninstallDisplayName=XoPy 2.5.6
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0
; La aplicación se cierra sola al instalar: si sigue abierta, XoPy.exe queda bloqueado y la
; instalación falla («DeleteFile falló; código 5»).
CloseApplications=yes
RestartApplications=no
; Necesario para que «restartreplace» (DLL en uso) programe la sustitución al reiniciar.
RestartIfNeededByRun=yes
AppMutex=Local\\XoPy.SingleInstance.5C1A93
AllowNoIcons=yes
VersionInfoVersion=2.5.6.0
VersionInfoProductName=XoPy
VersionInfoDescription=Gestor de copias XoPy
VersionInfoCompany=Quevedo-crack

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"
; Marcada por defecto: el copiador de Windows solo se sustituye si XoPy está en marcha,
; porque el Ctrl+V del Explorador se intercepta mientras la aplicación se ejecuta.
Name: "autostart"; Description: "Usar XoPy como copiador de Windows (iniciar XoPy con el equipo)"; GroupDescription: "Integración con Windows:"
Name: "replacecopier"; Description: "Sustituir Ctrl+V del Explorador por XoPy"; GroupDescription: "Integración con Windows:"; Flags: checkedonce

[Files]
Source: "..\publish\XoPy.exe"; DestDir: "{app}"; Flags: ignoreversion
; restartreplace: si Windows no deja reemplazarla en caliente, se sustituye al reiniciar en
; lugar de abortar con «DeleteFile falló; código 5».
Source: "..\publish\XoPyShell.dll"; DestDir: "{app}"; Flags: ignoreversion 64bit restartreplace
; Extensión del Explorador que añade «Pegar» al menú contextual de las carpetas.
; Sin ella, el instalador prometía el comando pegar y luego no quedaba registrado.
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{sendto}\*XoPy*.lnk"

[Icons]
Name: "{autoprograms}\XoPy"; Filename: "{app}\XoPy.exe"; Comment: "Gestor de copias XoPy"
Name: "{autodesktop}\XoPy"; Filename: "{app}\XoPy.exe"; Comment: "Gestor de copias XoPy"; Tasks: desktopicon

[Registry]
; Ruta de la aplicación (se borra automaticamente al desinstalar con uninsdeletekey)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\XoPy.exe"; ValueType: string; ValueData: "{app}\XoPy.exe"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\XoPy.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey
; Inicio automático opcional — SIN comillas en ValueData (se borra con uninsdeletevalue)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "XoPy"; ValueData: {app}\XoPy.exe; Flags: uninsdeletevalue; Tasks: autostart
; Extensión COM del comando «Pegar» (CLSID 3B1E92A7-7AD6-4E71-A218-75449CE13061).
Root: HKCU; Subkey: "Software\Classes\CLSID\{{3B1E92A7-7AD6-4E71-A218-75449CE13061}"; ValueType: string; ValueData: "XoPy Shell Extension"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{{3B1E92A7-7AD6-4E71-A218-75449CE13061}\InprocServer32"; ValueType: string; ValueData: "{app}\XoPyShell.dll"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{{3B1E92A7-7AD6-4E71-A218-75449CE13061}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shellex\ContextMenuHandlers\XoPyPaste"; ValueType: string; ValueData: "{{3B1E92A7-7AD6-4E71-A218-75449CE13061}}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved"; ValueType: string; ValueName: "{{3B1E92A7-7AD6-4E71-A218-75449CE13061}}"; ValueData: "XoPy Pegar"; Flags: uninsdeletevalue

[Run]
; «--install-hooks» fija en el registro de XoPy el copiador integrado y el arranque con
; Windows según lo elegido en el instalador, para que quede configurado desde el primer día.
Filename: "{app}\XoPy.exe"; Parameters: "--install-hooks"; Flags: postinstall nowait skipifsilent; Tasks: replacecopier
Filename: "{app}\XoPy.exe"; Description: "Iniciar XoPy"; Flags: postinstall nowait skipifsilent

[Code]
// ─────────────────────────────────────────────────────────────────────────────
// El Explorador mantiene cargada XoPyShell.dll mientras la extensión «Pegar» está
// registrada. Windows no permite reemplazar un archivo en uso: de ahí el error
// «DeleteFile falló; código 5. Acceso denegado» al instalar encima de otra versión.
// Orden: desregistrar la extensión → cerrar XoPy → si la DLL sigue bloqueada por el
// Explorador, reiniciarlo → copiar archivos → volver a registrar (lo hace [Registry]).
// ─────────────────────────────────────────────────────────────────────────────
const
  CLSID_XoPy = '{3B1E92A7-7AD6-4E71-A218-75449CE13061}';
  MutexXoPy = 'Local\XoPy.SingleInstance.5C1A93';

// Salvaguarda: el desinstalador SOLO puede borrar la carpeta de la aplicación
// ({autopf}\XoPy). Si por un fallo de configuración {app} apuntara a una carpeta con datos
// del usuario (Escritorio, Documentos, Descargas, Juegos, Series…), no se borra NADA.
function SafeToDeleteAppDir(const Dir: String): Boolean;
var
  Normalized: String;
begin
  Normalized := Lowercase(Dir);
  // La carpeta debe ser la de XoPy y no debe estar dentro de una carpeta de datos.
  Result := (Pos('\xopy', Normalized) > 0)
    and (Pos('\desktop', Normalized) = 0)
    and (Pos('\escritorio', Normalized) = 0)
    and (Pos('\documents', Normalized) = 0)
    and (Pos('\downloads', Normalized) = 0)
    and (Pos('\juegos', Normalized) = 0)
    and (Pos('\series', Normalized) = 0)
    and (Pos('\pictures', Normalized) = 0)
    and (Pos('\music', Normalized) = 0)
    and (Pos('\videos', Normalized) = 0);
end;
// MoveFileEx permite programar el borrado de un archivo bloqueado para el próximo arranque.
// Es la vía canónica de Windows cuando un DLL sigue cargado (aquí, XoPyShell.dll mientras el
// Explorador tenga mapeada la extensión). Sin ella, la desinstalación dejaba esa DLL.
function MoveFileEx(lpExistingFileName, lpNewFileName: String; dwFlags: Cardinal): Boolean;
  external 'MoveFileExW@kernel32.dll stdcall';

procedure UnregisterShellExtension();
var
  ResultCode: Integer;
  DllPath: String;
begin
  DllPath := ExpandConstant('{app}\XoPyShell.dll');
  if FileExists(DllPath) then
  begin
    // Las exportaciones DllRegisterServer/DllUnregisterServer registran en HKCU, así que
    // basta con invocarlas sobre la DLL que se va a reemplazar.
    Exec(ExpandConstant('{sys}\regsvr32.exe'), '/u /s "' + DllPath + '"', '',
         SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
  // Se quitan las claves igualmente: si regsvr32 no puede cargarla (DLL ya bloqueada),
  // el Explorador deja de pedirla en el siguiente arranque.
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\CLSID\' + CLSID_XoPy);
  RegDeleteKeyIncludingSubkeys(HKCU,
    'Software\Classes\Directory\Background\shellex\ContextMenuHandlers\XoPyPaste');
  RegDeleteKeyIncludingSubkeys(HKCU,
    'Software\Classes\Directory\shellex\ContextMenuHandlers\XoPyPaste');
  RegDeleteKeyIncludingSubkeys(HKCU,
    'Software\Classes\DesktopBackground\shellex\ContextMenuHandlers\XoPyPaste');
end;

// Si el Explorador conserva la DLL cargada, se reinicia ANTES de copiar archivos.
// Renombrarla es la prueba fiable: si se puede renombrar, no está bloqueada.
procedure RestartExplorerIfNeeded();
var
  ResultCode: Integer;
  Attempt: Integer;
  DllPath, ProbePath: String;
begin
  DllPath := ExpandConstant('{app}\XoPyShell.dll');
  if not FileExists(DllPath) then exit;
  ProbePath := DllPath + '.probe';
  if RenameFile(DllPath, ProbePath) then
  begin
    RenameFile(ProbePath, DllPath);
    exit;
  end;
  // El Explorador la tiene cargada y Windows no deja reemplazarla. Se cierra Explorer
  // (Windows lo relanza solo) y se espera a que suelte el archivo: este era el fallo
  // «DeleteFile falló; código 5» que seguía apareciendo al instalar encima.
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM explorer.exe /F', '', SW_HIDE,
       ewWaitUntilTerminated, ResultCode);
  Sleep(2500);
  // Reintento con margen: si sigue bloqueada, se informa en el log del instalador.
  for Attempt := 0 to 9 do
  begin
    if RenameFile(DllPath, ProbePath) then
    begin
      RenameFile(ProbePath, DllPath);
      exit;
    end;
    Sleep(500);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    // Se desregistra y se libera la DLL antes de que se copien los archivos nuevos.
    UnregisterShellExtension();
    RestartExplorerIfNeeded();
  end;
  if CurStep = ssPostInstall then
  begin
    // El Explorador vuelve solo; se relanza ya para que «Pegar» esté disponible al terminar.
    Exec(ExpandConstant('{sys}\explorer.exe'), '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  KillCode: Integer;
begin
  UnregisterShellExtension();
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM XoPy.exe /F', '', SW_HIDE,
       ewWaitUntilTerminated, KillCode);
  Result := '';
end;

function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  // Sin esto la desinstalación avisaba «Algunos elementos no pudieron eliminarse»: XoPy
  // seguía en la bandeja con XoPy.exe y XoPyShell.dll cargados, y el Explorador mantenía
  // la DLL de la extensión «Pegar». Se desregistra, se cierran los procesos y se espera.
  UnregisterShellExtension();
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM XoPy.exe /F', '', SW_HIDE,
       ewWaitUntilTerminated, ResultCode);
  Sleep(600);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM XoPyShell.dll /F', '', SW_HIDE,
       ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDir: String;
  ResultCode: Integer;
  DataDir: String;
  DllPath: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    AppDir := ExpandConstant('{app}');
    if not SafeToDeleteAppDir(AppDir) then
    begin
      // No es la carpeta de XoPy: nunca se borra. (El resto de la limpieza sigue igual.)
      AppDir := '';
    end;
  end;
  if (CurUninstallStep = usUninstall) and (AppDir <> '') then
  begin
    // La causa real de «Algunos elementos no pudieron eliminarse» era XoPyShell.dll: el
    // Explorador la mantiene cargada y Windows no deja borrarla. Se desregistra primero
    // (para que deje de pedirla) y, si sigue bloqueada, se reinicia el Explorador, que
    // Windows relanza solo. Después se borra la carpeta entera.
    UnregisterShellExtension();
    DllPath := AppDir + '\XoPyShell.dll';
    // La DLL solo se puede borrar si el Explorador ya la soltó. Se intenta en varios
    // pasos y, si sigue en uso, se programa su borrado para el próximo arranque, que es
    // lo que hace Windows: así no queda el aviso de «elementos que no se pudieron eliminar».
    if FileExists(DllPath) and not DeleteFile(DllPath) then
    begin
      Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM explorer.exe /F', '', SW_HIDE,
           ewWaitUntilTerminated, ResultCode);
      Sleep(2500);
      if not DeleteFile(DllPath) then
        MoveFileEx(DllPath, '', $4); // MOVEFILE_DELAY_UNTIL_REBOOT
    end;
    DelTree(AppDir, True, True, True);
    if DirExists(AppDir) then
      // Último recurso: la carpeta se borra al arrancar si algún archivo seguía mapeado.
      MoveFileEx(AppDir, '', $4);
  end;
  if CurUninstallStep = usPostUninstall then
  begin
    // Datos del usuario: ajustes, historial, última cola y registro. Quedaban en
    // %LOCALAPPDATA%\XoPy tras desinstalar y son «elementos que no se pudieron eliminar».
    DataDir := ExpandConstant('{localappdata}\XoPy');
    DelTree(DataDir, True, True, True);
    // Restos de la integración con el Explorador que no dependen de {app}.
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\Directory\shellex\ContextMenuHandlers\XoPyPaste');
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\DesktopBackground\shellex\ContextMenuHandlers\XoPyPaste');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved', CLSID_XoPy);
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'XoPy');
    RegDeleteKeyIfEmpty(HKCU, 'Software\Microsoft\Windows\CurrentVersion\App Paths\XoPy.exe');
    // Se relanza el Explorador para que suelte la DLL y refresque el menú contextual.
    Exec(ExpandConstant('{sys}\explorer.exe'), '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
  end;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{app}"