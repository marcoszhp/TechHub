; Isolated installer fixture: no shortcuts, uninstaller, registry, production IDs, or real user data.
[Setup]
AppId=TechHub-Isolated-Updater-Verification
AppName=TechHub updater test fixture
AppVersion={#FixtureVersion}
DefaultDirName={#FixtureDirectory}
UsePreviousAppDir=no
DisableDirPage=yes
PrivilegesRequired=lowest
Uninstallable=no
CreateUninstallRegKey=no
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
CloseApplications=no
RestartApplications=no
[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "Data\*,portable.flag,*.pdb"; Flags: recursesubdirs ignoreversion
