# Editor

F4 editing: the built-in editor, external editors and the editor settings. Read this when working on `EditorDocument`, `EditorSession`, `EditorSearch`, `ExternalEditor`, `ExternalEditFile` or an editor front-end.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Opening

- F4, the `F4 Edit` button or File > Edit opens the file under the cursor (not directories or `..`) in the editor chosen in the settings (`EditorKind`): the built-in editor (the default), the system editor or a custom command.
- `NavigationContext.OpenEditorAsync(entry)` returns an `EditorSession` for the built-in editor; `OpenExternalEditAsync(entry)` returns an `ExternalEditFile` for an external one.
- Local files are read and written directly. Files inside images and other volumes use a volume of their own from `OpenLocationAsync`, writing on the thread pool and committing the cached writes of a shared image afterwards (like file operations, see [Writable images](volumes.md#writable-images)).
- A file in a read-only image (ISO, zip, lha, lzx, images inside images) opens read-only in the built-in editor; external editors refuse it, since the changes would be lost.
- Both panes are refreshed after saving, as the other pane may show the same location.

## EditorDocument

- Holds the whole text in memory, so files up to `MaxLength` (16 MB) are edited. Files with zero bytes (and no UTF-16 byte order mark) are binary and refused.
- Encoding: UTF-8 or UTF-16 byte order mark, valid UTF-8, otherwise ISO-8859-1 (like the viewer). The byte order mark is written again.
- Line breaks: the first one found (LF, CR LF or CR) is the file's; the host's for files without one. `Text` has LF line breaks; `Encode` writes any line break as the file's, so a file keeps its line breaks.
- Characters ISO-8859-1 can't hold throw `IOException` when saving instead of being lost.
- Local files are written in place (open, write, set length), keeping attributes like hidden and permissions.

## Searching and replacing

- `EditorSearch` is shared by both front-ends and works on indexes in the text: text or regular expression, case sensitive, whole words, backwards. Plain text is searched as an escaped regular expression, so whole words work the same for both. Matches can span lines; empty matches are ignored; a regular expression has a timeout.
- Searching continues from the cursor (backwards from the start of the selection) and wraps around, shown as "Continued from the start/end".
- Replace (F4) replaces the selection when it's a match (`IsMatch`), then finds the next, so pressing F4 again replaces one at a time; groups like `$1` are substituted (`GetReplacement`). Replace all (`ReplaceAll`) replaces every match. Both can be undone.

## External editors

- `ExternalEditor.GetSystemEditor(terminal)` finds the system editor when asked: on Windows Microsoft Edit (`edit`, a terminal editor) in the TUI when installed, otherwise Notepad; in a Unix terminal `$VISUAL`, `$EDITOR`, nano or vi; on the macOS desktop TextEdit (`open -W -n -t`); on the Linux desktop the first of the common desktop text editors found (GNOME Text Editor, gedit, Kate, ...), started with arguments making them wait where they have one, otherwise `xdg-open`. The settings show the name of the editor found.
- A custom command (`ParseCommand`) is the program and its arguments separated by spaces, with double quotes around those with spaces; `%f` is replaced with the file, otherwise it's added last.
- Local files are edited in place. A file inside a volume is copied to a temporary directory with its name (`ExternalEditFile`) and written back by `SaveChangesAsync` when its SHA-256 hash changed; the copy is deleted afterwards.
- TUI: like Midnight Commander, the editor runs in the terminal while the TUI is shut down (`TerminalAction.RunEditor`), also desktop editors like Notepad. The copy is written back when the TUI runs again.
- GUI: a local file is opened and the panes are refreshed when the editor exits. For a copy, `ExternalEditDialog` is shown until the editor exits (written back) or Done is clicked; Discard drops the changes.
- An editor exiting within `ExternalEditor.HandOffTime` (2 seconds) has likely handed the file to a running instance (e.g. Notepad with tabs), so the TUI waits for a key and the GUI dialog waits for Done.

## Built-in editor

`EditorSession` holds the document, whether and why it's read-only, saving, the last search and the status text (`Line 12, Col 8   UTF-8, LF   Wrap   Modified`). The front-ends edit with their own text control:

- TUI: `EditorDialog` with `EditorTextView`, a Terminal.Gui `TextView`. The text view keeps rows and columns of the wrapped lines when word wrapped and doesn't expose the mapping to lines of the text, so `EditorTextView` calls its private `SetWrapModel`, `UpdateWrapModel`, `GetUnwrappedPosition` and `InsertAllText` by reflection (the package version is pinned; without them positions are of the wrapped lines and replacing all can't be undone). Ctrl+V pastes and Ctrl+Y redoes instead of the text view's emacs keys; Ctrl+L (open a file) is ignored.
- GUI: `EditorDialog` with an Avalonia `TextBox` (LF as new line, monospace font, theme colors). Replacing sets `SelectedText`, as setting `Text` clears the undo history. Word wrap also disables horizontal scrolling, as the text box only wraps at its width when it can't scroll horizontally.
- Closing with changes asks to save, discard or cancel (`SaveChangesDialog` in the GUI).
- Word wrap is off when the editor opens and toggled with F8, like F2 in the viewer (F2 saves in the editor, like Midnight Commander).

## Keys

Both editors: F2 or Ctrl+S save, F4 replace, F5 next, F6 previous, F7 or Ctrl+F search, F8 word wrap, Ctrl+G go to line, Ctrl+Z undo, Ctrl+Y redo, Ctrl+C/Ctrl+X/Ctrl+V copy, cut and paste, Ctrl+A select all, Esc or F10 close. The GUI also replaces with Ctrl+H (a terminal sends Ctrl+H for Backspace).

## Settings

The Editor tab of the settings dialog: the editor (built-in, system default with the name found, custom command) and the command (only for a custom editor, required for it). Stored as `Editor` and `EditorCommand` in each front-end's settings.
