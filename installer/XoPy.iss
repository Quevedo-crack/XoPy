#define AppName "XoPy"
#define AppVersion "1.0.0"

[Setup]
AppId={{A5C3DF30-ED8D-4C8D-9F3F-4FA49A1001CD}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Quevedo-crack
DefaultDirName={localappdata}\Programs\XoPy
DefaultGroupName=XoPy
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
OutputDir=..\installer-output
OutputBaseFilename=XoPy-Setup-win-x64
Compression=lzma
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"

[Files]
Source: "..\publish\XoPy.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\XoPy"; Filename: "{app}\XoPy.exe"
Name: "{autodesktop}\XoPy"; Filename: "{app}\XoPy.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\XoPy.exe"; Description: "Iniciar XoPy"; Flags: postinstall nowait skipifsilent
