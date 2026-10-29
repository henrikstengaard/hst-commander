# GUI Front-End

The Avalonia front-end (`Hst.Commander.Gui`). Read this when changing GUI behavior. What each feature does is in the feature docs; this covers how the GUI does it.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Files

- `MainWindow.axaml(.cs)`: the dual-pane window, menus, function key bar, window-level keys and error display.
- `PaneView.axaml(.cs)`: one pane: path panel, column header, entry `ListBox` (list or thumbnails), `IconLayoutView` for plugin navigation modes, the icon positions bar and the listing progress (an indeterminate progress bar with "Reading ..." shown when listing takes longer than `ListingProgress.Delay`, 2 seconds).
- `PaneNavigationMode.cs`, `IconLayoutView.cs`: navigation modes, see [Navigation modes](icons-and-navigation-modes.md#navigation-modes).
- `EntryItem.cs`: a display row for an `IEntry`.
- `Icons/`: `FileIcons` (loading and caching entry icons), `IFileIconProvider` with Windows, freedesktop and macOS implementations, and `ViewerIcon` for the icon viewer.
- `ViewerDialog` with `ViewerControl` and `ViewerSearchDialog`: the viewer.
- `ThemeManager.cs`, `GuiTheme.cs`, `MidnightCommanderStyles.axaml`, `GuiSettings.cs`: themes and settings.
- `BackgroundPicker`: the background chooser shared by the icon viewer and the image view.
- Dialogs, one `.axaml` each: `ErrorDialog`, `SettingsDialog`, `EditorDialog` (+ `EditorSearchDialog`, `GoToLineDialog`, `SaveChangesDialog`), `ExternalEditDialog`, `CopyDialog`, `CopyProgressDialog`, `OverwriteDialog`, `DeleteDialog`, `DeleteProgressDialog`, `QuitDialog`, `ConfirmDialog` (a question with two buttons or a message, e.g. restarting as administrator), `MakeDirectoryDialog`, `CreateVolumeDialog`, `CreateVolumeFieldsDialog`, `SelectGroupDialog`, `PropertiesDialog`, `IconViewerDialog`, `VolumeProviderDialog`, `LocationDialog`, `AboutDialog`.

## Avalonia

- Avalonia 12 with the Fluent theme and plain code-behind (no MVVM framework).
- Most keys (function keys, Tab, `+`/`-`/`*`, Ctrl+L, Ctrl+P/Alt+Enter, the volume provider keys, Ctrl+1-9) are handled at window level with tunnelling `KeyDown` handlers. Enter, Backspace, Ctrl+PageDown and Ctrl+\ are handled with a tunnelling handler on the list.
- The volume provider and create volume lists handle Enter with a tunnelling handler on the list, because the focused list item takes Enter before the default OK button sees it.
- `+`, `-` and `*` are matched by `KeySymbol` (plus `Key.Add`/`Subtract`/`Multiply`) and shown in the menu headers, because key gestures would display as `OemPlus`. Ctrl+\ matches the `\` symbol, `Key.OemPipe` or `Key.OemBackslash`.
- Dialogs are shown with `ShowDialog(owner)`. Progress dialogs (copy/move, delete) start the operation with `Task.Run` when opened and report through a `Progress<T>` created on the UI thread; questions such as overwriting are shown over them with `Dispatcher.UIThread.InvokeAsync`. Closing the window cancels, and the dialog closes when the operation has stopped and exposes its `Exception`.
- Errors are raised as `PaneView.NavigationFailed` and shown by `MainWindow` with `ErrorDialog`. `PaneView.CreateVolumeAsync` throws instead, so the error is shown before asking again.
- The function key bar buttons aren't focusable, so the active pane keeps the focus.

## Panes

- The name column fills the remaining width and is trimmed with an ellipsis. Column dividers are a non-hit-testable grid behind the list, so they continue below the last entry.
- The path panel keeps its height when the marked summary and disk space are empty.
- The `ListBox` isn't focusable and arrow keys only move the selection while an item has focus, so `PaneView.FocusEntries` focuses the selected item's container (realizing it with `UpdateLayout` if needed). The left pane has the focus on startup; Tab always switches panes.
- PageUp/PageDown and Home/End are handled in `PaneView`, because the list's `ScrollViewer` would otherwise only scroll the view without moving the selection.
- The current row is only shown in the active pane: `MainWindow.SetActivePane` sets `PaneView.IsActive`, which adds the `active` class; rows of the other list have no selection background in any theme.
- Row classes: `executable` (green) comes before the current-row and `marked` (yellow) styles, so marked entries stay yellow under the cursor.
- Icons: see [GUI icons](icons-and-navigation-modes.md#gui-icons).

## Viewer

- `ViewerControl` draws with `FormattedText` in a monospace font in the pane brushes, and the selection with `ViewerSelectionBackground`/`Foreground`. It captures the pointer while selecting and has a Copy/Select all context menu. Copying uses the window's `Clipboard`.
- The scroll bars are a vertical one for the position in the file (0-1000) and a horizontal one for text columns. In the image view: zoom buttons and a zoom combo box, a `BackgroundPicker` (the theme's background by default, not remembered) and a list of the file's images; Ctrl+mouse wheel zooms. See [Images in the viewer](viewer.md#images).

## Properties and Icon Viewer

- `PropertiesDialog` shows the entry icon (loaded in the dialog's pixel size; for several entries of one type the type icon) and `SelectableTextBlock` values, so paths can be copied.
- Clicking the icon opens `IconViewerDialog` with all icons of the entry: the plugin icon file's icons, otherwise the icons contained in the file, otherwise the OS icon in the distinct sizes the OS returns, otherwise the built-in icon. Images are fitted or zoomed on a selectable background; icons up to 256 pixels are scaled without smoothing so pixels stay sharp. `ViewerIcon` bitmaps are created only when shown.

## Themes and Settings

- Themes: Midnight Commander (default), Light and Dark. Midnight Commander is a custom `ThemeVariant` inheriting Dark plus `MidnightCommanderStyles` (the current row colors only), which `ThemeManager` adds to `Application.Styles` only for that theme. Only the panes are colored; menus, the function key bar and dialogs use the normal Fluent dark styling.
- Pane colors are per-theme brushes in `App.axaml` `ThemeDictionaries`, referenced with `DynamicResource` so theme changes apply live.
- The settings dialog groups the settings in a `TabControl`: Appearance (theme), Panes (volume keys, Enter on file), Copying (UAE metadata) and Editor (see [Editor settings](editor.md#settings)). Its tabs use the `SettingsTabItem` control theme instead of the Fluent theme's text headers, so they look like tabs (like the TUI's): bordered, the selected one with an accent line and joined to the bordered panel of its settings (drawn over the panel's border); the selected tab and the panel have the dialog's background, so only the borders mark them.
- Settings are stored in `~/.hst-commander/gui-settings.json`; a missing or invalid file gives defaults. The settings dialog previews theme changes live and reverts them on Cancel.
- The application icon is `src/icon/hst-commander.ico`, linked as `avares://Hst.Commander.Gui/Assets/hst-commander.ico`.
