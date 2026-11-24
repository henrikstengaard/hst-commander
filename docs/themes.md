# Themes

Built-in and plugin themes, the bottom area with its buttons, parent buttons, the button editor and the Directory Opus 4 theme.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Theme Model

- `Themes` (Core) lists the themes shown in Settings > Appearance: the built-in Midnight Commander, Light and Dark, then the `ITheme`s of `IPlugin.Themes` in plugin order (a theme whose `Id` is already listed is left out). Each is a `ThemeChoice(Id, Name, Plugin)`, where `Plugin` is null for a built-in theme.
- Both settings files store the theme's `Id` as a string. The built-in ids are the names of the old `TuiTheme`/`GuiTheme` enums (`MidnightCommander`, `Light`, `Dark`), so existing settings keep their theme. `Themes.Find` falls back to Midnight Commander for an unknown id, e.g. when the theme's plugin isn't installed.
- Built-in themes keep their hand-made colors: `TuiPalette` in the TUI, `App.axaml` ThemeDictionaries in the GUI.
- A plugin theme gives its colors once as `ThemeColors` ("#RRGGBB" strings). The optional `MarkedContainerBackground` and `MarkedItemBackground` give marked rows a background (the row under the cursor keeps the cursor background); without them only the text of marked entries is colored.
  - **TUI:** `TuiPalette.From` maps each color to the closest of the 16 colors (`Color.GetClosestNamedColor16`, with the RGB values from `TuiTrueColors`), so it works in every terminal.
  - **GUI:** `ThemeManager` creates a `ThemeVariant` per plugin theme. It inherits Light or Dark depending on `ITheme.IsDark`, which colors the controls the theme doesn't, and gets a theme dictionary of brushes with the keys `App.axaml` uses. `PluginThemeStyles` is added to `Application.Styles` only while a plugin theme is applied. It uses the extra keys `CursorBackground`, `MenuBackground`, `MenuForeground`, `BottomBackground`, `ButtonBackground` and `ButtonForeground` to color the current row, the menu bar, the window background, the bottom area, its buttons and the parent buttons.

## Bottom Area

- The bottom area of the main window shows the theme's `ButtonPanel` (`ThemeChoice.GetBottomPanel`). Built-in themes return `FunctionKeyBar.Default`: 10 columns with one button each (F1–F10, each with a `Key` shown before its label). So the function key bar and a plugin's button bank are drawn by the same view: `ButtonPanelView` in both front-ends.
- Columns share the width equally (in the TUI the extra columns go to the first columns, like the old function key bar). Each column lists its buttons from top to bottom, and the area is as high as the tallest column. The TUI panes get `Dim.Fill(rows)`.
- Plugin themes center the labels. A button's own `Background`/`Foreground` override the theme's button colors (the closest of the 16 colors in the TUI).
- Clicking a button raises `ActionInvoked` with its `Action`. `MainWindow.RunAction` (TUI) and `RunActionAsync` (GUI) run the matching `CommanderActions` command on the active pane. Each front-end handles every id in `CommanderActions.All`, so add new commands in both.
- Viewer and editor dialogs keep their own `FunctionKeyBarView` (TUI) key bars; themes don't change them.

## Fonts

- A theme can name a font for the GUI with `ITheme.FontFamily` (fonts separated by commas, the first installed one is used) and `FontSize`. Themes don't include fonts: when none of the fonts is installed, the theme's font and size are ignored and the default monospace fonts are used.
- The font chosen in Settings > Appearance overrides the theme's font, and its size the theme's size. See [GUI themes and settings](gui.md#themes-and-settings). The TUI uses the terminal's font.
- A theme can let the user download its font (`ITheme.FontDownloads`, a list of `ThemeFontDownload`s). While the theme is chosen, none of its fonts is installed and no font of the user's own is chosen, Settings > Appearance shows a Download font button. `FontDownloadDialog` shows each file's name, license and project before anything is downloaded.
- `FontDownloader` (Core) only downloads over https and checks each file's SHA-256 hash. It saves the files only when all of them match (through a `.tmp` file), and rejects file names with a directory. The files go to `~/.hst-commander/fonts`, which only Hst Commander loads fonts from (`GuiFonts.LoadDownloadedFonts`, an Avalonia font collection registered as `fonts:hst-commander`). Nothing is installed in the operating system, and deleting the files removes the font.
- Fonts copied to `~/.hst-commander/fonts` by hand are loaded too.

## Parent Buttons

- When `ITheme.ShowParentButtons` is true, each pane shows a narrow button on its outer side: left of the left pane and right of the right pane. Clicking it makes the pane active and goes to the parent (`NavigationContext.GoParentAsync`, the same as Backspace), including out of an image at its root.
- When `ITheme.HighlightActivePathPanel` is true, the whole panel above the active pane (path, marked summary and disk space) gets the active path's colors (`ActivePathBackground`/`ActivePathForeground`), not only the path. The border keeps the pane border's color. The inactive pane's panel stays in the pane's colors.
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

- The default `Hst.Commander.Plugins.DirectoryOpus` plugin adds `DirectoryOpus4Theme` (id `DirectoryOpus4`). It uses the eight colors of Directory Opus 4's default configuration (grey, black, white, blue, red, purple, orange and yellow): files in black and directories in blue on grey, and the whole panel above the active pane (path, marked summary and disk space) in white on red. Marked entries are inverted like selected entries in Directory Opus 4: directories white on blue and files white on black. Directory Opus 4 has no cursor, so the entry under the cursor is black on orange. It shows parent buttons, and has a 6-column, 3-row button bank like Directory Opus 4's (All/None/Toggle, Parent/Root/Rescan, Copy/Move/MakeDir, Delete/Info/Create, Read/Edit/Swap, Location/Bookmark/Config). Some buttons have their own colors.
- Its GUI font is Topaz, the Amiga font Directory Opus 4 used, at 16 (twice its 8 pixel height, so it stays sharp), falling back to MicroKnight. The family names are the ones stored in the [amigafonts](https://github.com/rewtnull/amigafonts) TrueType files. Topaz of Kickstart 2.x is `Topaz a600a1200a400`, though the readme says `a4000`.
- **License:** the amigafonts files are under the GPL with the font exception and © dMG/t!s^dS!, but the readme also says "Topaz is © AmigaInc". So it's unclear whether the font may be redistributed, and GPL components stay out of the apps. Hst Commander therefore doesn't include the font.
- **Download button:** it downloads `Topaz_a1200_v1.0.ttf` and the project's readme (saved as `amigafonts-README.txt`, with the license and credits) from GitHub. The download is pinned to commit `d42987535d94ebbfb51e64fee76f9974c6ae145a` and checked against the hashes in `DirectoryOpus4Theme.FontDownloads`.

### Installing the Font Manually

Download `Topaz_a1200_v1.0.ttf` from the `ttf` folder of [amigafonts](https://github.com/rewtnull/amigafonts), then:

- **Windows:** right-click the file and choose Install, or "Install for all users".
- **macOS:** double-click the file and click Install in Font Book.
- **Linux:** copy the file to `~/.local/share/fonts/` and run `fc-cache -f`.
- **Hst Commander only, any OS:** copy the file to `~/.hst-commander/fonts/`.

Then restart Hst Commander.
- The user's buttons are saved in `buttons.json` in the plugin's directory (`~/.hst-commander/plugins/directoryopus`, from `IPluginHost.GetPluginDirectory`), so the TUI and GUI share them. A missing or invalid file gives the default bank.
- The plugin's `readme.md` describes the theme, changing the buttons and getting the font for users.
