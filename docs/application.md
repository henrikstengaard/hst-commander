# Application

Versioning, release workflows, the About dialog and quitting.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Versioning

- `Directory.Build.props` is the single source of the version and sets the product, authors and copyright for all projects. Bump the major/minor version there.
- `Directory.Build.targets` replaces the last version part with `git rev-list --count HEAD` (e.g. `0.1.29`) unless a version is passed with `-p:Version=...`. The .NET SDK appends the commit sha to the product version.
- `AppInfo` (Core) reads the name, author and version (`Major.Minor.Build`) of the entry assembly: `NameAndVersion` (e.g. `Hst Commander v0.1.29`, also the GUI window title) and `AboutText`. Help > About shows it in both front-ends.

## Release Workflows

- `build-and-test.yml` builds and tests on Windows, macOS and Linux for every push.
- `build-and-release.yml` (run manually) publishes the TUI and GUI as self-contained, single-file, ReadyToRun executables (`hst.commander`, `hst.commander.gui`) for each runtime and packages them with the scripts in `src/packaging`:
  - Windows (x64, x86, arm64): Inno Setup installer and portable zip.
  - macOS (x64, arm64, built on a macOS runner to sign ad hoc): a `.dmg` with the GUI as `Hst Commander.app` in a window to drag to Applications (icon positions match the arrow in `dmg-background.svg`) and one with the TUI.
  - Linux (x64, arm, arm64): AppImage and `.deb` (installed in `/usr/lib/<package>` with a `/usr/bin` link, desktop file and icon).
- It creates a draft prerelease tagged with the version.
- Windows files are signed with Azure Artifact Signing only when the repository variable `AZURE_SIGNING_ENABLED` is `true`: the executable before packaging (so the zip and installer contain it signed) and the setup `.exe` after. The workflow lists the variables and secrets it needs.

## Running as Administrator

- `Elevation.IsElevated` (`Environment.IsPrivilegedProcess`) is true when running as administrator (elevated) on Windows or as root on Linux and macOS. Both front-ends then show `Administrator` or `root` in white on red at the right of the menu bar, and the GUI adds it to the window title.
- Choosing a volume provider with `IVolumeProvider.RequiresAdministrator` (the physical disks) when not elevated, with Alt+F1/Alt+F2, File > Left/Right volume or the location dialog's `Volumes...`, asks `Elevation.GetRestartQuestion` with Restart and Continue. Continue (or a cancelled UAC prompt, password dialog or failed sudo) shows the volume provider anyway; disks are still listed, but opening one fails with an access error.
- Restart starts Hst Commander again with `StartupOptions.FromPanes`: both panes where they are, the pane that chose the volume provider at its root and active.
  - Windows (TUI and GUI): `Elevation.StartAsAdministrator` with the `runas` verb (UAC), then quits without the quit question. The elevated TUI opens in a new console window.
  - Linux and macOS TUI: a `TerminalAction.RestartAsRoot` shuts the TUI down, `Elevation.RunAsRootInTerminal` runs `sudo -v` (the password is asked in the terminal) and then `sudo <program> <options>`, waiting until it exits, and quits. When sudo fails, the TUI is shown again with the error.
  - Linux GUI: there's no terminal to ask for the password, so `Elevation.StartAsAdministratorAsync` runs `pkexec /usr/bin/env <display variables> <program> <options>`, where the desktop's polkit agent asks for the password (the question doesn't mention sudo). pkexec clears the environment, so `DISPLAY`, `XAUTHORITY` (or `~/.Xauthority`), `WAYLAND_DISPLAY`, `XDG_RUNTIME_DIR`, `XDG_SESSION_TYPE`, `LANG` and `DOTNET_ROOT` are passed for the window to show. The GUI waits until the name of the pkexec process (`/proc/<pid>/comm`) changes, as pkexec runs the program in its own process once the password is given, and then quits; when pkexec exits first (dismissed, exit code 126) the volume provider is shown, other exit codes (e.g. no polkit agent) are shown as an error.
  - macOS GUI, and Linux without pkexec (`Elevation.CanStartAsAdministrator`): it says to start Hst Commander as root (`Elevation.GetRestartManuallyMessage`) and shows the volume provider.
  - Running from an AppImage, the AppImage (`APPIMAGE`) is started instead of the executable in its mount, which root can't read.
- Started with `dotnet app.dll`, the dotnet host is started with the assembly.

## Command Line

`StartupOptions.Parse` reads where the panes start, used by the restart above: `--left-volume`/`--right-volume` with a volume provider name, `--left-path`/`--right-path` with a location (as in the location dialog, e.g. `PhysicalDrive2\mbr\1` with a volume provider), and `--active right`. `PaneStartup.ApplyAsync` skips a volume provider or location that doesn't exist, so the pane still starts. The TUI applies them once before the window is first run (`MainWindow.ApplyStartupOptionsAsync`), the GUI when the window is opened (`MainWindow.StartupOptions`, from `desktop.Args`). Windows command lines are quoted with `Elevation.JoinWindowsArguments`.

## Quitting

- F10, Ctrl+Q, `F10 Quit` or File > Quit asks `AppInfo.QuitQuestion` (Yes/No; Esc or closing answers No). Esc and F10 in the viewer and dialogs close those, not the application.
- Ctrl+Q is there because some terminals use F10 themselves, e.g. GNOME Terminal (Ubuntu) opens its menu with it, so the TUI never gets it.
- TUI: Esc in the panes (`Application.QuitKey`) asks too. `MainWindow.Closing` confirms every stop of the window except a terminal action (running a program, Ctrl+O, restarting as root) and restarting as administrator.
- GUI: `MainWindow.Closing` asks for every user close (also Alt+F4 and the close button) by cancelling the close while `QuitDialog` is shown and closing again on Yes. Closes by the OS or application shutdown aren't confirmed, and closing while another dialog is open is cancelled.
