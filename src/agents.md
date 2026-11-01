# Hst Commander - Agent Specification

## Project Overview

Hst Commander is a dual-pane file manager for .NET 10, inspired by Norton Commander and Midnight Commander. The distinguishing feature is a **pluggable volume abstraction** that lets any container — a local directory, an ISO 9660 image, a disk image, or a future plugin type — be browsed transparently in the same dual-pane UI.

There are two front-ends with the same functionality: a terminal UI (TUI) built with Terminal.Gui v2 and a desktop GUI built with Avalonia 12. Both share `NavigationContext` from Core. The earlier ASP.NET Core + React web prototype has been removed and is no longer relevant.

**Front-end parity:** user-facing features are implemented in both front-ends. Put navigation and volume logic in `Hst.Commander.Core` (e.g. `NavigationContext`) so the TUI and GUI only contain presentation and key handling. Exceptions where a terminal can't do what the GUI does: the TUI has no entry icons or navigation modes and doesn't view images (they are viewed as binary files).

---

## Feature Docs

The behavior of each feature is described in `docs/` at the repository root. Read the doc for the area you're changing before changing it, and update it afterwards:

| Doc | Covers |
|---|---|
| [`docs/plugin-api.md`](../docs/plugin-api.md) | The Abstractions interfaces in detail, plugin loading and load contexts, `IPluginHost` |
| [`docs/volumes.md`](../docs/volumes.md) | Volume implementations, images, writable images, going to the root, changing location, volume provider keys, Enter on files |
| [`docs/file-operations.md`](../docs/file-operations.md) | Copying, UAE metadata, moving/renaming, deleting, making directories, creating volumes, selecting groups, properties |
| [`docs/viewer.md`](../docs/viewer.md) | F3 viewer: text/hex/image views, search, selection and copying |
| [`docs/editor.md`](../docs/editor.md) | F4 editing: built-in editor, search and replace, external editors, editor settings |
| [`docs/icons-and-navigation-modes.md`](../docs/icons-and-navigation-modes.md) | Icon providers, GUI entry icons, navigation modes (list, thumbnails, Workbench, def icons) |
| [`docs/tui.md`](../docs/tui.md) | TUI files, key handling, dialogs, themes, terminal programs |
| [`docs/gui.md`](../docs/gui.md) | GUI files, key handling, dialogs, themes, icons |
| [`docs/application.md`](../docs/application.md) | Versioning, release workflows, About, licenses and third-party notices, quitting |
| [`docs/testing.md`](../docs/testing.md) | Test helpers |
| [`docs/status.md`](../docs/status.md) | What works and what isn't implemented |

The code is the source of truth for constants, key lists and colors; the docs describe behavior and the reasons for it.

---

## Repository Layout

```
.github/workflows/              build-and-test.yml (every push, Windows/macOS/Linux), build-and-release.yml (manual, packages and drafts a prerelease)
docs/                           Feature docs (see above)
src/
  Hst.Commander.sln             Solution file (11 projects)
  Directory.Build.props         Shared product name, version, authors and copyright
  Directory.Build.targets       Replaces the last version part with the git commit count
  packaging/                    Release packaging scripts (Linux AppImage/.deb, macOS .dmg, Windows Inno Setup)
  icon/                         App icon sources, rendered sizes and make scripts
  Hst.Commander.Abstractions/   Plugin API: IVolume, IEntry, IVolumeProvider, IFileVolumeProvider, IPlugin, IPluginHost, IWriteCache, ... and Paths (no external deps)
  Hst.Commander.Plugins.Zip/    Zip archives (default plugin)
  Hst.Commander.Plugins.LhaLzx/ Lha and lzx archives (default plugin)
  Hst.Commander.Plugins.Disk/   MBR/GPT disk images with FAT/NTFS partitions, hybrid MBR/RDB disks (default plugin)
  Hst.Commander.Plugins.Amiga/  Writable adf and RDB images, .info icons, Workbench navigation mode, Amiga images (default plugin)
  Hst.Commander.Plugins.PhysicalDisk/ Internal and external physical disks on Windows, Linux and macOS (default plugin)
  Hst.Commander.Plugins.Smb/    Windows file shares and other SMB 2/3 servers (external plugin, released separately: SMBLibrary is LGPL)
  Hst.Commander.Core/           Volume implementations, NavigationContext, copy/move/delete, UAE metadata, viewer, plugin loading
  Hst.Commander.Tui/            Terminal.Gui TUI application
  Hst.Commander.Gui/            Avalonia 12 desktop GUI application
  Hst.Commander.Core.Tests/     xUnit unit tests
```

---

## Architecture

### Layer Diagram

```
[IVolumeProvider / IFileVolumeProvider / IPlugin / IVolume / IEntry / Paths]  <- Hst.Commander.Abstractions
        |                                    \
        |                          [ZipPlugin, ZipFileVolumeProvider, ZipVolume]  <- Hst.Commander.Plugins.Zip
        |                          [LhaLzxPlugin, Lha/LzxFileVolumeProvider,
        |                           LhaVolume, LzxVolume]  <- Hst.Commander.Plugins.LhaLzx
        |                          [DiskPlugin, DiskFileVolumeProvider,
        |                           PartitionTableVolume, HybridDiskVolume,
        |                           DiscFileSystemVolume]  <- Hst.Commander.Plugins.Disk
        |                          [AmigaPlugin, Adf/RdbFileVolumeProvider,
        |                           AmigaFileSystemVolume, RdbVolume]  <- Hst.Commander.Plugins.Amiga
        |                          [PhysicalDiskPlugin, PhysicalDiskVolumeProvider,
        |                           PhysicalDiskVolume, DiskStream,
        |                           CachedDiskStream]  <- Hst.Commander.Plugins.PhysicalDisk
        |                          [SmbPlugin, SmbVolumeProvider,
        |                           SmbServerVolumeProvider, SmbVolume,
        |                           SmbFileStream]  <- Hst.Commander.Plugins.Smb (external)
        |                                    /    (default plugins, Plugins.All)
[Volume Implementations]  <- Hst.Commander.Core
  LocalVolumeProvider (VolumeProviders.All),
  Iso9660FileVolumeProvider (VolumeProviders.Files),
  LocalVolume, Iso9660Volume, NestedVolume,
  EntryVolumeProvider, NullVolume
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

All are in `Hst.Commander.Abstractions`; see [`docs/plugin-api.md`](../docs/plugin-api.md) for the full contracts.

| Interface | Purpose |
|---|---|
| `IVolume` | A browsable container with a current directory: list, change directory, open/read/write, create container, delete, rename, set dates, Amiga metadata and attributes. Write members throw `NotSupportedException` when read-only |
| `IEntry` | An entry: `Name`, `Type` (container/item), `Size`, `ModifiedDate`, plus optional `DisplayName`, `Details`, `IsVolume`, `AmigaMetadata`, `Attributes` |
| `IVolumeProvider` | What a pane can show (Alt+F1/Alt+F2); `CreateVolume` returns a new, independent volume on each call; optional `PathRoot` shows paths like `\\server\share\dir` |
| `ILocationVolumeProvider` | A volume provider asking for a location when chosen (e.g. an SMB path); `Connect` returns a `VolumeConnection` or throws `CredentialsRequiredException` to ask for a user name and password |
| `IFileVolumeProvider` | Opens files as volumes (images), detected by magic signature with `IsMatch`; `CanWrite` for writable images |
| `IVolumeCreator` | Creates a new volume file (F9) from input fields |
| `IIconProvider` | Reads icons from icon files next to entries (e.g. `.info`) |
| `INavigationMode` | Lays out a directory as icons at positions in the GUI (e.g. Workbench) |
| `IImageProvider` | Reads the images of a file for the viewer (F3) |
| `IPlugin` | Returns a plugin's providers, creators and modes and its `ThirdPartyNotices` (shown in About > Licenses); `Initialize(IPluginHost)` |
| `IPluginHost` | Lets plugins work together without sharing types (e.g. the Disk plugin opens a PiStorm partition with the Amiga plugin) |
| `IWriteCache` | A writable image stream caching writes (e.g. physical disks), committed by Hst Commander when the file system is consistent (`Flush` doesn't write it) |

### Core Rules

These apply across features and aren't obvious from a single file:

- **Plugins reference only `Hst.Commander.Abstractions`.** Each loads in its own `AssemblyLoadContext` sharing Abstractions with the host, so plugins can't use each other's types and work together through `IPluginHost`. Built-in plugins are listed in `Plugins.BuiltIn`, and the order matters (Disk before Amiga, so hybrid disks open as hybrid disks).
- **Each `NavigationContext` owns a `NestedVolume`.** Opening an image pushes a volume (wrapped in `EntryVolumeProvider`); going up from its root pops and disposes it.
- **Background work never uses the pane's volume.** Copy, move, delete, viewing, editing, counting contents and reading icons inside volume entries open an independent volume with `NavigationContext.OpenLocationAsync`, because volumes aren't thread safe.
- **Writable images are shared.** A file of the root volume with an id (`IVolume.GetWritableImageId`, e.g. a local file or a physical disk) opened by a provider with `CanWrite` is opened once (`SharedImages`) and shared by both panes and file operations through `SharedImageVolume` views serialized by a semaphore; two volumes writing one file would damage its file system. The lock is only held on the thread pool, never by work waiting for a UI thread. A stream caching writes (`IWriteCache`, e.g. `CachedDiskStream` of physical disks) is committed (`IWriteCache.Commit`, not `Flush`) only between operations, when the file system is consistent. Images inside other images are read-only.
- **Every third-party component the apps include has a notice.** `THIRD-PARTY-NOTICES.txt` lists each package with its license text, made by `src/notices/make-notices.py` (add a section there for a new component, and rerun it after changing packages); `GivenLicenses` fails for a new package that isn't listed. LGPL/GPL libraries go in external plugins (e.g. SMB) with their notices in `IPlugin.ThirdPartyNotices`, never in the apps. See [Licenses](../docs/application.md#licenses).
- **File operations only use `IVolume`.** `VolumeCopy`, `VolumeMove` and `VolumeDelete` work between any two volumes, so a new volume type gets copy, move and delete for free.
- **Amiga metadata handling depends on `SupportsAmigaMetadata` of both sides** (copied as is, written as UAE metadata, read from UAE metadata, or both); see [`docs/file-operations.md`](../docs/file-operations.md#uae-metadata).
- **Marked vs. selected:** in code, "marked" means entries picked with Insert/Space and "selected" means the row under the cursor.

### Path Conventions

- `Paths.Root` = `"/"`
- `Paths.Parent` = `".."`
- `CurrentPathComponents` returns path segments as a `string[]`, not a joined string.
- The empty array represents the root of a volume.

---

## Not Implemented

- Other file-management operations (e.g. creating a new file with Shift+F4); changing the viewer's encoding and going to an offset; changing the editor's encoding and line breaks.
- SMB 1, SMB ports other than 445, browsing the network for servers and saving SMB credentials.
- Writing to images other than Amiga adf and RDB images; ISO images and zip, lha and lzx archives are read-only, as are Amiga images inside other images.
- Changing RDB, MBR and GPT partitions, other Amiga file systems (e.g. SFS), other PC file systems (exFAT, ext, ...) and vhdx/vmdk images.
- Unloading or reloading plugins while running, and a list of loaded plugins in the UI.

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
- Abstractions: `Hst.Commander.Abstractions` (interfaces, `Paths`)
- Zip plugin: `Hst.Commander.Plugins.Zip`
- Lha and lzx plugin: `Hst.Commander.Plugins.LhaLzx`
- Disk plugin: `Hst.Commander.Plugins.Disk`
- Amiga plugin: `Hst.Commander.Plugins.Amiga`
- Physical disk plugin: `Hst.Commander.Plugins.PhysicalDisk`
- SMB plugin: `Hst.Commander.Plugins.Smb`
- Core: `Hst.Commander.Core` (`AppInfo`, `EditorDocument`, `EditorKind`, `EditorSearch`, `EditorSession`, `EmbeddedIconReader`, `EnterFileAction`, `ExternalEditFile`, `ExternalEditor`, `Elevation`, `EntryProperties`, `FileSystemDetails`, `IconImage`, `ListingProgress`, `NavigationContext`, `NameFilter`, `PluginHost`, `PluginLoader`, `Plugins`, `IProgressIndicator`, `SelectGroupOptions`, `FileStarter`, `ImagePixels`, `SettingsPaths`, `SizeFormatter`, `StandardImageProvider`, `StartupOptions`, `PaneStartup`, `UaeMetadataFormat`, `ViewerDocument`, `ViewerSession`, `WindowsIconImageProvider`, `VolumeConnector`, `CredentialsRequest`, `Licenses`, `LicenseNotice`, `VolumeCreation`, `VolumeCreatorInput`), `Hst.Commander.Core.Volumes` (volumes, copying, UAE metadata)
- Tui: `Hst.Commander.Tui`
- Gui: `Hst.Commander.Gui`, `Hst.Commander.Gui.Icons` (entry icons)
- Tests: `Hst.Commander.Core.Tests`

### Error Handling
- Throw `ArgumentNullException.ThrowIfNull(arg)` for null guard checks.
- Surface errors to the TUI via `MessageBox.ErrorQuery("Error", e.Message, "OK")`.
- Surface errors to the GUI via `PaneView.NavigationFailed`, which `MainWindow` shows with `ErrorDialog`.

---

## How to Add a New Volume Type

1. **Create the class** in `Hst.Commander.Core/Volumes/` or a plugin project, implementing `IVolume` (and `IDisposable` when it holds resources).
2. **Implement all members**: `Name`, `CurrentPathComponents`, `ListEntries`, `ChangeDirectory`, `OpenEntry`, `OpenRead`, `OpenWrite`, `CreateContainer`, `Delete`, `Rename`, `SetModifiedDate` (throw `NotSupportedException` from the write members when the volume is read-only). Copying and moving to and from the volume then works through `VolumeCopy.CopyToAsync` and `VolumeMove.MoveToAsync`.
3. **Handle `Paths.Root` and `Paths.Parent`** in `ChangeDirectory` — both are case-insensitive string comparisons.
4. **Add unit tests** in `Hst.Commander.Core.Tests/` using the `Given<YourVolume>` naming pattern.
5. **To make it selectable with Alt+F1/Alt+F2**, implement `IVolumeProvider` (see `LocalVolumeProvider`) and add it to `VolumeProviders.All`. To ask for a location (e.g. a server path) and credentials when chosen, implement `ILocationVolumeProvider` instead (see `SmbVolumeProvider` and [`docs/plugin-api.md`](../docs/plugin-api.md#ilocationvolumeprovider)).
6. **Or open files as the volume** (images): implement `IFileVolumeProvider` with `IsMatch` checking the file's magic signature (see `Iso9660FileVolumeProvider` and `ZipFileVolumeProvider`) and add it to `VolumeProviders.Files`, or return it from `IPlugin.FileVolumeProviders` of a plugin. `NavigationContext` then opens matching files as images (see [Images](../docs/volumes.md#images)). Return true from `CanWrite` when the volume can write to the file (see [Writable images](../docs/volumes.md#writable-images) and `AdfFileVolumeProvider`).

7. **To create new volumes as files with F9**, implement `IVolumeCreator` (see `AdfVolumeCreator`) with its input fields and return it from `IPlugin.VolumeCreators` with `IPlugin.CanCreateVolumes` true (see [Creating volumes](../docs/file-operations.md#creating-volumes)).

8. **To show icons from icon files next to entries** (like Amiga `.info` files), implement `IIconProvider` (see `AmigaIconProvider`) and return it from `IPlugin.IconProviders` (see [Entry icons](../docs/icons-and-navigation-modes.md#entry-icons)).

9. **To show directories as icons at positions in the GUI** (like the Amiga Workbench), implement `INavigationMode` (see `WorkbenchNavigationMode`) and return it from `IPlugin.NavigationModes` (see [Navigation modes](../docs/icons-and-navigation-modes.md#navigation-modes)).

10. **To view images of a file format with F3** (like Amiga icons and IFF ILBM pictures), implement `IImageProvider` (see `AmigaImageProvider`) and return it from `IPlugin.ImageProviders` (see [Viewer](../docs/viewer.md#images)).

Plugins are separate projects referencing only `Hst.Commander.Abstractions`, e.g. `Hst.Commander.Plugins.Zip`: implement `IPlugin` returning the plugin's volume providers, file volume providers, volume creators and icon providers, and either add it to `Plugins.BuiltIn` (`Hst.Commander.Core`, which references the plugins included by default) or build it and copy its output to `~/.hst-commander/plugins/<Name>/` or `plugins/<Name>/` next to the executable, with `<Name>.dll` as the plugin assembly (reference `Hst.Commander.Abstractions` with `<Private>false</Private>` and `<ExcludeAssets>runtime</ExcludeAssets>` so it isn't copied; a copy is ignored anyway).

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

See [`docs/testing.md`](../docs/testing.md) for the test helpers (`TestVolume`, `TestVolumeProvider`, `ZipTestData`, `AmigaTestData`, `DiskTestData`, ...).

---

## Front-End Patterns

The details for each front-end are in [`docs/tui.md`](../docs/tui.md) and [`docs/gui.md`](../docs/gui.md). The essentials:

- **TUI:** Terminal.Gui **v2 pre-alpha** (`2.0.0-prealpha.1895`). Its API differs from v1, so consult the installed package, not online v1 docs. Each pane is a private `Pane` class holding its `NavigationContext`; pane keys are handled in `tableView.KeyDown`, window keys in `this.KeyUp`. Dialogs are `new Dialog { Buttons = [...] }` run with `Application.Run(dialog)`; background work reports back with `Application.Invoke`.
- **GUI:** Avalonia 12 with the Fluent theme and plain code-behind (no MVVM). `PaneView` holds a `NavigationContext`; most keys are handled at window level with tunnelling `KeyDown` handlers. Dialogs are shown with `ShowDialog(owner)`; background work reports through a `Progress<T>` created on the UI thread. Theme brushes live in `App.axaml` `ThemeDictionaries` and are referenced with `DynamicResource`.
- Both front-ends have the same File menu, function key bar (F3-F10), themes (Midnight Commander, Light, Dark) and settings dialog with tabs (Appearance, Panes, Copying, Editor; stored separately in `~/.hst-commander/tui-settings.json` and `gui-settings.json`).

## Git Workflow

- Always create a new branch from `main` before making changes (e.g. `feature/<name>`, `fix/<name>`, `docs/<name>`); never commit directly to `main`. Changes reach `main` through a pull request.

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

> **Note:** `src/Directory.Build.props` stops MSBuild from importing a `Directory.Build.props` from a parent folder, so build output lands in each project's `bin/`. A leftover `src/net10.0/` folder is old build output, ignored by `.gitignore`; do not commit build output. The build prints each project's version (`<project> version: 0.1.29`) and needs `git` on the path for the commit count (otherwise the version from `Directory.Build.props` is used).

---

## Dependencies

| Package | Version | Used in |
|---|---|---|
| `LTRData.DiscUtils.Iso9660` | 1.0.54 | `Hst.Commander.Core` — ISO 9660 reading |
| `SkiaSharp`, `SkiaSharp.NativeAssets.Linux` | 3.119.4 | `Hst.Commander.Core` — decoding PNG, JPEG, GIF, BMP and WebP images viewed with F3 in the GUI (the version Avalonia uses; part of the TUI too, as it references Core) |
| `System.Linq.Async` | 6.0.1 | `Hst.Commander.Core` — async LINQ |
| `Hst.Compression` | 0.4.96 | `Hst.Commander.Plugins.LhaLzx` — lha and lzx archive reading |
| `Hst.Amiga` | 0.6.245 | `Hst.Commander.Plugins.Amiga` — Amiga rigid disk block, Fast File System and PFS3 reading and writing, .info icons; `Hst.Commander.Core` — UAE metadata (UAEFSDB and metafiles) |
| `LTRData.DiscUtils.Vhd` | 1.0.54 | `Hst.Commander.Plugins.Amiga`, `Hst.Commander.Plugins.Disk` — vhd images |
| `LTRData.DiscUtils.Core`, `.Fat`, `.Ntfs` | 1.0.54 | `Hst.Commander.Plugins.Disk` — MBR/GPT partition tables, FAT and NTFS reading and writing |
| `SMBLibrary` | 1.5.8.1 | `Hst.Commander.Plugins.Smb` (external plugin, not included with the apps) — SMB 2/3 client (LGPL-3.0-or-later); its version and source commit are also in the plugin's `THIRD-PARTY-NOTICES.txt` and `build-and-release.yml` |
| `Terminal.Gui` | 2.0.0-prealpha.1895 | `Hst.Commander.Tui` — TUI framework |
| `Microsoft.Build.Tasks.Git` | 10.0.402 | `Hst.Commander.Tui` — build only (`PrivateAssets="all"`), overrides the vulnerable 8.0.0 (GHSA-23fw-v26w-5fgq) pulled in by Terminal.Gui's `Microsoft.SourceLink.GitHub` 8.0.0 dependency |
| `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent` | 12.1.4 | `Hst.Commander.Gui` — desktop GUI framework |
| `xunit` | 2.5.3 | `Hst.Commander.Core.Tests` |
| `coverlet.collector` | 6.0.0 | `Hst.Commander.Core.Tests` — coverage |
