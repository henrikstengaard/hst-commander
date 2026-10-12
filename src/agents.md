# HST Commander - Agent Specification

## Project Overview

HST Commander is a dual-pane file manager for .NET 9, inspired by Norton Commander and Midnight Commander. The distinguishing feature is a **pluggable volume abstraction** that lets any container — a local directory, an ISO 9660 image, a disk image, or a future plugin type — be browsed transparently in the same dual-pane UI.

The active front-end is a terminal UI (TUI) built with Terminal.Gui v2. A dormant ASP.NET Core + React web front-end exists but is not connected to the core model and should be ignored for new work.

---

## Repository Layout

```
src/
  Hst.Commander.sln                              Solution file (5 projects)
  Hst.Commander.Extensions.Volume.Abstractions/  IVolume / IEntry interfaces (no external deps)
  Hst.Commander.Core/                            Volume implementations + shared utilities
  Hst.Commander.Console/                         Terminal.Gui TUI application (active)
  Hst.Commander.Core.Tests/                      xUnit unit tests
  Hst.Commander.Gui/                             ASP.NET Core + React prototype (dormant, ignore)
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
[NavigationContext]  <- Hst.Commander.Console
  Wraps NestedVolume, owns pane-level state
        |
[MainWindow (TUI)]  <- Hst.Commander.Console
  Two TableView panes, key bindings, Terminal.Gui
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
- The .NET 9 Terminal.Gui v2 dual-pane browser, with per-pane navigation and current-directory startup.
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
- C# 12 / .NET 9. Enable nullable reference types (`<Nullable>enable</Nullable>`).
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
- Core: `Hst.Commander.Core.Volumes`
- Console: `Hst.Commander.Console`
- Tests: `Hst.Commander.Core.Tests`

### Error Handling
- Throw `ArgumentNullException.ThrowIfNull(arg)` for null guard checks.
- Surface errors to the TUI via `MessageBox.ErrorQuery("Error", e.Message, "OK")`.

---

## How to Add a New Volume Type

1. **Create the class** in `Hst.Commander.Core/Volumes/`, implementing `IVolume`.
2. **Implement all five members**: `Name`, `CurrentPathComponents`, `ListEntries`, `ChangeDirectory`, `OpenEntry`.
3. **Handle `Paths.Root` and `Paths.Parent`** in `ChangeDirectory` — both are case-insensitive string comparisons.
4. **Add unit tests** in `Hst.Commander.Core.Tests/` using the `Given<YourVolume>` naming pattern.
5. **Wire up detection** in `NavigationContext` or `MainWindow`: when `OpenEntry` returns an entry that matches your format, construct the volume and call `nestedVolume.PushVolume(new EntryVolumeProvider(entry, yourVolume))`.

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
- `TableView` with `DataTableSource` for the two panes.
- The name column expands to the remaining pane width; size and modified-date columns use fixed widths when space permits and shrink on narrow panes so all three columns and separators stay within the pane. File sizes use compact B/KB/MB/GB units; long names are shortened with `~`, retaining up to the final three filename stem characters and the extension when it fits.
- `".."` is inserted as row 0 when `context.HasParent` is true.
- Key bindings are attached via `tableView.KeyDown += async (_, key) => { ... }`.
- Up/Down move the selected row by one entry per key event; holding a key uses the OS/terminal's repeat rate. PageUp/PageDown move by one visible page and Home/End jump to the first/last entry.
- The pane backgrounds use a Midnight Commander-inspired blue palette, with directories in white, files in gray, and the current row in black on gray in both panes.
- Directory names have no slash prefix.
- Window-level key bindings use `this.KeyUp`.
- Dialogs: `new Dialog { Buttons = [okButton] }`, run with `Application.Run(dialog)`.

---

## Build and Test Commands

```shell
# Build the solution
dotnet build src/Hst.Commander.sln

# Run the console app
dotnet run --project src/Hst.Commander.Console/Hst.Commander.Console.csproj

# Run unit tests
dotnet test src/Hst.Commander.Core.Tests/Hst.Commander.Core.Tests.csproj

# Run tests with verbose output
dotnet test src/Hst.Commander.Core.Tests/Hst.Commander.Core.Tests.csproj --logger "console;verbosity=normal"
```

> **Note:** The Console project currently does not compile due to WIP issues in `MainWindow.cs` and `NavigationContext.cs` (see WIP section above). Running `dotnet build` on the solution will report errors. The Core and Tests projects build and pass independently.

---

## Dependencies

| Package | Version | Used in |
|---|---|---|
| `LTRData.DiscUtils` | 1.0.54 | `Hst.Commander.Core` — raw disk image support |
| `LTRData.DiscUtils.Iso9660` | 1.0.54 | `Hst.Commander.Core` — ISO 9660 reading |
| `System.Linq.Async` | 6.0.1 | `Hst.Commander.Core` — async LINQ |
| `Terminal.Gui` | 2.0.0-prealpha.1895 | `Hst.Commander.Console` — TUI framework |
| `xunit` | 2.5.3 | `Hst.Commander.Core.Tests` |
| `coverlet.collector` | 6.0.0 | `Hst.Commander.Core.Tests` — coverage |
