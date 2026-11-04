# GUI Front-End

The Avalonia front-end (`Hst.Commander.Gui`). Read this when changing GUI behavior. What each feature does is in the feature docs; this covers how the GUI does it.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Files

- `MainWindow.axaml(.cs)`: the dual-pane window, menus, function key bar, window-level keys and error display.
- `PaneView.axaml(.cs)`, `PaneView.DragDrop.cs` (see [Drag and drop](#drag-and-drop)): one pane: path panel, column header, entry `ListBox` (list or thumbnails), `IconLayoutView` for plugin navigation modes, the icon positions bar and the listing progress (an indeterminate progress bar with "Reading ..." shown when listing takes longer than `ListingProgress.Delay`, 2 seconds, and, with a progress bar and a Cancel button, when making a temporary copy takes as long through `NavigationContext.ProgressIndicator`).
- `PaneNavigationMode.cs`, `IconLayoutView.cs`: navigation modes, see [Navigation modes](icons-and-navigation-modes.md#navigation-modes).
- `EntryItem.cs`: a display row for an `IEntry`.
- `Windows/WindowsVirtualFileDrag.cs`, `MacOS/MacFilePromiseDrag.cs`: dragging entries of images and other volumes out as virtual files and file promises, see [Drag and drop](#drag-and-drop).
- `Icons/`: `FileIcons` (loading and caching entry icons), `IFileIconProvider` with Windows, freedesktop and macOS implementations, and `ViewerIcon` for the icon viewer.
- `ViewerDialog` with `ViewerControl` and `ViewerSearchDialog`: the viewer.
- `ThemeManager.cs`, `GuiTheme.cs`, `MidnightCommanderStyles.axaml`, `GuiSettings.cs`: themes and settings.
- `BackgroundPicker`: the background chooser shared by the icon viewer and the image view.
- Dialogs, one `.axaml` each: `ErrorDialog`, `SettingsDialog`, `EditorDialog` (+ `EditorSearchDialog`, `GoToLineDialog`, `SaveChangesDialog`), `ExternalEditDialog`, `CopyDialog`, `CopyProgressDialog`, `OverwriteDialog`, `DeleteDialog`, `DeleteProgressDialog`, `QuitDialog`, `ConfirmDialog` (a question with two buttons or a message, e.g. restarting as administrator), `MakeDirectoryDialog`, `CreateVolumeDialog`, `CreateVolumeFieldsDialog`, `SelectGroupDialog`, `PropertiesDialog`, `IconViewerDialog`, `VolumeProviderDialog`, `BookmarksDialog` (the bookmark list of the Bookmarks item), `BookmarkDialog` (the description of a bookmark), `LocationDialog`, `VolumeLocationDialog` (the location of a volume provider asking for one, e.g. an SMB path), `CredentialsDialog` (user name and password, the password hidden with `PasswordChar`), `AboutDialog` (+ `LicenseDialog`, a read-only text box with a license notice).

## Avalonia

- Avalonia 12 with the Fluent theme and plain code-behind (no MVVM framework).
- `Program.Main` has `[STAThread]` (not top-level statements, which run the main thread as MTA): Avalonia only uses OLE on an STA UI thread on Windows, and without OLE windows don't accept drops and dragging out fails.
- Most keys (function keys, Tab, `+`/`-`/`*`, Ctrl+L, Ctrl+B, Ctrl+R, Ctrl+U, Ctrl+P/Alt+Enter, the volume provider keys, Ctrl+1-9) are handled at window level with tunnelling `KeyDown` handlers. Enter, Backspace, Ctrl+PageDown and Ctrl+\ are handled with a tunnelling handler on the list.
- The volume provider and create volume lists handle Enter with a tunnelling handler on the list, because the focused list item takes Enter before the default OK button sees it.
- `+`, `-` and `*` are matched by `KeySymbol` (plus `Key.Add`/`Subtract`/`Multiply`) and shown in the menu headers, because key gestures would display as `OemPlus`. Ctrl+\ matches the `\` symbol, `Key.OemPipe` or `Key.OemBackslash`.
- Dialogs are shown with `ShowDialog(owner)`. Progress dialogs (copy/move, delete) start the operation with `Task.Run` when opened and report through a `Progress<T>` created on the UI thread; questions such as overwriting are shown over them with `Dispatcher.UIThread.InvokeAsync`. Closing the window cancels, and the dialog closes when the operation has stopped and exposes its `Exception`.
- Errors are raised as `PaneView.NavigationFailed` and shown by `MainWindow` with `ErrorDialog`. `PaneView.CreateVolumeAsync` throws instead, so the error is shown before asking again.
- `ErrorDialog` shows a red error icon left of the message, and dialogs asking a question (`ConfirmDialog`, `QuitDialog`, `SaveChangesDialog`, `DeleteDialog`, `OverwriteDialog`) show a blue question mark icon (`QuestionIcon`) left of the question; both are vector drawings, the same in all themes. A `ConfirmDialog` without a no button only shows a message and hides the icon.
- The function key bar buttons aren't focusable, so the active pane keeps the focus.

## Panes

- The name column fills the remaining width and is trimmed with an ellipsis. Column dividers are a non-hit-testable grid behind the list, so they continue below the last entry.
- The path panel keeps its height when the marked summary and disk space are empty.
- The `ListBox` isn't focusable and arrow keys only move the selection while an item has focus, so `PaneView.FocusEntries` focuses the selected item's container (realizing it with `UpdateLayout` if needed). The left pane has the focus on startup; Tab always switches panes.
- Ctrl+R (File > Refresh) lists the active pane again. Ctrl+U (File > Swap panes) swaps the panes' contents with `PaneView.SwapWithAsync` (navigation context, entries, cursor and navigation mode, as `LeftPane`/`RightPane` are fixed controls) and saves the navigation modes for their new sides; the focus moves to the other side, with the active pane's contents.
- PageUp/PageDown and Home/End are handled in `PaneView`, because the list's `ScrollViewer` would otherwise only scroll the view without moving the selection.
- The current row is only shown in the active pane: `MainWindow.SetActivePane` sets `PaneView.IsActive`, which adds the `active` class; rows of the other list have no selection background in any theme.
- Row classes: `executable` (green) comes before the current-row and `marked` (yellow) styles, so marked entries stay yellow under the cursor.
- Icons: see [GUI icons](icons-and-navigation-modes.md#gui-icons).

## Drag and Drop

The TUI has no drag and drop: terminals can't drag files out, and dropping only types the path in some terminals.

- **Dragging out:** in the list and thumbnail modes, dragging an entry with the left button (after moving 4 pixels) drags the marked entries when it's marked, otherwise just that entry (".." can't be dragged). Local entries are dragged as their files (`NavigationContext.GetLocalPaths`) with copy and move allowed. When another application moves them (e.g. Explorer with Shift), both panes are listed again.
- **Entries in images and other volumes** (archives, Amiga images, SMB, physical disks) aren't copied first: the drag starts at once and the application dropped on reads them through `VolumeDragSource`, which opens the pane's location again on the thread pool (so the pane can keep navigating) and gives them the names and UAE metadata copying to a local directory gives them with the UAE metadata setting (see [UAE metadata](file-operations.md#uae-metadata)): the metadata files (`_UAEFSDB.___` or `.uaem`) are made in memory for each dragged directory and dragged after its entries, metadata files of the source aren't dragged, and with `None` entries get local names (e.g. `file_1` for the Amiga name `file*1`). Dropped in a directory that already has a `_UAEFSDB.___` file, Explorer asks to replace it rather than adding to it.
  - **Windows** (`Windows/WindowsVirtualFileDrag.cs`): virtual files in an OLE data object of our own run with `SHDoDragDrop`, because Avalonia's drag source only offers data in memory. "FileGroupDescriptorW" lists the files and directories, "FileContents" gives a stream per file and "Preferred DropEffect" asks for a copy. The data object supports `IDataObjectAsyncCapability`, so Explorer copies after the drop with its own progress dialog. Explorer's calls arrive on the UI thread, so reads run on the thread pool and are waited for. The drag source is closed once the drag has ended, Explorer has finished copying (`EndOperation`) and every stream it was given has been read to its end (reading again at the end reads no bytes, as Explorer reads until it gets none, and seeking only moves the position the next read goes to, as Explorer seeks after reading the end); a drag whose files stopped being read (e.g. cancelled) is closed when the next drag starts. Explorer always copies (a move with Shift doesn't delete anything in the volume).
  - **macOS** (`MacOS/MacFilePromiseDrag.cs`): file promises (`NSFilePromiseProvider`) in a dragging session of our own on the window's `NSView` (`IMacOSTopLevelPlatformHandle`), with the delegate and dragging source registered as Objective-C classes through the Objective-C runtime. Finder asks for each dragged entry to be written where it was dropped (`VolumeDragSource.CopyToLocalAsync`, on an operation queue), copied there with UAE metadata like copying; the entry gets the promised name (its local name) unless it has UAE metadata, which refers to the name copying gave it.
  - **Linux:** no file manager reads such files, so these entries are only dragged to the panes.
  - Errors reading the entries are shown when the drop target reads them.
- **Dropping** on a pane, from either pane or another application, copies, or moves with Shift. In the list and thumbnails, dropping on a directory row copies into that directory (the row is outlined), otherwise into the current location (the pane is outlined). In the plugins' navigation modes (e.g. the Workbench, whose icons are dragged to move them), entries can only be dropped. The drive list only takes drops on a drive. A pane's own entries are only dropped on one of its other directories.
- A drop opens the copy or move dialog with the destination filled in, like F5/F6 (`MainWindow.DropAsync`): entries of a pane through `CopyAsync`/`MoveAsync` of their pane, local files through `DroppedEntries`. A drop always reports the copy effect to the application dragged from, because Explorer would delete the files of a move itself while Hst Commander still asks.
- Entries dragged from a pane travel in an in-process data format (`PaneDragFormat`) next to the files, so a drop on a pane copies them like F5. A drag of the operating system (virtual files, file promises) doesn't carry Avalonia's in-process data, so the dragged entries are kept in `s_nativePaneDrag` while it runs. Drops are handled after the drag has ended (`Dispatcher.UIThread.Post`), so the dialogs aren't opened inside the drag of the operating system.

## Viewer

- `ViewerControl` draws with `FormattedText` in a monospace font in the pane brushes, and the selection with `ViewerSelectionBackground`/`Foreground`. It captures the pointer while selecting and has a Copy/Select all context menu. Copying uses the window's `Clipboard`.
- The scroll bars are a vertical one for the position in the file (0-1000) and a horizontal one for text columns. In the image view: zoom buttons and a zoom combo box, a `BackgroundPicker` (the theme's background by default, not remembered) and a list of the file's images; Ctrl+mouse wheel zooms. See [Images in the viewer](viewer.md#images).

## Properties and Icon Viewer

- `PropertiesDialog` shows the entry icon (loaded in the dialog's pixel size; for several entries of one type the type icon) and `SelectableTextBlock` values, so paths can be copied.
- Clicking the icon opens `IconViewerDialog` with all icons of the entry: the plugin icon file's icons, otherwise the icons contained in the file, otherwise the OS icon in the distinct sizes the OS returns, otherwise the built-in icon. Images are fitted or zoomed on a selectable background; icons up to 256 pixels are scaled without smoothing so pixels stay sharp. `ViewerIcon` bitmaps are created only when shown.

## Themes and Settings

- Themes: Midnight Commander (default), Light and Dark. Midnight Commander is a custom `ThemeVariant` inheriting Dark plus `MidnightCommanderStyles` (the current row colors only), which `ThemeManager` adds to `Application.Styles` only for that theme. Only the panes are colored; menus, the function key bar and dialogs use the normal Fluent dark styling.
- Pane colors are per-theme brushes in `App.axaml` `ThemeDictionaries`, referenced with `DynamicResource` so theme changes apply live.
- The settings dialog groups the settings in a `TabControl`: Appearance (theme), Panes (volume keys, Enter on file, copy limit, see [Temporary copies](volumes.md#temporary-copies)), Copying (UAE metadata) and Editor (see [Editor settings](editor.md#settings)). Its tabs use the `SettingsTabItem` control theme instead of the Fluent theme's text headers, so they look like tabs (like the TUI's): bordered, the selected one with an accent line and joined to the bordered panel of its settings (drawn over the panel's border); the selected tab and the panel have the dialog's background, so only the borders mark them.
- Settings are stored in `~/.hst-commander/gui-settings.json`; a missing or invalid file gives defaults. The settings dialog previews theme changes live and reverts them on Cancel.
- The application icon is `src/icon/hst-commander.ico`, linked as `avares://Hst.Commander.Gui/Assets/hst-commander.ico`.
