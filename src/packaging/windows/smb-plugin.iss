; Inno Setup script for the SMB plugin, installed for the current user in ~/.hst-commander/plugins, where both the TUI
; and the GUI load it, e.g.:
; iscc /DVersion=1.0.190 /DSourceDir=plugin /DOutputDir=dist /DOutputName=hst-commander.smb-plugin.setup smb-plugin.iss
; SourceDir holds the Hst.Commander.Plugins.Smb directory and SMBLibrary-source-<version>.zip, like the plugin's zip.

[Setup]
AppId={{69CD2657-D1EA-41FF-9CF6-2CD31118B246}
AppName=Hst Commander SMB plugin
AppVersion={#Version}
AppVerName=Hst Commander SMB plugin v{#Version}
AppPublisher=Henrik Nørfjand Stengaard
AppPublisherURL=https://github.com/henrikstengaard/hst-commander
VersionInfoVersion={#Version}
; the plugin directory in the user's settings directory, so no administrator rights are needed
PrivilegesRequired=lowest
DefaultDirName={%USERPROFILE}\.hst-commander\plugins\Hst.Commander.Plugins.Smb
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
; the uninstaller isn't put in the plugin directory
UninstallFilesDir={localappdata}\Hst Commander SMB plugin
UninstallDisplayName=Hst Commander SMB plugin
UninstallDisplayIcon={uninstallexe}
; SMBLibrary's notices (LGPL), shown before installing
InfoBeforeFile={#SourceDir}\Hst.Commander.Plugins.Smb\THIRD-PARTY-NOTICES.txt
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
SetupIconFile=..\..\icon\hst-commander.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Hst Commander locks the plugin's files while running
CloseApplications=yes

[InstallDelete]
; files of an older version of the plugin
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#SourceDir}\Hst.Commander.Plugins.Smb\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\SMBLibrary-source-*.zip"; DestDir: "{app}"; Flags: ignoreversion

[Messages]
FinishedLabel=The SMB plugin has been installed. Start Hst Commander (or start it again) to use it: SMB is listed in the volume provider list (F1/F2).
