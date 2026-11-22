[Setup]
AppName=XoPy
AppVersion=2.4.6
DefaultDirName={pf}\XoPy

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"
Name: "autostart"; Description: "Iniciar XoPy al encender el equipo"; GroupDescription: "Integración con Windows:"; Flags: unchecked

[InstallDelete]
Type: files; Name: "{sendto}\*XoPy*.lnk"

[Icons]
Name: "{autoprograms}\XoPy"; Filename: "{app}\XoPy.exe"; Comment: "Gestor de copias XoPy"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\XoPy.exe"; ValueType: string; ValueData: "{app}\XoPy.exe"; Flags: uninsdeletekey

[Files]
Source: "..\publish\XoPy.exe"; DestDir: "{app}"; Flags: ignoreversion