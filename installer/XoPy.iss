[Setup]
AppId={{A5C3DF30-ED8D-4C8D-9F3F-4FA49A1001CD}}
AppName=XoPy
AppVersion=2.4.6
AppVerName=XoPy 2.4.6
AppPublisher=Quevedo-crack
AppPublisherURL=https://github.com/Quevedo-crack/XoPy
DefaultDirName={pf}\XoPy
DefaultGroupName=XoPy
DisableProgramGroupPage=yes
OutputDir=installer-output
OutputBaseFilename=XoPy-Setup
SetupIconFile=XoPy\Assets\xopy.ico
UninstallDisplayIcon={app}\XoPy.exe
UninstallDisplayName=XoPy 2.4.6
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
VersionInfoProductName=XoPy
VersionInfoDescription=Gestor de copias XoPy
VersionInfoCompany=Quevedo-crack

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Files]
Source: "..\publish\XoPy.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\XoPy"; Filename: "{app}\XoPy.exe"; Comment: "Gestor de copias XoPy"

[Run]
Filename: "{app}\XoPy.exe"; Description: "Iniciar XoPy"; Flags: postinstall nowait skipifsilent