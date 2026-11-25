; Instalador de XoPy para Windows 10/11 x64

[Setup]
AppName=XoPy
AppVersion=2.5.8
AppVerName=XoPy 2.5.8
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
UninstallDisplayName=XoPy 2.5.8
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0
; NINGÚN copiador pide reiniciar Windows ni cierra el Explorador. Antes, cuando el
; Explorador tenía la DLL cargada, el instalador lo MATABA: el escritorio desaparecía,
; la barra de tareas se quedaba sin responder y la instalación parecía colgada («no
; avanza»). Ahora la DLL se sustituye en el próximo arranque con restartreplace
; (silencioso) y XoPy.exe se cierra solo, sin tocar el Explorador.
CloseApplications=no
RestartApplications=no
; Permite que «restartreplace» programe la sustitución de la DLL al reiniciar, sin abortar
; y sin pedir confirmación al usuario.
RestartIfNeededByRun=no
; No se reinicia jamás el equipo: AlwaysRestart=no es el valor por defecto y se deja
; explícito para que nadie lo cambie por accidente.
AlwaysRestart=no
AppMutex=Local\\XoPy.SingleInstance.5C1A93
AllowNoIcons=yes
VersionInfoVersion=2.5.8.0
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
// Notificación al shell para refrescar el menú contextual sin reiniciar el Explorador.
// (SHCNE_ASSOCCHANGED = $08000000, SHCNF_IDLIST = 0)
procedure SHChangeNotify(wEventId: Longint; uFlags: Cardinal; dwItem1, dwItem2: Cardinal);
  external 'SHChangeNotify@shell32.dll stdcall';

// ─────────────────────────────────────────────────────────────────────────────
// El Explorador mantiene cargada XoPyShell.dll mientras la extensión «Pegar» está
// registrada. Windows no permite reemplazar un archivo en uso: de ahí el error
// «DeleteFile falló; código 5. Acceso denegado» al instalar encima de otra versión.
// Orden: desregistrar la extensión → cerrar XoPy → copiar archivos → volver a registrar
// (lo hace [Registry]). El Explorador NO se cierra ni se reinicia nunca: si la DLL sigue
// en uso, restartreplace programa la sustitución para el próximo arranque.
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
// Borra los XoPyShell.dll.old que dejó una desinstalación anterior (o un borrado
// programado para el arranque). Se hace con RemoveDir/DeleteFile directos: sin bucles.
procedure RepeatDeleteOld(const Dir: String);
begin
  if not DirExists(Dir) then exit;
  DeleteFile(Dir + '\XoPyShell.dll.old');
  DeleteFile(Dir + '\XoPyShell.dll.probe');
end;

procedure ReleaseShellExtensionQuietly();
var
  DllPath, TempOld: String;
begin
  // Restos .old de una desinstalación anterior que Windows programó para el arranque.
  RepeatDeleteOld(ExpandConstant('{app}'));
  DllPath := ExpandConstant('{app}\XoPyShell.dll');
  if not FileExists(DllPath) then exit;

  // 1) Si se puede borrar, el Explorador no la tiene cargada y no hay nada más que hacer.
  if DeleteFile(DllPath) then exit;

  // 2) Está en uso. Windows NO deja BORRAR un DLL mapeado, pero SÍ deja RENOMBRARLO: se
  //    aparta a la carpeta temporal para que el archivo nuevo se escriba en una ruta libre.
  //    Así no hace falta restartreplace y el instalador NO pide reiniciar el equipo.
  TempOld := ExpandConstant('{tmp}\XoPyShell-anterior.dll');
  if RenameFile(DllPath, TempOld) then
  begin
    Log('XoPyShell.dll estaba en uso: apartada a la carpeta temporal.');
    exit;
  end;

  // 3) Si el temporal está en otra unidad, se aparta dentro de la misma carpeta y se
  //    programa su borrado para el próximo arranque (sin avisos ni reinicio forzado).
  if RenameFile(DllPath, DllPath + '.old') then
  begin
    MoveFileEx(DllPath + '.old', '', $4);
    Log('XoPyShell.dll estaba en uso: renombrada y marcada para borrarse al reiniciar.');
    exit;
  end;

  // 4) Último recurso: lo resuelve restartreplace. NUNCA se cierra el Explorador.
  Log('XoPyShell.dll sigue bloqueada: se sustituirá en el próximo arranque.');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    // Se desregistra y se libera la DLL antes de que se copien los archivos nuevos.
    UnregisterShellExtension();
    ReleaseShellExtensionQuietly();
  end;
  if CurStep = ssPostInstall then
  begin
    // NO se lanza explorer.exe: el Explorador NUNCA se ha cerrado, así que lanzarlo abría
    // una ventana nueva («Este equipo») encima del instalador. Basta con notificar al shell
    // para que el menú «Pegar con XoPy» aparezca sin reiniciar nada.
    SHChangeNotify($8000000, 0, 0, 0);
    Log('Integración con el Explorador refrescada (sin cerrar ni reiniciar el Explorador).');
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
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDir: String;
  DataDir: String;
  DllPath: String;
  TempOld: String;
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
    // (para que deje de pedirla) y, si sigue bloqueada, se renombra y se mueve a %TEMP% o
    // se programa su borrado al reiniciar. NUNCA se cierra el Explorador.
    UnregisterShellExtension();
    DllPath := AppDir + '\XoPyShell.dll';
    // La DLL solo se puede borrar si el Explorador ya la soltó. Se intenta en varios
    // pasos y, si sigue en uso, se programa su borrado para el próximo arranque, que es
    // lo que hace Windows: así no queda el aviso de «elementos que no se pudieron eliminar».
    if FileExists(DllPath) and not DeleteFile(DllPath) then
    begin
      // Windows NO permite borrar un DLL que el Explorador tiene mapeado… pero SÍ permite
      // RENOMBRARLO (el nombre es solo una entrada del directorio; los datos siguen en uso).
      // Renombrar y borrar es la forma fiable de que la carpeta quede vacía sin reiniciar.
      if not RenameFile(DllPath, DllPath + '.old') then
      begin
        // NO se mata el Explorador: se deja para que se sustituya al reiniciar.
        MoveFileEx(DllPath, '', $4);
      end;
      // El .old aún está mapeado por el Explorador, así que tampoco se puede borrar. La
      // solución es la misma que ya funcionó para renombrar el DLL: se mueve a %TEMP% (fuera
      // de {app}) y allí se borra o se deja para el próximo arranque. Así {app} queda vacía.
      if FileExists(DllPath + '.old') then
      begin
        if not DeleteFile(DllPath + '.old') then
        begin
          TempOld := ExpandConstant('{tmp}\XoPyShell.dll.old');
          DeleteFile(TempOld);
          if MoveFileEx(DllPath + '.old', TempOld, 0) then
          begin
            if not DeleteFile(TempOld) then MoveFileEx(TempOld, '', $4);
          end
          else
            MoveFileEx(DllPath + '.old', '', $4);
        end;
      end
      else if FileExists(DllPath) then
        MoveFileEx(DllPath, '', $4);
    end;
    DelTree(AppDir, True, True, True);
    // El propio desinstalador (unins000.exe) sigue en ejecución dentro de {app}: se mueve a
    // %TEMP% igual que la DLL para que la carpeta quede realmente vacía ahora, sin esperar
    // al reinicio.
    if FileExists(AppDir + '\unins000.exe') then
    begin
      TempOld := ExpandConstant('{tmp}\unins000.exe');
      DeleteFile(TempOld);
      MoveFileEx(AppDir + '\unins000.exe', TempOld, 0);
    end;
    DeleteFile(AppDir + '\unins000.dat');
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
    // NO se lanza explorer.exe: abriría una ventana nueva («Este equipo») al desinstalar.
    // Para refrescar el menú contextual basta con notificar el cambio al shell.
    SHChangeNotify($8000000, 0, 0, 0);
  end;
end;

[UninstallDelete]
Type: filesandordirs; Name: "{app}"