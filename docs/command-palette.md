# Command palette

Ctrl+Z in a pane (or Commands > Command palette) opens a list of all menu commands, like opencode's command list, in both front-ends. It's handy for commands without a key of their own (Settings, About, Restart as administrator/root) and for finding a command's key.

## Behavior

- A search box at the top has the focus. Below it the commands are listed under a header for each menu: File, Mark, Go, Commands, then View (GUI only: the navigation modes) and Help. Each command shows its key at the right, as in the menus.
- Typing filters the commands: a command matches when every word typed is part of its name or its group's name, case-insensitively (`cop` matches Copy, `mark` lists the Mark menu, `pane same` matches Same location in other pane). Groups with no matching command are left out.
- The first matching command is selected. Up and Down move between the commands and skip the headers, which can't be selected (also not by clicking them); PageUp and PageDown move by about a page.
- Enter or double-click closes the palette and runs the selected command on the active pane, as the menu item does. Esc closes it without running anything. With no matching command Enter does nothing.
- The palette doesn't list itself. Restart as administrator/root is listed only when the menu shows it.

## Implementation

- `CommandPalette` (Core) lists the menu commands (`MenuCommands`, `PaletteCommand(Group, Name, Action, Shortcut)`), filters them into rows of headers and commands (`Filter`, `PaletteRow`) and moves the selection skipping headers (`FirstCommandIndex`, `MoveIndex`). The front-ends only draw the rows and handle the keys.
- A command's `Action` is a `CommanderActions` id, run with `MainWindow.RunAction` (TUI) or `RunActionAsync` (GUI), the same as the bottom area's buttons. `Home`, `CommandLine`, `About` and `RestartElevated` were added to `CommanderActions` for the palette (plugin API 1.3), so theme buttons can run them too. The GUI's navigation modes use `NavigationMode:<name>` actions handled by the GUI.
- The front-ends replace shortcuts that differ from `MenuCommands`' with `with`: the configured volume provider keys for Left/Right volume, and Quit's `F10/Ctrl+Q` in the TUI.
- TUI: `CommandPaletteDialog` is a `TuiDialog` with a `TextField` and a `ListView`; headers are drawn bold with `RowRender`, and `ValueChanging` keeps them from being selected. Ctrl+Z is handled in the panes' `KeyDown`, so the command line keeps it for undo.
- GUI: `CommandPaletteDialog` is a window with a `TextBox` and a `ListBox`; a tunnelling key handler on the window handles Up/Down/PageUp/PageDown/Enter/Esc before the text box and list. Ctrl+Z is handled at window level only while a pane has the focus (`IsPaneFocused`), so text boxes keep undo.

## Key

Ctrl+P was taken by Properties (the TUI's main Properties key, as terminals use Alt+Enter for full screen), so the palette uses Ctrl+Z. As Ctrl+Z is undo in text boxes, it opens the palette only while a pane has the focus; Commands > Command palette works from anywhere.
