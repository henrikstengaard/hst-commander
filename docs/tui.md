# TUI Front-End

The Terminal.Gui front-end (`Hst.Commander.Tui`). Read this when changing TUI behavior. What each feature does is in the feature docs; this covers how the TUI does it.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Files

- `MainWindow.cs`: the dual-pane window, File and Help menus, columns, key bindings, theme colors and the handlers for every function key and dialog.
- `EntryTableView.cs`: the pane's `TableView`, with column dividers below the last entry and a scroll thumb.
- `PathPanelView.cs`: the panel above each pane (path, marked summary, disk space) and the pane's top border.
- `ListingProgressView.cs`: a framed spinner with "Reading ..." shown in the middle of a pane when listing it takes longer than `ListingProgress.Delay` (2 seconds), e.g. the physical disks while a sleeping disk wakes up, and, with a progress bar and a Cancel button, when making a temporary copy takes as long (the view is the pane's `NavigationContext.ProgressIndicator`; the table gets the focus back when it's hidden).
- `FunctionKeyBarView.cs`: the clickable function key bar. The title follows the key with no space (`F5Copy`, the key in its own color, like Midnight Commander), so the ten items of the main window fit in 80 columns.
- `TerminalScreen.cs`: the terminal output screen while the TUI is shut down, also running external editors (F4).
- `TerminalProgressOutput.cs`: the progress of file operations in the terminal's tab or taskbar button, see [Terminals](#terminals).
- `TuiSettings.cs`, `TuiTheme.cs`, `TuiPalette.cs`: settings and the 16-color palette per theme; `TuiTrueColors.cs`: the RGB values of the 16 colors with true color.
- Dialogs, one file each: `ViewerDialog` (+ `ViewerView`, `ViewerSearchDialog`), `EditorDialog` (+ `EditorTextView`, `EditorSearchDialog`, `GoToLineDialog`), `SettingsDialog`, `CopyDialog`, `CopyProgressDialog`, `DeleteProgressDialog`, `MakeDirectoryDialog`, `CreateVolumeDialog`, `CreateVolumeFieldsDialog`, `SelectGroupDialog`, `PropertiesDialog`, `VolumeProviderDialog`, `BookmarksDialog` (the bookmark list of the Bookmarks item), `BookmarkDialog` (the description of a bookmark), `LocationDialog`, `VolumeLocationDialog` (the location of a volume provider asking for one, e.g. an SMB path), `CredentialsDialog` (user name and password, the password hidden with `TextField.Secret`), `AboutDialog` (+ `LicenseDialog`, a read-only `TextView` with a license notice).

## Terminal.Gui

- Terminal.Gui **v2 pre-alpha** (`2.0.0-prealpha.1895`). The API differs from v1: consult the installed package, not online v1 docs.
- Colors: on Linux and macOS Terminal.Gui writes 24-bit colors, which terminals without true color ignore (e.g. Terminal.app before macOS 26 shows the TUI in monochrome). Unless `COLORTERM` is `truecolor` or `24bit` (`TerminalScreen.SupportsTrueColor`), `Program.cs` sets `Application.Force16Colors` after each `Application.Init` (which resets it), so the 16 standard colors of `TuiPalette` are written as such. On Windows, `WindowsDriver` already uses 16 colors outside Windows Terminal. With true color (e.g. GNOME Terminal, Windows Terminal), the 16 colors are written with the RGB values of Windows Terminal's Campbell scheme (`TuiTrueColors`) instead of Terminal.Gui's, whose Gray (128,128,128) is barely lighter than DarkGray and made the cursor row, the active path and text fields very dark. They're set before any palette is created, because an attribute keeps the RGB values of its colors (a class of its own, as reading a static field of `TuiPalette` creates its palettes first), and after each `Application.Init`, because 16-color output looks up the nearest color in the same table.
- Pane keys are attached with `tableView.KeyDown += async (_, key) => { ... }`; window-level keys use `this.KeyUp`. The panes handle F9 and F10 before the menu bar does.
- Dialogs: `new Dialog { Buttons = [okButton] }`, run with `Application.Run(dialog)`.
- Background work (copy, move, delete, counting, search) runs with `Task.Run` and reports with `Application.Invoke`. Questions from a background operation (e.g. overwrite) are asked with `MessageBox.Query` invoked on the UI thread through a `TaskCompletionSource`. Cancel or Esc in a progress dialog cancels, and the dialog stays open until the operation has stopped.
- Errors are shown with `MessageBox.ErrorQuery("Error", e.Message, "OK")`.

## Panes

- Each pane is a private `Pane` class holding its `NavigationContext`, views, entries and `DataTable`.
- Names start with a marker character: the OS directory separator (`\` on Windows, `/` elsewhere) for directories (and `..`), `>` for files that can be opened as volumes (see [Detecting volume files](volumes.md#detecting-volume-files)) and a space for other files. The marker is set when the file is detected after listing.
- The name column takes the remaining width; size and date columns shrink on narrow panes so everything fits. Long names are shortened with `~`, keeping the end of the stem and the extension when they fit. Sizes are humanized (`SizeFormatter.FormatEntrySize`): containers with a size, i.e. drives, partition tables and partitions, show it, other directories show `<DIR>`.
- The path panel highlights the path of the focused pane and trims it from the left with `...`; the disk space is dropped when the panel is too narrow.
- The cursor row is only drawn in the active pane (the pane last focused), so it stays visible while a dialog or the menu has the focus. Marked entries are yellow, also under the cursor.
- PageUp/PageDown move by a visible page; Ctrl+PageDown is checked before PageDown.
- Ctrl+R (File > Refresh) lists the active pane again. Ctrl+U (File > Swap panes) swaps the `Pane` objects and their views' `X`, so each pane moves to the other side with its location, entries, marks and cursor; the active pane stays active, so the focus moves to the other side.
- Ctrl+\ arrives as the control character `0x1C`, which is also sent for Ctrl+4, so both match. The Windows driver decodes Ctrl with the key's character in the current layout, so Ctrl+`<` matches too: the key left of Z is `<` on e.g. Danish and German layouts.
- Ctrl+P is the main properties key (shown in the menu), because Windows Terminal and the classic console use Alt+Enter for full screen; Alt+Enter still works where the terminal passes it on.
- No drag and drop: terminals can't drag files out and only some type a dropped file's path (see [Drag and drop](gui.md#drag-and-drop) in the GUI).
- The volume provider list is centered over its pane. It and the create volume list (F9) have OK and Cancel buttons like the GUI. Enter in the list is handled in the list's `KeyDown` and chooses the selected item, as do OK and double-click.

## Viewer

`ViewerView` draws in the pane colors (offsets in the header color, the match in the cursor color, the selection in the dialog focus color). Selecting with the mouse uses `WantMousePositionReports` and `Application.GrabMouse` while dragging. Copying uses `Clipboard.TrySetClipboardData`, and the status line says when the terminal doesn't support it. No image view.

## Themes and Settings

- Themes: Midnight Commander (default), Light and Dark, from `TuiPalette`. `MainWindow.ApplyTheme` reapplies them. The settings dialog previews theme changes live and reverts them on Cancel.
- Message boxes (`MessageBox.Query`, `ErrorQuery`) use Terminal.Gui's global "Dialog" and "Error" color schemes, whose focused button barely differs from the others (dark gray instead of black text), so the focus seemed stuck. `MainWindow.ApplyMessageBoxColors` sets them to `TuiPalette.DialogColorScheme` and `ErrorColorScheme`, on theme changes and on `Application.InitializedChanged`, because `Application.Init` reloads Terminal.Gui's configuration and resets them.
- The settings dialog groups the settings in a `TabView`: Appearance (theme), Panes (volume keys, Enter on file, copy limit, see [Temporary copies](volumes.md#temporary-copies)), Copying (UAE metadata) and Editor (see [Editor settings](editor.md#settings)). Its radio groups have `HighlightStyle.None`: Terminal.Gui saves a view's color scheme when it's pressed and sets it as the view's own when released, so a clicked radio group would keep the previous theme's colors instead of inheriting the dialog's previewed theme.
- Settings are stored in `~/.hst-commander/tui-settings.json`; a missing or invalid file gives defaults.

## Terminal Programs

- Programs that run in the terminal (`FileStarter.IsTerminalProgram`: console-subsystem `.exe` by PE header, `.com`, `.bat`, `.cmd` on Windows; files with execute permission elsewhere) suspend the TUI like Midnight Commander. `MainWindow.StartFile` stores a `TerminalAction.RunProgram` and stops the window.
- `Program.cs` loops: `TerminalScreen.EnterTui`, `Application.Init`, `Application.Run(mainWindow)` (whose `Loaded` lists both panes at the same time and draws the window again when each is listed, so a pane waiting for a sleeping disk doesn't keep the other blank: `Application.Init` installs Terminal.Gui's synchronization context, so awaiting a listing that isn't done at once, like the physical disks, before the main loop runs would hang with a blank screen), `Application.Shutdown`, `TerminalScreen.LeaveTui`, then runs the pending terminal action (none when quitting) and shows the same `MainWindow` again. Errors starting a program are shown once the window is loaded again. Other files, including GUI programs on Windows, are started with the shell without suspending.
- Ctrl+B adds a bookmark (`BookmarkDialog`); the Bookmarks item of `VolumeProviderDialog` opens `BookmarksDialog` (or a message when there are no bookmarks), where Del deletes and F2 renames the selected bookmark.
- Ctrl+O (`TerminalAction.ShowOutput`) shows the output screen until a key is pressed.
- Windows Terminal and Unix terminals draw the TUI on the alternate screen, so the output survives. The classic Windows console doesn't, so `EnterTui` gives the TUI a screen buffer of its own there and `LeaveTui` restores the original, which is also needed because Terminal.Gui's `WindowsConsole.Cleanup` activates a new empty buffer on shutdown. Windows Terminal is detected by `WT_SESSION` or a pseudo console window (`PseudoConsoleWindow`): a program started as administrator, e.g. when restarting as administrator, doesn't get `WT_SESSION` but is shown in Windows Terminal when it's the default terminal, and a screen buffer of its own is never shown there, leaving the screen blank.

## Terminals

- From Windows, run the TUI on a remote machine with the Windows OpenSSH client (`ssh`) in Windows Terminal; it's the tested route. SSH usually doesn't pass on `COLORTERM`, so the TUI uses 16 colors there (see [Terminal.Gui](#terminalgui)).
- PuTTY isn't supported, and there's no code for it: it sends `TERM=xterm` like other terminals, and only its answerback to ENQ identifies it.
  - F1-F4 don't work with PuTTY's default keyboard setting, which sends `ESC[11~` to `ESC[14~` instead of xterm's `ESC OP` to `ESC OS` that Terminal.Gui reads. Set Terminal > Keyboard > "The Function keys and keypad" to Xterm R6.
  - Some controls show black text on a black background in PuTTY; the cause isn't known yet.
- The copy, move and delete progress dialogs show their progress in the terminal's tab or taskbar button (see [Common rules](file-operations.md#common-rules)) with the ConEmu escape sequence `ESC ] 9 ; 4 ; state ; percent BEL` (`TerminalProgress`), written to the standard output by `TerminalProgressOutput` on the UI thread, where Terminal.Gui draws too. It's only written to terminals known to show it (`TerminalProgress.IsSupported`): Windows Terminal (`TerminalScreen.IsWindowsTerminal`), ConEmu (`ConEmuANSI=ON`), WezTerm and Ghostty (`TERM_PROGRAM`) and iTerm2 3.6 or later (`TERM_PROGRAM_VERSION`), because other terminals, e.g. older iTerm2, show any `OSC 9` as a desktop notification. Over SSH `TERM_PROGRAM` usually isn't passed on, so nothing is shown there.
