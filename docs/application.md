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

## Quitting

- F10, `F10 Quit` or File > Quit asks `AppInfo.QuitQuestion` (Yes/No; Esc or closing answers No). Esc and F10 in the viewer and dialogs close those, not the application.
- TUI: Esc in the panes (`Application.QuitKey`) asks too. `MainWindow.Closing` confirms every stop of the window except a terminal action (running a program, Ctrl+O).
- GUI: `MainWindow.Closing` asks for every user close (also Alt+F4 and the close button) by cancelling the close while `QuitDialog` is shown and closing again on Yes. Closes by the OS or application shutdown aren't confirmed, and closing while another dialog is open is cancelled.
