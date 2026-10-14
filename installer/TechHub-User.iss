#define MyAppName "TechHub"
#define MyAppVersion "1.2.1"
#define MyAppPublisher "TechHub"
#define MyAppExeName "TechHub.App.exe"

[Setup]
AppId={{B6E1C2B4-77CB-4B9D-BE20-5E4C6E0F6C11}
AppName={#MyAppName} (Usuário)
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\TechHub
DefaultGroupName=TechHub
OutputDir={#OutputDir}
OutputBaseFilename=TechHub-User-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
Uninstallable=yes
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "portable.flag,Data\*,*.pdb"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{userprograms}\TechHub (Usuário)"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\TechHub (Usuário)"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir TechHub"; Flags: nowait postinstall skipifsilent
