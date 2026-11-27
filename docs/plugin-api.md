# Plugin API

The interfaces in `Hst.Commander.Abstractions` and how plugins are loaded. Read this when changing the plugin API or writing a plugin. The interface files are the reference for signatures; this describes the contracts they don't spell out.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Interfaces

### IVolume

- A volume has a current directory; `ListEntries`, `OpenRead`, `OpenWrite`, `CreateContainer`, `Delete`, `Rename` and `SetModifiedDate` act on names in it. `ChangeDirectory` takes path components and handles `Paths.Root` and `Paths.Parent` case-insensitively; a missing directory throws an `IOException` and keeps the current directory. `CurrentPathComponents` is an `IReadOnlyList<string>` from the root, empty at the root.
- `ListEntries` doesn't include `..`; dates (`IEntry.ModifiedDate`, `SetModifiedDate`) are UTC, and dates of kind unspecified are treated as UTC.
- Write members throw `NotSupportedException` when read-only. `OpenWrite` creates or overwrites; `CreateContainer` is a no-op when the container exists; `Delete` only deletes items and empty containers; `Rename` throws `IOException` when the new name exists (case-only changes are allowed); `SetModifiedDate` treats an unspecified `DateTimeKind` as UTC.
- `GetWritableImageId(name)` and `OpenReadWrite(name)` (optional, default null and `NotSupportedException`): an item with an id unique across volumes (e.g. a local file's full path, a file's UNC path on an SMB share or a physical disk's device path) is opened once for reading and writing as a writable image shared by both panes, see [Writable images](volumes.md#writable-images). `OpenReadWrite` throws `UnauthorizedAccessException` or `IOException` when the item can only be read, and the image is then opened read-only with `OpenRead`.
- Optional members with defaults: `SupportsAmigaMetadata` (the current directory stores Amiga names, protection bits and comments as they are, see [UAE metadata](file-operations.md#uae-metadata)), `CanSetAmigaMetadata`/`SetAmigaMetadata` and `GetChangeableAttributes`/`SetAttributes` (only the changeable attributes are changed). The properties dialog uses `CanSetAmigaMetadata` and `GetChangeableAttributes` to decide what can be changed. `GetSpace` (default null) returns the free and total space (`VolumeSpace(Free, Total)`) of the file system of the current directory, read after each listing and shown as free/total in the path panel like local drives (e.g. FAT, NTFS and Amiga file systems); volumes wrapping other volumes (partition tables, RDB partitions, hybrid disks) forward it to the current one.
- `GetHostPath(name)` (optional, default null): a path of an item the host operating system can open, e.g. the full path of a local file or the UNC path of a file in an SMB share on Windows. Enter starts files with a host path with the host OS like local files, and they can be executables (see [Starting files](volumes.md#starting-files)). Only the root volume's host paths are used, never inside images. `NestedVolume` forwards it.
- `GetReadOrder(name)` (optional, default null): the position of an item in the order the volume stores its data, e.g. the files of a solid archive, where reading a file decompresses the files before it. Copying then reads the items in this order (see [Copying](file-operations.md#copying)), so each file is decompressed once. `NestedVolume` and `EntryVolumeProvider` forward it.

### IEntry

- `Name`, `Type` (`Container` or `Item`), `Size`, `ModifiedDate`.
- `DisplayName` (default `Name`): shown in the panes and at the top of the properties dialog, e.g. `1 - DH0 - Workbench`. Paths, file operations and other dialogs use `Name`.
- `Details`: `EntryDetails(TypeName, Sections) { UsedFraction }` made of `EntryProperty(Label, Value)` lines, shown in the properties dialog of a single entry. Use `EntryProperty.FromBytes` for sizes.
- `IsVolume`: a container that is the root of a volume of its own (e.g. an RDB partition), whose icon can be inside it.
- `AmigaMetadata`: `AmigaMetadata(ProtectionBits, Comment)` with the bits as stored on disk (`hsparwed`; `rwed` are set when *not* allowed, so 0 is `----rwed`).
- `Attributes`: DOS/Windows `EntryAttributes` (the values of `FileAttributes`).

### IVolumeProvider

- What a pane can show (the volume provider list). Each `CreateVolume` call must return a volume of its own, because file operations open a second one on a background thread. Volumes implementing `IDisposable` are disposed when no longer used.
- `RequiresAdministrator` (optional, default false): the volumes need administrator rights (root on Linux and macOS), e.g. physical disks; choosing the provider when not elevated offers to restart Hst Commander as administrator, see [Running as administrator](application.md#running-as-administrator).
- `PathRoot` (optional, default null): the root of the paths shown for the volumes, e.g. `\\server`, so `CurrentPath` is `\\server\share\dir` instead of `share\dir`, and a location entered starting with it is changed to in the volumes.
- `ReadListedFiles` (optional, default true): false skips reading listed files in the background after listing (`NavigationContext.ReadsListedFiles`), also inside images opened from the provider's volumes: [detecting volume files](volumes.md#detecting-volume-files), finding icon files (e.g. `.info`, so `HasIcon` is false) and the GUI reading icons contained in files. E.g. for FTP servers, which often allow one connection per user, so reading the files on connections of their own would block browsing. Files are still detected as images when opened, and icons read for the properties and icon viewer.
- `VolumeProviders.All` is the built-in `Local` provider followed by the plugins' providers.

### ILocationVolumeProvider

- An `IVolumeProvider` asking for a location when chosen, e.g. an SMB path. The front-ends show a dialog with `LocationLabel` and `LocationExample`, then `VolumeConnector.ConnectAsync` calls `Connect(location, credentials)`, first with null credentials. Throwing `CredentialsRequiredException` (an `IOException`) asks the user for a user name and password (`VolumeCredentials`, whose `ToString` hides the password) and calls it again; other `IOException`s are shown as errors.
- `Connect` returns a `VolumeConnection(VolumeProvider, PathComponents)`: a volume provider bound to the location and credentials (its `CreateVolume` gives a new volume with a connection of its own each time, like any provider) and the path components the pane starts in. `NavigationContext.ChangeVolumeProviderAsync(connection)` changes to them before showing the volume, so a failure leaves the pane unchanged. `VolumeConnector.ConnectAsync` returns a `LocationConnection` with the credentials it connected with, kept by the pane for [bookmarks](volumes.md#bookmarks), which connect again to a location made of the connected provider's `PathRoot` and path. The `CreateVolume` of the location provider itself isn't used and may throw `IOException`.

### IConnectionVolumeProvider

- An `ILocationVolumeProvider` asking for connection details and credentials in one dialog when chosen, e.g. FTP/SFTP, instead of a location text. The front-ends show `ConnectionDialog` with a choice of `Protocols` (`ConnectionProtocol(Name, Label, DefaultPort)`), a host, a port (empty for the protocol's `DefaultPort`, shown in the field), a path (empty for the home directory), a user name and a password.
- `VolumeConnector.CreateConnectionRequest` checks the input (a host, a port from 1 to 65535) and makes the location with `FormatLocation(ConnectionDetails)`, which must not hold the credentials, e.g. `sftp://server:2222/home/user`; invalid input is shown in the dialog, which stays open. The credentials are null when no user name is entered (e.g. an anonymous FTP login). `VolumeConnector.ConnectAsync` connects with them first, and asks with `CredentialsDialog` when `Connect` throws `CredentialsRequiredException`, like other location providers.
- `ParseLocation(location)` returns the `ConnectionDetails(Protocol, Host, Port, Path)` of a location: the dialog shows those of the last location entered (`VolumeConnector.GetLastConnectionDetails`) and the last user name connected with (`VolumeConnector.GetLastUserName`), both kept while Hst Commander runs. Bookmarks work like those of other location providers: the location is the connected provider's `PathRoot` and path, and the password is offered to be stored in the credential store.

### IFileVolumeProvider

- Opens files as volumes (images). `IsMatch(name, stream)` checks the magic signature; the stream is at the start and may not be seekable. `CreateVolume(stream)` gets a seekable stream. Hst Commander calls `CreateVolume(name, stream)` (plugin API 1.1), which also gets the file's name, e.g. to name the file in a compressed file that doesn't store it (`disk.img` in `disk.img.xz`), and defaults to `CreateVolume(stream)`. It calls it on a background thread inside `VolumeOpening.RunAsync`, so a volume reading its entries when it's created (e.g. an archive with many files) doesn't stall the UI; it reports each entry with `VolumeOpening.ReportEntryRead()`, which shows the number read when opening takes long and throws `OperationCanceledException` when the user cancels. `ArchiveVolume<TArchiveEntry>.AddEntry` reports them itself. Outside `RunAsync` (e.g. a volume opened again for copying) `ReportEntryRead` does nothing.
- A file split by 7-Zip (`name.001`, `name.002`, ...) is passed to the providers as the joined file (see [Split files](volumes.md#split-files)), so providers don't handle split files themselves.
- Hst Commander owns the stream: it stays open while the volume is used and is disposed after the volume. It is read-only unless `CanWrite` is true and the file can be written, see [Writable images](volumes.md#writable-images).
- `VolumeProviders.Files` is the built-in ISO 9660 provider followed by the plugins' providers, asked in order.

### ArchiveVolume

- `ArchiveVolume<TArchiveEntry>` is a base class for read-only archive volumes (used by the Zip, LhaLzx and Archives plugins), so archive plugins don't each need their own directory tree. A derived class adds the archive's entries with `AddEntry(path, size, modifiedDate, archiveEntry, amigaMetadata, isDirectory)` (with a `long?` size for files whose size isn't known, plugin API 1.1, e.g. bzip2) (each reported to `VolumeOpening`, see `IFileVolumeProvider`) and opens them with `OpenArchiveEntry(name, archiveEntry, cancellationToken)`, throwing `NotSupportedException` for entries it can't read (e.g. encrypted ones). A derived class overriding `ReadsInArchiveOrder` with true (e.g. for a solid archive) makes `GetReadOrder` return the order files were added in, the archive's order.
- Directories are the archive's directory entries plus the directories in file paths; `/` and `\` both separate, a path ending with one (or `isDirectory`) is a directory, and empty, `.` and `..` components are skipped, so entries can't point outside the archive. Names match exactly, otherwise case-insensitively.
- The write members throw `NotSupportedException` ("<Name> archives are read-only"). `SupportsAmigaMetadata` returns the constructor's `supportsAmigaMetadata`, true for archives storing Amiga protection bits and comments (lha, lzx). A volume owning a disposable archive implements `IDisposable` itself.

### IVolumeCreator

- Creates a new volume file with F9. `FileExtension` is added to the file name when missing. `Fields` are `VolumeCreatorField`s (`Text`, `Number` as long, `Size` as long bytes entered with a unit, `Boolean`, `Choice` of `VolumeCreatorChoice` values and labels; the optional `IsRequired`, `DefaultValue`, `Choices` and `EnabledByField`, naming a check box that enables the field, are init properties), read from `VolumeCreatorValues`.
- `Create(stream, values)` writes to an empty seekable, readable and writable stream and throws `IOException` for invalid values.
- `CreatesSparseFile` true asks for a sparse file (e.g. a large disk image that is mostly empty); `Create` then sets the stream's length and only writes what isn't zeros.
- The volume creators of all plugins are listed in `VolumeProviders.Creators`, in plugin order. See [Creating volumes](file-operations.md#creating-volumes).

### IIconProvider

- `GetIconFileName(name)` finds an icon file by name only (`file.info` for both `file` and `file.info`); `GetVolumeIconFileName` gives the icon file inside a volume entry (e.g. `Disk.info`).
- `ReadIcon(name, stream, size)` checks the signature and returns null for other files; `ReadIcons` returns all icons for the icon viewer; `ReadIconProperties` returns lines for the properties dialog.
- `IconImage` is PNG data or 32-bit BGRA pixels (not premultiplied, top-down) with an optional `BitsPerPixel` (an init property); `EntryIcon(Name, Images)` is one icon with its images. See [Entry icons](icons-and-navigation-modes.md#entry-icons).

### IPropertiesTabProvider

- `GetTabs(context)` returns `PropertiesTab`s shown after the General tab of the properties dialog of a single entry, in plugin order (`IPlugin.PropertiesTabProviders`, `VolumeProviders.PropertiesTabs`). It's called on the thread using the pane's volume; exceptions leave the provider's tabs out.
- `IPropertiesContext` gives the pane's volume (`Volume`, at the entry's directory), the `Entry`, its `IconFile` found by an icon provider, `CanWrite` (files in the location can be written), and the Amiga metadata of the entry or its UAE metadata (`AmigaMetadata`, null when it has none; `CanSetAmigaMetadata`, also true without metadata when UAE metadata can be created; `SetAmigaMetadata`, which updates `AmigaMetadata` and creates or removes UAE metadata as needed).
- `PropertiesTab(Title, Text)` is a text that can be added, edited and deleted: `IsMultiline`, `MaxLength`, `NoTextMessage`, `AddText`, `DeleteText` and `Save(text)`, called with the new text (null when deleted or empty) when the user clicks Save and the text changed; null when it can't be changed. See [Tabs of plugins](file-operations.md#tabs-of-plugins).

### IImageProvider

- `ReadImages(name, stream)` gets the whole file in memory (seekable) and returns `EntryImage(Name, Image)`s, empty when the file isn't one of its formats. The plugins' providers are asked before the built-in ones. See [Images in the viewer](viewer.md#images).

### INavigationMode

- `Layout(volume, entries, options)` returns a `NavigationIcon` per entry shown, in order: entries without an icon are hidden, a null image shows Hst Commander's icon of the entry, a null selected image is drawn highlighted, a null position is placed by Hst Commander and an icon with `IsMovable = false` can't be dragged. Entries don't include `..`.
- `SavePositions` stores the positions of icons moved by the user.
- Both get the pane's volume and may change its directory (e.g. into a volume entry) as long as they change back before returning.
- `Options` are `NavigationModeOption`s shown as check boxes; `GetValue(options)` gives the default for a missing value. See [Navigation modes](icons-and-navigation-modes.md#navigation-modes).

### ITheme

- `Id` is stored in the settings, so it must never change. `Name` is shown in Settings > Appearance. `IsDark` picks the Fluent variant the GUI uses for controls the theme doesn't color.
- `Colors` is a `ThemeColors` record of "#RRGGBB" strings. The TUI uses the closest of its 16 colors for each one. Its optional `MarkedContainerBackground` and `MarkedItemBackground` (init properties) give marked directories and files a background of their own. Plugin API 1.2 adds optional colors for a marked entry under the cursor, the open menu item, the menu's hot keys, the keys before button labels, focused controls of TUI dialogs and selected text in the GUI's viewer and editor (`MarkedCursorForeground`, `MenuFocusBackground`/`Foreground`, `MenuHotForeground`, `KeyBackground`/`Foreground`, `DialogFocusBackground`/`Foreground`, `ViewerSelectionBackground`/`Foreground`); without them the cursor's, menu's or marked colors are used, as before.
- A theme needing only colors doesn't need a plugin: a JSON theme file in `~/.hst-commander/themes` does the same (see [JSON themes](themes.md#json-themes)).
- `GetBottomPanel()` returns the buttons of the bottom area as a `ButtonPanel` of `ButtonColumn`s of `PanelButton(Label, Action, Key, Background, Foreground)`. `Action` is one of the `CommanderActions` ids. Plugin API 1.3 adds `Home`, `CommandLine`, `About` and `RestartElevated`, the menu commands the [command palette](command-palette.md) runs. It's called again after the settings change.
- `ShowParentButtons` (default false) shows a narrow parent button on the outer side of each pane. See [Themes](themes.md).
- `HighlightActivePathPanel` (default false) colors the whole panel above the active pane with the active path's colors, not only the path.
- `FontFamily` and `FontSize` (default null) set the font of the panes and the bottom area in the GUI. `FontFamily` names fonts separated by commas and is only used when one of them is installed, as themes don't include fonts. The user's font setting overrides it.
- `FontDownloads` (default none) lists font files (`ThemeFontDownload`: file name, https URL pinned to a version, SHA-256 hash, name, license and project page) the user can download in the GUI's settings when none of the theme's fonts is installed. See [Fonts](themes.md#fonts).

### ICustomizableButtonPanel

- A theme implementing it gets a settings tab (`SettingsTitle`) with the button editor, starting from `Layout`. Reset restores `DefaultLayout`.
- `SaveLayout(layout)` is called after OK. The plugin stores it itself (e.g. JSON in `IPluginHost.GetPluginDirectory`), so both front-ends use it. See [Button editor](themes.md#button-editor).

### IEntryColumn

- A column the user can add to the panes in the settings (plugin API 1.4, `IPlugin.EntryColumns`, `VolumeProviders.EntryColumns`), listed after the built-in Name, Size, Modified and Attributes columns. See [Pane columns](file-operations.md#pane-columns).
- `Id` is saved in the settings, unique among all columns, prefixed with the plugin's name (e.g. `Amiga.ProtectionBits`). `Title` names it in the settings, `Header` (default `Title`) in the panes' headers. `Width` is the width of its texts in characters (widened for the header with the sort arrow), `AlignEnd` (default false) aligns them to the right.
- `GetText(entry, context)` returns the text of an entry, called on the UI thread for every listed entry, so it must be quick; an exception shows nothing. `Compare(x, y, context)` (default: the texts, ordinal) sorts by the column when its header is clicked.
- `IEntryColumnContext` gives `SupportsAmigaMetadata` of the location, `GetAmigaMetadata(entry)` (the entry's own, or its UAE metadata, which is only read while a shown column has `UsesAmigaMetadata`) and `GetAttributesText(entry)`, the text of the built-in Attributes column.

### IPlugin

- Returns the plugin's volume providers, file volume providers, volume creators, icon providers, navigation modes, image providers, properties tab providers, themes and pane columns; each defaults to none. `Initialize(host)` is called once before the plugin is used.
- A plugin references only Abstractions. Plugins can't use each other's types, because each loads in its own load context, so they work together through `IPluginHost`.
- `ThirdPartyNotices` (optional, default null): the copyright notices and licenses of the third-party components the plugin uses, shown in Help > About > Licenses while it's loaded, e.g. the SMB plugin returns SMBLibrary's notice with the LGPL and GPL texts (an embedded resource). A plugin throwing from it is left out of the list.

### IWriteCache

- A stream returned by `IVolume.OpenReadWrite` can implement it when it caches writes, e.g. `CachedDiskStream` of the PhysicalDisk plugin. `HasCachedWrites` tells whether writes are waiting and `ShouldFlush` asks to be committed soon. Hst Commander calls `Commit` on a shared writable image's stream when its file system is consistent, see [Writable images](volumes.md#writable-images). `Stream.Flush` shouldn't write the cached writes, as file systems flush their stream after each change (e.g. PFS3); the stream writes by itself only when its cache is full and when it's disposed.

### IPluginHost

- `OpenVolume(name, stream, exclude)` opens a seekable stream as a volume with the first file volume provider that detects it (skipping `exclude`), null when none does. The stream is passed on as it is (writable when it is) and stays owned by the caller. E.g. the Disk plugin opens PiStorm partitions with the Amiga plugin's `RdbFileVolumeProvider` this way.
- `GetPluginDirectory(name)` is `~/.hst-commander/plugins/<name in lower case>` (it may not exist).
- `ReadImages` copies the stream to memory (at most `PluginHost.MaxImageLength`) and asks the image providers in order, ignoring their errors.
- `PluginHost.Instance` in Core implements it.

## Loading Plugins

- `Plugins.BuiltIn` lists the plugins included by default (Zip, LhaLzx, Archives, Disk, Amiga, PhysicalDisk, Ftp, DirectoryOpus), referenced by Core. Disk comes before Amiga so hybrid disks (an MBR and a rigid disk block, which `RdbFileVolumeProvider` would also match) are opened by the Disk plugin. `Plugins.All` is the built-in plugins followed by the loaded ones; the `VolumeProviders` lists are rebuilt when it changes.
- Both front-ends call `Plugins.Load()` once at startup, before the panes are created, because each `NavigationContext` takes `VolumeProviders.Files` when it's created: the TUI in `Program.cs`, the GUI in `App.OnFrameworkInitializationCompleted`. In the GUI it must be written `Hst.Commander.Core.Plugins`, as `Plugins` there resolves to the `Hst.Commander.Plugins` namespace.
- Load errors (including a failing `Initialize`) are joined by `Plugins.FormatErrors` and shown once the main window is loaded; the other plugins still load.
- `PluginLoader.Directories` are `~/.hst-commander/plugins` and then `plugins` next to the executable; missing directories are skipped and a directory listed twice is scanned once.
- In each directory, `Name/Name.dll` (the recommended layout, with its dependencies) is loaded first, then single `*.dll` files, ordered by path. Public non-abstract `IPlugin` classes with a parameterless constructor are created. Assemblies without plugins are skipped (a `BadImageFormatException` is ignored for single assemblies but reported for `Name/Name.dll`), as are plugin types already loaded, e.g. a copy of a built-in plugin.
- Each plugin assembly gets its own `PluginLoadContext`, sharing Abstractions with the default context (so `IPlugin` is the same type; a plugin's own copy is ignored) and resolving other dependencies with its `.deps.json` or from its directory. This works in the single-file published apps. Plugins can't be unloaded and their assemblies stay locked while Hst Commander runs.
- To build an external plugin, reference Abstractions with `<Private>false</Private>` and `<ExcludeAssets>runtime</ExcludeAssets>` and copy the output to `~/.hst-commander/plugins/<Name>/`. `Hst.Commander.Plugins.Smb` is an example: it sets `EnableDynamicLoading`, so `dotnet publish` puts its dependencies (`SMBLibrary.dll`) and deps file next to it.
- A plugin built for an incompatible plugin API version isn't loaded, with an error naming both versions (see [Plugin API versions](#plugin-api-versions)).

## Plugin API versions

- The plugin API version is the assembly version of Abstractions (`AssemblyVersion` in its project, major and minor; `PluginLoader.ApiVersion`). It's independent of the app version, whose last part is the git commit count, so it only changes when the plugin API does.
- A plugin records the version it was built for in its reference to Abstractions, so plugins don't declare it themselves. Before creating a plugin's types, `PluginLoader` compares it with its own (`PluginLoader.IsCompatible`): the major versions must be the same and the plugin's minor version not newer. Otherwise the plugin isn't loaded and the error says whether the plugin is older (install a version made for this Hst Commander) or needs a newer Hst Commander. Without the check such a plugin would fail with a type load error, or later with a missing method. Assemblies not referencing Abstractions have no plugins and are skipped as before.
- Increase the minor version for additions plugins built for an older version keep working with: new interfaces and types, new interface members with default implementations (like the optional members above), new optional parameters of Hst Commander's own types. Increase the major version (and reset the minor version) for any other change, e.g. removing, renaming or changing a member or adding an interface member without a default implementation; plugins then have to be built again.
- The built-in plugins are project references of Core, not loaded by `PluginLoader`, so they're always built for the current version.
- Records of the plugin API take their required values as positional parameters and their optional values as init properties (e.g. `new PanelButton("Copy", CommanderActions.Copy) { Key = "F5" }`), so a new optional value is added as an init property in a minor version. Adding a positional parameter changes the record's constructor, which plugins built for the older version call, so it's a major version.
