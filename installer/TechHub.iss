#define MyAppName "TechHub"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "TechHub"
#define MyAppExeName "TechHub.App.exe"

[Setup]
AppId={{B6E1C2B4-77CB-4B9D-BE20-5E4C6E0F6C11}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\TechHub
DefaultGroupName=TechHub
OutputDir={#OutputDir}
OutputBaseFilename=TechHub-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\TechHub"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\TechHub"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir TechHub"; Flags: nowait postinstall skipifsilent
