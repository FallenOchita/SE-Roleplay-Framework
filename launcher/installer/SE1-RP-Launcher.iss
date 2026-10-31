#define MyAppName "SE1 Roleplay Launcher"
#define MyAppVersion "0.2.0"
#define MyAppPublisher "SE1 Roleplay Framework"
#define MyAppExeName "SE.Roleplay.Framework.Launcher.exe"
[Setup]
AppId={{7B6B41B8-3B6B-4D6C-9F7B-5F8D0C8B41E2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\SE1 Roleplay Launcher
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=SE1-RP-Launcher-Setup-{#MyAppVersion}-x64
ArchitecturesInstallIn64BitMode=x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: postinstall nowait skipifsilent
