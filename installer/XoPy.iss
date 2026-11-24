; Instalador de XoPy para Windows 10/11 x64

[Setup]
AppName=XoPy
AppVersion=2.5.1
AppVerName=XoPy 2.5.1
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
UninstallDisplayName=XoPy 2.5.1
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
VersionInfoVersion=2.5.1.0
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

[Run]
; «--install-hooks» fija en el registro de XoPy el copiador integrado y el arranque con
; Windows según lo elegido en el instalador, para que quede configurado desde el primer día.
Filename: "{app}\XoPy.exe"; Parameters: "--install-hooks"; Flags: postinstall nowait skipifsilent; Tasks: replacecopier
Filename: "{app}\XoPy.exe"; Description: "Iniciar XoPy"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"