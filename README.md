<p align="center">
  <img src="src/icon/hst-commander.svg" alt="Hst Commander icon" width="128" height="128">
</p>

<h1 align="center">Hst Commander</h1>

<p align="center">
  A dual-pane file manager for Windows, macOS and Linux, inspired by Norton Commander and Midnight Commander.
</p>

---

Hst Commander is a dual-pane file manager written in C# for .NET 10. Its distinguishing feature is a **pluggable volume abstraction**: any container, such as a local directory, an ISO image, an archive, a disk image, an Amiga disk, a physical disk or a server, can be browsed and used in the same panes, and plugins can add new ones.

Hst Commander comes with two front-ends that have the same functionality:

- **Terminal UI (TUI)**: built with [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) v2. It runs in any terminal (also over SSH and in PuTTY) and suspends itself to run console programs, like Midnight Commander.
- **Desktop GUI**: built with [Avalonia](https://avaloniaui.net/) 12. It adds entry icons, navigation modes (list, thumbnails, Amiga Workbench), an image viewer and drag and drop.

## Features

### Locations

- Local drives and directories, with the free and total space of the drive.
- Images browsed like directories, detected by their contents regardless of the extension, also inside other images:
  - ISO 9660 images.
  - Zip, lha, lzx, rar and 7z archives.
  - MBR and GPT disk images (raw, VHD, VHDX, VDI or DMG) with FAT, exFAT, NTFS, ext2/3/4, XFS, Btrfs, HFS+, SquashFS and UDF partitions, PiStorm disks and hybrid MBR/RDB disks, and file systems without a partition table.
  - Amiga disk files (adf) and hard disk images with a rigid disk block (RDB), with Fast File System, PFS3 and SFS partitions.
- Writing to Amiga adf, RDB and partition images and to FAT, exFAT and NTFS images (also in VHD, VHDX and VDI images), shared safely by both panes and file operations.
- Physical disks, internal and external (e.g. USB), read and written like disk images through a block cache (as administrator or root; Hst Commander offers to restart elevated).
- FTP, FTPS (explicit TLS) and SFTP servers, entered in a connection dialog.
- Windows file shares and other SMB 2/3 servers with the SMB plugin, installed separately (see [Download](#download)), entered as `\\server\share` or `smb://server/share`.
- Bookmarks of any location, with passwords stored in Windows Credential Manager, the macOS Keychain or the Linux Secret Service.
- Changing location by typing a path, including paths into images, or clicking the path above a pane.
- The panes start where they were when last quit (in the TUI the right pane, as the left pane starts in the current directory).

### File operations

- Copying, moving, renaming, making directories and deleting between any two locations, with progress, speed and time left (also on the taskbar or terminal tab), filters and overwrite confirmation.
- The clipboard (Ctrl+C, Ctrl+X, Ctrl+V), also with Explorer and other applications, including entries inside images.
- Drag and drop with other applications and between the panes (GUI).
- Keeping Amiga names, protection bits and comments when copying between Amiga volumes, and storing them as UAE metadata (UAEFSDB or FS-UAE metafiles) on other volumes.
- Creating volumes with F9: empty, formatted or bootable Amiga disk files and exFAT file systems as sparse files.
- Searching by name (wildcards or regular expressions) and contents (text, regular expressions or hex values), optionally inside images and archives, and quick search in a pane by typing.
- Selecting entries one by one, as a group with wildcards or regular expressions, or inverting the selection, and sorting by name, size or date.
- Properties of files, directories, partitions and drives, with the contents counted in the background, and changing attributes, Amiga protection bits, comments and icon tooltypes.

### Viewing and editing

- Viewing files of any size as text (word wrapped or not) or hex, with search, selection and copying.
- Viewing images in the GUI: PNG, JPEG, GIF, BMP, WebP, Windows icons, Amiga icons and IFF ILBM pictures.
- Editing text files in the built-in editor with search and replace, also inside writable images, or in an external editor.

### Interface

- A command line above the bottom area running commands with the shell, inserting entry names and paths, and a context menu with Terminal, PowerShell and Command prompt here.
- Starting files with their associated application.
- Entry icons in the GUI: Amiga `.info` icons, icons contained in programs and `.ico` files, and the host OS icons.
- GUI navigation modes: list, thumbnails and the Amiga Workbench with icons at their positions, which can be dragged and saved.
- Themes: Midnight Commander (default), Light, Dark and Directory Opus 4, whose button bank can be changed in the settings.
- Plugins loaded at startup from `~/.hst-commander/plugins`, adding locations, image formats, volume creators, icons, navigation modes, properties tabs and themes (see the [plugin API](docs/plugin-api.md)).
- An error log, and unexpected errors offered to be reported as a GitHub issue the user reviews first.

## Screenshots

### Desktop GUI

Local drives and an SMB server in the Midnight Commander theme.

![GUI with local drives and an SMB server in the Midnight Commander theme](assets/hst-commander_gui_1.png)

Local drives and an SMB server in the Light theme.

![GUI with local drives and an SMB server in the Light theme](assets/hst-commander_gui_2.png)

Local drives and an SMB server in the Dark theme.

![GUI with local drives and an SMB server in the Dark theme](assets/hst-commander_gui_3.png)

An Amiga Workbench disk and an RDB partition in list mode.

![GUI showing an Amiga Workbench disk and an RDB partition in list mode](assets/hst-commander_gui_4.png)

An Amiga Workbench disk in thumbnail mode.

![GUI showing an Amiga Workbench disk in thumbnail mode](assets/hst-commander_gui_5.png)

An Amiga Workbench disk in Workbench navigation mode.

![GUI showing an Amiga Workbench disk in Workbench navigation mode](assets/hst-commander_gui_6.png)

Amiga disk files and an RDB partition in the Directory Opus 4 theme.

![GUI showing Amiga disk files and an RDB partition in the Directory Opus 4 theme](assets/hst-commander_gui_7.png)

Properties of an Amiga icon, including its protection bits.

![GUI properties dialog for an Amiga icon](assets/hst-commander_gui_8.png)

The image viewer displaying an Amiga icon.

![GUI image viewer displaying an Amiga icon](assets/hst-commander_gui_9.png)

### Terminal UI

Local drives and an SMB server in the Midnight Commander theme.

![TUI with local drives and an SMB server in the Midnight Commander theme](assets/hst-commander_tui_1.png)

Local drives and an SMB server in the Light theme.

![TUI with local drives and an SMB server in the Light theme](assets/hst-commander_tui_2.png)

Local drives and an SMB server in the Dark theme.

![TUI with local drives and an SMB server in the Dark theme](assets/hst-commander_tui_3.png)

An Amiga Workbench disk and an RDB partition in the Dark theme.

![TUI showing an Amiga Workbench disk and an RDB partition in the Dark theme](assets/hst-commander_tui_4.png)

Amiga disk files and an RDB partition in the Directory Opus 4 theme.

![TUI showing Amiga disk files and an RDB partition in the Directory Opus 4 theme](assets/hst-commander_tui_5.png)

Properties of an Amiga icon, including its protection bits.

![TUI properties dialog for an Amiga icon](assets/hst-commander_tui_6.png)

## Keys

The keys are the same in both front-ends unless noted. The menus list them too: File (the file operations, Settings and Quit), Mark (selecting entries), Go (volumes, locations, bookmarks and the panes) and Commands (searching and the command line), plus View in the GUI.

| Key | Action |
|---|---|
| Tab | Switch between the left and right pane |
| Enter / double-click | Open a directory or image, start a file |
| Backspace | Go to the parent directory |
| Ctrl+\ or / | Go to the root of the volume (typing `/` or `\` works with every keyboard layout) |
| Ctrl+PageDown | Open a file as a volume |
| Insert / Space | Mark the entry under the cursor (also Shift+Down / Shift+Up, marking while moving; Terminal.app needs [a keyboard setting](docs/volumes.md#macos-terminal) for them) |
| Ctrl+A | Mark all entries |
| + / - / * | Mark / unmark a group of entries, invert the marks |
| F1 / F2 | Choose the volume provider of the left / right pane (also Alt+F1 / Alt+F2 or Shift+F1 / Shift+F2, configurable in the settings) |
| F3 | View |
| F4 | Edit |
| F5 | Copy |
| F6 | Move or rename |
| Shift+F6 | Rename |
| F7 | Make directory |
| F8 / Delete | Delete |
| F9 | Create a volume, e.g. an Amiga disk file |
| F10 / Ctrl+Q | Quit |
| Ctrl+C / Ctrl+X / Ctrl+V | Copy, cut and paste entries with the clipboard (also Alt+V in the TUI, as terminals paste text with Ctrl+V) |
| Ctrl+P / Alt+Enter | Properties |
| Ctrl+S | Search |
| Ctrl+F | Quick search |
| Ctrl+L | Change location |
| Ctrl+H | Go to the home directory |
| Ctrl+B | Add a bookmark (bookmarks are listed with F1 / F2) |
| Ctrl+R | Refresh the active pane |
| Ctrl+U | Swap the panes |
| Ctrl+D | Show the same location in the other pane |
| Ctrl+T | Go to the command line |
| Shift+F10 / Ctrl+F10 / menu key | Context menu |
| Ctrl+1 to Ctrl+9 | Choose the navigation mode (GUI only) |
| Ctrl+O | Show the terminal output (TUI only) |

The viewer and the editor have their own keys; see the [viewer](docs/viewer.md) and the [editor](docs/editor.md).

## Download

The TUI can be installed with one command, which downloads the latest release, installs it for the current user and adds `hst.commander` to the path.

Windows (PowerShell):

```powershell
irm https://github.com/henrikstengaard/hst-commander/releases/latest/download/install.ps1 | iex
```

macOS and Linux:

```sh
curl -fsSL https://github.com/henrikstengaard/hst-commander/releases/latest/download/install.sh | bash
```

To install the SMB plugin too, pass `-Smb` or `--smb`:

```powershell
& ([scriptblock]::Create((irm https://github.com/henrikstengaard/hst-commander/releases/latest/download/install.ps1))) -Smb
```

```sh
curl -fsSL https://github.com/henrikstengaard/hst-commander/releases/latest/download/install.sh | bash -s -- --smb
```

`-SmbOnly`/`--smb-only` installs only the SMB plugin, `-Uninstall`/`--uninstall` removes the TUI. See [Install scripts](docs/application.md#install-scripts).

Releases are published on the [GitHub releases page](https://github.com/henrikstengaard/hst-commander/releases), with the TUI and GUI packaged separately:

- **Windows** (x64, x86, arm64): setup installer and portable zip.
- **macOS** (x64, arm64): dmg.
- **Linux** (x64, arm, arm64; there's no 32-bit x86 build, as .NET doesn't support it on Linux): AppImage and deb package, and a tar.gz of the TUI. Make a downloaded AppImage executable before running it (`chmod +x hst-commander_*.AppImage`); it needs FUSE 2 (`libfuse2t64` on Ubuntu 24.04 and later, `libfuse2` before).
- **SMB plugin**: released separately as it uses the LGPL licensed [SMBLibrary](https://github.com/TalAloni/SMBLibrary). On Windows, `smb-plugin_windows_setup.exe` installs it for both apps. On every platform, `smb-plugin.zip` holds its `Hst.Commander.Plugins.Smb` directory to copy to `~/.hst-commander/plugins/` (see the [SMB plugin](src/Hst.Commander.Plugins.Smb/readme.md)).

Settings, bookmarks and the error log are stored in `~/.hst-commander` (`%USERPROFILE%\.hst-commander` on Windows).

## Code signing

Releases of Hst Commander are built from the source code in this repository by GitHub Actions and code signed as part of the build:
- Windows: TUI and GUI apps and their installers are signed with [Azure Artifact Signing](https://learn.microsoft.com/azure/artifact-signing/).
- macOS: TUI and GUI apps and their dmgs are signed with an Apple Developer ID certificate and notarized by Apple.
- Linux: Releases are not code signed.

## Building

Hst Commander requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and `git` on the path (the last part of the version is the git commit count).

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

Local builds include the SMB plugin. See [docs/application.md](docs/application.md) for publishing and the release workflows.

## Project structure

| Project | Description |
|---|---|
| [`Hst.Commander.Abstractions`](src/Hst.Commander.Abstractions/readme.md) | Plugin API: `IVolume`, `IEntry`, `IPlugin` and the provider interfaces, versioned separately (see [plugin API versions](docs/plugin-api.md#plugin-api-versions)) |
| [`Hst.Commander.Core`](src/Hst.Commander.Core/readme.md) | Built-in volumes (local, ISO 9660, nested), navigation, file operations, UAE metadata, viewer, editor, search, bookmarks and plugin loading, shared by both front-ends |
| [`Hst.Commander.Tui`](src/Hst.Commander.Tui/readme.md) | Terminal UI application |
| [`Hst.Commander.Gui`](src/Hst.Commander.Gui/readme.md) | Desktop GUI application |
| [`Hst.Commander.Plugins.Zip`](src/Hst.Commander.Plugins.Zip/readme.md) | Zip archives (default plugin) |
| [`Hst.Commander.Plugins.LhaLzx`](src/Hst.Commander.Plugins.LhaLzx/readme.md) | Lha and lzx archives (default plugin) |
| [`Hst.Commander.Plugins.RarSevenZip`](src/Hst.Commander.Plugins.RarSevenZip/readme.md) | Rar and 7z archives (default plugin) |
| [`Hst.Commander.Plugins.Disk`](src/Hst.Commander.Plugins.Disk/readme.md) | MBR/GPT and VHD/VHDX/VDI/DMG disk images with FAT, exFAT, NTFS, ext, XFS, Btrfs, HFS+, SquashFS and UDF, PiStorm and hybrid disks, and creating exFAT file systems (default plugin) |
| [`Hst.Commander.Plugins.Amiga`](src/Hst.Commander.Plugins.Amiga/readme.md) | Amiga adf, RDB and partition images, `.info` icons, Workbench navigation mode, Amiga pictures, comment and tooltypes tabs (default plugin) |
| [`Hst.Commander.Plugins.PhysicalDisk`](src/Hst.Commander.Plugins.PhysicalDisk/readme.md) | Internal and external physical disks, opened like disk images (default plugin) |
| [`Hst.Commander.Plugins.Ftp`](src/Hst.Commander.Plugins.Ftp/readme.md) | FTP, FTPS and SFTP servers (default plugin) |
| [`Hst.Commander.Plugins.DirectoryOpus`](src/Hst.Commander.Plugins.DirectoryOpus/readme.md) | Directory Opus 4 theme with a customizable button bank (default plugin) |
| [`Hst.Commander.Plugins.Smb`](src/Hst.Commander.Plugins.Smb/readme.md) | Windows file shares and other SMB 2/3 servers (external plugin, released separately) |
| [`Hst.Commander.Core.Tests`](src/Hst.Commander.Core.Tests/readme.md) | Unit tests |

See [src/agents.md](src/agents.md) for the architecture and coding conventions and [docs](docs) for how each feature works.

## Status

See [docs/status.md](docs/status.md) for what works. Not implemented yet, among others:

- Creating a new file (Shift+F4), and changing the encoding of the viewer and the editor.
- Writing to ISO images and zip, lha, lzx, rar and 7z archives, to ext partitions and to Amiga images inside other images.
- Changing RDB, MBR and GPT partitions, other Amiga file systems (e.g. SFS\2), writing XFS, Btrfs, HFS+, SquashFS and UDF, APFS and vmdk images.
- SMB 1, browsing the network for servers, implicit FTPS and SFTP private keys.
- Unloading or reloading plugins while running.

## License

Hst Commander is licensed under the [MIT license](LICENSE). It includes third-party components under their own licenses, listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) and shown in Help > About > Licenses. The SMB plugin is released separately with its own notices, as it uses [SMBLibrary](https://github.com/TalAloni/SMBLibrary) (LGPL-3.0-or-later).

## Author

Henrik Nørfjand Stengaard
