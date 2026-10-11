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
1. Call `OpenEntry` to get the `IEntry` (and its underlying stream).
2. Construct the appropriate `IVolume` (e.g., `Iso9660Volume`).
3. Wrap it in an `EntryVolumeProvider`.
4. Call `nestedVolume.PushVolume(entryVolumeProvider)`.

Going up when at the root of a nested volume calls `PopVolume()`.

### Path Conventions

- `Paths.Root` = `"/"`
- `Paths.Parent` = `".."`
- `CurrentPathComponents` returns path segments as a `string[]`, not a joined string.
- The empty array represents the root of a volume.

---

## Current WIP State (as of June 2026)

**Do not break the following areas — they are actively being refactored:**

| File | Issue |
|---|---|
| `Hst.Commander.Console/MainWindow.cs:52` | Incomplete statement `leftContext.` — compilation error |
| `Hst.Commander.Console/MainWindow.cs:55` | `new NavigationContext()` called without required `volume` argument |
| `Hst.Commander.Console/MainWindow.cs:431` | `Parent()` method references undefined locals (`provider`, `prevLocation`) |
| `Hst.Commander.Console/NavigationContext.cs:17` | `GetLocation()` throws `NotImplementedException` |
| `Hst.Commander.Console/NavigationContext.cs:24` | Incomplete statement `_nestedVolume.` — compilation error; dead code below it |
| `Hst.Commander.Core/Volumes/DiskImageVolume.cs` | All methods throw `NotImplementedException` — intentional stub |

**What is working:**
- The full volume abstraction layer (`IVolume`, `IEntry`, `EntryType`, `Paths`).
- `LocalVolume`, `Iso9660Volume`, `NestedVolume`, `EntryVolumeProvider`, `NullVolume`.
- All unit tests in `Hst.Commander.Core.Tests`.

**What is broken (do not ship as-is):**
- `MainWindow.cs` and `NavigationContext.cs` do not compile.
- The `Hst.Commander.Console` project as a whole does not build until these are fixed.

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
- Entry names are prefixed: `"/"` = container (directory/volume), `" "` = item (file).
- `".."` is inserted as row 0 when `context.HasParent` is true.
- Key bindings are attached via `tableView.KeyDown += async (_, key) => { ... }`.
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
