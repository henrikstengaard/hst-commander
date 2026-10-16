<p align="center">
  <img src="src/icon/hst-commander.svg" alt="Hst Commander icon" width="128" height="128">
</p>

<h1 align="center">Hst Commander</h1>

<p align="center">
  A dual-pane file manager for Windows, macOS and Linux, inspired by Norton Commander and Midnight Commander.
</p>

---

Hst Commander is a dual-pane file manager written in C# for .NET 10. Its distinguishing feature is a **pluggable volume abstraction**: any container, such as a local directory, an ISO 9660 image, a disk image or a future plugin type, can be browsed in the same dual-pane UI.

Hst Commander comes with two front-ends that have the same functionality:

- **Terminal UI (TUI)**: built with [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) v2. It runs in any terminal and suspends itself to run console programs, like Midnight Commander.
- **Desktop GUI**: built with [Avalonia](https://avaloniaui.net/) 12. It shows the host OS icons for files, folders and drives.

## Features

- Dual-pane browsing of local drives and directories.
- Browsing ISO 9660 images as if they were directories, also as the source or destination of copying.
- Copying, moving, renaming, making directories and deleting, with progress dialogs, speed and ETA, filters and overwrite confirmation.
- Selecting entries one by one or as a group with wildcards or regular expressions, and inverting the selection.
- Properties of files, directories and drives, with the size and contents counted in the background.
- Starting files with their associated application and running programs.
- Changing location by typing a path, including paths into ISO images.
- Choosing the volume provider of each pane.
- Three themes: Midnight Commander (default), Light and Dark.

## Keys

| Key | Action |
|---|---|
| Tab | Switch between the left and right pane |
| Enter / double-click | Open a directory or image, start a file |
| Backspace | Go to the parent directory |
| Insert / Space | Select (mark) the entry under the cursor |
| + / - | Select / unselect a group of entries |
| * | Invert the selection |
| F5 | Copy |
| F6 | Move or rename |
| F7 | Make directory |
| F8 / Delete | Delete |
| Ctrl+P / Alt+Enter | Properties |
| Ctrl+L | Change location |
| Alt+F1 / Alt+F2 (Windows), Shift+F1 / Shift+F2 (Linux and macOS) | Choose the volume provider of the left / right pane (configurable in settings) |
| Ctrl+O | Show the terminal output (TUI only) |
| F9 / Alt+F | Open the menu (TUI) |

## Download

Releases are published on the [GitHub releases page](https://github.com/henrikstengaard/hst-commander/releases) as:

- **Windows** (x64, x86, arm64): setup installer and portable zip.
- **macOS** (x64, arm64): dmg for the GUI app and the TUI.
- **Linux** (x64, arm, arm64): AppImage and deb package.

## Building

Hst Commander requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and `git` on the path (the build number is the git commit count).

```shell
# Build the solution
dotnet build src/Hst.Commander.sln

# Run the GUI app
dotnet run --project src/Hst.Commander.Gui/Hst.Commander.Gui.csproj

# Run the TUI app
dotnet run --project src/Hst.Commander.Tui/Hst.Commander.Tui.csproj

# Run unit tests
dotnet test src/Hst.Commander.Core.Tests/Hst.Commander.Core.Tests.csproj
```

## Project structure

| Project | Description |
|---|---|
| `Hst.Commander.Extensions.Volume.Abstractions` | `IVolumeProvider`, `IVolume` and `IEntry` interfaces for volume plugins |
| `Hst.Commander.Core` | Volume implementations (local, ISO 9660, nested volumes), copy/move/delete and the shared navigation logic used by both front-ends |
| `Hst.Commander.Tui` | Terminal UI application |
| `Hst.Commander.Gui` | Desktop GUI application |
| `Hst.Commander.Core.Tests` | Unit tests |

See [src/agents.md](src/agents.md) for the full specification, architecture and coding conventions.

## Status

Hst Commander is in early development. Not implemented yet:

- View (F3) and edit (F4).
- Loading volume provider plugins at startup.
- Raw disk images (MBR, GPT and RDB partition tables).
- Opening ISO images located inside another ISO image.

## Author

Henrik Nørfjand Stengaard

## License

Hst Commander is licensed under the [MIT License](LICENSE).
