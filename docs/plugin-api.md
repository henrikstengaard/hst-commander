# Plugin API

The interfaces in `Hst.Commander.Abstractions` and how plugins are loaded. Read this when changing the plugin API or writing a plugin. The interface files are the reference for signatures; this describes the contracts they don't spell out.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Interfaces

### IVolume

- A volume has a current directory; `ListEntries`, `OpenEntry`, `OpenRead`, `OpenWrite`, `CreateContainer`, `Delete`, `Rename` and `SetModifiedDate` act on names in it. `ChangeDirectory` takes path components and handles `Paths.Root` and `Paths.Parent` case-insensitively.
- Write members throw `NotSupportedException` when read-only. `OpenWrite` creates or overwrites; `CreateContainer` is a no-op when the container exists; `Delete` only deletes items and empty containers; `Rename` throws `IOException` when the new name exists (case-only changes are allowed); `SetModifiedDate` treats an unspecified `DateTimeKind` as UTC.
- `GetWritableImageId(name)` and `OpenReadWrite(name)` (optional, default null and `NotSupportedException`): an item with an id unique across volumes (e.g. a local file's full path or a physical disk's device path) is opened once for reading and writing as a writable image shared by both panes, see [Writable images](volumes.md#writable-images). `OpenReadWrite` throws `UnauthorizedAccessException` or `IOException` when the item can only be read, and the image is then opened read-only with `OpenRead`.
- Optional members with defaults: `SupportsAmigaMetadata` (the current directory stores Amiga names, protection bits and comments as they are, see [UAE metadata](file-operations.md#uae-metadata)), `CanSetAmigaMetadata`/`SetAmigaMetadata` and `GetChangeableAttributes`/`SetAttributes` (only the changeable attributes are changed). The properties dialog uses `CanSetAmigaMetadata` and `GetChangeableAttributes` to decide what can be changed. `GetSpace` (default null) returns the free and total space (`VolumeSpace(Free, Total)`) of the file system of the current directory, read after each listing and shown as free/total in the path panel like local drives (e.g. FAT, NTFS and Amiga file systems); volumes wrapping other volumes (partition tables, RDB partitions, hybrid disks) forward it to the current one.

### IEntry

- `Name`, `Type` (`Container` or `Item`), `Size`, `ModifiedDate`.
- `DisplayName` (default `Name`): shown in the panes and at the top of the properties dialog, e.g. `1 - DH0 - Workbench`. Paths, file operations and other dialogs use `Name`.
- `Details`: `EntryDetails(TypeName, Sections, UsedFraction)` made of `EntryProperty(Label, Value)` lines, shown in the properties dialog of a single entry. Use `EntryProperty.FromBytes` for sizes.
- `IsVolume`: a container that is the root of a volume of its own (e.g. an RDB partition), whose icon can be inside it.
- `AmigaMetadata`: `AmigaMetadata(ProtectionBits, Comment)` with the bits as stored on disk (`hsparwed`; `rwed` are set when *not* allowed, so 0 is `----rwed`).
- `Attributes`: DOS/Windows `EntryAttributes` (the values of `FileAttributes`).

### IVolumeProvider

- What a pane can show (the volume provider list). Each `CreateVolume` call must return a volume of its own, because file operations open a second one on a background thread. Volumes implementing `IDisposable` are disposed when no longer used.
- `RequiresAdministrator` (optional, default false): the volumes need administrator rights (root on Linux and macOS), e.g. physical disks; choosing the provider when not elevated offers to restart Hst Commander as administrator, see [Running as administrator](application.md#running-as-administrator).
- `VolumeProviders.All` is the built-in `Local` provider followed by the plugins' providers.

### IFileVolumeProvider

- Opens files as volumes (images). `IsMatch(name, stream)` checks the magic signature; the stream is at the start and may not be seekable. `CreateVolume(stream)` gets a seekable stream.
- Hst Commander owns the stream: it stays open while the volume is used and is disposed after the volume. It is read-only unless `CanWrite` is true and the file can be written, see [Writable images](volumes.md#writable-images).
- `VolumeProviders.Files` is the built-in ISO 9660 provider followed by the plugins' providers, asked in order.

### IVolumeCreator

- Creates a new volume file with F9. `FileExtension` is added to the file name when missing. `Fields` are `VolumeCreatorField`s (`Text`, `Number` as long, `Boolean`; required, default value), read from `VolumeCreatorValues`.
- `Create(stream, values)` writes to an empty seekable, readable and writable stream and throws `IOException` for invalid values.
- Only plugins with `CanCreateVolumes` are listed in `VolumeProviders.Creators`. See [Creating volumes](file-operations.md#creating-volumes).

### IIconProvider

- `GetIconFileName(name)` finds an icon file by name only (`file.info` for both `file` and `file.info`); `GetVolumeIconFileName` gives the icon file inside a volume entry (e.g. `Disk.info`).
- `ReadIcon(name, stream, size)` checks the signature and returns null for other files; `ReadIcons` returns all icons for the icon viewer; `ReadIconProperties` returns lines for the properties dialog.
- `IconImage` is PNG data or 32-bit BGRA pixels (not premultiplied, top-down) with an optional `BitsPerPixel`; `EntryIcon(Name, Images)` is one icon with its images. See [Entry icons](icons-and-navigation-modes.md#entry-icons).

### IImageProvider

- `ReadImages(name, stream)` gets the whole file in memory (seekable) and returns `EntryImage(Name, Image)`s, empty when the file isn't one of its formats. The plugins' providers are asked before the built-in ones. See [Images in the viewer](viewer.md#images).

### INavigationMode

- `Layout(volume, entries, options)` returns a `NavigationIcon` per entry shown, in order: entries without an icon are hidden, a null image shows Hst Commander's icon of the entry, a null selected image is drawn highlighted, a null position is placed by Hst Commander and `IsMovable` false can't be dragged. Entries don't include `..`.
- `SavePositions` stores the positions of icons moved by the user.
- Both get the pane's volume and may change its directory (e.g. into a volume entry) as long as they change back before returning.
- `Options` are `NavigationModeOption`s shown as check boxes; `GetValue(options)` gives the default for a missing value. See [Navigation modes](icons-and-navigation-modes.md#navigation-modes).

### IPlugin

- Returns the plugin's volume providers, file volume providers, volume creators (with `CanCreateVolumes`), icon providers, navigation modes and image providers. `Initialize(host)` is called once before the plugin is used.
- A plugin references only Abstractions. Plugins can't use each other's types, because each loads in its own load context, so they work together through `IPluginHost`.

### IWriteCache

- A stream returned by `IVolume.OpenReadWrite` can implement it when it caches writes, e.g. `CachedDiskStream` of the PhysicalDisk plugin. `HasCachedWrites` tells whether writes are waiting and `ShouldFlush` asks to be committed soon. Hst Commander calls `Commit` on a shared writable image's stream when its file system is consistent, see [Writable images](volumes.md#writable-images). `Stream.Flush` shouldn't write the cached writes, as file systems flush their stream after each change (e.g. PFS3); the stream writes by itself only when its cache is full and when it's disposed.

### IPluginHost

- `OpenVolume(name, stream, exclude)` opens a seekable stream as a volume with the first file volume provider that detects it (skipping `exclude`), null when none does. The stream is passed on as it is (writable when it is) and stays owned by the caller. E.g. the Disk plugin opens PiStorm partitions with the Amiga plugin's `RdbFileVolumeProvider` this way.
- `GetPluginDirectory(name)` is `~/.hst-commander/plugins/<name in lower case>` (it may not exist).
- `ReadImages` copies the stream to memory (at most `PluginHost.MaxImageLength`) and asks the image providers in order, ignoring their errors.
- `PluginHost.Instance` in Core implements it.

## Loading Plugins

- `Plugins.BuiltIn` lists the plugins included by default (Zip, LhaLzx, Disk, Amiga, PhysicalDisk), referenced by Core. Disk comes before Amiga so hybrid disks (an MBR and a rigid disk block, which `RdbFileVolumeProvider` would also match) are opened by the Disk plugin. `Plugins.All` is the built-in plugins followed by the loaded ones; the `VolumeProviders` lists are rebuilt when it changes.
- Both front-ends call `Plugins.Load()` once at startup, before the panes are created, because each `NavigationContext` takes `VolumeProviders.Files` when it's created: the TUI in `Program.cs`, the GUI in `App.OnFrameworkInitializationCompleted`. In the GUI it must be written `Hst.Commander.Core.Plugins`, as `Plugins` there resolves to the `Hst.Commander.Plugins` namespace.
- Load errors (including a failing `Initialize`) are joined by `Plugins.FormatErrors` and shown once the main window is loaded; the other plugins still load.
- `PluginLoader.Directories` are `~/.hst-commander/plugins` and then `plugins` next to the executable; missing directories are skipped and a directory listed twice is scanned once.
- In each directory, `Name/Name.dll` (the recommended layout, with its dependencies) is loaded first, then single `*.dll` files, ordered by path. Public non-abstract `IPlugin` classes with a parameterless constructor are created. Assemblies without plugins are skipped (a `BadImageFormatException` is ignored for single assemblies but reported for `Name/Name.dll`), as are plugin types already loaded, e.g. a copy of a built-in plugin.
- Each plugin assembly gets its own `PluginLoadContext`, sharing Abstractions with the default context (so `IPlugin` is the same type; a plugin's own copy is ignored) and resolving other dependencies with its `.deps.json` or from its directory. This works in the single-file published apps. Plugins can't be unloaded and their assemblies stay locked while Hst Commander runs.
- To build an external plugin, reference Abstractions with `<Private>false</Private>` and `<ExcludeAssets>runtime</ExcludeAssets>` and copy the output to `~/.hst-commander/plugins/<Name>/`.
