# TUI Front-End

The Terminal.Gui front-end (`Hst.Commander.Tui`). Read this when changing TUI behavior. What each feature does is in the feature docs; this covers how the TUI does it.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Files

- `MainWindow.cs`: the dual-pane window, File and Help menus, columns, key bindings, theme colors and the handlers for every function key and dialog.
- `EntryTableView.cs`: the pane's `TableView`, with column dividers below the last entry and a scroll thumb.
- `PathPanelView.cs`: the panel above each pane (path, marked summary, disk space) and the pane's top border.
- `ListingProgressView.cs`: a framed spinner with "Reading ..." shown in the middle of a pane when listing it takes longer than `ListingProgress.Delay` (2 seconds), e.g. the physical disks while a sleeping disk wakes up, and, with a progress bar and a Cancel button, when making a temporary copy takes as long (the view is the pane's `NavigationContext.ProgressIndicator`; the table gets the focus back when it's hidden).
- `FunctionKeyBarView.cs`: the clickable function key bar.
- `TerminalScreen.cs`: the terminal output screen while the TUI is shut down, also running external editors (F4).
- `TuiSettings.cs`, `TuiTheme.cs`, `TuiPalette.cs`: settings and the 16-color palette per theme.
- Dialogs, one file each: `ViewerDialog` (+ `ViewerView`, `ViewerSearchDialog`), `EditorDialog` (+ `EditorTextView`, `EditorSearchDialog`, `GoToLineDialog`), `SettingsDialog`, `CopyDialog`, `CopyProgressDialog`, `DeleteProgressDialog`, `MakeDirectoryDialog`, `CreateVolumeDialog`, `CreateVolumeFieldsDialog`, `SelectGroupDialog`, `PropertiesDialog`, `VolumeProviderDialog`, `LocationDialog`, `VolumeLocationDialog` (the location of a volume provider asking for one, e.g. an SMB path), `CredentialsDialog` (user name and password, the password hidden with `TextField.Secret`), `AboutDialog` (+ `LicenseDialog`, a read-only `TextView` with a license notice).

## Terminal.Gui

- Terminal.Gui **v2 pre-alpha** (`2.0.0-prealpha.1895`). The API differs from v1: consult the installed package, not online v1 docs.
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
- Ctrl+\ arrives as the control character `0x1C`, which is also sent for Ctrl+4, so both match. The Windows driver decodes Ctrl with the key's character in the current layout, so Ctrl+`<` matches too: the key left of Z is `<` on e.g. Danish and German layouts.
- Ctrl+P is the main properties key (shown in the menu), because Windows Terminal and the classic console use Alt+Enter for full screen; Alt+Enter still works where the terminal passes it on.
- The volume provider list is centered over its pane. It and the create volume list (F9) have OK and Cancel buttons like the GUI. Enter in the list is handled in the list's `KeyDown` and chooses the selected item, as do OK and double-click.

## Viewer

`ViewerView` draws in the pane colors (offsets in the header color, the match in the cursor color, the selection in the dialog focus color). Selecting with the mouse uses `WantMousePositionReports` and `Application.GrabMouse` while dragging. Copying uses `Clipboard.TrySetClipboardData`, and the status line says when the terminal doesn't support it. No image view.

## Themes and Settings

- Themes: Midnight Commander (default), Light and Dark, from `TuiPalette`. `MainWindow.ApplyTheme` reapplies them. The settings dialog previews theme changes live and reverts them on Cancel.
- The settings dialog groups the settings in a `TabView`: Appearance (theme), Panes (volume keys, Enter on file, copy limit, see [Temporary copies](volumes.md#temporary-copies)), Copying (UAE metadata) and Editor (see [Editor settings](editor.md#settings)). Its radio groups have `HighlightStyle.None`: Terminal.Gui saves a view's color scheme when it's pressed and sets it as the view's own when released, so a clicked radio group would keep the previous theme's colors instead of inheriting the dialog's previewed theme.
- Settings are stored in `~/.hst-commander/tui-settings.json`; a missing or invalid file gives defaults.

## Terminal Programs

- Programs that run in the terminal (`FileStarter.IsTerminalProgram`: console-subsystem `.exe` by PE header, `.com`, `.bat`, `.cmd` on Windows; files with execute permission elsewhere) suspend the TUI like Midnight Commander. `MainWindow.StartFile` stores a `TerminalAction.RunProgram` and stops the window.
- `Program.cs` loops: `TerminalScreen.EnterTui`, `Application.Init`, `Application.Run(mainWindow)` (whose `Loaded` lists both panes at the same time and draws the window again when each is listed, so a pane waiting for a sleeping disk doesn't keep the other blank: `Application.Init` installs Terminal.Gui's synchronization context, so awaiting a listing that isn't done at once, like the physical disks, before the main loop runs would hang with a blank screen), `Application.Shutdown`, `TerminalScreen.LeaveTui`, then runs the pending terminal action (none when quitting) and shows the same `MainWindow` again. Errors starting a program are shown once the window is loaded again. Other files, including GUI programs on Windows, are started with the shell without suspending.
- Ctrl+O (`TerminalAction.ShowOutput`) shows the output screen until a key is pressed.
- Windows Terminal and Unix terminals draw the TUI on the alternate screen, so the output survives. The classic Windows console doesn't, so `EnterTui` gives the TUI a screen buffer of its own there and `LeaveTui` restores the original, which is also needed because Terminal.Gui's `WindowsConsole.Cleanup` activates a new empty buffer on shutdown. Windows Terminal is detected by `WT_SESSION` or a pseudo console window (`PseudoConsoleWindow`): a program started as administrator, e.g. when restarting as administrator, doesn't get `WT_SESSION` but is shown in Windows Terminal when it's the default terminal, and a screen buffer of its own is never shown there, leaving the screen blank.
