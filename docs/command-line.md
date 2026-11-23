# Command Line

A command line like Midnight Commander's and Total Commander's, in both front-ends. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Behavior

- **The row:** it sits between the panes and the bottom area (the function key bar or a theme's buttons). Its prompt is just `>`: the active pane's directory, where commands run, is already in the pane's path panel, so the command gets the whole row.
- **Typing:** Ctrl+T or Commands > Command line focuses the row. Typing in a pane still does what it did, so `+`, `-`, `*`, Space and Insert keep selecting entries. A terminal can't tell typed text from shortcuts, which is why the row isn't focused while typing in a pane.
- **Keeping the command:** the command stays in the row until it runs or is cleared. Esc and Tab go back to the active pane and keep it.
- **Inserting entries:**
  - Ctrl+Enter inserts the name of the entry under the cursor of the active pane; `..` inserts nothing.
  - Ctrl+Shift+Enter inserts the entry's full path, or its name where there is no host directory.
  - From a pane, the text goes at the end of the command, so pressing Ctrl+Enter on several entries builds a command without leaving the pane. In the row, the text goes at the cursor.
  - A space is added before the name when it would join the text before it, and one after it, so the next name is separated.
  - Names with spaces or shell characters are quoted (`CommandLine.Quote`): double quotes for cmd.exe on Windows, where names can't contain double quotes, and single quotes elsewhere (`'` becomes `'\''`).
- **Running:** Enter in the row runs the command in the active pane's directory and clears the row. Both panes are listed again afterwards, as the command may have changed files. Enter on an empty row does nothing.
- **History:** Up and Down in the row go through the commands run during the session (`CommandLine.History`, not saved). The command being typed is kept and comes back after the newest command. A command run twice in a row is added once.
- **Where commands run:** only in local directories (`NavigationContext.CurrentHostDirectory`). Inside images, on other volume providers (FTP, SMB, physical disks) and in the drive list, Enter shows "Commands can only run in local directories". cmd.exe can't use a UNC path as its current directory, so SMB shares are left out too.
- **The shell:** `ShellCommand.CreateStartInfo` runs the command.
  - On Windows it uses `cmd.exe /d /s /c "command"` (`ComSpec` when set). `/s` keeps the quotes inside the command, and the arguments are passed as a string, because cmd.exe parses its own command line.
  - Elsewhere it uses `$SHELL -c command`, or `/bin/sh` without `SHELL`.
- **Not done:** a built-in `cd` (`cd` runs in the shell and doesn't change the pane), saving the history, and completion.

## TUI

- **Layout:** the row is a `Label` (the `> ` prompt) and a `TextField` at `Pos.AnchorEnd(bottomHeight + 1)`. `MainWindow.ApplyTheme` sets the panes to `Dim.Fill(bottomHeight + 1)` to make room for it.
- **Keys:**
  - Ctrl+T, Ctrl+Enter and Ctrl+Shift+Enter are handled in the pane's `KeyDown`.
  - The row's own keys (Enter, Esc, Tab, Up, Down and the insert keys) are handled in the `TextField`'s `KeyDown`, before Terminal.Gui's bindings. Esc therefore doesn't quit there.
  - Ctrl+J also inserts the name, because terminals on Linux and macOS send Ctrl+Enter as a line feed, which is also what they send for Ctrl+J.
- **Running:** a command runs like a program opened in a pane (see [Terminal programs](tui.md#terminal-programs)). `TerminalAction.RunCommand` stops the window, `TerminalScreen.RunCommand` runs the shell in the terminal with the directory and command printed first (`C:\work> dir`), and the window is shown again with the panes listed. Ctrl+O shows the output afterwards.

## GUI

- **Layout:** the row is a `DockPanel` docked above `BottomPanel` in `MainWindow.axaml`, holding the `CommandPrompt` text block (`>`) and the `CommandBox` text box.
- **Keys:**
  - The window's tunnelling `KeyDown` handles Ctrl+T, plus Ctrl+Enter and Ctrl+Shift+Enter while a pane or the row has the focus.
  - While the row has the focus, it also handles Enter, Esc, Tab, Up and Down. It leaves `+`, `-`, `*` and Del to the text box, so they're typed instead of selecting or deleting entries.
  - `PaneView`'s Enter ignores Enter with Ctrl, so Ctrl+Enter doesn't open the entry.
- **Running:** the command is started with the shell without waiting for it, and the focus goes back to the active pane. Both panes are listed again when the command has exited. The GUI has no terminal, so nothing shows the output. On Windows, cmd.exe opens a console window for console commands, and it closes when they exit. On Linux and macOS no terminal opens.
