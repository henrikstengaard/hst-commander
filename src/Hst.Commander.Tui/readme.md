# Hst.Commander.Tui

Hst Commander TUI is a terminal based dual-pane file manager inspired by Norton Commander and Midnight Commander, built with [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui) v2. It has the same functionality as the desktop GUI (`Hst.Commander.Gui`), sharing the navigation logic in `Hst.Commander.Core`, except that a terminal can't show entry icons, navigation modes and images.

The executable is published as `hst.commander`.

## Features

- Two panes, both starting in the current directory, with name, size and date columns that fit the width of the terminal.
- Browsing local drives and directories, and images as if they were directories: ISO 9660 images, zip, lha and lzx archives, MBR/GPT disk images with FAT and NTFS partitions, and Amiga adf and RDB hard disk images, also inside other images.
- Copying, moving, renaming, deleting and making directories between any locations, also into writable images (Amiga adf and RDB images), with progress dialogs, speed and ETA, a filter and overwrite confirmation.
- Creating volumes provided by plugins, e.g. empty, formatted or bootable Amiga disk files.
- Selecting entries one by one or as a group with wildcards or regular expressions, and inverting the selection.
- Properties of files, directories, partitions and drives, with the contents counted in the background, and changing attributes and Amiga protection bits.
- Viewing files of any size as text or hex, with searching, selecting and copying. Images are shown in the hex view.
- Changing location by typing a path, including paths into images.
- Running programs: programs that run in the terminal (console programs, batch files and scripts) suspend the TUI while they run, like Midnight Commander, and Ctrl+O shows their output. Other files are started with their associated application.
- Three themes: Midnight Commander (default), Light and Dark. Settings are stored in `~/.hst-commander/tui-settings.json`.
- Plugins loaded at startup from `~/.hst-commander/plugins` and `plugins` next to the executable.

## Keys

| Key | Action |
|---|---|
| Tab | Switch between the left and right pane |
| Enter | Open a directory or image, start a file |
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
| F10 / Ctrl+Q / Esc | Quit (Ctrl+Q or Esc when the terminal uses F10, e.g. GNOME Terminal) |
| Ctrl+P / Alt+Enter | Properties |
| Ctrl+L | Change location |
| Alt+F1 / Alt+F2 (Windows), Shift+F1 / Shift+F2 (Linux and macOS) | Choose the volume provider of the left / right pane (configurable in the settings) |
| Ctrl+O | Show the output of programs run |

## Running

```shell
dotnet run --project src/Hst.Commander.Tui/Hst.Commander.Tui.csproj
```

See [`docs/tui.md`](../../docs/tui.md) for how the TUI is built.
