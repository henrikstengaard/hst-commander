# HST Commander - Agent Specification

## Project Overview

HST Commander is a dual-pane file manager for .NET 10, inspired by Norton Commander and Midnight Commander. The distinguishing feature is a **pluggable volume abstraction** that lets any container — a local directory, an ISO 9660 image, a disk image, or a future plugin type — be browsed transparently in the same dual-pane UI.

There are two front-ends with the same functionality: a terminal UI (TUI) built with Terminal.Gui v2 and a desktop GUI built with Avalonia 12. Both share `NavigationContext` from Core. The earlier ASP.NET Core + React web prototype has been removed and is no longer relevant.

**Front-end parity:** user-facing features are implemented in both front-ends. Put navigation and volume logic in `Hst.Commander.Core` (e.g. `NavigationContext`) so the TUI and GUI only contain presentation and key handling.

---

## Repository Layout

```
src/
  Hst.Commander.sln                              Solution file (5 projects)
  Hst.Commander.Extensions.Volume.Abstractions/  IVolume / IEntry interfaces (no external deps)
  Hst.Commander.Core/                            Volume implementations, NavigationContext + shared utilities
  Hst.Commander.Tui/                             Terminal.Gui TUI application
    MainWindow.cs                                Dual-pane window, columns, key bindings, colors
    EntryTableView.cs                            TableView with empty-row dividers and scroll thumb
    PathPanelView.cs                             Bordered panel with current path, marked-entries summary and disk space above the pane, plus the pane's top border
  Hst.Commander.Core.Tests/                      xUnit unit tests
  Hst.Commander.Gui/                             Avalonia 12 desktop GUI application
    MainWindow.axaml(.cs)                        Dual-pane window, File menu, Tab pane switching, error display
    PaneView.axaml(.cs)                          One pane: path, column header, entry ListBox, Enter/Backspace
    EntryItem.cs                                 Display row for an IEntry (size/date formatting)
    ErrorDialog.axaml(.cs)                       Modal error dialog
    SettingsDialog.axaml(.cs)                    Modal settings view (theme selection with live preview)
    GuiSettings.cs                               GUI settings record, loaded/saved as JSON
    GuiTheme.cs                                  Theme enum: MidnightCommander, Light, Dark
    ThemeManager.cs                              Applies a theme (theme variant + Midnight Commander styles)
    MidnightCommanderStyles.axaml(.cs)           Styles only active in the Midnight Commander theme
```

---

## Architecture

### Layer Diagram

```
[IVolume / IEntry]  <- Hst.Commander.Extensions.Volume.Abstractions
        |
[Volume Implementations]  <- Hst.Commander.Core
  LocalVolume, Iso9660Volume, NestedVolume,
  EntryVolumeProvider, DiskImageVolume (stub), NullVolume
        |
[NavigationContext]  <- Hst.Commander.Core
  Wraps NestedVolume, owns pane-level state
        |
[MainWindow (TUI)]  <- Hst.Commander.Tui
  Two TableView panes, key bindings, Terminal.Gui
[MainWindow (GUI)]  <- Hst.Commander.Gui
  Two PaneView (ListBox) panes, key bindings, Avalonia
```

### Key Interfaces

**`IVolume`** (`Hst.Commander.Extensions.Volume.Abstractions/IVolume.cs`)
```csharp
string Name { get; }
IEnumerable<string> CurrentPathComponents { get; }
IAsyncEnumerable<IEntry> ListEntries(CancellationToken cancellationToken = default);
Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default);
Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default);
```

**`IEntry`** (`Hst.Commander.Extensions.Volume.Abstractions/IEntry.cs`)
- `Name`, `Type` (`EntryType.Container` | `EntryType.Item`), `Size`, `ModifiedDate`

### Volume Implementations

| Class | Purpose |
|---|---|
| `LocalVolume` | Browses the real file system; lists drives when at root |
| `Iso9660Volume` | Reads ISO 9660 / CD-ROM images via `LTRData.DiscUtils.Iso9660` |
| `NestedVolume` | Compositor — holds a `LinkedList<IVolume>`. Drilling into a file pushes a new volume; going up pops it. The combined `CurrentPathComponents` spans all nested volumes |
| `EntryVolumeProvider` | Adapter that presents a child `IVolume` as if it starts at the path of the opening `IEntry`. Used when pushing volumes into `NestedVolume` |
| `DiskImageVolume` | **Stub** — all methods throw `NotImplementedException`. Intended for raw disk images (MBR/GPT/RDB partition tables) |
| `NullVolume` | Null object — returns an empty entry list, safe fallback |

### Navigation Model

Each UI pane has a `NavigationContext` that owns a `NestedVolume`. Browsing into a directory calls `ChangeDirectory` on the current volume. Browsing into a file that represents a mountable container (e.g., `.iso`) should:
1. For local files, open the selected ISO by its path; `IEntry` currently provides metadata only, not a stream.
2. Construct an `Iso9660Volume` over the open file stream.
3. Wrap it in an `EntryVolumeProvider` and call `nestedVolume.PushVolume(entryVolumeProvider)`.

Going up when at the root of a nested volume calls `PopVolume()`.

### Path Conventions

- `Paths.Root` = `"/"`
- `Paths.Parent` = `".."`
- `CurrentPathComponents` returns path segments as a `string[]`, not a joined string.
- The empty array represents the root of a volume.

---

## Current Status

**Working:**
- The Terminal.Gui v2 TUI and Avalonia 12 GUI dual-pane browsers, with per-pane navigation and current-directory startup.
- Local drive/directory listing, parent navigation, and read-only browsing of local ISO images as nested volumes.
- The volume abstraction layer and implementations: `LocalVolume`, `Iso9660Volume`, `NestedVolume`, `EntryVolumeProvider`, and `NullVolume`.
- All unit tests in `Hst.Commander.Core.Tests`.

**Not implemented:**
- Copy, move, delete, and other file-management operations.
- Opening ISO images located inside another ISO; the current `IEntry` contract does not expose file contents.
- `DiskImageVolume` remains an intentional stub.

---

## Coding Conventions

### Language and Style
- C# / .NET 10 (`net10.0`). Enable nullable reference types (`<Nullable>enable</Nullable>`).
- Use primary constructor syntax where appropriate: `public class Foo(IVolume volume)`.
- Use `record` types for simple data carriers (e.g., `Entry`, `Location`).
- `async`/`await` and `IAsyncEnumerable<T>` for I/O. Use `System.Linq.Async` (`ToListAsync()`, etc.).
- Prefer expression bodies for trivial members.

### Naming
- Classes: `PascalCase`. Interfaces: `I` prefix (`IVolume`, `IEntry`).
- Test classes: `Given<Context>` (e.g., `GivenLocalVolume`, `GivenNestedVolumeWithTestVolume`).
- Test methods: `When_<action>_Then_<expectation>` (e.g., `When_ListEntries_Then_EntriesAreReturned`).

### Namespaces
- Abstractions: `Hst.Commander.Extensions.Volume.Abstractions`
- Core: `Hst.Commander.Core` (`NavigationContext`, `Paths`, `SettingsPaths`, `SizeFormatter`), `Hst.Commander.Core.Volumes` (volumes)
- Tui: `Hst.Commander.Tui`
- Gui: `Hst.Commander.Gui`
- Tests: `Hst.Commander.Core.Tests`

### Error Handling
- Throw `ArgumentNullException.ThrowIfNull(arg)` for null guard checks.
- Surface errors to the TUI via `MessageBox.ErrorQuery("Error", e.Message, "OK")`.
- Surface errors to the GUI via `PaneView.NavigationFailed`, which `MainWindow` shows with `ErrorDialog`.

---

## How to Add a New Volume Type

1. **Create the class** in `Hst.Commander.Core/Volumes/`, implementing `IVolume`.
2. **Implement all five members**: `Name`, `CurrentPathComponents`, `ListEntries`, `ChangeDirectory`, `OpenEntry`.
3. **Handle `Paths.Root` and `Paths.Parent`** in `ChangeDirectory` — both are case-insensitive string comparisons.
4. **Add unit tests** in `Hst.Commander.Core.Tests/` using the `Given<YourVolume>` naming pattern.
5. **Wire up detection** in `NavigationContext` (`Hst.Commander.Core`): when `OpenEntry` returns an entry that matches your format, construct the volume and call `nestedVolume.PushVolume(new EntryVolumeProvider(entry, yourVolume))`.

Example test pattern (from `GivenNestedVolumeWithTestVolume.cs`):
```csharp
[Fact]
public async Task When_VolumeIsPushed_Then_NestedVolumeListsEntriesFromNewVolume()
{
    // arrange
    var testVolume = new TestVolume();
    testVolume.AddEntry([], new TestEntry("dir1", EntryType.Container));

    // act
    nestedVolume.PushVolume(testVolume);
    var entries = await nestedVolume.ListEntries().ToListAsync();

    // assert
    Assert.Single(entries);
}
```

### Test Helpers

- `TestVolume` (`Hst.Commander.Core.Tests/TestVolume.cs`): in-memory `IVolume` backed by a `Dictionary<string[], List<TestEntry>>`.
- `TestEntry` (`Hst.Commander.Core.Tests/TestEntry.cs`): in-memory `IEntry` with optional `byte[]? Data`.
- Use `AddEntry(pathComponents, entry)` to build the in-memory tree.

---

## Adding TUI Features

The TUI uses **Terminal.Gui v2 pre-alpha** (`2.0.0-prealpha.1895`). The API surface differs from v1 — consult the installed package, not online v1 docs.

Key TUI patterns already in use:
- `EntryTableView` (a `TableView` with `DataTableSource`) for the two panes. It draws column dividers below the last entry and a scroll thumb on the right border.
- `PathPanelView` draws a bordered panel above each pane with two lines: the current path on the first line, and `NavigationContext.MarkedSummary` (e.g. `12K in 3 selected`, hidden when no entries are marked) left-aligned with `NavigationContext.DiskSpace` (free/total, e.g. `87G/256G`) right-aligned on the second line; the path is highlighted in the focused pane and trimmed from the left with `...`, the summary is truncated and the disk space is dropped when the panel is too narrow. Below the panel it draws the pane's top border, joining the column dividers of the table.
- Each pane is a private `Pane` class holding its `NavigationContext` (from Core), views, entries and `DataTable`.
- The name column expands to the remaining pane width; size and modified-date columns use fixed widths when space permits and shrink on narrow panes so all three columns and separators stay within the pane. File sizes use the same humanized format as the disk space (e.g. `87G`) and directories show `<DIR>`; long names are shortened with `~`, retaining up to the final three filename stem characters and the extension when it fits.
- `NavigationContext.DiskSpace` returns the free/total space of the local drive (longest matching mount point) as humanized integers via `SizeFormatter.FormatHumanized` (e.g. `87G/256G`, 1024-based); it is null in the drive list and inside images.
- `".."` is inserted as row 0 by `NavigationContext.ListEntriesAsync` when `HasParent` is true (shared by both front-ends).
- The size column shows `<DIR>` for directories and humanized sizes for files (e.g. `512B`, `2K`, `87G`, same as the disk space), both via `SizeFormatter.FormatEntrySize` (shared by both front-ends).
- After navigation both front-ends pick the selected entry with `NavigationContext.GetSelectedIndex`, passing the path shown before the refresh: navigating into a directory (or ISO image) selects the first entry; going to the parent selects the directory (or ISO image/drive) that was left, held in `NavigationContext.PreviousEntryName`, e.g. going up from `c:	emp\dir1` selects `dir1`; when the location is unchanged (e.g. opening a plain file) the selection is kept.
- Entries are selected (marked) with Insert or Space, which toggles the mark of the entry under the cursor and moves the cursor down. Marked entries are drawn in yellow (also under the cursor). The marks live in `NavigationContext` (`ToggleMark`, `IsMarked`, `MarkedEntries`, `MarkedSize`, `MarkedSummary`; shared by both front-ends): `..` can't be marked, marks are cleared when the location changes and `ListEntriesAsync` drops marks of entries no longer listed. In code "marked" is used to avoid confusion with the selected row (cursor).
- Key bindings are attached via `tableView.KeyDown += async (_, key) => { ... }`.
- Up/Down move the selected row by one entry per key event; holding a key uses the OS/terminal's repeat rate. PageUp/PageDown move by one visible page and Home/End jump to the first/last entry.
- The pane backgrounds use a Midnight Commander-inspired blue palette, with directories in white, files in gray, and the current row in black on gray in both panes.
- Directory names have no slash prefix.
- Window-level key bindings use `this.KeyUp`.
- Dialogs: `new Dialog { Buttons = [okButton] }`, run with `Application.Run(dialog)`.

## Adding GUI Features

The GUI uses **Avalonia 12** with the Fluent theme and plain code-behind (no MVVM framework).

Key GUI patterns already in use:
- `PaneView` (UserControl) holds a `NavigationContext`, a bordered panel above the pane with the path badge on the first line and, on the second line, the marked-entries summary (`NavigationContext.MarkedSummary`, hidden when null, i.e. no entries are marked) left-aligned and the disk space (`NavigationContext.DiskSpace`, hidden when null) right-aligned, and a bordered pane with a column header row and a `ListBox` of `EntryItem` rows. The name column fills the remaining width; size and modified columns have fixed widths and long names are trimmed with an ellipsis.
- Column dividers are drawn by a non-hit-testable grid behind the list, so they continue below the last entry.
- The left pane has focus on startup. `PaneView.FocusEntries` focuses the selected item's container (selecting the first entry if none is, and realizing it with `UpdateLayout` if needed); the `ListBox` itself is not focusable, and arrow keys only move the selection while an item has focus.
- Up/Down use the `ListBox` built-in navigation. PageUp/PageDown (one visible page) and Home/End are handled in `PaneView`, because the list's `ScrollViewer` would otherwise consume them and only scroll the view without moving the selection. Enter (or double-click) opens the selected entry and Backspace goes to the parent; both are handled with a tunnelling `KeyDown` handler on the list.
- Insert or Space toggles the mark of the current entry (via `NavigationContext.ToggleMark`) and moves down. `EntryItem.IsMarked` notifies changes and adds the `marked` class to the row, styled with `MarkedEntryForeground` (yellow) after the current-row style so marked entries stay yellow under the cursor.
- Tab is handled at window level (tunnelling) and always switches focus between the two panes.
- Three themes, selected in File > Settings: **Midnight Commander** (default), **Light** and **Dark**.
  - Midnight Commander uses the same palette as the TUI: blue panes, directories in white, files in gray, current row in black on gray in the focused pane (cyan in the unfocused pane), cyan menu bar and the active pane's path highlighted. It is a custom `ThemeVariant` (`ThemeManager.MidnightCommanderVariant`, inheriting Dark) plus `MidnightCommanderStyles`, which `ThemeManager` adds to `Application.Styles` only for this theme.
  - Light and Dark use the normal Fluent control styling (including list selection) in light/dark colors.
  - Pane colors are per-theme brushes in `App.axaml` `ThemeDictionaries` (`PaneBackground`, `PaneBorder`, `HeaderForeground`, `ContainerForeground`, `ItemForeground`, `PathForeground`, `ActivePathBackground`, `ActivePathForeground`, `SelectedEntryForeground`, `MarkedEntryForeground`), referenced with `DynamicResource` so theme changes apply live.
- Settings are stored in `~/.hst-commander/gui-settings.json` (`SettingsPaths.Directory` in Core, `GuiSettings.FilePath`). A missing or invalid file falls back to defaults. The settings dialog previews theme changes live, saves on OK and reverts on Cancel.
- Errors are shown with `ErrorDialog` via `ShowDialog(owner)`.
- A top `Menu` docked above the panes has a File menu: Settings opens the modal `SettingsDialog` and Quit closes the main window (exiting the application).

---

## Build and Test Commands

```shell
# Build the solution
dotnet build src/Hst.Commander.sln

# Run the GUI app
dotnet run --project src/Hst.Commander.Gui/Hst.Commander.Gui.csproj

# Run the TUI app
dotnet run --project src/Hst.Commander.Tui/Hst.Commander.Tui.csproj

# Run unit tests
dotnet test src/Hst.Commander.Core.Tests/Hst.Commander.Core.Tests.csproj

# Run tests with verbose output
dotnet test src/Hst.Commander.Core.Tests/Hst.Commander.Core.Tests.csproj --logger "console;verbosity=normal"
```

> **Note:** Build output may land in `src/net10.0/` instead of each project's `bin/` when a `Directory.Build.props` outside the repository (e.g. in a parent folder) sets `OutputPath`. That folder is ignored by `.gitignore`; do not commit build output.

---

## Dependencies

| Package | Version | Used in |
|---|---|---|
| `LTRData.DiscUtils` | 1.0.54 | `Hst.Commander.Core` — raw disk image support |
| `LTRData.DiscUtils.Iso9660` | 1.0.54 | `Hst.Commander.Core` — ISO 9660 reading |
| `System.Linq.Async` | 6.0.1 | `Hst.Commander.Core` — async LINQ |
| `Terminal.Gui` | 2.0.0-prealpha.1895 | `Hst.Commander.Tui` — TUI framework |
| `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent` | 12.1.4 | `Hst.Commander.Gui` — desktop GUI framework |
| `xunit` | 2.5.3 | `Hst.Commander.Core.Tests` |
| `coverlet.collector` | 6.0.0 | `Hst.Commander.Core.Tests` — coverage |
