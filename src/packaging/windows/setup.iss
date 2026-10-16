; Inno Setup script for a published Hst Commander app, e.g.:
; iscc /DApp=tui /DVersion=0.1.29 /DArch=x64 /DSourceDir=publish /DOutputDir=dist /DOutputName=hst-commander.tui.win-x64.setup setup.iss
; App is tui or gui, Arch is x64, x86 or arm64.

#if App == "gui"
  #define AppName "Hst Commander"
  #define AppExeName "hst.commander.gui.exe"
  #define AppGuid "{{EBDEF06C-5D7E-4070-B906-9CD81B5A7156}"
#elif App == "tui"
  #define AppName "Hst Commander TUI"
  #define AppExeName "hst.commander.exe"
  #define AppGuid "{{0F6C836F-E74A-46FA-AF37-6521C23845D9}"
#else
  #error App must be tui or gui
#endif

[Setup]
AppId={#AppGuid}
AppName={#AppName}
AppVersion={#Version}
AppVerName={#AppName} v{#Version}
AppPublisher=Henrik Nørfjand Stengaard
AppPublisherURL=https://github.com/henrikstengaard/hst-commander
VersionInfoVersion={#Version}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
SetupIconFile=..\..\icon\hst-commander.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
#if Arch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#elif Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
