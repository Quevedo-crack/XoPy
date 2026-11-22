; Instalador de XoPy para Windows 10/11 x64
#define AppName "XoPy"
#define AppVersion "2.3.0"
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
VersionInfoVersion=2.3.0.0
VersionInfoProductName={#AppName}
VersionInfoDescription=Instalador de XoPy
VersionInfoCompany={#AppPublisher}

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"
Name: "explorer"; Description: "Añadir XoPy al menú contextual del Explorador (Copiar, Copiar a…, Mover, Verificar, Pegar)"; GroupDescription: "Integración con Windows:"; Flags: checkedonce
Name: "sendto"; Description: "Añadir XoPy al menú ""Enviar a"""; GroupDescription: "Integración con Windows:"; Flags: checkedonce
Name: "autostart"; Description: "Iniciar XoPy al encender el equipo"; GroupDescription: "Integración con Windows:"; Flags: unchecked

[Files]
Source: "..\publish\XoPy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\XoPy"; Filename: "{app}\{#AppExe}"; Comment: "Gestor de copias XoPy"
Name: "{autodesktop}\XoPy"; Filename: "{app}\{#AppExe}"; Comment: "Gestor de copias XoPy"; Tasks: desktopicon
Name: "{sendto}\Copiar con XoPy"; Filename: "{app}\{#AppExe}"; Parameters: "--copy"; Tasks: sendto
Name: "{sendto}\Mover con XoPy"; Filename: "{app}\{#AppExe}"; Parameters: "--move"; Tasks: sendto

[Registry]
; --- Menú contextual: archivos ---
Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyCopy"; ValueType: string; ValueName: ""; ValueData: "Copiar con XoPy"; Flags: uninsdeletekey; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyCopy"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyCopy"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyCopy\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --copy ""%1"""; Tasks: explorer

Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyMove"; ValueType: string; ValueName: ""; ValueData: "Mover con XoPy"; Flags: uninsdeletekey; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyMove"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyMove"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyMove\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --move ""%1"""; Tasks: explorer

Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyCopyTo"; ValueType: string; ValueName: ""; ValueData: "Copiar a… con XoPy"; Flags: uninsdeletekey; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyCopyTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyCopyTo"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyCopyTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --copy --ask-destination ""%1"""; Tasks: explorer

Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyMoveTo"; ValueType: string; ValueName: ""; ValueData: "Mover a… con XoPy"; Flags: uninsdeletekey; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyMoveTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyMoveTo"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyMoveTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --move --ask-destination ""%1"""; Tasks: explorer

Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyVerify"; ValueType: string; ValueName: ""; ValueData: "Verificar con XoPy"; Flags: uninsdeletekey; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyVerify"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyVerify"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\*\shell\XoPyVerify\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --test ""%1"""; Tasks: explorer

; --- Menú contextual: carpetas ---
Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyCopy"; ValueType: string; ValueName: ""; ValueData: "Copiar con XoPy"; Flags: uninsdeletekey; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyCopy"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyCopy"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyCopy\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --copy ""%1"""; Tasks: explorer

Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyMove"; ValueType: string; ValueName: ""; ValueData: "Mover con XoPy"; Flags: uninsdeletekey; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyMove"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyMove"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyMove\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --move ""%1"""; Tasks: explorer

Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyCopyTo"; ValueType: string; ValueName: ""; ValueData: "Copiar a… con XoPy"; Flags: uninsdeletekey; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyCopyTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyCopyTo"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyCopyTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --copy --ask-destination ""%1"""; Tasks: explorer

Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyMoveTo"; ValueType: string; ValueName: ""; ValueData: "Mover a… con XoPy"; Flags: uninsdeletekey; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyMoveTo"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyMoveTo"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyMoveTo\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --move --ask-destination ""%1"""; Tasks: explorer

Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyVerify"; ValueType: string; ValueName: ""; ValueData: "Verificar con XoPy"; Flags: uninsdeletekey; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyVerify"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyVerify"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\shell\XoPyVerify\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --test ""%1"""; Tasks: explorer

; --- Menú contextual del fondo de carpeta: Pegar con XoPy ---
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\XoPyPaste"; ValueType: string; ValueName: ""; ValueData: "Pegar con XoPy"; Flags: uninsdeletekey; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\XoPyPaste"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\XoPyPaste\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --paste --to ""%V"""; Tasks: explorer

Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\XoPyOpen"; ValueType: string; ValueName: ""; ValueData: "Abrir XoPy aquí"; Flags: uninsdeletekey; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\XoPyOpen"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"",0"; Tasks: explorer
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\XoPyOpen\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --to ""%V"""; Tasks: explorer

; --- Registro de la aplicación ---
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\XoPy.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\XoPy.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "XoPy"; ValueData: """{app}\{#AppExe}"" --tray"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "Iniciar XoPy"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
