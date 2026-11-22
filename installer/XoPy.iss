; Instalador de XoPy para Windows 10/11 x64
#define AppName "XoPy"
#define AppVersion "2.4.6"
#define AppPublisher "Quevedo-crack"
#define AppExe "XoPy.exe"

[Setup]
AppId={{A5C3DF30-ED8D-4C8D-9F3F-4FA49A1001CD}}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/Quevedo-crack/XoPy
AppSupportURL=https://github.com/Quevedo-crack/XoPy/issues
AppUpdatesURL=https://github.com/Quevedo-crack/XoPy/releases
DefaultDirName={autopf}\XoPy
DefaultGroupName=XoPy
DisableProgramGroupPage=yes
OutputDir=..\installer-output
OutputBaseFilename=XoPy-Setup-win-x64
SetupIconFile=..\XoPy\Assets\xopy.ico
UninstallDisplayIcon={app}\XoPy.exe
UninstallDisplayName={#AppName} {#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0
CloseApplications=no
RestartApplications=no
AllowNoIcons=yes
VersionInfoVersion=2.4.6.0
VersionInfoProductName={#AppName}
VersionInfoDescription=Gestor de copias XoPy
VersionInfoCompany={#AppPublisher}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"
Name: "autostart"; Description: "Iniciar XoPy al encender el equipo"; GroupDescription: "Integración con Windows:"; Flags: unchecked

[Files]
Source: "..\publish\XoPy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{sendto}\*XoPy*.lnk"

[Icons]
Name: "{autoprograms}\XoPy"; Filename: "{app}\{#AppExe}"; Comment: "Gestor de copias XoPy"
Name: "{autodesktop}\XoPy"; Filename: "{app}\{#AppExe}"; Comment: "Gestor de copias XoPy"; Tasks: desktopicon

[Registry]
; Ruta de la aplicación
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\XoPy.exe"; ValueType: string; ValueData: "{app}\{#AppExe}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\XoPy.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey
; Inicio automático opcional
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "XoPy"; ValueData: """"{app}\{#AppExe}""""; Flags: uninsdeletevalue; Tasks: autostart

[Code]
{ Borra un directorio y todo su contenido recursivamente. }
procedure DeleteDirectoryTree(const Dir: String);
var
  FindRec: TFindRec;
  Name: String;
begin
  if not DirExists(Dir) then Exit;
  if FindFirst(Dir + '\*', FindRec) then
  begin
    repeat
      if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
      begin
        if (FindRec.Attributes and 16) <> 0 then
          DeleteDirectoryTree(Dir + '\' + FindRec.Name)
        else
          DeleteFile(Dir + '\' + FindRec.Name);
      end;
    until not FindNext(FindRec);
    FindClose(FindRec);
  end;
  RemoveDirectory(Dir);
end;

{ Borra una clave de registro y todas sus subclaves recursivamente. }
function RegDeleteKeyIncludingSubkeys(const RootKey: HKEY; const SubKeyName: String): Boolean;
var
  i: Integer;
  SubKeyNames: TArrayOfString;
begin
  Result := False;
  if not RegQuerySubKeyNames(RootKey, SubKeyName, SubKeyNames) then
  begin
    Result := RegDeleteKey(RootKey, SubKeyName);
    Exit;
  end;
  for i := 0 to GetArrayLength(SubKeyNames) - 1 do
    RegDeleteKeyIncludingSubkeys(RootKey, SubKeyName + '\' + SubKeyNames[i]);
  Result := RegDeleteKey(RootKey, SubKeyName);
end;

{ Limpia toda la integración que XoPy creó: registros, menús, Enviar a, etc. }
procedure CleanupXoPyIntegration();
begin
  { Inicio automático }
  RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'XoPy');
  { Rutas de la aplicación }
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\App Paths\XoPy.exe');
  { Claves antiguas del menú contextual y CLSID }
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\CLSID\{3B1E92A7-7AD6-4E71-A218-75449CE13061}');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\Directory\Background\shellex\ContextMenuHandlers\XoPyPaste');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\Directory\shellex\ContextMenuHandlers\XoPyPaste');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\*\shellex\ContextMenuHandlers\XoPyPaste');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\*\shell\XoPyCopy');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\*\shell\XoPyCopyTo');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\*\shell\XoPyMove');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\*\shell\XoPyMoveTo');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\*\shell\XoPyVerify');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\Directory\shell\XoPyCopy');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\Directory\shell\XoPyCopyTo');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\Directory\shell\XoPyMove');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\Directory\shell\XoPyMoveTo');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\Directory\shell\XoPyVerify');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\Directory\Background\shell\XoPyPaste');
  RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, 'Software\Classes\Directory\Background\shell\XoPyOpen');
  { Acceso de Enviar a }
  DeleteFile(ExpandConstant('{sendto}\XoPy.lnk'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Keys: array of String;
  I: Integer;
begin
  if CurStep = ssInstall then
  begin
    { Retira entradas de menú contextual creadas por versiones 2.4.0 y anteriores. }
    SetArrayLength(Keys, 12);
    Keys[0] := 'Software\Classes\*\shell\XoPyCopy';
    Keys[1] := 'Software\Classes\*\shell\XoPyCopyTo';
    Keys[2] := 'Software\Classes\*\shell\XoPyMove';
    Keys[3] := 'Software\Classes\*\shell\XoPyMoveTo';
    Keys[4] := 'Software\Classes\*\shell\XoPyVerify';
    Keys[5] := 'Software\Classes\Directory\shell\XoPyCopy';
    Keys[6] := 'Software\Classes\Directory\shell\XoPyCopyTo';
    Keys[7] := 'Software\Classes\Directory\shell\XoPyMove';
    Keys[8] := 'Software\Classes\Directory\shell\XoPyMoveTo';
    Keys[9] := 'Software\Classes\Directory\shell\XoPyVerify';
    Keys[10] := 'Software\Classes\Directory\Background\shell\XoPyPaste';
    Keys[11] := 'Software\Classes\Directory\Background\shell\XoPyOpen';
    for I := 0 to GetArrayLength(Keys) - 1 do
      RegDeleteKeyIncludingSubkeys(HKEY_CURRENT_USER, Keys[I]);
  end;
end;

function InitializeUninstall: Boolean;
begin
  { Siempre: quitar toda la integración que XoPy creó (menús, Enviar a, inicio automático, rutas de la aplicación). }
  CleanupXoPyIntegration();

  { Elección entre desinstalación total o conservar el historial. }
  if MsgBox('¿Desea realizar una desinstalación total de XoPy?' + #13#10 +
            'Sí: elimina también el historial, los registros y los datos temporales (no se podrán recuperar).' + #13#10 +
            'No: se conserva el historial; solo se elimina la aplicación y sus entradas de menú.',
            mbYesNo, mbIconQuestion) = IDYES then
  begin
    { Desinstalación total: borrar %LOCALAPPDATA%\XoPy (historial, registros, temporales, ajustes). }
    DeleteDirectoryTree(ExpandConstant('{localappdata}\XoPy'));
  end;

  Result := True;
end;

[Run]
Filename: "{app}\{#AppExe}"; Description: "Iniciar XoPy"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"