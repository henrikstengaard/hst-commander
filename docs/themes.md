# Themes

Built-in and plugin themes, the bottom area with its buttons, parent buttons, the button editor and the Directory Opus 4 theme.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Theme Model

- `Themes` (Core) lists the themes shown in Settings > Appearance: the built-in Midnight Commander, Light and Dark, then the `ITheme`s of `IPlugin.Themes` in plugin order (a theme whose `Id` is already listed is left out). Each is a `ThemeChoice(Id, Name, Plugin)`, where `Plugin` is null for a built-in theme.
- Both settings files store the theme's `Id` as a string. The built-in ids are the names of the old `TuiTheme`/`GuiTheme` enums (`MidnightCommander`, `Light`, `Dark`), so existing settings keep their theme. `Themes.Find` falls back to Midnight Commander for an unknown id, e.g. when the theme's plugin isn't installed.
- Built-in themes keep their hand-made colors: `TuiPalette` in the TUI, `App.axaml` ThemeDictionaries in the GUI.
- A plugin theme gives its colors once as `ThemeColors` ("#RRGGBB" strings):
  - **TUI:** `TuiPalette.From` maps each color to the closest of the 16 colors (`Color.GetClosestNamedColor16`, with the RGB values from `TuiTrueColors`), so it works in every terminal.
  - **GUI:** `ThemeManager` creates a `ThemeVariant` per plugin theme. It inherits Light or Dark depending on `ITheme.IsDark`, which colors the controls the theme doesn't, and gets a theme dictionary of brushes with the keys `App.axaml` uses. `PluginThemeStyles` is added to `Application.Styles` only while a plugin theme is applied. It uses the extra keys `CursorBackground`, `MenuBackground`, `MenuForeground`, `BottomBackground`, `ButtonBackground` and `ButtonForeground` to color the current row, the menu bar, the window background, the bottom area, its buttons and the parent buttons.

## Bottom Area

- The bottom area of the main window shows the theme's `ButtonPanel` (`ThemeChoice.GetBottomPanel`). Built-in themes return `FunctionKeyBar.Default`: 10 columns with one button each (F1–F10, each with a `Key` shown before its label). So the function key bar and a plugin's button bank are drawn by the same view: `ButtonPanelView` in both front-ends.
- Columns share the width equally (in the TUI the extra columns go to the first columns, like the old function key bar). Each column lists its buttons from top to bottom, and the area is as high as the tallest column. The TUI panes get `Dim.Fill(rows)`.
- Plugin themes center the labels. A button's own `Background`/`Foreground` override the theme's button colors (the closest of the 16 colors in the TUI).
- Clicking a button raises `ActionInvoked` with its `Action`. `MainWindow.RunAction` (TUI) and `RunActionAsync` (GUI) run the matching `CommanderActions` command on the active pane. Each front-end handles every id in `CommanderActions.All`, so add new commands in both.
- Viewer and editor dialogs keep their own `FunctionKeyBarView` (TUI) key bars; themes don't change them.

## Parent Buttons

- When `ITheme.ShowParentButtons` is true, each pane shows a narrow button on its outer side: left of the left pane and right of the right pane. Clicking it makes the pane active and goes to the parent (`NavigationContext.GoParentAsync`, the same as Backspace), including out of an image at its root.
- **GUI:** the `LeftParentButton`/`RightParentButton` of `PaneView` (16 pixels wide, spanning the pane's height), shown through `PaneView.ParentButtonSide`.
- **TUI:** a `ParentStripView` one column wide inside the pane view. `MainWindow.ShowParentStrip` moves the path panel, table and bottom line next to it.

## Button Editor

- A theme that also implements `ICustomizableButtonPanel` gets a settings tab named by its `SettingsTitle` while it's chosen in Appearance. The dialog grows to make room for the tab.
- The tab edits a `ButtonPanelEditor` (Core) made from `Layout`. The editor is kept per theme while the dialog is open, so switching themes keeps the changes. It always has at least one column and leaves empty columns out of `ToPanel`. Reset restores `DefaultLayout`. Colors typed by the user are normalized by `ButtonPanelEditor.TryParseColor` (`#RGB`, `RRGGBB` or `#RRGGBB`, empty for the theme's color).
- On OK the dialog returns the panel in `ChangedButtons`. `MainWindow` calls `SaveLayout` (showing an error if saving fails) and applies the theme again, so the bottom area shows the new buttons. Cancel changes nothing.
- **GUI (`ButtonPanelEditorView`):**
  - Columns are drawn side by side with their buttons as tiles. Drag a tile to another place in its column or into another column. Drag a column by its header.
  - The drag uses an in-process `DataFormat`. The drop position is worked out from the pointer: the column under it, then the row before the first tile whose center is below it.
  - The toolbar adds and removes columns and buttons. The fields below change the selected button's label, command (from `CommanderActions.All`) and colors. A preview shows the result with `ButtonPanelView`.
- **TUI (`ButtonPanelEditorView`, `PanelButtonDialog`):**
  - The TUI has no drag and drop, so the cursor moves over column headers and buttons with the arrows instead.
  - Ctrl+Up/Down moves a button within its column. Ctrl+Left/Right moves a button to the next column, or moves the whole column when the cursor is on its header.
  - Ins adds a button below the cursor (or a column, on a header) and Del removes it. Enter or a double-click opens `PanelButtonDialog` to change the label, command and colors.
  - Add column, Add button, Remove and Reset buttons do the same with the mouse.

## Directory Opus 4

- The default `Hst.Commander.Plugins.DirectoryOpus` plugin adds `DirectoryOpus4Theme` (id `DirectoryOpus4`). It uses the eight colors of Directory Opus 4's default configuration (grey, black, white, blue, red, purple, orange and yellow): files in black and directories in blue on grey, the selected entry in white on black and the active path in white on red. Marked entries are red, as the theme can only change their text color. It shows parent buttons, and has a 6-column, 3-row button bank like Directory Opus 4's (All/None/Toggle, Parent/Root/Rescan, Copy/Move/MakeDir, Delete/Info/Create, Read/Edit/Swap, Location/Bookmark/Config). Some buttons have their own colors.
- The user's buttons are saved in `buttons.json` in the plugin's directory (`~/.hst-commander/plugins/directoryopus`, from `IPluginHost.GetPluginDirectory`), so the TUI and GUI share them. A missing or invalid file gives the default bank.
