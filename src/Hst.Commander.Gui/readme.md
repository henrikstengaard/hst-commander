# Hst.Commander.Gui

Hst Commander GUI is a desktop dual-pane file manager for Windows, macOS and Linux inspired by Norton Commander and Midnight Commander, built with [Avalonia](https://avaloniaui.net/) 12. It has the same functionality as the terminal UI (`Hst.Commander.Tui`), sharing the navigation logic in `Hst.Commander.Core`, and adds entry icons, navigation modes and an image viewer.

The executable is published as `hst.commander.gui`, on macOS as `Hst Commander.app`.

## Features

- Two panes, both starting in the current directory, with the same function key bar, File menu and themes as the TUI.
- Browsing local drives and directories, and images as if they were directories: ISO 9660 images, zip, lha and lzx archives, MBR/GPT disk images with FAT and NTFS partitions, and Amiga adf and RDB hard disk images, also inside other images.
- Copying, moving, renaming, deleting and making directories between any locations, also into writable images (Amiga adf and RDB images), with progress dialogs, speed and ETA, a filter and overwrite confirmation.
- Creating volumes provided by plugins, e.g. empty, formatted or bootable Amiga disk files.
- Selecting entries one by one or as a group with wildcards or regular expressions, and inverting the selection.
- Properties of files, directories, partitions and drives, with the contents counted in the background, and changing attributes and Amiga protection bits. Clicking the icon shows all images of the entry's icon.
- Viewing files of any size as text or hex, with searching, selecting and copying, and viewing images: PNG, JPEG, GIF (all frames), BMP, WebP, Windows icons, Amiga icons and IFF ILBM pictures, fitted or zoomed.
- Changing location by typing a path or double-clicking the path, including paths into images.
- Starting files with their associated application.
- Three themes: Midnight Commander (default), Light and Dark. Settings are stored in `~/.hst-commander/gui-settings.json`.
- Plugins loaded at startup from `~/.hst-commander/plugins` and `plugins` next to the executable.

### Icons

Each entry is shown with an icon, in this order:

1. Icons of plugin icon files, e.g. Amiga `.info` files.
2. Icons contained in files, e.g. Windows programs and `.ico` files, also inside images.
3. The host OS icons for file types, folders and drives (Windows shell, freedesktop icon themes on Linux, macOS).

### Navigation modes

The View menu (Ctrl+1 to Ctrl+9) chooses how each pane shows its entries:

- **List**: the default, with name, size and date columns.
- **Thumbnails**: large icons in rows.
- Navigation modes of plugins, e.g. the Amiga **Workbench**, showing icons at the positions stored in their `.info` files, which can be dragged, sorted by name and saved.

## Keys

| Key | Action |
|---|---|
| Tab | Switch between the left and right pane |
| Enter / double-click | Open a directory or image, start a file |
| Backspace | Go to the parent directory |
| Ctrl+\ | Go to the root of the volume |
| Ctrl+PageDown | Open a file as a volume |
| Insert / Space | Select (mark) the entry under the cursor |
| + / - / * | Select / unselect a group of entries, invert the selection |
| F3 | View |
| F5 | Copy |
| F6 | Move or rename |
| F7 | Make directory |
| F8 / Delete | Delete |
| F9 | Create a volume |
| F10 / Ctrl+Q | Quit |
| Ctrl+P / Alt+Enter | Properties |
| Ctrl+L | Change location |
| Ctrl+1 to Ctrl+9 | Choose the navigation mode |
| Alt+F1 / Alt+F2 (Windows), Shift+F1 / Shift+F2 (Linux and macOS) | Choose the volume provider of the left / right pane (configurable in the settings) |

## Running

```shell
dotnet run --project src/Hst.Commander.Gui/Hst.Commander.Gui.csproj
```

See [`docs/gui.md`](../../docs/gui.md) for how the GUI is built.
