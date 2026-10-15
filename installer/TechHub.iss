#define MyAppName "TechHub"
#ifndef MyAppVersion
#define MyAppVersion "1.3.0"
#endif
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
AppMutex=Local\TechHub.App.Running,Global\TechHub.App.Machine.Running
CloseApplications=no
RestartApplications=no

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "portable.flag,Data\*,*.pdb"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\TechHub"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\TechHub"; Filename: "{app}\{#MyAppExeName}"

[InstallDelete]
; A instalação usa LocalAppData. Não remover a pasta Data de uma versão antiga.
Type: files; Name: "{app}\portable.flag"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir TechHub"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveStringToFile(ExpandConstant('{app}\install-mode.txt'), 'Machine', False);
end;
