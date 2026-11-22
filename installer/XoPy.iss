; Instalador de XoPy para Windows 10/11 x64
#define AppName "XoPy"
#define AppVersion "2.4.1"
#define AppPublisher "Quevedo-crack"
#define AppExe "XoPy.exe"

[Setup]
AppId={{A5C3DF30-ED8D-4C8D-9F3F-4FA49A1001CD}
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
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes
VersionInfoVersion=2.4.1.0
VersionInfoProductName={#AppName}
VersionInfoDescription=Instalador de XoPy
VersionInfoCompany={#AppPublisher}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"
Name: "autostart"; Description: "Iniciar XoPy al encender el equipo"; GroupDescription: "Integración con Windows:"; Flags: unchecked

[Files]
Source: "..\publish\XoPy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\XoPyShell.dll"; DestDir: "{app}"; Flags: ignoreversion restartreplace
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{sendto}\*XoPy*.lnk"

[Icons]
Name: "{autoprograms}\XoPy"; Filename: "{app}\{#AppExe}"; Comment: "Gestor de copias XoPy"
Name: "{autodesktop}\XoPy"; Filename: "{app}\{#AppExe}"; Comment: "Gestor de copias XoPy"; Tasks: desktopicon

[Registry]
; Solo se registra el inicio automático opcional. XoPy no crea verbos de menú contextual.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\XoPy.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\XoPy.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "XoPy"; ValueData: """{app}\{#AppExe}"" --tray"; Flags: uninsdeletevalue; Tasks: autostart
Root: HKCU; Subkey: "Software\Classes\CLSID\{{3B1E92A7-7AD6-4E71-A218-75449CE13061}}\InprocServer32"; ValueType: string; ValueName: ""; ValueData: "{app}\XoPyShell.dll"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\CLSID\{{3B1E92A7-7AD6-4E71-A218-75449CE13061}}\InprocServer32"; ValueType: string; ValueName: "ThreadingModel"; ValueData: "Apartment"
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shellex\ContextMenuHandlers\XoPyPaste"; ValueType: string; ValueName: ""; ValueData: "{{3B1E92A7-7AD6-4E71-A218-75449CE13061}}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shellex\ContextMenuHandlers\XoPyPaste"; ValueType: string; ValueName: ""; ValueData: "{{3B1E92A7-7AD6-4E71-A218-75449CE13061}}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\*\shellex\ContextMenuHandlers\XoPyPaste"; ValueType: string; ValueName: ""; ValueData: "{{3B1E92A7-7AD6-4E71-A218-75449CE13061}}"; Flags: uninsdeletekey

[Code]
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

[Run]
Filename: "{app}\{#AppExe}"; Description: "Iniciar XoPy"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
