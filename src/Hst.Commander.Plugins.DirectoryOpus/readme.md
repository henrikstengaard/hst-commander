# Hst.Commander.Plugins.DirectoryOpus

The Directory Opus plugin adds a theme looking like Directory Opus 4 on the Amiga to Hst Commander. It is included with Hst Commander by default and references only `Hst.Commander.Abstractions`, without other packages.

## Features

- Choose **Directory Opus 4** in Settings > Appearance > Theme, in the TUI and the GUI.
- **Colors** of Directory Opus 4's default configuration: files in black and directories in blue on grey, and the whole panel above the active pane (path, marked summary and disk space) in white on red. Marked entries are inverted like selected entries in Directory Opus 4: directories white on blue and files white on black. Directory Opus 4 has no cursor, so the entry under the cursor is black on orange.
- **Parent buttons**: a narrow button on the outer side of each pane, left of the left pane and right of the right pane. Clicking it makes the pane active and goes to the parent directory, the same as Backspace, also out of an image at its root.
- **Button bank**: the bottom area shows colored buttons in 6 columns of 3, like Directory Opus 4's, running the commands of Hst Commander instead of the function key bar:

| | | | | | |
|---|---|---|---|---|---|
| All | Parent | Copy | Delete | Read | Location |
| None | Root | Move | Info | Edit | Bookmark |
| Toggle | Rescan | MakeDir | Create | Swap | Config |

## Changing the Buttons

While the theme is chosen, the settings dialog has a **Buttons** tab to add, remove and move columns and buttons, and to change each button's label, command and colors (`#RGB` or `#RRGGBB`, empty for the theme's color). Reset restores the default bank.

- **GUI:** drag buttons within or between columns, and columns by their header. The fields below change the selected button, and a preview shows the result.
- **TUI:** move the cursor with the arrows; Ctrl+Up/Down moves a button within its column, Ctrl+Left/Right moves it to the next column (or the whole column from its header), Ins adds and Del removes, and Enter changes the button.

The buttons are saved in `buttons.json` in the plugin's directory, `~/.hst-commander/plugins/directoryopus` (e.g. `C:\Users\<name>\.hst-commander\plugins\directoryopus` on Windows), shared by the TUI and GUI. Delete the file to get the default bank back; a missing or invalid file gives the default bank too.

## Font (GUI)

The GUI uses Topaz, the Amiga font Directory Opus 4 used, at size 16 (twice its 8 pixel height, so it stays sharp), falling back to MicroKnight, when one of the TrueType versions of [amigafonts](https://github.com/rewtnull/amigafonts) is installed. Otherwise the default monospace font is used. The TUI uses the terminal's font.

The font isn't included with Hst Commander, as its license is unclear (GPL with the font exception, © dMG/t!s^dS!, and "Topaz is © AmigaInc"). To get it:

- **Download font** in Settings > Appearance (shown while the theme is chosen and the font isn't installed) downloads `Topaz_a1200_v1.0.ttf` and the project's readme with the license and credits from GitHub to `~/.hst-commander/fonts`, which only Hst Commander uses. The download is pinned to a commit of amigafonts and checked against SHA-256 hashes.
- Or install `Topaz_a1200_v1.0.ttf` from the `ttf` folder of amigafonts yourself: on Windows right-click it and choose Install, on macOS open it and click Install in Font Book, on Linux copy it to `~/.local/share/fonts/` and run `fc-cache -f`, or on any OS copy it to `~/.hst-commander/fonts/`. Then restart Hst Commander.

A font chosen in Settings > Appearance overrides the theme's font.

## Classes

- `DirectoryOpusPlugin` implements `IPlugin`, returning the theme, and gives it the plugin's directory (`IPluginHost.GetPluginDirectory`) in `Initialize`.
- `DirectoryOpus4Theme` implements `ITheme` (colors, parent buttons, the highlighted active path panel, the font and its downloads) and `ICustomizableButtonPanel` (the default bank in `DefaultLayout`, the user's bank in `Layout`, saved with `SaveLayout`). It's an example of a theme plugin; see [Themes](../../docs/themes.md) for how themes, the button editor and font downloads work.
