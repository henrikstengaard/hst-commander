# File Operations

Copying, UAE metadata, moving, renaming, deleting, making directories, creating volumes, selecting groups and properties. Read this when working on any of them.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Common Rules

- The front-ends call `NavigationContext` methods; the work happens in extension methods on `IVolume` (`VolumeCopy`, `VolumeMove`, `VolumeDelete`, `VolumeContents` in `Hst.Commander.Core.Volumes`) that only use `IVolume` members, so they work between any two volumes.
- Long operations run on a background thread with volumes of their own from `OpenLocationAsync`, never the pane's volume. For local paths that's a new `LocalVolume`; images are opened again from their files (images inside images from the reopened image they're in); other providers get a new volume changed to the current path.
- `GetOperationEntries` returns the marked entries, or the entry under the cursor when none are marked (never `..`). Marks are cleared after a successful copy or delete.
- Progress is reported at most every 100 ms and at item boundaries.
- Operations that can't run in a location (the drive list, a read-only image) have a `...NotSupportedMessage` property, which the front-ends show as an error instead of opening the dialog; the method itself then throws `NotSupportedException`.
- Created and renamed entries are selected once by `GetSelectedIndex` when the entries are listed again.

## Copying

- `VolumeCopy.CopyToAsync` first scans the entries (containers recursively) to know the total size, then copies. Both volumes are changed back to their starting directory afterwards. Containers are merged with existing ones.
- Modified dates are kept: items right after they're written, containers after everything is copied, deepest first, because writing into a container changes its date. A destination throwing `NotSupportedException` for dates is ignored.
- Before overwriting an existing item, `CopyOptions.ConfirmOverwrite` is asked with an `OverwriteRequest` and answers `Yes`, `No` (skip; removed from the progress totals) or `All`. Without a callback items are overwritten. Destination directories are listed once to detect existing items, and the speed/ETA clock is paused while asking.
- `CopyOptions` also holds the filter (`EntryFilter`: `*`/`?` wildcards, several patterns separated by `;`, case-insensitive, applied to items only; containers are always copied) and an optional target name for copying a single item under another name.
- When a copy is cancelled or fails during an item, the destination stream is closed and the partial item is deleted; cleanup errors are ignored so the original error is kept.
- `NavigationContext.CopyAsync` resolves the destination text: the other pane's current path means that pane's location (which may be an image); otherwise a local directory (relative to the source's local directory, created when missing); a single file copied to a non-directory path whose parent exists is copied under that name. Copying entries onto themselves or a directory into itself throws `IOException`.

## UAE Metadata

Amiga names can contain characters other file systems can't (e.g. `*`, `?`, `#`), and protection bits and comments can't be stored outside Amiga file systems. Like Hst Imager, `VolumeCopy.TransferAsync` decides what to do from `IVolume.SupportsAmigaMetadata` of the source and destination. It's true for Amiga file systems and lha/lzx archives, and forwarded by the wrapping volumes (`NestedVolume`, `EntryVolumeProvider`, `SharedImageVolume`, `RdbVolume`, `VolumeListVolume`, `HybridDiskVolume`); false in partition lists.

| Source | Destination | What happens |
|---|---|---|
| Amiga | Amiga | Names copied as they are; `SetAmigaMetadata` before the modified date (containers last) |
| Amiga | other | Names, protection bits and comments are written as UAE metadata in the format of `CopyOptions.UaeMetadata` |
| other | Amiga | UAE metadata of each source directory is read; entries get their Amiga names, bits and comments; metadata files aren't copied |
| other | other | Metadata is read as for copying to Amiga and written as for copying from Amiga, so entries keep their Amiga names and metadata even without their metadata files |

- Formats (`UaeMetadataFormat`): `UaeFsDb` (default; WinUAE and FS-UAE; a `_UAEFSDB.___` file per directory, names needing it get `__uae___` + a safe name made unique with random characters), `UaeMetafile` (FS-UAE; a `<name>.uaem` text file next to the entry, names `%xx` encoded) or `None` (no metadata, invalid characters replaced with `_`).
- Windows reserved names (`CON`, `NUL`, `COM1`, ...) need a normal name on Windows. Entries with a usable name and default metadata get no metadata. Existing metadata in the destination is kept, and an Amiga name copied again keeps its normal name.
- `UaeMetadataWriter` writes through `IVolume` only: metafiles right away, `_UAEFSDB.___` files after all items (also when the copy fails or is cancelled).
- `UaeMetadataReader` reads `_UAEFSDB.___`, on Windows also the version 2 nodes WinUAE writes as NTFS alternate data streams of local entries, plus valid `.uaem` metafiles. Invalid or unreadable metadata files are copied as normal files.
- `UaeMetadataUpdater` keeps metadata up to date in volumes that don't store Amiga metadata:
  - **Renaming:** the entered name is the new Amiga name. Only `/`, `\` and control characters are invalid (also `:` when the name needs metadata). An entry with metadata gets a normal name for the new Amiga name, keeping its bits and comment (alternate data streams are replaced by the format of the setting); an entry without metadata is renamed as usual unless the name can't be stored (e.g. `a*b` on Windows), then it gets a normal name and metadata. The normal name is selected.
  - **Deleting:** metadata of deleted entries is removed, also when deleting fails or is cancelled.
  - **Moving:** metadata of moved entries is removed from the source, so moved directories can be deleted. On the same local drive, nodes and metafiles move to the destination; entries that need it go through `VolumeMove.MoveToAsync`.
  - **Properties:** changing the protection bits of an entry with UAE metadata rewrites its node or metafile.
- The panes show names as stored, e.g. `__uae___file_1`. The properties dialog shows the Amiga name, comment and protection bits from the entry's UAE metadata.
- `None` turns all UAE metadata handling off: no reading, writing, updating or showing.
- The format is set in both front-ends' settings and passed to `NavigationContext.UaeMetadata` of both panes. Core references `Hst.Amiga` for the readers and writers.

## Moving and Renaming

- `VolumeMove.MoveToAsync` copies like `CopyToAsync` (sharing `VolumeCopy.TransferAsync`) and deletes each item right after it's copied. Containers are deleted afterwards, deepest first and only when empty, so skipped and filtered-out items stay in the source with their containers.
- `NavigationContext.MoveAsync` resolves the destination like `CopyAsync`. When both locations are local directories on the same drive and the filter matches everything (`*`, `*.*` or empty), entries whose name doesn't exist in the destination are moved with `File.Move`/`Directory.Move` (fast, no extra space); the rest go through `MoveToAsync`.
- A single entry moved to a plain name (no separator, not rooted, not the other pane's path and not an existing directory in the current location) is renamed instead: `GetRenameNameAsync` returns the name (null for a move) and `RenameAsync` renames on the pane's own volume, without a progress dialog. Names are validated like making a directory; another entry with the name throws `IOException`, case-only changes are allowed.

## Deleting

- `VolumeDelete.DeleteAsync` scans first (entries inside a container before the container), then deletes in that order, using only `Delete` of items and empty containers. The volume is changed back to its starting directory, also when cancelled or failed.
- `NavigationContext.DeleteAsync` runs it on a volume of its own and removes the UAE metadata of deleted entries.

## Making Directories

`CreateDirectoryAsync(name)` creates the directory on the pane's own volume. The name is trimmed; empty, `.`, `..`, names with separators or invalid file name characters and existing names (case-insensitive on Windows) throw `IOException`.

## Creating Volumes

- F9 shows the list of `VolumeProviders.Creators`, then a dialog with the file name and the creator's fields (text/number fields as text, booleans as check boxes, required fields marked with `*` by `VolumeCreation.GetLabel`). When creating fails, the error is shown and the fields dialog is shown again with the input kept.
- `CreateVolumeAsync(creator, fileName, input)` adds the extension when missing, validates the name like making a directory and converts the input with `VolumeCreation.GetValues` (trimmed, required fields checked, numbers parsed, defaults for missing input; `IOException` otherwise). Local files are created with `FileMode.CreateNew` and written on the thread pool; elsewhere the volume is created in a temporary file and copied with `OpenWrite`. A partly created file is deleted on failure. The other pane is refreshed too.
- `AdfVolumeCreator` (Amiga plugin) creates an 880 KB adf: `Volume name` (required, default `Empty`), `Format` (checked: Fast File System `DOS\3`; unchecked: all zeros, other fields ignored) and `Bootable` (writes the standard boot code with its checksum).

## Selecting Groups

- `MarkGroup`/`UnmarkGroup(entries, SelectGroupOptions)` add or remove marks of entries matching a filter (`CaseSensitive`, `FilesOnly`), keeping other marks; `InvertMarks` toggles every entry. `..` is never marked.
- `NameFilter`: a filter containing a regex character (`NameFilter.RegexCharacters`) is a regular expression matching any part of the name; otherwise wildcard patterns like `EntryFilter`, matching whole names. Empty matches everything; an invalid regex throws `IOException`.
- `+` selects, `-` unselects, `*` inverts. The keys are matched by the typed character so they work on any keyboard layout (plus the keypad keys). The filter starts as `*` and is remembered for the session, shared by select and unselect.

## Properties

`GetPropertiesAsync(entries)` builds `EntryProperties`: a name and sections of `PropertyLine`s, similar to the properties of Windows Explorer.

- **A single entry:** type (the Windows shell type name, elsewhere e.g. `TXT File (.txt)`), link target, location, size, size on disk (local files on Windows), contains (directories), and for local files the created/modified/accessed dates, attributes and on Unix the permissions. Entries inside images and other volumes only have their modified date. `IEntry.Details` adds the volume's own type and sections (and `UsedFraction` as a bar), shown with `DisplayName`. Further sections show the Amiga name/comment and the icon file with the lines its icon provider reads.
- **Check boxes** (`FlagGroups`): `Attributes` (the attributes the volume can change; for local files those of `LocalVolume.ChangeableAttributes`) and `Protection bits` of Amiga entries (checked as the Amiga `List` command shows them, so `rwed` are checked when allowed). A group can change when the volume can (`GetChangeableAttributes`, `CanSetAmigaMetadata`, or UAE metadata where entries can be moved); otherwise the boxes are disabled. With changeable boxes the dialogs have OK and Cancel, and OK calls `ChangeFlagsAsync(properties, checked)`, which changes only groups that differ and refreshes both panes. Nothing else (e.g. the name) can be changed here.
- **Several entries:** the counts as name (e.g. `3 Files, 2 Folders`), `All of type ...` or `Multiple types`, `All in ...`, sizes and contents.
- **Drives:** label and drive, type, file system (or not ready), used/free space and capacity with a bar; several drives are summed.
- Sizes are like Explorer, e.g. `1.17 MB (1,234,567 bytes)` (`SizeFormatter.FormatWithBytes`).
- When `HasContents`, the dialogs count contents with `CountContentsAsync` in the background while open (`Calculating...` until the first report; closing cancels). Local directories are read from the file system, skipping inaccessible directories and not following directory links; other locations use a volume of their own. The number of lines doesn't depend on the contents, so the dialogs only update values.
