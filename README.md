<p align="center">
  <img src="src/icon/hst-commander.svg" alt="Hst Commander icon" width="128" height="128">
</p>

<h1 align="center">Hst Commander</h1>

<p align="center">
  A dual-pane file manager for Windows, macOS and Linux, inspired by Norton Commander and Midnight Commander.
</p>

---

Hst Commander is a dual-pane file manager written in C# for .NET 10. Its distinguishing feature is a **pluggable volume abstraction**: any container, such as a local directory, an ISO image, an archive, a disk image or an Amiga disk, can be browsed and used in the same dual-pane UI, and plugins can add new ones.

Hst Commander comes with two front-ends that have the same functionality:

- **Terminal UI (TUI)**: built with [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) v2. It runs in any terminal and suspends itself to run console programs, like Midnight Commander.
- **Desktop GUI**: built with [Avalonia](https://avaloniaui.net/) 12. It adds entry icons, navigation modes (list, thumbnails, Amiga Workbench) and an image viewer.

## Features

- Dual-pane browsing of local drives and directories.
- Browsing images as if they were directories, detected by their contents regardless of the extension, also inside other images:
  - ISO 9660 images.
  - Zip, lha and lzx archives.
  - MBR and GPT disk images (raw or vhd) with FAT and NTFS partitions, PiStorm disks and hybrid MBR/RDB disks.
  - Amiga disk files (adf) and hard disk images with a rigid disk block (RDB), with Fast File System and PFS3 partitions.
- Writing to Amiga adf and RDB images and FAT and NTFS partitions of disk images, shared safely by both panes.
- Reading and writing physical disks, internal and external (e.g. USB), opened like MBR, GPT and RDB disk images and shown with their model (as administrator or root).
- Copying, moving, renaming, making directories and deleting between any locations, with progress dialogs, speed and ETA, filters and overwrite confirmation.
- Keeping Amiga names, protection bits and comments when copying between Amiga volumes, and storing them as UAE metadata (UAEFSDB or FS-UAE metafiles) on other volumes.
- Creating volumes provided by plugins with F9, e.g. empty Amiga disk files (adf), formatted and bootable.
- Viewing files of any size as text or hex with search, selection and copying, and viewing images in the GUI: PNG, JPEG, GIF, BMP, WebP, Windows icons, Amiga icons and IFF ILBM pictures.
- Selecting entries one by one or as a group with wildcards or regular expressions, and inverting the selection.
- Properties of files, directories, partitions and drives, with the contents counted in the background, and changing attributes and Amiga protection bits.
- Entry icons in the GUI: Amiga `.info` icons, icons contained in programs and `.ico` files, and the host OS icons.
- GUI navigation modes: list, thumbnails and the Amiga Workbench with icons at their positions, which can be dragged and saved.
- Starting files with their associated application and running programs.
- Changing location by typing a path, including paths into images.
- Choosing the volume provider of each pane.
- Plugins loaded at startup from `~/.hst-commander/plugins`.
- Three themes: Midnight Commander (default), Light and Dark.

## Keys

| Key | Action |
|---|---|
| Tab | Switch between the left and right pane |
| Enter / double-click | Open a directory or image, start a file |
| Backspace | Go to the parent directory |
| Ctrl+\ | Go to the root of the volume |
| Ctrl+PageDown | Open a file as a volume |
| Insert / Space | Select (mark) the entry under the cursor |
| + / - | Select / unselect a group of entries |
| * | Invert the selection |
| F3 | View |
| F5 | Copy |
| F6 | Move or rename |
| F7 | Make directory |
| F8 / Delete | Delete |
| F9 | Create a volume, e.g. an Amiga disk file |
| F10 | Quit |
| Ctrl+P / Alt+Enter | Properties |
| Ctrl+L | Change location |
| Alt+F1 / Alt+F2 (Windows), Shift+F1 / Shift+F2 (Linux and macOS) | Choose the volume provider of the left / right pane (configurable in settings) |
| Ctrl+1 to Ctrl+9 | Choose the navigation mode (GUI only) |
| Ctrl+O | Show the terminal output (TUI only) |

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
| [`Hst.Commander.Abstractions`](src/Hst.Commander.Abstractions/readme.md) | Plugin API: `IVolume`, `IEntry`, `IPlugin` and the provider interfaces |
| [`Hst.Commander.Core`](src/Hst.Commander.Core/readme.md) | Built-in volumes (local, ISO 9660, nested), navigation, file operations, UAE metadata, viewer and plugin loading, shared by both front-ends |
| [`Hst.Commander.Tui`](src/Hst.Commander.Tui/readme.md) | Terminal UI application |
| [`Hst.Commander.Gui`](src/Hst.Commander.Gui/readme.md) | Desktop GUI application |
| [`Hst.Commander.Plugins.Zip`](src/Hst.Commander.Plugins.Zip/readme.md) | Zip archives (default plugin) |
| [`Hst.Commander.Plugins.LhaLzx`](src/Hst.Commander.Plugins.LhaLzx/readme.md) | Lha and lzx archives (default plugin) |
| [`Hst.Commander.Plugins.Disk`](src/Hst.Commander.Plugins.Disk/readme.md) | MBR/GPT disk images with FAT and NTFS partitions, PiStorm and hybrid disks (default plugin) |
| [`Hst.Commander.Plugins.Amiga`](src/Hst.Commander.Plugins.Amiga/readme.md) | Amiga adf and RDB images, `.info` icons, Workbench navigation mode and Amiga pictures (default plugin) |
| [`Hst.Commander.Plugins.PhysicalDisk`](src/Hst.Commander.Plugins.PhysicalDisk/readme.md) | Internal and external physical disks, opened like MBR, GPT and RDB disk images (default plugin) |
| [`Hst.Commander.Core.Tests`](src/Hst.Commander.Core.Tests/readme.md) | Unit tests |

See [src/agents.md](src/agents.md) for the architecture and coding conventions and [docs](docs) for how each feature works.

## Status

Hst Commander is in early development. Not implemented yet:

- Edit (F4); changing the viewer's encoding, word wrapping and going to an offset.
- Writing to ISO images and zip, lha and lzx archives, and to Amiga images inside other images.
- Changing RDB, MBR and GPT partitions.
- Other Amiga file systems (e.g. SFS), other PC file systems (e.g. exFAT, ext) and vhdx/vmdk images.
- Unloading or reloading plugins while running.

## Author

Henrik Nørfjand Stengaard

## License

Hst Commander is licensed under the [MIT License](LICENSE).
