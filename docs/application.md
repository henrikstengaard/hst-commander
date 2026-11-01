# Application

Versioning, release workflows, the About dialog and quitting.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Versioning

- `Directory.Build.props` is the single source of the version and sets the product, authors and copyright for all projects. Bump the major/minor version there.
- `Directory.Build.targets` replaces the last version part with `git rev-list --count HEAD` (e.g. `0.1.29`) unless a version is passed with `-p:Version=...`. The .NET SDK appends the commit sha to the product version.
- `AppInfo` (Core) reads the name, author and version (`Major.Minor.Build`) of the entry assembly: `NameAndVersion` (e.g. `Hst Commander v0.1.29`, also the GUI window title) and `AboutText`. Help > About shows it in both front-ends (`AboutDialog`), with the Licenses section below.

## Licenses

- Hst Commander is MIT licensed (`LICENSE`). `THIRD-PARTY-NOTICES.txt` at the root of the repository lists every third-party component the TUI and GUI include, each with a `Packages:` line of its NuGet packages, followed by its copyright notice and license text. `GivenLicenses` fails when a package with runtime or native files in the TUI's or GUI's `project.assets.json` isn't listed, so a new dependency needs its notice added (and the test checks SMBLibrary isn't included).
- `src/notices/make-notices.py` (Python 3, standard library only) makes `THIRD-PARTY-NOTICES.txt` and the SMB plugin's `THIRD-PARTY-NOTICES.txt` after `dotnet restore src/Hst.Commander.sln`: it reads the packages and their versions from the TUI's, GUI's and plugin's `project.assets.json`, and the license texts from the NuGet packages or `src/notices/licenses/` (copies of the upstream license files, downloaded again with `--fetch`). Each component is an entry in its `SECTIONS` (title, url, license, package patterns, text); the script fails for an included package no section matches and leaves out listed packages the apps don't include. `--check` exits with 1 when the files are outdated, e.g. after changing a package version; `build-and-test.yml` runs it on Linux after restoring, so a push with outdated notices fails. The SMBLibrary version and commit come from `SMBLIBRARY_VERSION` and `SMBLIBRARY_COMMIT` in `build-and-release.yml`, which must match the plugin's package version.
- Core embeds `LICENSE`, `THIRD-PARTY-NOTICES.txt` and the SkiaSharp native asset package's `THIRD-PARTY-NOTICES.txt` (the notices of Skia, HarfBuzz and the libraries they include, the same for every platform) and copies them next to the apps as `LICENSE.txt`, `THIRD-PARTY-NOTICES.txt` and `THIRD-PARTY-NOTICES-SKIASHARP.txt`; text files aren't bundled in single-file apps, so every package gets them. `Directory.Build.targets` embeds the .NET runtime pack's notices in self-contained apps (the resource `THIRD-PARTY-NOTICES-DOTNET.txt` of the app's assembly) and copies them next to the published app as `THIRD-PARTY-NOTICES-DOTNET.txt`. The About dialog doesn't need any of the files, as it shows the embedded texts; they're installed so the notices can be read without running the app.
- `Licenses.GetNotices` (Core) gives the notices for the About dialog's Licenses section: Hst Commander's license, the third-party components, the Skia and HarfBuzz native libraries, the .NET runtime (embedded in the app, else the published file, else a link when running on an installed runtime) and each loaded plugin's `IPlugin.ThirdPartyNotices`. View license (or Enter/double-click) shows a notice in `LicenseDialog`.
- Plugins with dependencies under other licenses return their notices from `IPlugin.ThirdPartyNotices`, e.g. the SMB plugin's SMBLibrary (LGPL-3.0-or-later). Libraries under the LGPL or GPL aren't included with the apps: the SMB plugin is an external plugin, released separately (see below), with `SMBLibrary.dll` as a separate file users can replace, as the LGPL requires.

## Release Workflows

- `build-and-test.yml` builds and tests on Windows, macOS and Linux for every push, and checks the third-party notices are up to date (on Linux).
- `build-and-release.yml` (run manually) publishes the TUI and GUI as self-contained, single-file, ReadyToRun executables (`hst.commander`, `hst.commander.gui`) for each runtime and packages them with the scripts in `src/packaging`:
  - Windows (x64, x86, arm64): Inno Setup installer and portable zip.
  - macOS (x64, arm64, built on a macOS runner to sign ad hoc): a `.dmg` with the GUI as `Hst Commander.app` in a window to drag to Applications (icon positions match the arrow in `dmg-background.svg`) and one with the TUI.
  - Linux (x64, arm, arm64): AppImage and `.deb` (installed in `/usr/lib/<package>` with a `/usr/bin` link, desktop file and icon).
- The SMB plugin is published separately as `<version>_smb-plugin.zip` (managed only, so one zip for every platform): the `Hst.Commander.Plugins.Smb` directory to copy to `~/.hst-commander/plugins/` (the plugin, `SMBLibrary.dll`, its deps file, `THIRD-PARTY-NOTICES.txt` and the readme) and `SMBLibrary-source-<version>.zip`, the source of the SMBLibrary version included, downloaded from GitHub by its commit (`SMBLIBRARY_COMMIT` in the workflow; update it, the plugin's notices and the package version together).
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
