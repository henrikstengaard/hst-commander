# Hst.Commander.Core

The shared core of Hst Commander used by both front-ends, the terminal UI (`Hst.Commander.Tui`) and the desktop GUI (`Hst.Commander.Gui`). It holds the built-in volumes, the navigation logic of a pane, file operations, the viewer and plugin loading, so the front-ends only contain presentation and key handling.

## Volumes

Built-in volumes in `Hst.Commander.Core.Volumes`:

- **`LocalVolume`**: local drives, directories and files; the root lists the drives. Attributes can be changed (read-only, hidden, system and archive on Windows; write permissions elsewhere).
- **`Iso9660Volume`**: read-only ISO 9660 images, opened by `Iso9660FileVolumeProvider`.
- **`NestedVolume`**: a stack of volumes, so images can be opened inside directories and inside other images, e.g. `c:\images\disk.iso\archive.zip\dir1`. Going up from the root of an image closes it.
- **`EntryVolumeProvider`**: shows an opened image as if it starts at the path of its file.

`VolumeProviders` lists the volume providers, file volume providers, volume creators, icon providers, navigation modes and image providers of the built-in volumes and all plugins.

## Navigation

`NavigationContext` holds the state of a pane and is shared by both front-ends:

- Listing, changing directory, going to the parent and the root (Ctrl+\) and changing the volume provider of a pane.
- Opening files as images with Enter (detected by their contents, regardless of the extension) or starting them with the host OS, configurable with the `Enter on file` setting. Ctrl+PageDown opens a file as a volume.
- Changing location by a typed path, including paths into images, and going to the home directory.
- Marking entries one by one or as a group with wildcards or regular expressions (`NameFilter`), and inverting the marks.
- Entry icons from plugin icon providers and icons contained in files (`EmbeddedIconReader`), and layouts of navigation modes.

Writable images (Amiga adf and RDB images) are opened once and shared by both panes and file operations (`SharedImages`), so two volumes never write the same file.

## File operations

Copying, moving and deleting work between any two volumes, because `VolumeCopy`, `VolumeMove` and `VolumeDelete` only use `IVolume`:

- **Copy (F5)** and **move (F6)** with a filter, overwrite confirmation, progress, speed and ETA, keeping modified dates. A single entry moved to a plain name is renamed. Moving on the same local drive moves entries directly.
- **Delete (F8)** with progress.
- **Make directory (F7)** and **create volume (F9)** with the volume creators of plugins (`VolumeCreation`).
- **UAE metadata**: Amiga names, protection bits and comments are kept when copying between Amiga volumes and stored as UAE metadata (`_UAEFSDB.___` files or FS-UAE `.uaem` metafiles, `UaeMetadataFormat`) on other volumes, and read from it when copying back. Renaming, moving and deleting keep the metadata up to date.
- **Properties** (`EntryProperties`) of files, directories, partitions and drives, with the contents counted in the background, and changing attributes and Amiga protection bits.

Long operations run on a background thread with a volume of their own, opened with `NavigationContext.OpenLocationAsync`, because volumes aren't thread safe.

## Viewer

`ViewerDocument` and `ViewerSession` are the F3 viewer shared by both front-ends:

- Files of any size are read through a block cache and shown as text (UTF-8 or ISO-8859-1) or hex.
- Searching for text or regular expressions, forwards and backwards, and selecting and copying text or hex values.
- Images (GUI only): PNG, JPEG, GIF, BMP and WebP (`StandardImageProvider`, SkiaSharp), Windows icon files (`WindowsIconImageProvider`) and the image providers of plugins, fitted or zoomed.

## Plugins

`Plugins.BuiltIn` lists the plugins included by default: Zip, LhaLzx, Disk and Amiga (Disk before Amiga, so hybrid disks open as hybrid disks). `Plugins.Load` loads other plugins at startup with `PluginLoader` from `~/.hst-commander/plugins` and `plugins` next to the executable, each in a load context of its own. `PluginHost` implements `IPluginHost`, letting plugins work together.

## Other

- `AppInfo`: the application name, author and version shown in Help > About.
- `FileStarter`: starts files with the host OS and detects terminal programs.
- `SizeFormatter`: sizes like Windows Explorer, e.g. `1.17 MB (1,234,567 bytes)`.
- `SettingsPaths`: the settings directory `~/.hst-commander`.

## Dependencies

- `LTRData.DiscUtils.Iso9660`: ISO 9660 images.
- `Hst.Amiga`: UAE metadata.
- `SkiaSharp`: decoding images viewed with F3.
- `System.Linq.Async`: async LINQ.
