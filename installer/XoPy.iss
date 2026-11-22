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
DefaultDirName={autopf}\\XoPy
DefaultGroupName=XoPy
DisableProgramGroupPage=yes
OutputDir=..\\installer-output
OutputBaseFilename=XoPy-Setup-win-x64
SetupIconFile=..\\XoPy\\Assets\\xopy.ico
UninstallDisplayIcon={app}\\XoPy.exe
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
VersionInfoVersion=2.4.1.0
VersionInfoProductName={#AppName}
VersionInfoDescription=Instalador de XoPy
VersionInfoCompany={#AppPublisher}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"
Name: "autostart"; Description: "Iniciar XoPy al encender el equipo"; GroupDescription: "Integración con Windows:"; Flags: unchecked

[Files]
Source: "..\\publish\\XoPy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\\README.md"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{sendto}\\*XoPy*.lnk"

[Icons]
Name: "{autoprograms}\\XoPy"; Filename: "{app}\\{#AppExe}"; Comment: "Gestor de copias XoPy"
Name: "{autodesktop}\\XoPy"; Filename: "{app}\\{#AppExe}"; Comment: "Gestor de copias XoPy"; Tasks: desktopicon

[Registry]
; Solo se registra el inicio automático opcional y la ruta de la aplicación.
; XoPy no crea verbos de menú contextual ni entradas «Pegar».
Root: HKCU; Subkey: "Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\XoPy.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\\{#AppExe}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\XoPy.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\\Microsoft\\Windows\\CurrentVersion\\Run"; ValueType: string; ValueName: "XoPy"; ValueData: """{app}\\{#AppExe}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\\{#AppExe}"; Description: "Iniciar XoPy"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"