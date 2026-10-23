# Hst Commander - Agent Specification

## Project Overview

Hst Commander is a dual-pane file manager for .NET 10, inspired by Norton Commander and Midnight Commander. The distinguishing feature is a **pluggable volume abstraction** that lets any container — a local directory, an ISO 9660 image, a disk image, or a future plugin type — be browsed transparently in the same dual-pane UI.

There are two front-ends with the same functionality: a terminal UI (TUI) built with Terminal.Gui v2 and a desktop GUI built with Avalonia 12. Both share `NavigationContext` from Core. The earlier ASP.NET Core + React web prototype has been removed and is no longer relevant.

**Front-end parity:** user-facing features are implemented in both front-ends. Put navigation and volume logic in `Hst.Commander.Core` (e.g. `NavigationContext`) so the TUI and GUI only contain presentation and key handling.

---

## Repository Layout

```
.github/workflows/
  build-and-test.yml                             Builds and tests the solution on Windows, macOS and Linux on every push
  build-and-release.yml                          Manual: publishes and packages TUI and GUI per runtime (setup/portable zip, dmg, AppImage/deb) and creates a draft prerelease
src/
  Hst.Commander.sln                              Solution file (9 projects)
  Directory.Build.props                          Shared product name, version, authors and copyright for all projects
  Directory.Build.targets                        Replaces the last version part with the git commit count
  packaging/                                     Release packaging used by build-and-release.yml: linux/package.sh (AppImage + .deb), macos/package.sh (.dmg, GUI as .app bundle in a create-dmg window with dmg-background.svg/.png, rendered by make-dmg-background.cmd, showing the drag to Applications), windows/setup.iss (Inno Setup)
  icon/                                          App icon in the style of the Hst Imager icon: hst-commander.svg source, simplified pixel aligned hst-commander-16x16.svg/-32x32.svg for the small sizes, PNGs (1024, 512, 192, 32, 16), .ico (Windows, 16-256), favicon/16x16/32x32 .ico, .icns (macOS) and make-*.cmd/make-icns.sh scripts (ImageMagick, sips/iconutil)
  Hst.Commander.Abstractions/                    IVolumeProvider / IFileVolumeProvider / IVolumeCreator / IIconProvider / IPlugin / IPluginHost / IVolume / IEntry interfaces, VolumeCreatorField/VolumeCreatorValues, IconImage, EntryDetails/EntryProperty, AmigaMetadata and Paths (no external deps)
  Hst.Commander.Plugins.Zip/                     Zip plugin included by default (ZipPlugin, ZipFileVolumeProvider, ZipVolume), references only Abstractions
  Hst.Commander.Plugins.LhaLzx/                  Lha and lzx plugin included by default (LhaLzxPlugin, LhaFileVolumeProvider, LzxFileVolumeProvider, ArchiveVolume, LhaVolume, LzxVolume) using the Hst.Compression package, references only Abstractions
  Hst.Commander.Plugins.Disk/                    Disk plugin included by default (DiskPlugin, DiskFileVolumeProvider, VolumeListVolume, PartitionTableVolume, HybridDiskVolume, DiscFileSystemVolume): MBR and GPT disk images (raw or vhd) with FAT and NTFS partitions via LTRData.DiscUtils, other partitions (e.g. PiStorm RDB) opened through IPluginHost, references only Abstractions
  Hst.Commander.Plugins.Amiga/                   Amiga plugin included by default (AmigaPlugin, AdfFileVolumeProvider, RdbFileVolumeProvider, AdfVolumeCreator, AmigaIconProvider, AmigaFileSystems, AmigaFileSystemVolume, RdbVolume): writable adf and RDB hard disk images (raw or vhd) and .info icons using the Hst.Amiga and LTRData.DiscUtils.Vhd packages, references only Abstractions
  Hst.Commander.Core/                            Volume implementations, volume providers, file volume providers, Plugins + PluginLoader, NavigationContext + shared utilities
  Hst.Commander.Tui/                             Terminal.Gui TUI application
    MainWindow.cs                                Dual-pane window, File menu, function key bar, columns, key bindings, theme colors, F5 copy, F6 move/rename, F7 make directory, F8/Delete delete, F9 create volume, Ctrl+P/Alt+Enter properties
    FunctionKeyBarView.cs                        Function key bar at the bottom (F3 View ... F9 Create), clickable items
    SettingsDialog.cs                            Modal settings dialog (theme selection with live preview, volume provider keys, Enter on file)
    CopyDialog.cs                                Modal copy/move confirm dialog (destination and filter), titled Copy or Move
    CopyProgressDialog.cs                        Modal copy/move progress dialog (2 progress bars, speed, ETA, cancel)
    DeleteProgressDialog.cs                      Modal delete progress dialog (current entry, total progress bar, cancel)
    MakeDirectoryDialog.cs                       Modal make directory dialog (F7): directory name, OK, Cancel
    CreateVolumeDialog.cs                        Modal list of the volume creators (F9), titled Create
    CreateVolumeFieldsDialog.cs                  Modal dialog for the file name and fields of a volume creator: text fields, check boxes, OK, Cancel
    SelectGroupDialog.cs                         Modal select/unselect group dialog (+/-): filter, case sensitive, files only, OK, Cancel
    PropertiesDialog.cs                          Modal properties dialog (Ctrl+P or Alt+Enter): name, sections of property lines, drive usage bar, OK
    VolumeProviderDialog.cs                      Modal list of volume providers for a pane (Alt+F1/Alt+F2), centered over the pane
    LocationDialog.cs                            Modal location dialog (Ctrl+L or double-click path): path, Volumes, OK, Cancel
    TerminalScreen.cs                            Terminal output screen used while the TUI is shut down (like Midnight Commander): runs programs, Ctrl+O shows the output, own screen buffer on the classic Windows console
    TuiSettings.cs                               TUI settings record, loaded/saved as JSON
    TuiTheme.cs                                  Theme enum: MidnightCommander, Light, Dark
    TuiPalette.cs                                16-color palette per theme (panes, cursor, menu, dialog)
    EntryTableView.cs                            TableView with empty-row dividers and scroll thumb
    PathPanelView.cs                             Bordered panel with current path, marked-entries summary and disk space above the pane, plus the pane's top border
  Hst.Commander.Core.Tests/                      xUnit unit tests
  Hst.Commander.Gui/                             Avalonia 12 desktop GUI application
    MainWindow.axaml(.cs)                        Dual-pane window, File menu, function key bar, Tab pane switching, F5 copy, F6 move/rename, F7 make directory, F8/Delete delete, F9 create volume, Ctrl+P/Alt+Enter properties, error display
    PaneView.axaml(.cs)                          One pane: path, column header, entry ListBox, Enter/Backspace
    EntryItem.cs                                 Display row for an IEntry (size/date formatting, icon)
    Icons/FileIcons.cs                           Loads and caches the entry icons (embedded, OS registered, built-in fallback)
    Icons/IFileIconProvider.cs                   Host OS icons for file types, folders, drives and files with own icons
    Icons/WindowsFileIconProvider.cs             Windows shell icons (SHGetFileInfo)
    Icons/FreedesktopFileIconProvider.cs         Linux/freedesktop icons (shared MIME database + PNG icon themes)
    Icons/MacFileIconProvider.cs                 macOS icons (NSWorkspace via the Objective-C runtime)
    ErrorDialog.axaml(.cs)                       Modal error dialog
    SettingsDialog.axaml(.cs)                    Modal settings view (theme selection with live preview, volume provider keys, Enter on file)
    CopyDialog.axaml(.cs)                        Modal copy/move confirm dialog (destination and filter), titled Copy or Move
    CopyProgressDialog.axaml(.cs)                Modal copy/move progress dialog running the copy or move (2 progress bars, speed, ETA, cancel)
    OverwriteDialog.axaml(.cs)                   Modal confirm overwriting an existing file (Yes, No, All)
    DeleteDialog.axaml(.cs)                      Modal confirm deleting entries (Yes, No)
    DeleteProgressDialog.axaml(.cs)              Modal delete progress dialog running the delete (current entry, total progress bar, cancel)
    MakeDirectoryDialog.axaml(.cs)               Modal make directory dialog (F7): directory name, OK, Cancel
    CreateVolumeDialog.axaml(.cs)                Modal list of the volume creators (F9), titled Create
    CreateVolumeFieldsDialog.axaml(.cs)          Modal dialog for the file name and fields of a volume creator: text boxes, check boxes (added in code), OK, Cancel
    SelectGroupDialog.axaml(.cs)                 Modal select/unselect group dialog (+/-): filter, case sensitive, files only, OK, Cancel
    PropertiesDialog.axaml(.cs)                  Modal properties dialog (Ctrl+P or Alt+Enter): name, sections of property lines, drive usage bar, OK; clicking the icon opens IconViewerDialog
    Icons/ViewerIcon.cs                          Icons and images shown by the icon viewer (bitmaps created when shown)
    IconViewerDialog.axaml(.cs)                  Modal icon viewer opened from the properties dialog: all icons of the entry (list) and their images (sizes) in a higher resolution, fitted or zoomed (100%-800%), on a selectable background (transparent checkerboard, common colors or custom)
    VolumeProviderDialog.axaml(.cs)              Modal list of volume providers for a pane (Alt+F1/Alt+F2), centered over the pane
    LocationDialog.axaml(.cs)                    Modal location dialog (Ctrl+L or double-click path): path, Volumes, OK, Cancel
    AboutDialog.axaml(.cs)                       Modal about dialog (Help > About): name, version and author
    GuiSettings.cs                               GUI settings record, loaded/saved as JSON
    GuiTheme.cs                                  Theme enum: MidnightCommander, Light, Dark
    ThemeManager.cs                              Applies a theme (theme variant + Midnight Commander styles)
    MidnightCommanderStyles.axaml(.cs)           Current row colors only active in the Midnight Commander theme
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
        |                                    /    (default plugins, Plugins.All)
[Volume Implementations]  <- Hst.Commander.Core
  LocalVolumeProvider (VolumeProviders.All),
  Iso9660FileVolumeProvider (VolumeProviders.Files),
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

**`IVolume`** (`Hst.Commander.Abstractions/IVolume.cs`)
```csharp
string Name { get; }
IEnumerable<string> CurrentPathComponents { get; }
IAsyncEnumerable<IEntry> ListEntries(CancellationToken cancellationToken = default);
Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default);
Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default);
Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default);   // item in current directory
Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default);  // creates/overwrites; NotSupportedException when read-only
Task CreateContainer(string name, CancellationToken cancellationToken = default);    // no-op when it exists; NotSupportedException when read-only
Task Delete(string name, CancellationToken cancellationToken = default);             // item or empty container; NotSupportedException when read-only
Task Rename(string name, string newName, CancellationToken cancellationToken = default); // IOException when newName exists (case-only changes allowed); NotSupportedException when read-only
Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default); // unspecified kind = UTC; NotSupportedException when read-only
Task<bool> SupportsAmigaMetadata(CancellationToken cancellationToken = default) => Task.FromResult(false); // current directory stores Amiga names, protection bits and comments as they are
Task SetAmigaMetadata(string name, AmigaMetadata metadata, CancellationToken cancellationToken = default); // default throws NotSupportedException
```

**`IVolumeProvider`** (`Hst.Commander.Abstractions/IVolumeProvider.cs`)
```csharp
string Name { get; }          // shown in the Alt+F1/Alt+F2 list, e.g. "Local"
string Description { get; }   // shown next to the name
Task<IVolume> CreateVolume(CancellationToken cancellationToken = default); // new, independent volume at its root
```
Volume providers are what a pane can show. `VolumeProviders.All` (`Hst.Commander.Core`) is the list shown to the user: the built-in `VolumeProviders.Local` (`LocalVolumeProvider`) followed by the volume providers of the plugins (`IPlugin.VolumeProviders`; none so far). Each `CreateVolume` call must return a volume of its own (copy/delete open a second one on a background thread); a volume implementing `IDisposable` is disposed when no longer used.

**`IFileVolumeProvider`** (`Hst.Commander.Abstractions/IFileVolumeProvider.cs`)
```csharp
string Name { get; }          // e.g. "Zip"
string Description { get; }   // e.g. "Zip archives"
bool CanWrite => false;       // true when volumes can write to the file (gets a writable stream when the file can be written)
Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default); // magic signature check; stream at the start, may not be seekable
Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default);     // new volume at its root over a seekable stream
```
File volume providers register files that open as volumes (images, e.g. ISO images and zip archives), see Images in the Navigation Model. `VolumeProviders.Files` is the list asked in order: the built-in `VolumeProviders.Iso9660` (`Iso9660FileVolumeProvider`, signature `CD001` at offset `0x8001`) followed by the file volume providers of the plugins (`IPlugin.FileVolumeProviders`). The stream passed to `CreateVolume` stays open while the volume is used and is disposed by Hst Commander after the volume (disposed first when `IDisposable`); like volume providers, each call must return a volume of its own. The stream is read-only (`CanWrite` false) unless the provider's `CanWrite` is true and the file can be written, see Writable Images.

**`IPlugin`** (`Hst.Commander.Abstractions/IPlugin.cs`)
```csharp
string Name { get; }
IEnumerable<IVolumeProvider> VolumeProviders => [];          // appended to VolumeProviders.All
IEnumerable<IFileVolumeProvider> FileVolumeProviders => [];  // appended to VolumeProviders.Files
bool CanCreateVolumes => false;                              // true to list VolumeCreators in the create dialog (F9)
IEnumerable<IVolumeCreator> VolumeCreators => [];            // appended to VolumeProviders.Creators when CanCreateVolumes
IEnumerable<IIconProvider> IconProviders => [];              // appended to VolumeProviders.Icons
void Initialize(IPluginHost host) { }                        // called once when loaded, before the plugin is used
```
A plugin is a project referencing only `Hst.Commander.Abstractions` (`Paths` is there for plugins too). Plugins can't use each other's types (each loads in its own load context), so they work together through `IPluginHost`.

**`IPluginHost`** (`Hst.Commander.Abstractions/IPluginHost.cs`)
```csharp
Task<IVolume?> OpenVolume(string name, Stream stream, IFileVolumeProvider? exclude = null, CancellationToken cancellationToken = default);
```
Opens a seekable stream (e.g. a partition) as a volume with the first file volume provider of `VolumeProviders.Files` detecting it (`IsMatch` from position 0, skipping `exclude`; unreadable streams aren't detected), null when none does. The stream is passed on as it is (writable when it is) and stays owned by the caller, disposed after the volume. `PluginHost` (`Hst.Commander.Core`, `PluginHost.Instance`) implements it; `Plugins` calls `IPlugin.Initialize(PluginHost.Instance)` on the built-in plugins and `PluginLoader.Load(directories, loadedPlugins, host)` on each loaded plugin (a plugin failing to initialize is reported like failing to load). E.g. the Disk plugin opens a PiStorm partition (MBR type `0x76`) with the Amiga plugin's `RdbFileVolumeProvider` this way. Plugins are included by default (`Plugins.BuiltIn`) or loaded from the plugin directories at startup, see Plugins.

**`IEntry`** (`Hst.Commander.Abstractions/IEntry.cs`)
- `Name`, `Type` (`EntryType.Container` | `EntryType.Item`), `Size`, `ModifiedDate`
- `DisplayName` (default `Name`): the name shown in the panes of both front-ends (TUI name column, GUI `EntryItem.DisplayName`), e.g. `1 - DH0 - Workbench` for an RDB partition, also the name at the top of the properties dialog; paths, file operations and other dialogs use `Name`
- `Details` (default null): an `EntryDetails(TypeName, Sections, UsedFraction = null)` with properties only the volume knows, shown in the properties dialog of a single entry (see Properties), e.g. the partition number, drive name and volume name of an RDB partition. `EntryProperty(Label, Value)` is a line; `EntryProperty.FromBytes(label, bytes)` a size formatted like Hst Commander's own sizes
- `IsVolume` (default false): a container that is the root of a volume of its own, e.g. an RDB partition, whose icon can be inside it (`IIconProvider.GetVolumeIconFileName`)
- `AmigaMetadata` (default null): the Amiga protection bits and comment of an entry in an Amiga file system, an `AmigaMetadata(ProtectionBits, Comment)` with the protection bits as stored on disk (bits 7-0 `hsparwed`, `rwed` set when not allowed, so 0 is `----rwed`; `IsDefault` when 0 and no comment), see UAE Metadata

**`IVolumeCreator`** (`Hst.Commander.Abstractions/IVolumeCreator.cs`)
```csharp
string Name { get; }                              // shown in the create dialog, e.g. "Amiga disk file"
string Description { get; }                       // shown next to the name
string FileExtension { get; }                     // e.g. ".adf", added to the file name when missing
IReadOnlyList<VolumeCreatorField> Fields { get; } // input fields entered by the user, in order
Task Create(Stream stream, VolumeCreatorValues values, CancellationToken cancellationToken = default); // writes the new volume
```
**`IIconProvider`** (`Hst.Commander.Abstractions/IIconProvider.cs`)
```csharp
string Name { get; }                       // e.g. "Amiga icons"
string Description { get; }                // e.g. "Amiga .info icon files"
string? GetIconFileName(string name);      // icon file of an entry by name only, e.g. "file.info" for "file" and "file.info"; null when none
string? GetVolumeIconFileName(string name) => null; // icon file inside a volume entry (IEntry.IsVolume), e.g. "Disk.info"
Task<IconImage?> ReadIcon(string name, Stream stream, int size, CancellationToken cancellationToken = default); // checks the magic signature, null when no icon
Task<IReadOnlyList<EntryIcon>> ReadIcons(string name, Stream stream, CancellationToken cancellationToken = default); // all icons for the icon viewer; defaults to ReadIcon in 256 pixels
```
Icon providers read icons from icon files next to entries, see Entry Icons. `IconImage(Width, Height, PngData, BgraPixels, BitsPerPixel = null)` is PNG data or 32-bit BGRA pixels (not premultiplied, top-down) with the color depth stored in the file when known, also used by `EmbeddedIconReader` and the GUI icon providers. `EntryIcon(Name, Images)` is one icon of a file with its images, e.g. the selected image of an Amiga icon or an icon group of a Windows program with several sizes.

A plugin can create several kinds of volumes; `VolumeProviders.Creators` lists the volume creators of the plugins with `CanCreateVolumes`, see Creating Volumes. `VolumeCreatorField(Name, Label, Type, IsRequired = false, DefaultValue = null)` is an input field: `VolumeCreatorFieldType.Text` (string), `Number` (long) or `Boolean` (check box, bool). `VolumeCreatorValues` holds the values by field name (`GetText`, `GetNumber`, `GetBoolean`). `Create` gets an empty seekable, readable and writable stream and throws `IOException` for invalid values.

### Plugins

- `Plugins.BuiltIn` (`Hst.Commander.Core`) lists the plugins included by default, currently `ZipPlugin` (`Hst.Commander.Plugins.Zip`), `LhaLzxPlugin` (`Hst.Commander.Plugins.LhaLzx`), `DiskPlugin` (`Hst.Commander.Plugins.Disk`) and `AmigaPlugin` (`Hst.Commander.Plugins.Amiga`), all referenced by `Hst.Commander.Core`. The Disk plugin comes before the Amiga plugin, so hybrid disks (an MBR and a rigid disk block, which `RdbFileVolumeProvider` would match too) are opened by the Disk plugin. `Plugins.All` is the built-in plugins followed by the plugins loaded from disk; `VolumeProviders.All`/`Files` are created again from it when it changes.
- Both front-ends call `Plugins.Load()` at startup, before the panes (and their `NavigationContext`s, which take `VolumeProviders.Files` when created) are created: the TUI in `Program.cs`, the GUI in `App.OnFrameworkInitializationCompleted` (fully qualified as `Hst.Commander.Core.Plugins`, since `Plugins` in `Hst.Commander.Gui` resolves to the `Hst.Commander.Plugins` namespace). It loads once and returns the errors of plugins that failed to load; `Plugins.FormatErrors` joins them into one message (null when none), shown once the main window is loaded (TUI `MainWindow.ShowErrorWhenLoaded`, GUI `MainWindow.StartupError` shown with `ErrorDialog` when opened). Other plugins still load.
- `PluginLoader.Directories` are scanned in order: `~/.hst-commander/plugins` (`SettingsPaths.Directory`) and `plugins` in the directory Hst Commander is executed from (`AppContext.BaseDirectory`); missing directories are skipped and a directory listed twice is scanned once.
- In a plugin directory, `PluginLoader.Load(directories, loadedPlugins)` loads `Name/Name.dll` (a plugin in a directory of its own with its dependencies, the recommended layout) and then single assemblies `*.dll`, ordered by path. Public non-abstract classes implementing `IPlugin` with a parameterless constructor are created. Assemblies without plugins are skipped (e.g. dependencies; a `BadImageFormatException`, e.g. a native library, is ignored for single assemblies but reported for `Name/Name.dll`), as are plugins whose type full name is already loaded (e.g. a copy of the built-in Zip plugin). Errors are reported per assembly in `PluginLoadResult.Errors`.
- Each plugin assembly is loaded in an `AssemblyLoadContext` of its own (`PluginLoadContext`) that shares `Hst.Commander.Abstractions` with Hst Commander (the default load context, so `IPlugin` etc. are the same types; a plugin's own copy of it is ignored) and resolves other dependencies with the plugin's `.deps.json` (`AssemblyDependencyResolver`), or from the plugin's directory when the resolver isn't available; framework assemblies come from the default context. This works in the single-file published apps. Loaded plugin assemblies can't be unloaded and stay locked while Hst Commander runs.

### Volume Implementations

| Class | Purpose |
|---|---|
| `LocalVolumeProvider` | Volume provider "Local" creating a `LocalVolume` at the drive list |
| `LocalVolume` | Browses the real file system; lists drives when at root |
| `Iso9660Volume` | Reads ISO 9660 / CD-ROM images via `LTRData.DiscUtils.Iso9660`, created by `Iso9660FileVolumeProvider` |
| `ZipVolume` | `Hst.Commander.Plugins.Zip`: reads zip archives via `System.IO.Compression.ZipArchive`, created by `ZipFileVolumeProvider` (signatures `PK\3\4`, `PK\5\6` for empty and `PK\7\8` for spanned archives). Directories are the archive's directory entries plus the directories in file paths; `/` and `\` separate path components, empty, `.` and `..` components are skipped. Names are matched exactly, otherwise case-insensitive. Disposes its `ZipArchive` |
| `LhaVolume` | `Hst.Commander.Plugins.LhaLzx`: reads lha archives (`.lha`, `.lzh`) via `Hst.Compression.Lha.LhaArchive` (Amiga ISO-8859-1 names), created by `LhaFileVolumeProvider` (method `-lh?-`, `-lz?-` or `-pm?-` at offset 2). Entries are decompressed while read with `LhaArchive.OpenEntry`. Names are cut at a zero character (the Amiga comment follows it); the attribute is the Amiga protection bits as stored on disk (0 for level 1/2 headers of other operating systems, e.g. `M`, `U`, `w`); `-lhd-` entries without a trailing separator (symbolic links) are skipped |
| `LzxVolume` | `Hst.Commander.Plugins.LhaLzx`: reads Amiga lzx archives (`.lzx`) via `Hst.Compression.Lzx.LzxArchive`, created by `LzxFileVolumeProvider` (signature `LZX`). Entries are decompressed while read with `LzxArchive.OpenEntry`; dates in local time are converted to UTC; the attributes (set when allowed) are converted to the Amiga protection bits as stored on disk, with the comment |
| `ArchiveVolume<T>` | `Hst.Commander.Plugins.LhaLzx`: read-only base of `LhaVolume` and `LzxVolume`, building the directory tree like `ZipVolume` (directory entries end with `/` or `\`, plus the directories in file paths, same skipping and name matching). Entries have their `AmigaMetadata` and `SupportsAmigaMetadata` is true, so copying from them works like copying from an Amiga volume (see UAE Metadata). The archives aren't disposed, as `LhaArchive`/`LzxArchive.Dispose` would dispose the stream owned by Hst Commander |
| `NestedVolume` | Compositor — holds a `LinkedList<IVolume>`. Drilling into a file pushes a new volume; going up pops it. The combined `CurrentPathComponents` spans all nested volumes |
| `EntryVolumeProvider` | Adapter that presents a child `IVolume` as if it starts at the path of the opening `IEntry`. Used when pushing volumes into `NestedVolume` |
| `DiskImageVolume` | **Stub** — all methods throw `NotImplementedException`. Intended for raw disk images (MBR/GPT/RDB partition tables) |
| `NullVolume` | Null object — returns an empty entry list, safe fallback |
| `AmigaFileSystemVolume` | `Hst.Commander.Plugins.Amiga`: reads and writes an Amiga file system via `Hst.Amiga` (`IFileSystemVolume`): the Fast File System (`DOS\0`-`DOS\7`) of an adf, created by `AdfFileVolumeProvider` (`DOS` + 0-7 at offset 0 and the size of a double (901120) or high (1802240) density disk; `AmigaFileSystems.MountAdf`), or the Fast File System or PFS3 (`PFS\3`, `PDS\3`) of an RDB partition (`AmigaFileSystems.MountPartition`; other file systems, e.g. SFS, throw `IOException`). Writable when the stream is; changes are flushed after each operation and when a written stream is closed (PFS3 caches). Names are case-insensitive and can't contain `/` or `:`; names longer than the file system's maximum (30, 107 for `DOS\6`/`DOS\7`, PFS3's file name size - 1) throw `IOException`, as Hst.Amiga would truncate them. A case-only rename goes through a temporary name. Amiga dates are local time (converted from/to UTC, dates before 1978 are set to 1978-01-01). Delete and overwrite ignore protection bits. Entries have their `AmigaMetadata` (`ProtectionBitsConverter.ToProtectionValue`), `SupportsAmigaMetadata` is true and `SetAmigaMetadata` sets the protection bits and comment (cut to 79 characters). Hst.Amiga errors are thrown as `IOException`. Disposing (PFS3 flushes) and closing a written stream wait on the thread pool, as Hst.Amiga waits for async writes there, which would deadlock on a UI thread |
| `PartitionTableVolume` | `Hst.Commander.Plugins.Disk`: disk images with a master boot record (MBR) or GUID partition table (GPT), created by `DiskFileVolumeProvider` (`EFI PART` at offset 512 for GPT, the protective MBR isn't shown; otherwise `0x55 0xAA` at offset 510 with a valid MBR partition table holding a partition, not the boot sector of an unpartitioned FAT/exFAT/NTFS volume; raw images and vhd images like `RdbFileVolumeProvider`). Sectors of 512 and 4096 bytes (4Kn, e.g. images of some USB drives) are supported: GPT by the offset of its header (512 or 4096), MBR (which doesn't store the sector size) with 512 unless the partitions only fit on the disk with 4096 byte sectors, or also fit and more of them start with a volume there (`0x55 0xAA` at offset 510, `RDSK` in the first 16 blocks or the ext superblock magic); the sector size is passed to DiscUtils in the `Geometry`. Partitions are read with `DiscUtils.Partitions` (`BiosPartitionTable`, logical partitions included, or `GuidPartitionTable`). The root lists the partition table as a directory, `mbr` or `gpt` (lower case in paths, shown as `MBR`/`GPT`; not `IsVolume`, `Details` of type `Partition table` with the partition table, number of partitions and sector size), so paths always show it, e.g. `disk.img\mbr\1`. It lists the partitions as directories `1`..`n` marked `IsVolume`, shown as `<number> - <volume label>`, `<number> - PiStorm RDB` for PiStorm partitions (MBR type `0x76` or GPT type `3F82EEBC-87C9-4097-8165-89D6540557C0`) or the number otherwise (e.g. `1 - EMU68BOOT`, `2 - PiStorm RDB`), with `Details` (type `MBR partition`/`GPT partition`; partition number, partition table, type with id (`0x0C`) or GUID, GPT name, volume name, file system; active/primary or partition id, sectors, sector size, partition size; used/free space and capacity of FAT/NTFS with the used fraction as a bar). A partition is mounted when listed or opened, as FAT or NTFS (`DiscFileSystemVolume`), otherwise through `IPluginHost.OpenVolume` on the partition stream (e.g. a PiStorm partition with a rigid disk block opens as `RdbVolume`), otherwise it can't be mounted (`IOException` when opened, tried once when listed). The partition list is read-only (`NotSupportedException`) |
| `HybridDiskVolume` | `Hst.Commander.Plugins.Disk`: a disk where `IPluginHost.OpenVolume` finds another volume on the whole disk too (excluding the Disk provider), e.g. an MBR in block 0 and a rigid disk block in blocks 1-15. The root lists the entries in the roots of both volumes, their partition tables, e.g. `mbr` (a `PartitionTableVolume`) and `rdb` (an `RdbVolume`), delegating to the volume of the entry opened (going up to its root returns to the hybrid root), e.g. `hybrid.img\rdb\1` |
| `VolumeListVolume` | `Hst.Commander.Plugins.Disk`: abstract base of `PartitionTableVolume`: a root listing a directory named by the volume's `Name` in lower case (the partition table, `ListName`) listing volumes as directories, mounting them when listed or opened, delegating to the current one (going up from its root returns to the list) and disposing them with itself |
| `DiscFileSystemVolume` | `Hst.Commander.Plugins.Disk`: reads and writes a DiscUtils `DiscFileSystem` (FAT with long names, NTFS), writable when the file system is. Names are case-insensitive and can't contain `\`, `/`, `:`, `*`, `?`, `"`, `<`, `>` or the pipe character; a case-only rename goes through a temporary name; dates are UTC. Errors are thrown as `IOException` |
| `RdbVolume` | `Hst.Commander.Plugins.Amiga`: hard disk images with a rigid disk block, created by `RdbFileVolumeProvider` (`RDSK` at the start of one of the first 16 blocks of 512 bytes; raw images such as `.hdf`/`.img` and fixed vhd images directly, dynamic/differencing vhd images, `conectix` at offset 0, through `DiscUtils.Vhd.Disk.Content`). The root lists the partition table as the directory `rdb` (lower case in paths, shown as `RDB`; not `IsVolume`, `Details` of type `Partition table` with the number of partitions), so paths always show it, e.g. `disk.hdf\rdb\1` or `pistorm.img\mbr\2\rdb\1`. It lists the partitions as directories `1`..`n` in RDB order (size = partition size), shown with their number, drive and volume names separated by ` - ` (`DisplayName`, e.g. `1 - DH0 - Workbench`; no drive name when it has none, no volume name when the partition can't be mounted, e.g. SFS) and marked `IsVolume`, with `Details` (type `Amiga partition`; partition number, drive name, volume name, file system with its name and DOS type e.g. `Fast File System, international (DOS)` (`AmigaFileSystems.FormatFileSystem`); bootable, boot priority, cylinders; used/free space and capacity of the mounted file system, with the used fraction as a bar). A partition is mounted as an `AmigaFileSystemVolume` when listed or opened and stays mounted until disposed (a partition that can't be mounted is only tried once when listed). The partition list itself is read-only (`NotSupportedException`). `CreateVolume` throws `IOException` when there is no rigid disk block or it has no partitions, shown as an error |

`Iso9660Volume`, `ZipVolume`, `LhaVolume` and `LzxVolume` are read-only: `OpenWrite`, `CreateContainer`, `Delete`, `Rename` and `SetModifiedDate` throw `NotSupportedException`. `LocalVolume.CreateContainer` throws `NotSupportedException` in the drive list.

### Copying

- `VolumeCopy.CopyToAsync` (`Hst.Commander.Core.Volumes`) is an extension method copying entries from the current directory of any `IVolume` to the current directory of another, using only `OpenRead`, `OpenWrite`, `CreateContainer`, `ListEntries` and `ChangeDirectory`. It first scans the entries (containers recursively) to know the total size, then copies; both volumes are changed back to their starting directory afterwards. Containers are merged with existing ones. Modified dates are preserved with `IVolume.SetModifiedDate`: items right after they are written, containers after everything is copied, deepest first (writing into a container changes its date); a destination throwing `NotSupportedException` keeps the written dates. Before an item that already exists in the destination is overwritten, `CopyOptions.ConfirmOverwrite` is asked with an `OverwriteRequest` (name, both entries and a shared `Message` with exact sizes in bytes, e.g. `1,234,567 bytes`, and dates) and answers `OverwriteAnswer.Yes`, `No` (skip the item, removed from the progress totals) or `All` (overwrite the rest without asking); without a callback items are overwritten. Destination entries are listed once per directory to detect existing items, and the speed/ETA clock is paused while asking. `CopyOptions` holds the filter (`EntryFilter`: `*`/`?` wildcards, several patterns separated by `;`, case-insensitive, applied to items only — containers are always copied) and an optional target name for copying a single item under another name. Progress is reported as `CopyProgress` (current item and total bytes/items, average speed, ETA, plus shared display texts) at most every 100 ms and at each item start/end. When a copy is cancelled or fails while an item is being written, the destination stream is closed and the partial item is deleted with `IVolume.Delete` (cleanup errors are ignored so the original error is kept).
- `NavigationContext.CopyAsync` is what the front-ends call. It never uses the pane's own volumes: `NavigationContext.OpenLocationAsync` opens an independent `VolumeLocation` (a new `LocalVolume`, the images opened again from their files (images inside images from the reopened image they are in), or for other volume providers a new volume from `IVolumeProvider.CreateVolume` changed to `[Paths.Root, ..current path components]`) so the copy can run on a background thread. The destination text is resolved as: the other pane's current path → that pane's location (may be an image); otherwise a local directory (relative paths are relative to the source's local directory; a missing directory is created); a single file copied to a non-directory path whose parent exists is copied under that name. Copying entries onto themselves or a directory into itself throws `IOException`.
- `NavigationContext.GetOperationEntries` returns the marked entries, or the entry under the cursor when none are marked (never `..`). Marks are cleared after a successful copy or delete.

### UAE Metadata

- Amiga names can contain characters other file systems can't (e.g. `*`, `?`, `#`), and Amiga protection bits and comments can't be stored there. Like Hst Imager, copying and moving (`VolumeCopy.TransferAsync`) detects what to do from `IVolume.SupportsAmigaMetadata` of the source and destination (true for Amiga file systems and lha/lzx archives; forwarded by `NestedVolume`, `EntryVolumeProvider`, `SharedImageVolume`, `RdbVolume`, `VolumeListVolume` and `HybridDiskVolume` to the current volume, false in partition lists):
  - both store them (Amiga to Amiga, lha/lzx to Amiga): names are copied as they are and `IEntry.AmigaMetadata` is set with `SetAmigaMetadata` (before the modified date, containers last);
  - only the source (Amiga or lha/lzx to e.g. a local directory, a FAT/NTFS partition or a writable volume of another provider): names, protection bits and comments are stored as UAE metadata in the format of `CopyOptions.UaeMetadata` (`UaeMetadataFormat`, `Hst.Commander.Core`): `UaeFsDb` (default, WinUAE and FS-UAE: a `_UAEFSDB.___` file of 600 byte version 1 nodes per directory; names needing it get `__uae___` + `UaeFsDbNodeHelper.MakeSafeFilename`, made unique with 8 random characters), `UaeMetafile` (FS-UAE: a `<name>.uaem` text file next to the entry with the protection bits, local date and comment; names `%xx` encoded with `UaeMetafileHelper`) or `None` (no metadata, invalid characters replaced with `_`). Windows reserved names (`CON`, `NUL`, `COM1`, ...) need a normal name on Windows. Entries with a usable name and default metadata get no metadata. Existing metadata of the destination directory is kept and an Amiga name copied again keeps its normal name. `UaeMetadataWriter` (`Hst.Commander.Core.Volumes`, internal) writes through `IVolume` only; metafiles are written right away, the `_UAEFSDB.___` files after all items (before the container dates, also when the copy fails or is cancelled);
  - only the destination (e.g. a local directory to Amiga): `UaeMetadataReader` reads the UAE metadata of each source directory (`_UAEFSDB.___`, or on Windows the version 2 nodes WinUAE writes as NTFS alternate data streams `<name>:_UAEFSDB.___` of local entries, plus valid `.uaem` metafiles of listed entries), entries are copied with their Amiga names, protection bits and comments, and the metadata files aren't copied. Invalid or unreadable metadata files are copied as normal files. When moving, the metadata of moved entries is removed from the source (metafiles deleted, `_UAEFSDB.___` rewritten or deleted) so moved directories can be deleted;
  - neither (e.g. local to local): a plain copy, metadata files are copied as normal files.
- The panes show the names as they are stored, e.g. `__uae___file_1` in a local directory. The properties dialog of a single entry shows `Protection bits` (`EntryProperties.FormatProtectionBits`, like the Amiga List command, e.g. `----rwed`) and `Comment` of entries with `IEntry.AmigaMetadata` (Amiga volumes, lha/lzx archives), and in other volumes `Amiga name`, `Protection bits` and `Comment` when the UAE metadata of the current directory has the entry (`NavigationContext.GetPropertiesAsync` reads the entry's metafile, `_UAEFSDB.___` or alternate data stream with the pane's volume).
- The format is chosen in File > Settings of both front-ends (`UAE metadata`; `UaeMetadataFormats.All`/`GetName`), stored as `TuiSettings`/`GuiSettings.UaeMetadata` and set as `NavigationContext.UaeMetadata` of both panes at startup and on OK, which `CopyAsync`/`MoveAsync` pass in `CopyOptions`. Core references the `Hst.Amiga` package for the UAE metadata readers and writers.

### Moving and Renaming

- `VolumeMove.MoveToAsync` (`Hst.Commander.Core.Volumes`) is an extension method moving entries between any two writable volumes: it copies like `VolumeCopy.CopyToAsync` (same `CopyOptions`, `CopyProgress` and overwrite confirmation; both share `VolumeCopy.TransferAsync`) and deletes each item from the source right after it is copied. Containers are deleted from the source after everything is copied, deepest first, only when empty, so items answered `No` or not matching the filter are kept in the source with their containers.
- `NavigationContext.MoveAsync` is what the front-ends call, resolving the destination like `CopyAsync` (and throwing `IOException` when moving onto itself or a directory into itself). When both locations are local directories on the same drive (`FindDrive`) and the filter matches all items (`*`, `*.*` or empty), entries whose name doesn't exist in the destination are moved with `File.Move`/`Directory.Move` (fast, no extra space); the rest (existing names to confirm or merge, other drives, volumes or filters) go through `VolumeMove.MoveToAsync` on volumes of its own from `OpenLocationAsync`.
- A single entry moved to a plain name (no `/` or `\`, not rooted, not the other pane's path and not an existing directory in the current location, which the entry is moved into instead) is renamed: `NavigationContext.GetRenameNameAsync` returns that name (null for a move) and `RenameAsync(entry, newName)` renames it with `IVolume.Rename` on the pane's own volume. The name is validated like making a directory; another entry with the name throws `IOException`, a case-only change is allowed. The renamed entry is selected once by `GetSelectedIndex`, like a created directory. `MoveAsync` renames too when given such a name.
- `NavigationContext.MoveNotSupportedMessage` is the reason entries can't be moved (the local drive list, inside a read-only image), null when supported; `MoveAsync`/`RenameAsync` then throw `NotSupportedException`. Both front-ends show it as an error instead of the move dialog. `NavigationContext.GetMoveDescription` gives the dialog text, e.g. `Move 'a.txt'` or `Move 3 entries`.

### Deleting

- `VolumeDelete.DeleteAsync` (`Hst.Commander.Core.Volumes`) is an extension method deleting entries from the current directory of any `IVolume` using only `ListEntries`, `ChangeDirectory` and `Delete` (which only deletes items and empty containers). It first scans the entries (containers recursively, entries within a container before the container), then deletes them in that order. Progress is reported as `DeleteProgress` (current entry, deleted/total entries counting items and containers, shared `TotalText` e.g. `3/10 entries`) at most every 100 ms and when done. The volume is changed back to its starting directory, also when cancelled or failed. Read-only volumes throw `NotSupportedException`.
- `NavigationContext.DeleteAsync` is what the front-ends call; like copying it uses a volume of its own from `OpenLocationAsync` so it can run on a background thread. `NavigationContext.GetDeleteDescription` gives the confirm question, e.g. `Delete 'a.txt'?` or `Delete 3 entries?`.

### Making Directories

- `NavigationContext.CreateDirectoryAsync(name)` creates a directory in the current location with `IVolume.CreateContainer` on the pane's own volume. The name is trimmed; an empty name, `.`, `..`, names with `/`, `\` or invalid file name characters and names of existing entries (case-insensitive on Windows) throw `IOException`. The created directory is selected once by `GetSelectedIndex` when the entries are listed again.
- `NavigationContext.CreateDirectoryNotSupportedMessage` is the reason a directory can't be created (the local drive list, inside a read-only image), null when supported; `CreateDirectoryAsync` then throws `NotSupportedException`. Both front-ends show it as an error instead of the make directory dialog.

### Creating Volumes

- F9, the `F9 Create` function key item or File > Create volume opens the create dialog in the active pane of both front-ends: a list of `VolumeProviders.Creators` (name and description; Enter, double-click or OK chooses, Esc/Cancel cancels), then a dialog titled `Create <name>` with the file name (`File name (.adf) *`) and the creator's fields (text and number fields as text fields, booleans as check boxes, defaults from `DefaultValue`, required text/number fields labelled with `*`, `VolumeCreation.GetLabel`). The input is a `VolumeCreatorInput` (file name and input by field name: text and numbers as entered text, check boxes as booleans). When creating fails, the error is shown and the fields dialog is shown again with the input entered.
- `NavigationContext.CreateVolumeAsync(creator, fileName, input)` creates the file in the current location: the name is validated like making a directory with the creator's extension added when missing (`VolumeCreation.GetFileName`, case-insensitive), the input is checked and converted (`VolumeCreation.GetValues`: text trimmed, required fields must be entered, numbers parsed, missing input gets the default value; `IOException` otherwise) and an existing entry with the name throws `IOException`. Local files are created with `FileMode.CreateNew` and written by `IVolumeCreator.Create` on the thread pool; elsewhere (other volume providers, writable images) the volume is created in a temporary file and copied with `IVolume.OpenWrite` of the pane's volume. A partially created file is deleted when creating fails. The created file is selected once by `GetSelectedIndex`; the other pane is refreshed too.
- `NavigationContext.GetCreateVolumeNotSupportedMessage(creators)` is the reason a volume can't be created (no volume creators, the local drive list, inside a read-only image), null when supported; `CreateVolumeAsync` then throws `NotSupportedException`. Both front-ends show it as an error instead of the dialog.
- `AdfVolumeCreator` (Amiga plugin, `Amiga disk file`, `.adf`) creates a double density (880 KB) adf with the fields `Volume name` (text, required, default `Empty`, at most 30 characters without `/` or `:`), `Format` (default checked: Fast File System `DOS\3` with the volume name; unchecked: all zeros, the other fields are ignored) and `Bootable` (default unchecked: writes the standard boot code with the boot block checksum, `BootBlockBuilder`).

### Entry Icons

- `VolumeProviders.Icons` lists the icon providers of the plugins (`IPlugin.IconProviders`), in plugin order. `NavigationContext.ListEntriesAsync` finds the icon file of each listed entry (not `..`, not in the drive list) with the first provider whose `GetIconFileName` names a listed file (matched exactly, otherwise case-insensitive); `NavigationContext.GetIconFile(entry)` returns it, so an entry and its icon file (e.g. `file` and `file.info`, or the directory `drawer` and `drawer.info`) share the icon. The `NavigationContext` constructor's optional `iconProviders` defaults to `VolumeProviders.Icons`.
- Volume entries (`IEntry.IsVolume` containers, e.g. RDB partitions) without a listed icon file get the providers' `GetVolumeIconFileName` (e.g. `Disk.info` from `AmigaIconProvider`), looked up when the icon is read. `NavigationContext.HasIcon(entry)` is true for entries with an icon file or such volume entries. `ReadIconAsync`/`ReadIconsAsync` of a volume entry open the location once per listing with `OpenLocationAsync` (started on the calling thread, closed when the entries are listed again or the context is disposed), change into the entry, read the first provider's icon file that exists and change back, on the thread pool and serialized by a `SemaphoreSlim`, so the pane's volume isn't changed; a partition without the file or that can't be mounted has no icon.
- `NavigationContext.ReadIconAsync(entry, size)` reads the icon file into memory (at most 4 MB; local files on a background thread, files inside images and other volumes with the pane's volume) and calls `IIconProvider.ReadIcon` on the thread pool. Unreadable or invalid icon files and plugin errors give null.
- `NavigationContext.ReadIconsAsync(entry)` reads all icons of the icon file the same way with `IIconProvider.ReadIcons`, empty when none.
- `AmigaIconProvider` (Amiga plugin) reads Amiga icons (disk objects in `.info` files, magic `0xE310`, or PNG for AmigaOS 4 true color icons) with `Hst.Amiga.DataTypes.DiskObjects.AmigaIconHelper`, choosing a true color (PNG) icon, a color icon (GlowIcons), the first NewIcon image or the planar image drawn with the full AmigaOS palette (`AmigaOsPalette.FullPalette`, the 8 colors of `amigaos-full-palette.json` in Hst.Amiga, for 4 and 8 color icons; pen 0 transparent, higher pens wrap around), in that order. `ReadIcons` returns every image in that order, each an icon named e.g. `Normal (true color)`, `Selected (GlowIcon)`, `Normal (NewIcon)` or `Selected (planar)` (the normal and the selected image of each kind present, with its depth as `BitsPerPixel`); `ReadIcon` is the first.
- `EmbeddedIconReader.ReadIcons(name, stream)` reads all icons contained in a file: an `.ico` file is one icon `Icon` with all its images, a Windows executable an icon per icon group resource (named by the resource name or id, e.g. `#1`, at most `MaxIcons` = 1000), images largest first, then most colors. Invalid images are skipped. On Windows 10 and later, system programs keep their icons in `C:\Windows\SystemResources\*.mun` files, so their own file has none.
- The GUI shows the icon left of the entry name (`FileIcons`, see Adding GUI Features), instead of the icons contained in files and the OS icons. The TUI has no icons.

### Selecting Groups

- `NavigationContext.MarkGroup(entries, SelectGroupOptions)` marks the listed entries whose names match a filter, adding to the entries already marked, and returns the number of matching entries. `SelectGroupOptions` (`Hst.Commander.Core`) holds the `Filter`, `CaseSensitive` (default false) and `FilesOnly` (default false; directories are marked too when false). `..` is never marked.
- `NavigationContext.UnmarkGroup(entries, SelectGroupOptions)` unmarks the matching entries the same way, keeping other marks. `NavigationContext.InvertMarks(entries)` toggles the mark of every listed entry, directories included (`..` is never marked).
- `NameFilter` (`Hst.Commander.Core`) matches the names: a filter containing a character of `NameFilter.RegexCharacters` (`^$\[](){}|+`) is a regular expression matching any part of a name (anchor with `^`/`$`), otherwise it is wildcard patterns like `EntryFilter` (`*.txt;*.md`, whole names). Case-insensitive unless case sensitive (`EntryFilter` takes an optional `caseSensitive`). An empty filter matches all names; an invalid regular expression throws `IOException`.
- Both front-ends open the select group dialog with `+` or File > Select group and the same dialog titled `Unselect group` with `-` or File > Unselect group; `*` or File > Invert selection inverts the marks without a dialog. All act on the active pane; the keys are matched by the typed character, so they work on any keyboard layout, plus the keypad `+`, `-` and `*`. The filter starts as `*` and is remembered for the next time in the session (shared by select and unselect); the check boxes start unchecked. Errors are shown like other errors.

### Properties

- Ctrl+P, Alt+Enter or File > Properties shows the properties of the marked entries, or the entry under the cursor (`GetOperationEntries`), of the active pane in both front-ends, with similar details as the properties of Windows Explorer. `NavigationContext.GetPropertiesAsync(entries)` (used by both front-ends; `GetProperties` without UAE metadata) creates an `EntryProperties` (`Hst.Commander.Core`): the `Name` shown at the top and sections of `PropertyLine`s (label and value) from `GetSections(contents)`, shown separated by lines.
  - A single entry: `Type` (`FileSystemDetails.GetTypeName`: the Windows shell type name, e.g. `Text Document (.txt)`, elsewhere e.g. `TXT File (.txt)`, `File folder` for directories), `Link target` for links, `Location` (the current path), `Size`, `Size on disk` (local files on Windows only), `Contains` (directories), and for local files `Created`, `Modified`, `Accessed` (`FormatDate`: local time, culture full date/time), `Attributes` (`FormatAttributes`, e.g. `Read-only, Hidden, Archive`) and on other systems than Windows `Permissions` (`FormatUnixMode`, e.g. `rwxr-xr-x (755)`). Entries inside images and other volumes only have their `Modified` date. An entry with `IEntry.Details` (see Key Interfaces) is shown with its `DisplayName`, the details' type (when not null) and its sections after the type section (sizes from `EntryProperty.FromBytes` formatted with `SizeFormatter.FormatWithBytes`), and its `UsedFraction` as a bar like drives, e.g. the details of an RDB partition from the Amiga plugin. A last section shows the Amiga name (UAE metadata only), protection bits and comment (see UAE Metadata).
  - Several entries: the name is the count (`FormatCount`, e.g. `3 Files, 2 Folders`), `Type` is `All of type ...` or `Multiple types`, `Location` is `All in ...`, plus the size lines and `Contains`.
  - Drives in the drive list: the name is the label and drive, e.g. `Windows (C:)`, with `Type` (`Local Disk`, `CD Drive`, ...), `Drive`, `File system` (or `Status: Not ready`), `Used space`, `Free space` and `Capacity`; `UsedFraction` is shown as a bar. Several drives are summed.
- Sizes use `SizeFormatter.FormatWithBytes` like Explorer, e.g. `1.17 MB (1,234,567 bytes)`.
- When `EntryProperties.HasContents`, the dialogs count the size and contents with `NavigationContext.CountContentsAsync` on a background thread while open and update the values (`Calculating...` until the first report); closing the dialog cancels counting. Local directories are read from the file system (`FileSystemDetails.CountContents`, skipping inaccessible directories and not following directory links; the size on disk on Windows is the compressed size rounded up to clusters), other locations with a volume of their own from `OpenLocationAsync` (`VolumeContents.CountContentsAsync`, `Hst.Commander.Core.Volumes`). `EntryContents` holds the size, size on disk (null when unknown), files, folders and `IsComplete`, reported at most every 100 ms and when done. `Contains` counts the entries within directories, plus the entries themselves when several are selected. The number of lines doesn't depend on the contents, so the dialogs only update the values. The properties are read-only (no attribute changes or renaming).

### Navigation Model

Each UI pane has a `NavigationContext` that owns a `NestedVolume` over a root volume from a volume provider. It starts with `VolumeProviders.Local` at the initial path; `NavigationContext.ChangeVolumeProviderAsync(provider)` (Alt+F1/Alt+F2) creates a new root volume from the provider, closes opened images and the previous root volume (when disposable) and clears marks. `NavigationContext.VolumeProvider` is the provider shown. `CurrentPath` at the root is `Computer` for the local drive list and the provider's `Name` otherwise. Local-only features (disk space, starting files, executables) need a `LocalVolume` root. Browsing into a directory calls `ChangeDirectory` on the current volume.

#### Images

Opening a file (Enter or double-click in both front-ends, `NavigationContext.OpenEntryAsync`) first detects whether it is an image, a file browsed as a volume, by its magic signature, asking all file volume providers regardless of the extension, so e.g. a `.nupkg` package, `.jar` or `.docx` document opens as a zip archive; a file no provider detects is started (see Enter on Files). Opening it as a volume (Ctrl+PageDown or File > Open as volume in both front-ends, `NavigationContext.OpenAsVolumeAsync`, like Total Commander) asks the file volume providers the same way, also when Enter is set to start files with the host OS, and throws `IOException` (`'name' can't be opened as a volume`) when none detects it; directories and `..` are opened like Enter. Images are detected in any location (local directories, other volume providers and inside images, so e.g. a zip archive inside an ISO image or another zip archive opens too):
1. The file volume providers (`NavigationContext` constructor's optional `fileVolumeProviders`, default `VolumeProviders.Files`) are asked in order with `IFileVolumeProvider.IsMatch`, reading the file with `IVolume.OpenRead` of the current volume. A seekable stream is reset to the start for the next provider, otherwise the file is opened again. Files that can't be read (`IOException`, `UnauthorizedAccessException`, `NotSupportedException`, e.g. locked) are not images.
2. The first matching provider's `CreateVolume` gets the file opened again as a seekable stream (writable local files of providers that can write are shared, see Writable Images); a stream that isn't seekable (e.g. a file inside a zip archive) is first copied to a temporary file deleted on close.
3. The volume is wrapped in an `EntryVolumeProvider` and pushed with `nestedVolume.PushVolume`, and the image (provider, file name, directory of the file inside the image it's in, volume and stream) is pushed on the `NavigationContext`'s image stack, used by `OpenLocationAsync` to open the images again. `NavigationContext.IsInImage` is true while an image is open.

Going up when at the root of an image calls `PopVolume()` and disposes the image's volume and stream. Changing the volume provider or location closes all images.

#### Writable Images

- A file volume provider with `IFileVolumeProvider.CanWrite` (the Amiga plugin's adf and RDB providers) gets a writable stream for a local file opened directly from a local directory (not inside another image). `SharedImages` (`Hst.Commander.Core`, internal) opens such a file once per full path (case-insensitive on Windows and macOS) with `FileAccess.ReadWrite`, `FileShare.ReadWrite` and no buffering (changes are in the file right away), falling back to read-only (e.g. a read-only file), and shares the volume between both panes and the volumes of file operations from `OpenLocationAsync`: two volumes writing the same file would each keep their own file system state (e.g. free blocks) and damage it. Each user gets a `SharedImageVolume` view with its own current directory; every operation (and each read/write of its streams, `LockedStream`) holds the image's `SemaphoreSlim` and changes the shared volume to the view's directory first, so a copy on a background thread and the pane are serialized. The operations run on the thread pool while holding the lock, so the lock is never held by work waiting for a blocked UI thread. The file is closed when the last view is disposed.
- Other images of such providers (inside other images, e.g. an adf inside a zip archive or on an RDB partition, copied to a temporary file when not seekable) get a `ReadOnlyStream` (`Hst.Commander.Core.Volumes`) and are read-only, as changes would be lost.
- `OpenedImage.IsWritable` (shared and the file opened for writing) decides `CreateDirectoryNotSupportedMessage`/`MoveNotSupportedMessage` (`... not supported in a read-only image`) inside images. Copying into, deleting, renaming and making directories work in writable images like in local directories.
- `LocalVolume.OpenRead` shares the file with writers (`FileShare.ReadWrite`), so an open writable image can still be detected in the other pane or copied.

#### Going to the Root

Ctrl+\ in a pane of both front-ends calls `NavigationContext.GoRootAsync`, going to the root of the current volume: the local drive root (e.g. `c:\temp\test` goes to `c:\`, `/home/user` goes to `/`; a new `LocalVolume` at `Path.GetPathRoot`), the root of the image when inside one (pressing again at the image root closes the images and goes to the root of the drive or volume they are in) or the root of another volume provider's volume. The location is unchanged in the drive list or at the root. Either way the first entry (`..` when there is a parent) is selected once by `GetSelectedIndex`, also when the location is unchanged.

With Enter set to start files with the host OS (see Enter on Files), local files are started without detecting images. With Enter, a file no file volume provider detects (e.g. a text file named `text.zip`) is started as usual. Paths typed in the location dialog are detected like opening as a volume, regardless of the extension.

Opening any other local file (Enter or double-click in both front-ends) calls the `NavigationContext` constructor's optional `startFile` callback with the full path, defaulting to `FileStarter.StartWithShell` (`Process.Start` with `UseShellExecute`, working directory = the file's directory): programs are executed and other files open in their associated application. Files inside images are not started. The GUI uses the default; the TUI runs terminal programs itself (see Adding TUI Features). Errors (e.g. no associated application) are shown like navigation errors.

`NavigationContext.IsExecutable(entry)` tells whether a listed local file is executable, updated by `ListEntriesAsync`: on Windows by extension (`ExecutableExtensions`: `.exe`, `.com`, `.bat`, `.cmd`, `.msi`; `IsExecutableName`), on other systems by the execute permission (`FileStarter.HasExecutePermission`). Directories and files inside images are never executable. Both front-ends draw executables in green.

### Changing Location

Ctrl+L, double-clicking the path of a pane or File > Location opens the location dialog of a pane (the active pane for Ctrl+L and the menu) in both front-ends: a path field filled with `NavigationContext.CurrentPath`, a `Volumes...` button opening the volume provider list of the pane (choosing one closes the location dialog and shows that provider with `ChangeVolumeProviderAsync`), OK and Cancel. The result is a `LocationDialogResult` (path or volume provider). OK calls `NavigationContext.ChangeLocationAsync(path)`, which leaves the location unchanged when the path fails (the error is shown) or equals the current path, and resolves:
- empty, or `Computer` in a local volume: the local drive list;
- a local directory (relative to the current local directory; `c:` means `c:\`), switching to the Local volume provider;
- a local path going into an image, e.g. `c:\images\disk.iso\dir1` or `c:\images\disk.iso\archive.zip\dir1` (the closest existing file in the path must be detected as an image, the remaining path components are directories or images; opened in a `NavigationContext` of its own so the location is unchanged when it fails);
- otherwise, when a non-local volume provider is shown and the path isn't rooted, a path from the root of a new volume of that provider (its `Name` is the root).

### Volume Provider Keys

The keys opening the volume provider list of the left/right pane are chosen in File > Settings of both front-ends (`Volume keys`): `VolumeProviderKeys.AltFunctionKeys` (Alt+F1/Alt+F2) or `ShiftFunctionKeys` (Shift+F1/Shift+F2). `TuiSettings`/`GuiSettings.VolumeProviderKeys` is null until chosen, and `EffectiveVolumeProviderKeys` then falls back to `VolumeProviderKeyBindings.Default`, the best fit for the host OS: Alt on Windows, Shift elsewhere (Linux desktops take Alt+F1/Alt+F2, e.g. Alt+F2 opens the run dialog on GNOME/KDE/XFCE, and macOS terminals only send Alt with Option set as Meta key). `WithVolumeProviderKeys` keeps the setting unset when the keys already in effect are chosen. The settings dialogs mark the OS default with `(default)`; the File menu shows the keys in effect (`GetLeftKeyText`/`GetRightKeyText`) and changes apply on OK.

### Enter on Files

What opening a file with Enter or double-click does is chosen in File > Settings of both front-ends (`Enter on file`): `EnterFileAction.OpenAsVolume` (`Open as volume`, the default: files detected by any file volume provider of the built-in volume types and plugins are browsed as images regardless of their extension, e.g. `.nupkg` as a zip archive, and files none of them detects fall back to being started by the host OS, see Images) or `StartWithHost` (`Start with host OS`: local files are always started with the `startFile` callback without detecting images, e.g. a zip archive opens in its associated application; files inside images can't be started and are still opened as images when detected; Ctrl+PageDown still opens a file as a volume). `EnterFileActions` (`Hst.Commander.Core`) lists the actions and their names. `TuiSettings`/`GuiSettings.EnterFileAction` is stored in the settings file and both front-ends set `NavigationContext.EnterFileAction` of both panes at startup and on OK in the settings dialog.

### Versioning and About

- Like Hst Imager, `src/Directory.Build.props` is the single source of truth for the version (`<Version>`, e.g. `0.1.0`) and sets `Product` (`Hst Commander`), `Authors`/`Company` and `Copyright` for all projects. `src/Directory.Build.targets` (`SetVersionFromGitCommitCount`) replaces the last version part with `git rev-list --count HEAD` (e.g. `0.1.29`), unless the version is passed with `-p:Version=...`; the .NET SDK appends the commit sha to the product version (`0.1.29+98d7be8...`). Bump the major/minor version in `Directory.Build.props`.
- GitHub workflows (like Hst Imager): `build-and-test.yml` runs `dotnet build`/`dotnet test` on Windows, macOS and Linux for every push. `build-and-release.yml` (run manually) publishes `Hst.Commander.Tui` and `Hst.Commander.Gui` as self-contained, single-file, ReadyToRun executables (renamed `hst.commander`/`hst.commander.gui`), passing `-p:Version=<major>.<minor>.<commit count>`, and packages them with the scripts in `src/packaging`: on Windows (`win-x64`, `win-x86`, `win-arm64`) an Inno Setup installer (`setup.iss`, installed if missing) and a portable zip; on macOS (`osx-x64`, `osx-arm64`, built on a macOS runner to sign ad hoc) a `.dmg` with the GUI as `Hst Commander.app` in a styled window to drag to Applications (`create-dmg` from brew; icon positions match the arrow in `dmg-background.svg`) and one with the TUI executable (`hdiutil`); on Linux (`linux-x64`, `linux-arm`, `linux-arm64`) an AppImage (appimagetool 1.9.1) and a `.deb` (packages `hst-commander`/`hst-commander-gui`, installed in `/usr/lib/<package>` with a `/usr/bin` link, desktop file and icon). It creates a draft prerelease tagged with the version with files named e.g. `hst-commander_v0.1.29-98d7be8_tui_windows_x64_setup.exe`, `..._gui_macos_arm64.dmg` or `..._gui_linux_amd64.deb`. Windows files are signed with Azure Artifact Signing (`azure/artifact-signing-action`, logged in with `azure/login` using OpenID Connect) only when the repository variable `AZURE_SIGNING_ENABLED` is `true`: the published executable before packaging (so the portable zip and installer contain it signed) and the setup `.exe` after; it needs the variables `AZURE_SIGNING_ENDPOINT`, `AZURE_SIGNING_ACCOUNT_NAME`, `AZURE_SIGNING_CERTIFICATE_PROFILE_NAME` and the secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.
- `AppInfo` (`Hst.Commander.Core`) reads the name (`AssemblyProductAttribute`), author (`AssemblyCompanyAttribute`) and version (`Major.Minor.Build`) of the entry assembly: `AppInfo.Current`, `NameAndVersion` (e.g. `Hst Commander v0.1.29`) and `AboutText`.
- The GUI main window title is `AppInfo.NameAndVersion` (e.g. `Hst Commander v0.1.29`).
- Both front-ends have a Help menu with About: the TUI shows `AppInfo.AboutText` with `MessageBox.Query`, the GUI shows the modal `AboutDialog`.

### Path Conventions

- `Paths.Root` = `"/"`
- `Paths.Parent` = `".."`
- `CurrentPathComponents` returns path segments as a `string[]`, not a joined string.
- The empty array represents the root of a volume.

---

## Current Status

**Working:**
- The Terminal.Gui v2 TUI and Avalonia 12 GUI dual-pane browsers, with per-pane navigation and current-directory startup.
- Local drive/directory listing, parent navigation, and read-only browsing of ISO images, zip archives (the default Zip plugin) and lha/lzx archives (the default LhaLzx plugin) as nested volumes, detected by their magic signature, also inside other images.
- Reading and writing MBR and GPT disk images (raw or vhd) with FAT and NTFS partitions, PiStorm RDB partitions (MBR type `0x76`, GPT type `3F82EEBC-87C9-4097-8165-89D6540557C0`) and hybrid MBR/RDB disks with the default Disk plugin, working with the Amiga plugin through `IPluginHost`.
- Reading and writing Amiga disk files (adf, Fast File System) and the partitions (Fast File System, PFS3) of Amiga hard disk images with a rigid disk block (raw or vhd) with the default Amiga plugin, shared between panes and file operations (see Writable Images).
- Starting/executing local files with Enter or double-click, with executables drawn in green.
- GUI entry icons: Amiga .info icons (the default Amiga plugin's icon provider) for the .info file and the entry it belongs to, icons contained in programs and .ico files (also inside images), otherwise the host OS icons for file types, folders and drives.
- The volume abstraction layer and implementations: `LocalVolume`, `Iso9660Volume`, `ZipVolume`, `LhaVolume`, `LzxVolume`, `NestedVolume`, `EntryVolumeProvider`, and `NullVolume`.
- All unit tests in `Hst.Commander.Core.Tests`.

- Going to the root of the volume with Ctrl+\ in both front-ends.
- Changing location with Ctrl+L or by double-clicking the path (location dialog), including paths into images.
- Build versioning from the git commit count and a Help > About menu item in both front-ends.
- Choosing the volume provider of the left/right pane with Alt+F1/Alt+F2 or Shift+F1/Shift+F2 (configurable, see Volume Provider Keys) or File > Left/Right volume from `VolumeProviders.All`; only the built-in Local volume provider exists so far.
- Copying (F5) between panes and volumes, with a confirm dialog (destination, filter) and a progress dialog.
- Keeping Amiga names, protection bits and comments when copying or moving between Amiga volumes (and from lha/lzx archives), and storing/reading them as UAE metadata (UAEFSDB or FS-UAE metafiles, chosen in the settings) when copying between Amiga volumes and other volumes, shown in the properties dialog.
- Moving (F6) between panes and volumes, with the same confirm and progress dialogs as copying; a single entry moved to a plain name is renamed.
- Making a directory (F7) in the active pane, with a dialog for the directory name.
- Creating volumes (F9) with the volume creators of plugins, e.g. empty, formatted or bootable Amiga disk files with the Amiga plugin.
- Deleting (F8 or Delete) the marked entries or the entry under the cursor, with a confirm dialog and a progress dialog.
- Showing the properties of entries and drives (Ctrl+P, Alt+Enter or File > Properties), with the size and contents counted in the background.
- Loading plugins at startup from `~/.hst-commander/plugins` and `plugins` next to the executable in both front-ends, showing load errors.
- Selecting (marking) or unselecting a group of entries with + or - (File > Select group / Unselect group), using wildcards or a regular expression, and inverting the selection with * (File > Invert selection).

**Not implemented:**
- View (F3), edit (F4) and other file-management operations.
- Writing to images other than Amiga adf and RDB images; ISO images and zip, lha and lzx archives are read-only, as are Amiga images inside other images.
- Changing RDB, MBR and GPT partitions, other Amiga file systems (e.g. SFS), other PC file systems (exFAT, ext, ...) and vhdx/vmdk images.
- Unloading or reloading plugins while running, and a list of loaded plugins in the UI.
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
- Abstractions: `Hst.Commander.Abstractions` (interfaces, `Paths`)
- Zip plugin: `Hst.Commander.Plugins.Zip`
- Lha and lzx plugin: `Hst.Commander.Plugins.LhaLzx`
- Disk plugin: `Hst.Commander.Plugins.Disk`
- Amiga plugin: `Hst.Commander.Plugins.Amiga`
- Core: `Hst.Commander.Core` (`AppInfo`, `EmbeddedIconReader`, `EnterFileAction`, `EntryProperties`, `FileSystemDetails`, `IconImage`, `NavigationContext`, `NameFilter`, `PluginHost`, `PluginLoader`, `Plugins`, `SelectGroupOptions`, `FileStarter`, `SettingsPaths`, `SizeFormatter`, `UaeMetadataFormat`, `VolumeCreation`, `VolumeCreatorInput`), `Hst.Commander.Core.Volumes` (volumes, copying, UAE metadata)
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
5. **To make it selectable with Alt+F1/Alt+F2**, implement `IVolumeProvider` (see `LocalVolumeProvider`) and add it to `VolumeProviders.All`.
6. **Or open files as the volume** (images): implement `IFileVolumeProvider` with `IsMatch` checking the file's magic signature (see `Iso9660FileVolumeProvider` and `ZipFileVolumeProvider`) and add it to `VolumeProviders.Files`, or return it from `IPlugin.FileVolumeProviders` of a plugin. `NavigationContext` then opens matching files as images (see Images). Return true from `CanWrite` when the volume can write to the file (see Writable Images and `AdfFileVolumeProvider`).

7. **To create new volumes as files with F9**, implement `IVolumeCreator` (see `AdfVolumeCreator`) with its input fields and return it from `IPlugin.VolumeCreators` with `IPlugin.CanCreateVolumes` true (see Creating Volumes).

8. **To show icons from icon files next to entries** (like Amiga `.info` files), implement `IIconProvider` (see `AmigaIconProvider`) and return it from `IPlugin.IconProviders` (see Entry Icons).

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

### Test Helpers

- `TestVolume` (`Hst.Commander.Core.Tests/TestVolume.cs`): in-memory `IVolume` backed by a `Dictionary<string, List<TestEntry>>`. Supports `..` in `ChangeDirectory`, reading/writing `TestEntry.Data`, creating, deleting and renaming entries; `GetEntry(pathComponents)` looks up an entry for assertions.
- `TestVolumeProvider` (`Hst.Commander.Core.Tests/TestVolumeProvider.cs`): `IVolumeProvider` creating a new `TestVolume` filled by a setup action on each `CreateVolume`.
- `TestEntry` (`Hst.Commander.Core.Tests/TestEntry.cs`): in-memory `IEntry` with optional `byte[]? Data`.
- Use `AddEntry(pathComponents, entry)` to build the in-memory tree.
- `ZipTestData` (`Hst.Commander.Core.Tests/ZipTestData.cs`): creates zip archives in memory (`CreateZip((name, content)...)`, names ending with `/` are directories).
- `AmigaTestData` (`Hst.Commander.Core.Tests/AmigaTestData.cs`): creates a formatted adf (`CreateAdf`), raw and dynamic vhd RDB images with Fast File System (`Dos3DosType`) and PFS3 (`Pfs3DosType`) partitions (`CreateRdbImage`, `CreateVhdRdbImage`) and lists an adf mounted again (`ListAdfNames`).
- `SingleThreadSynchronizationContext` (`Hst.Commander.Core.Tests/SingleThreadSynchronizationContext.cs`): `Run(operation, timeout)` runs an operation with continuations on one thread like a UI thread, throwing `TimeoutException` on a deadlock.
- `AmigaIconTestData` (`Hst.Commander.Core.Tests/AmigaIconTestData.cs`): creates Amiga .info icons with a planar image (`CreatePlanarIcon`, optionally with a selected image), an 8 color planar image (`CreateEightColorPlanarIcon`) or a NewIcon image (`CreateNewIcon`).
- `DiskTestData` (`Hst.Commander.Core.Tests/DiskTestData.cs`): creates an MBR disk with a FAT partition (`CreateMbrDisk`), a GPT disk with FAT and NTFS partitions (`CreateGptDisk`), GPT and MBR disks with 4096 byte sectors (`CreateLargeSectorDisk`), an MBR disk with a small partition that would also fit with 4096 byte sectors (`CreateSmallPartitionMbrDisk`), PiStorm MBR and GPT disks with a `0x76` or `3F82EEBC-...` partition holding a rigid disk block (`CreatePiStormDisk`, `CreateGptPiStormDisk`, using `AmigaTestData.WriteRdbToStream`) and a hybrid disk with the rigid disk block in block 2 and an MBR in block 0 (`CreateHybridDisk`).
- `ArchiveTestData` (`Hst.Commander.Core.Tests/ArchiveTestData.cs`): lha and lzx archives in `TestData/Archives` (from the Hst.Compression tests), e.g. `dirs-files.lha`/`.lzx` (`ArchiveTestData.DirsFiles`), and `special_chars.lha`/`.lzx` (from the Hst Imager tests) with Amiga names, protection bits and comments.

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
- After navigation both front-ends pick the selected entry with `NavigationContext.GetSelectedIndex`, passing the path shown before the refresh: navigating into a directory (or image) selects the first entry; going to the parent selects the directory (or image/drive) that was left, held in `NavigationContext.PreviousEntryName`, e.g. going up from `c:	emp\dir1` selects `dir1`; when the location is unchanged (e.g. opening a plain file) the selection is kept.
- The current row (cursor) is only drawn in the active pane (`_activePane`, the pane last focused; `GetRowColorScheme`), so it stays visible while a dialog or the menu has the focus.
- Entries are selected (marked) with Insert or Space, which toggles the mark of the entry under the cursor and moves the cursor down. Marked entries are drawn in yellow (also under the cursor). The marks live in `NavigationContext` (`ToggleMark`, `IsMarked`, `MarkedEntries`, `MarkedSize`, `MarkedSummary`; shared by both front-ends): `..` can't be marked, marks are cleared when the location changes and `ListEntriesAsync` drops marks of entries no longer listed. In code "marked" is used to avoid confusion with the selected row (cursor).
- Key bindings are attached via `tableView.KeyDown += async (_, key) => { ... }`.
- Up/Down move the selected row by one entry per key event; holding a key uses the OS/terminal's repeat rate. PageUp/PageDown move by one visible page and Home/End jump to the first/last entry. Ctrl+PageDown (panes' `KeyDown`, checked before PageDown) opens the entry under the cursor as a volume with `NavigationContext.OpenAsVolumeAsync` through `RunNavigationAsync` (see Images).
- A `MenuBar` at the top of `MainWindow` (panes start at row 1) has a File menu: Copy (F5), Move/rename (F6), Make directory (F7), Delete (F8 or Delete), Create volume (F9), Properties (Ctrl+P), Open as volume (Ctrl+PgDn), Select group (+), Unselect group (-) and Invert selection (*) act on the active pane (the pane last focused), Left volume and Right volume (showing the configured volume provider keys as `MenuItem.Help`) choose a pane's volume provider, Location (Ctrl+L) opens the location dialog of the active pane, Settings opens the modal `SettingsDialog` and Quit calls `Application.RequestStop()`, exiting the application. A Help menu has About (see Versioning and About). F10 (`MenuBar.Key`, as F9 creates a volume) or Alt+F opens the menu.
- A `FunctionKeyBarView` spans the full width below the panes (panes use `Dim.Fill(1)`) with the items `F3 View`, `F4 Edit`, `F5 Copy`, `F6 Move`, `F7 Mkdir`, `F8 Delete` and `F9 Create`, each getting an equal share of the width; the key is drawn in `TuiPalette.FunctionKey` and the title in `TuiPalette.Menu`. Clicking `F5 Copy`, `F6 Move`, `F7 Mkdir`, `F8 Delete` or `F9 Create` copies, moves, makes a directory, deletes or creates a volume in the active pane like the File menu; the other items have no action yet.
- F5 copies the marked entries (or the entry under the cursor) to the other pane: `CopyDialog` confirms the destination (default: the other pane's path) and filter (default `*`), then `NavigationContext.CopyAsync` runs on a background thread (`Task.Run`) while the modal `CopyProgressDialog` shows the current file and total progress bars, speed and ETA. Progress is marshalled with `Application.Invoke`; existing files are confirmed with `MessageBox.Query` (Yes, No, All; Esc answers No) invoked on the UI thread from the background copy through a `TaskCompletionSource`. Cancel or Esc cancels the copy and the dialog stays open until the copy has stopped. Errors are shown with `MessageBox.ErrorQuery`, both panes are refreshed afterwards.
- F6 moves the marked entries (or the entry under the cursor) like F5 copies: `CopyDialog` titled `Move`, then `NavigationContext.MoveAsync` in a `CopyProgressDialog` titled `Move` (see Moving and Renaming). When `GetRenameNameAsync` gives a new name, the entry is renamed with `RenameAsync` through `RunNavigationAsync` without a progress dialog, which selects it; the other pane is refreshed too. Where entries can't be moved, `MoveNotSupportedMessage` is shown with `MessageBox.ErrorQuery` instead of the dialog.
- F7 opens the modal `MakeDirectoryDialog` (name field, OK, Cancel; Esc cancels) and creates the directory with `NavigationContext.CreateDirectoryAsync` (see Making Directories) through `RunNavigationAsync`, which selects it; the other pane is refreshed too. Where directories can't be created, `CreateDirectoryNotSupportedMessage` is shown with `MessageBox.ErrorQuery` instead of the dialog.
- F9 (panes' `KeyDown`) creates a volume (see Creating Volumes): the modal `CreateVolumeDialog` (a `ListView` of the volume creators, centered) and then `CreateVolumeFieldsDialog` (a `TextField` for the file name and each text/number field, a `CheckBox` for each boolean field, OK, Cancel; Esc cancels), creating with `NavigationContext.CreateVolumeAsync`; errors are shown with `MessageBox.ErrorQuery` before asking again. Both panes are refreshed afterwards, selecting the created file. Where volumes can't be created, `GetCreateVolumeNotSupportedMessage` is shown instead of the dialog.
- F8 or Delete deletes the marked entries (or the entry under the cursor) after confirming with `MessageBox.Query` (Yes, No; Esc answers No), then `NavigationContext.DeleteAsync` runs on a background thread while the modal `DeleteProgressDialog` shows the current entry and a total progress bar, same pattern as copying (progress via `Application.Invoke`, Cancel or Esc cancels and the dialog stays open until the delete has stopped). Errors are shown with `MessageBox.ErrorQuery`, both panes are refreshed afterwards.
- Ctrl+P or Alt+Enter (panes' `KeyDown`) or File > Properties opens the modal `PropertiesDialog` (see Properties): the name, a `Line` above each section, label and value `Label`s, a `ProgressBar` for the used space of drives and OK (Esc closes). `NavigationContext.CountContentsAsync` runs with `Task.Run` and reports with `Application.Invoke` to `PropertiesDialog.Update`; a counting error is shown with `MessageBox.ErrorQuery` over the dialog. Ctrl+P is the main key (shown in the menu), as Windows Terminal and the classic console use Alt+Enter for full screen; Alt+Enter still works where the terminal passes it on.
- `+` and `-` (panes' `KeyDown`, `key.AsRune`) open the modal `SelectGroupDialog` titled `Select group`/`Unselect group` (filter field, `Case sensitive` and `Files only` `CheckBox`es, OK, Cancel; Esc cancels) and mark or unmark the matching entries with `NavigationContext.MarkGroup`/`UnmarkGroup`; `*` inverts the marks with `InvertMarks` (see Selecting Groups). The pane and its marked-entries summary are redrawn afterwards.
- Ctrl+\ (panes' `KeyDown`, `IsGoRootKey`) goes to the root of the volume with `NavigationContext.GoRootAsync` through `RunNavigationAsync` (see Going to the Root). Terminals send Ctrl+\ as the control character `0x1C`, also sent for Ctrl+4, so Ctrl+4 and the bare `0x1C` key code match too. The Windows driver decodes Ctrl with the key's character in the current layout, so Ctrl+`<` matches too: the key left of Z (`\` on US layouts, like `Key.OemBackslash` in the GUI) is `<` on e.g. Danish and German layouts.
- Ctrl+L (panes' `KeyDown`) and double-clicking the path line (`PathPanelView.PathDoubleClicked`, raised from `OnMouseEvent`) open `LocationDialog` for that pane; its Volumes button runs the `VolumeProviderDialog` on top (see Changing Location).
- The volume provider keys (Alt+F1/Alt+F2 or Shift+F1/Shift+F2, see Volume Provider Keys; handled in the panes' `KeyDown`, so from either pane) open the modal `VolumeProviderDialog` for the left/right pane, titled `Left`/`Right` and centered over that pane: a `ListView` of `VolumeProviders.All` (`Name  Description`) with the pane's current provider selected; Enter or double-click chooses, Esc cancels. The chosen provider is shown with `NavigationContext.ChangeVolumeProviderAsync` and the pane gets the focus.
- Three themes, selected in File > Settings, same as the GUI: **Midnight Commander** (default), **Light** and **Dark**. Colors come from `TuiPalette` (16 legacy colors); `MainWindow.ApplyTheme` reapplies them to the menu bar and panes. Midnight Commander uses blue panes with directories in white, files in gray, executables in bright green, marked entries in yellow, the current row in black on gray and a cyan menu bar.
- Settings are stored in `~/.hst-commander/tui-settings.json` (`SettingsPaths.Directory`, `TuiSettings.FilePath`), separate from the GUI settings. A missing or invalid file falls back to defaults. The settings dialog uses a `RadioGroup` for the theme, one for the volume provider keys and one for what Enter does with files (see Enter on Files) and one for the UAE metadata format (see UAE Metadata), previews theme changes live, saves on OK and reverts on Cancel/Esc.
- Directory names have no slash prefix.
- Opening a program that runs in the terminal (`FileStarter.IsTerminalProgram`: on Windows console-subsystem `.exe` (`FileStarter.IsConsoleExecutable` reads the PE header), `.com`, `.bat`, `.cmd`; elsewhere files with execute permission) suspends the TUI like Midnight Commander: `MainWindow.StartFile` requests a `TerminalAction.RunProgram`, which stores the action and stops the window with `Application.RequestStop`. `Program.cs` loops: `TerminalScreen.EnterTui`, `Application.Init`, refresh both panes, `Application.Run(mainWindow)`, `Application.Shutdown`, `TerminalScreen.LeaveTui`, then does `MainWindow.TakePendingTerminalAction` (null when quitting) and runs the same `MainWindow` again. `TerminalScreen.Run` runs the program in the terminal in its directory below the previous output (Ctrl+C is ignored by Hst Commander) and returns directly when it exits, without a pause; an error starting it is shown with `MessageBox.ErrorQuery` once the window is loaded again (`MainWindow.ShowErrorWhenLoaded`). The `Loaded` event focuses the active pane on every run. Other files, including GUI programs on Windows, are started with the shell without suspending.
- Ctrl+O shows the output screen like Midnight Commander (`TerminalAction.ShowOutput`): the TUI is shut down the same way and `TerminalScreen.ShowOutput` waits for any key before the TUI returns. TUI only, the GUI has no terminal. The output screen is kept because Windows Terminal and Unix terminals draw the TUI on the alternate screen; the classic Windows console (no `WT_SESSION`) draws it on the standard output's screen buffer, so `EnterTui` gives the TUI a screen buffer of its own there (`CreateConsoleScreenBuffer` + `SetStdHandle`). `LeaveTui` restores the standard output handle and makes its screen buffer active again, also needed because Terminal.Gui's `WindowsConsole.Cleanup` activates a new empty buffer on shutdown.
- Window-level key bindings use `this.KeyUp`.
- Dialogs: `new Dialog { Buttons = [okButton] }`, run with `Application.Run(dialog)`.

## Adding GUI Features

The GUI uses **Avalonia 12** with the Fluent theme and plain code-behind (no MVVM framework).

Key GUI patterns already in use:
- `PaneView` (UserControl) holds a `NavigationContext`, a bordered panel above the pane with the path badge on the first line and, on the second line, the marked-entries summary (`NavigationContext.MarkedSummary`, empty when null, i.e. no entries are marked) left-aligned and the disk space (`NavigationContext.DiskSpace`, empty when null, e.g. in the drive list) right-aligned; both stay visible with empty text so the panel keeps its height, and a bordered pane with a column header row and a `ListBox` of `EntryItem` rows. The name column fills the remaining width; size and modified columns have fixed widths and long names are trimmed with an ellipsis.
- Column dividers are drawn by a non-hit-testable grid behind the list, so they continue below the last entry.
- The left pane has focus on startup. `PaneView.FocusEntries` focuses the selected item's container (selecting the first entry if none is, and realizing it with `UpdateLayout` if needed); the `ListBox` itself is not focusable, and arrow keys only move the selection while an item has focus.
- Up/Down use the `ListBox` built-in navigation. PageUp/PageDown (one visible page) and Home/End are handled in `PaneView`, because the list's `ScrollViewer` would otherwise consume them and only scroll the view without moving the selection. Enter (or double-click) opens the selected entry, Ctrl+PageDown opens it as a volume (`PaneView.OpenSelectedEntryAsVolumeAsync`, see Images), Backspace goes to the parent and Ctrl+\ to the root of the volume (`PaneView.IsGoRootKey`: the `\` key symbol, `Key.OemPipe` or `Key.OemBackslash` with Ctrl, see Going to the Root); all are handled with a tunnelling `KeyDown` handler on the list.
- Each row shows a 16x16 icon left of the name (`EntryItem.Icon`). Rows start with the cached type icon (or the built-in `FileIcons.DefaultFolderIcon`/`DefaultFileIcon`), then `PaneView.LoadIcons` runs `FileIcons.LoadAsync` after each listing (cancelled when the pane navigates or lists again) with the icon size in pixels (16 × render scaling):
  1. Type icons from the host OS (`IFileIconProvider`), cached per extension, folder and drive: Windows shell icons (`SHGetFileInfo` with file attributes, so files don't need to exist), freedesktop.org icons on Linux (extension → MIME type from `mime/globs2`, icon name from `mime/icons`, `<type>-<subtype>`, `mime/generic-icons`, `<media>-x-generic`; looked up as PNG in the current icon theme from `gsettings`/`kdeglobals`, the themes it inherits, common themes, `hicolor` and `pixmaps`; SVG-only icons are not supported) and NSWorkspace icons on macOS (called on the UI thread). Drives in the drive list get their own drive icon. Without an OS icon the built-in icons are kept.
  2. Icons of plugin icon files (see Entry Icons, e.g. Amiga `.info` icons), also for directories, and the icons inside volume entries (e.g. the `Disk.info` of an RDB partition, shown as the partition's disk icon): `NavigationContext.HasIcon` and `ReadIconAsync`, cached by the icon file's location, size, modified date and pixel size, so an entry and its icon file share the bitmap (icons inside volume entries are read on each listing, not cached). Shown instead of the icons below; the icons have their own size and are scaled into the 16x16 image keeping their aspect ratio.
  3. Icons contained in files: `EmbeddedIconReader` (`Hst.Commander.Core`) reads the first icon group of a Windows executable (`.exe`, `.scr`) or an `.ico` file without OS support, choosing the smallest image at least the wanted size with the most colors, as an `IconImage` (PNG data or BGRA pixels, DIBs decoded with the AND mask). Local files are read on a background thread by `NavigationContext.GetLocalPath`; files inside images are read with `NavigationContext.OpenReadAsync` on the UI thread, as the pane's volume is not thread safe. When a local file has no icon of its own and the OS shows it with one (`IFileIconProvider.HasFileIcon`: on Windows programs, e.g. system programs with icons in `C:\Windows\SystemResources`, shortcuts `.lnk`/`.url`; `.icns` on macOS), the OS icon of the file is used. File icons are cached by path, size and modified date.
- `EntryItem.IsExecutable` adds the `executable` class to the row, styled with `ExecutableForeground` (green); the current row and marked styles come after it.
- Insert or Space toggles the mark of the current entry (via `NavigationContext.ToggleMark`) and moves down. `EntryItem.IsMarked` notifies changes and adds the `marked` class to the row, styled with `MarkedEntryForeground` (yellow) after the current-row style so marked entries stay yellow under the cursor.
- Tab is handled at window level (tunnelling) and always switches focus between the two panes.
- The current row is only shown in the active pane: `MainWindow.SetActivePane` (on the panes' `GotFocus`) sets `PaneView.IsActive`, which adds the `active` class to the entry list; selected rows of a list without it have no background and keep their normal foreground, in all themes.
- Three themes, selected in File > Settings: **Midnight Commander** (default), **Light** and **Dark**.
  - Midnight Commander uses the same palette as the TUI: blue panes, directories in white, files in gray, executables in green, current row in black on gray and the active pane's path highlighted. Only the panes are colored: the menu, function key bar and dialogs use the normal Fluent dark styling. It is a custom `ThemeVariant` (`ThemeManager.MidnightCommanderVariant`, inheriting Dark) plus `MidnightCommanderStyles` (the current row colors only), which `ThemeManager` adds to `Application.Styles` only for this theme.
  - Light and Dark use the normal Fluent control styling (including list selection) in light/dark colors.
  - Pane colors are per-theme brushes in `App.axaml` `ThemeDictionaries` (`PaneBackground`, `PaneBorder`, `HeaderForeground`, `ContainerForeground`, `ItemForeground`, `ExecutableForeground`, `PathForeground`, `ActivePathBackground`, `ActivePathForeground`, `SelectedEntryForeground`, `MarkedEntryForeground`), referenced with `DynamicResource` so theme changes apply live.
- Settings are stored in `~/.hst-commander/gui-settings.json` (`SettingsPaths.Directory` in Core, `GuiSettings.FilePath`). A missing or invalid file falls back to defaults. The settings dialog has combo boxes for the theme, the volume provider keys, what Enter does with files (see Enter on Files) and the UAE metadata format (see UAE Metadata), previews theme changes live, saves on OK and reverts on Cancel.
- The application icon is `src/icon/hst-commander.ico`: `ApplicationIcon` of both the GUI and TUI executables, and the GUI main window `Icon`, linked as the Avalonia resource `avares://Hst.Commander.Gui/Assets/hst-commander.ico`.
- Errors are shown with `ErrorDialog` via `ShowDialog(owner)`.
- A top `Menu` docked above the panes has a File menu: Copy (F5), Move/rename (F6), Make directory (F7), Delete (F8 or Delete), Create volume (F9), Properties (Alt+Enter), Open as volume (Ctrl+PageDown), Select group (+), Unselect group (-) and Invert selection (*) act on the active pane (the +, - and * keys are shown in the headers, as key gestures would display as "OemPlus" etc.) (tracked with `GotFocus`), Left volume and Right volume (showing the configured volume provider keys as `InputGesture`) choose a pane's volume provider, Location (Ctrl+L) opens the location dialog of the active pane, Settings opens the modal `SettingsDialog` and Quit closes the main window (exiting the application). A Help menu has About, showing the modal `AboutDialog` (see Versioning and About).
- A function key bar (`UniformGrid` of non-focusable `Button.functionKey`, so the active pane keeps focus) is docked full width below the panes with `F3 View`, `F4 Edit`, `F5 Copy`, `F6 Move`, `F7 Mkdir`, `F8 Delete` and `F9 Create`. `CopyButton`, `MoveButton`, `MkdirButton`, `DeleteButton` and `CreateButton` copy, move, make a directory, delete or create a volume in the active pane like the File menu; the other buttons have no action yet.
- F5 is handled at window level (tunnelling): `CopyDialog` confirms the destination and filter, then `CopyProgressDialog` runs `NavigationContext.CopyAsync` with `Task.Run` when opened, reporting through a `Progress<CopyProgress>` created on the UI thread. Existing files are confirmed with the modal `OverwriteDialog` (Yes, No, All; closing answers No), shown over the progress dialog via `Dispatcher.UIThread.InvokeAsync`. Cancel or closing the window cancels the copy; the dialog closes when the copy has stopped and exposes the `Exception`. `PaneView.SelectedEntry` gives the entry under the cursor and `PaneView.RefreshEntriesAsync` relists a pane after copying.
- F6 is handled at window level (tunnelling) and moves like F5 copies: `CopyDialog` titled `Move`, then `CopyProgressDialog` titled `Move` running `NavigationContext.MoveAsync` (see Moving and Renaming). When `GetRenameNameAsync` gives a new name, `PaneView.RenameAsync` renames and selects the entry without a progress dialog; the other pane is refreshed too. Where entries can't be moved, `MoveNotSupportedMessage` is shown with `ErrorDialog` instead of the dialog.
- `+`, `-` and `*` are handled at window level (tunnelling, `KeyEventArgs.KeySymbol` or `Key.Add`/`Subtract`/`Multiply`): `+`/`-` show the modal `SelectGroupDialog` titled `Select group`/`Unselect group` (filter, `Case sensitive` and `Files only` check boxes, OK, Cancel; closing cancels) and `PaneView.MarkGroup`/`UnmarkGroup` change the marks; `*` calls `PaneView.InvertMarks` (see Selecting Groups). The rows' `EntryItem.IsMarked` and the summary are updated.
- Ctrl+L (window level, tunnelling, active pane) and double-clicking the path badge (`PaneView.PathDoubleClicked`) open `LocationDialog` for that pane; its Volumes button shows the `VolumeProviderDialog` owned by the location dialog. `PaneView.ChangeLocationAsync` changes to the entered path (see Changing Location).
- The volume provider keys (Alt+F1/Alt+F2 or Shift+F1/Shift+F2, see Volume Provider Keys) are handled at window level (tunnelling) and open the modal `VolumeProviderDialog` for the left/right pane, titled `Left`/`Right` and positioned over that pane when opened: a `ListBox` of `VolumeProviders.All` (name and description) with the pane's current provider selected; OK, Enter or double-click chooses, Cancel/Esc/closing cancels. `PaneView.ChangeVolumeProviderAsync` shows the chosen provider and the pane gets the focus.
- F7 is handled at window level (tunnelling): the modal `MakeDirectoryDialog` (name, OK, Cancel; closing cancels) asks for the name and `PaneView.CreateDirectoryAsync` creates and selects the directory (see Making Directories); the other pane is refreshed too. Where directories can't be created, `CreateDirectoryNotSupportedMessage` is shown with `ErrorDialog` instead of the dialog.
- Alt+Enter and Ctrl+P (same keys as the TUI) are handled at window level (tunnelling) and open the modal `PropertiesDialog` (see Properties): the entry icon (32x32) left of the name, a `Separator` above each section, a grid of labels and `SelectableTextBlock` values (paths can be copied), a `ProgressBar` for the used space of drives and OK. When opened it runs `NavigationContext.CountContentsAsync` with `Task.Run`, reporting through a `Progress<EntryContents>` created on the UI thread; closing cancels counting and an error is shown with `ErrorDialog`. The icon starts as the built-in folder/file icon and is loaded when opened with `FileIcons.LoadIconAsync` in the dialog's pixel size (type icons of other sizes than the pane icons are cached separately): the icon contained in the file or the OS type/drive icon for a single entry, the type icon for several entries of the same type (folders or one extension), otherwise the built-in icon is kept. The icon is a button (tooltip `View icon`) opening the modal `IconViewerDialog` (titled `Icon - <name>`), which loads all icons of the entry with `FileIcons.LoadIconsAsync` (passed as `loadIcons`, as `ViewerIcon`s with `ViewerIconImage`s whose bitmaps are created when shown): the icons of its plugin icon file (`NavigationContext.ReadIconsAsync`, e.g. the normal and selected images of an Amiga icon), otherwise the icons contained in the file (`EmbeddedIconReader.ReadIcons`, e.g. all icons of a Windows program or all images of an .ico file), otherwise the OS icon of the file or of its type asked in 16 to 256 pixels and kept in the distinct sizes the OS returns (e.g. 16 and 32 on Windows, the theme sizes on Linux, rendered sizes on macOS), or the built-in icon. With several icons a list on the left shows each (32 pixel thumbnail, name, size or number of images); the `Image` combo box lists the images of the selected icon (e.g. `256 × 256, 32-bit`), the largest first and selected. The image is shown on a checkerboard (transparency), fitted to the window (default, resizable) or zoomed 100%, 200%, 400% or 800% of its pixels with scroll bars. Icons up to 256 pixels are scaled without smoothing (`BitmapInterpolationMode.None`) so their pixels stay sharp. The `Background` button (swatch and name) sets the preview background, `Transparent` (the checkerboard) by default: its flyout has rows of swatches (`BackgroundRows`; the name as tooltip, clicking one applies it): transparent, Workbench gray `#AAAAAA`, Workbench 1.3 blue `#0055AA` and Midnight Commander blue `#0000AA`, then a basic color palette of six grays (black to white) and purple, navy, blue, green, yellow, orange, red, maroon and brown, each with a 60% and a 20% tint and a custom color with red/green/blue `NumericUpDown`s (0-255) and a hex `TextBox` (`#RRGGBB`, `#RGB`, `#` optional) kept in sync, applied with Apply or Enter (Apply is disabled while the hex text is invalid). The background isn't remembered. GUI only, the TUI shows no icons.
- F9 is handled at window level (tunnelling) and creates a volume (see Creating Volumes): the modal `CreateVolumeDialog` (a `ListBox` of the volume creators with name and description) and then `CreateVolumeFieldsDialog` (file name `TextBox`, a `TextBox` per text/number field and a `CheckBox` per boolean field added to `FieldsPanel` in code, OK, Cancel; closing cancels); `PaneView.CreateVolumeAsync` creates and selects the file, throwing instead of raising `NavigationFailed` so `ErrorDialog` is shown before asking again. The other pane is refreshed too. Where volumes can't be created, `GetCreateVolumeNotSupportedMessage` is shown with `ErrorDialog` instead of the dialog.
- F8 and Delete are handled at window level (tunnelling): the modal `DeleteDialog` confirms (Yes, No; closing answers No), then `DeleteProgressDialog` runs `NavigationContext.DeleteAsync` with `Task.Run` when opened, showing the current entry and a total progress bar, with the same cancel/close/`Exception` behaviour as `CopyProgressDialog`. Both panes are refreshed afterwards.

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

> **Note:** `src/Directory.Build.props` stops MSBuild from importing a `Directory.Build.props` from a parent folder, so build output lands in each project's `bin/`. A leftover `src/net10.0/` folder is old build output, ignored by `.gitignore`; do not commit build output. The build prints each project's version (`<project> version: 0.1.29`) and needs `git` on the path for the commit count (otherwise the version from `Directory.Build.props` is used).

---

## Dependencies

| Package | Version | Used in |
|---|---|---|
| `LTRData.DiscUtils` | 1.0.54 | `Hst.Commander.Core` — raw disk image support |
| `LTRData.DiscUtils.Iso9660` | 1.0.54 | `Hst.Commander.Core` — ISO 9660 reading |
| `System.Linq.Async` | 6.0.1 | `Hst.Commander.Core` — async LINQ |
| `Hst.Compression` | 0.4.96 | `Hst.Commander.Plugins.LhaLzx` — lha and lzx archive reading |
| `Hst.Amiga` | 0.6.243 | `Hst.Commander.Plugins.Amiga` — Amiga rigid disk block, Fast File System and PFS3 reading and writing, .info icons; `Hst.Commander.Core` — UAE metadata (UAEFSDB and metafiles) |
| `LTRData.DiscUtils.Vhd` | 1.0.54 | `Hst.Commander.Plugins.Amiga`, `Hst.Commander.Plugins.Disk` — vhd images |
| `LTRData.DiscUtils.Core`, `.Fat`, `.Ntfs` | 1.0.54 | `Hst.Commander.Plugins.Disk` — MBR/GPT partition tables, FAT and NTFS reading and writing |
| `Terminal.Gui` | 2.0.0-prealpha.1895 | `Hst.Commander.Tui` — TUI framework |
| `Microsoft.Build.Tasks.Git` | 10.0.402 | `Hst.Commander.Tui` — build only (`PrivateAssets="all"`), overrides the vulnerable 8.0.0 (GHSA-23fw-v26w-5fgq) pulled in by Terminal.Gui's `Microsoft.SourceLink.GitHub` 8.0.0 dependency |
| `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent` | 12.1.4 | `Hst.Commander.Gui` — desktop GUI framework |
| `xunit` | 2.5.3 | `Hst.Commander.Core.Tests` |
| `coverlet.collector` | 6.0.0 | `Hst.Commander.Core.Tests` — coverage |
