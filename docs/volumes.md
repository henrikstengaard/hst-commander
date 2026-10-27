# Volumes and Navigation

The volume implementations, images, writable images and how a pane navigates and changes location. Read this when working on a volume, a file volume provider or `NavigationContext` navigation.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Volume Implementations

Core volumes (`Hst.Commander.Core.Volumes`):

| Class | Notes |
|---|---|
| `LocalVolume` | The real file system; the root lists the drives. Created by `LocalVolumeProvider`. `SetAttributes` changes `LocalVolume.ChangeableAttributes` (read-only, hidden, system, archive on Windows; only read-only, i.e. write permissions, elsewhere) |
| `Iso9660Volume` | ISO 9660 images via DiscUtils, created by `Iso9660FileVolumeProvider` |
| `NestedVolume` | Holds a stack of volumes; opening an image pushes one, going up from its root pops it. `CurrentPathComponents` spans all of them |
| `EntryVolumeProvider` | Presents a child volume as if it starts at the path of the entry that opened it |
| `NullVolume` | Empty fallback |
| `DiskImageVolume` | Intentional stub |

Plugin volumes:

| Class | Notes |
|---|---|
| `ZipVolume` (Zip) | Directories are the archive's directory entries plus the directories in file paths; `/` and `\` both separate, empty, `.` and `..` components are skipped. Names match exactly, otherwise case-insensitively |
| `ArchiveVolume<T>`, `LhaVolume`, `LzxVolume` (LhaLzx) | Same directory tree rules as zip. Entries carry `AmigaMetadata` and `SupportsAmigaMetadata` is true, so copying from them works like copying from an Amiga volume. Lha names are cut at a zero character (the comment follows); `-lhd-` entries without a trailing separator (symbolic links) are skipped; lzx dates are local time. The archives aren't disposed, as that would dispose the stream Hst Commander owns |
| `AmigaFileSystemVolume` (Amiga) | Fast File System of an adf or an RDB partition, or PFS3 of an RDB partition; other file systems (e.g. SFS) throw `IOException`. See below |
| `RdbVolume` (Amiga) | Hard disk images with a rigid disk block (raw, fixed or dynamic vhd). See below |
| `VolumeListVolume`, `PartitionTableVolume` (Disk) | MBR and GPT disk images. See below |
| `HybridDiskVolume` (Disk) | A disk with both an MBR and another volume on the whole disk (e.g. a rigid disk block in blocks 1-15): the root lists both partition tables, e.g. `hybrid.img\rdb\1` |
| `DiscFileSystemVolume` (Disk) | FAT (long names) and NTFS via DiscUtils, writable when the file system is; dates are UTC; attributes can be changed |

ISO images and zip, lha and lzx archives are read-only. `LocalVolume.CreateContainer` throws `NotSupportedException` in the drive list.

### Amiga file systems

- Writable when the stream is. Changes are flushed after each operation and when a written stream is closed (PFS3 caches).
- Names are case-insensitive and can't contain `/` or `:`. Names longer than the file system allows throw `IOException`, because Hst.Amiga would silently truncate them. Case-only renames go through a temporary name.
- Amiga dates are local time; dates before 1978 become 1978-01-01. Delete and overwrite ignore protection bits. Comments are cut to 79 characters.
- `OpenRead` returns Hst.Amiga's seekable stream, so e.g. the viewer reads it directly.
- Disposing and closing a written stream wait on the thread pool, because Hst.Amiga waits for async writes there and would deadlock on a UI thread.

### Partition tables

- The root of an RDB, MBR or GPT disk lists the partition table as a single directory (`rdb`, `mbr` or `gpt` in paths, shown in upper case), so paths always show it, e.g. `disk.hdf\rdb\1` or `pistorm.img\mbr\2\rdb\1`. It lists the partitions as directories `1`..`n` marked `IsVolume`, with a `DisplayName` (e.g. `1 - DH0 - Workbench`, `2 - PiStorm RDB`) and `Details` for the properties dialog.
- A partition is mounted when listed or opened and stays mounted until disposed; one that can't be mounted is only tried once when listed and throws `IOException` when opened. Partition lists are read-only.
- MBR/GPT partitions are mounted as FAT or NTFS, otherwise through `IPluginHost.OpenVolume` (e.g. a PiStorm partition, MBR type `0x76` or its GPT type, opens as an `RdbVolume`).
- Sector sizes of 512 and 4096 bytes are supported. GPT tells by the header offset. MBR doesn't store it, so 4096 is used only when the partitions only fit with 4096-byte sectors, or also fit and more of them start with a recognizable volume there.
- `DiskFileVolumeProvider` doesn't match the boot sector of an unpartitioned FAT/exFAT/NTFS volume, which also ends with `0x55 0xAA`.

## Navigation Model

Each pane's `NavigationContext` owns a `NestedVolume` over a root volume from a volume provider (initially `Local` at the startup directory). `ChangeVolumeProviderAsync` creates a new root volume, closes opened images and the previous root volume, and clears the marks. `CurrentPath` at the root is `Computer` for the local drive list and the provider's `Name` otherwise. Local-only features (disk space, starting files, executables) need a `LocalVolume` root.

### Images

Opening a file (Enter or double-click, `OpenEntryAsync`) first checks whether it is an image by asking every file volume provider, regardless of extension, so e.g. a `.nupkg`, `.jar` or `.docx` opens as a zip archive. A file no provider detects is started. `OpenAsVolumeAsync` (Ctrl+PageDown, File > Open as volume) does the same detection but throws `IOException` when nothing matches. Images are detected in any location, including inside other images.

1. Providers are asked in order with `IsMatch`, reading through the current volume's `OpenRead`. A seekable stream is reset for the next provider, otherwise the file is opened again. Files that can't be read (`IOException`, `UnauthorizedAccessException`, `NotSupportedException`) aren't images.
2. The first match gets the file opened again as a seekable stream; a stream that isn't seekable (e.g. inside a zip) is copied to a temporary file deleted on close.
3. The volume is pushed on the `NestedVolume` (wrapped in `EntryVolumeProvider`) and the image (provider, file name, the directory inside its parent image, volume and stream) on the context's image stack, which `OpenLocationAsync` uses to open the images again.

Going up from an image's root pops and disposes its volume and stream. Changing the volume provider or location closes all images.

### Writable Images

- A provider with `CanWrite` (the Amiga adf and RDB providers) gets a writable stream for a local file opened directly from a local directory.
- `SharedImages` opens such a file once per full path (case-insensitive on Windows and macOS) with read/write sharing and no buffering, falling back to read-only. The volume is shared by both panes and the volumes of file operations, because two volumes writing one file would each keep their own file system state (e.g. free blocks) and damage it.
- Each user gets a `SharedImageVolume` view with its own current directory. Every operation, and each read/write of its streams (`LockedStream`), holds the image's semaphore and first changes the shared volume to the view's directory. Operations run on the thread pool while holding the lock, so the lock is never held by work waiting for a blocked UI thread. The file is closed when the last view is disposed.
- Images inside other images get a `ReadOnlyStream`, since changes would be lost.
- `OpenedImage.IsWritable` decides whether making directories, moving and similar operations are supported inside an image.
- `LocalVolume.OpenRead` shares files with writers, so an open writable image can still be detected in the other pane or copied.

### Going to the Root

`GoRootAsync` (Ctrl+\) goes to the root of the current volume: the local drive root (via `Path.GetPathRoot`), the root of the image when inside one (pressing again at an image root closes the images), or the root of another provider's volume. The first entry is selected once, even when the location doesn't change.

### Starting Files

- Local files no provider detects (or all local files with [Enter on files](#enter-on-files) set to start with the host OS) go to the context's `startFile` callback, defaulting to `FileStarter.StartWithShell` (shell execute in the file's directory). Files inside images aren't started. The TUI runs terminal programs itself, see [`tui.md`](tui.md#terminal-programs).
- `IsExecutable(entry)` is by extension on Windows (`ExecutableExtensions`) and by execute permission elsewhere; never for directories or files inside images. Both front-ends draw executables in green.

## Changing Location

The location dialog (Ctrl+L, double-clicking the path, File > Location) has the path, `Volumes...` (the volume provider list), `Home` (`GoHomeAsync`, switching to the Local provider) and OK. `ChangeLocationAsync(path)` keeps the location when the path fails or is unchanged, and resolves:

- empty, or `Computer` in a local volume: the drive list;
- a local directory (relative to the current local directory; `c:` means `c:\`), switching to the Local provider;
- a local path into an image, e.g. `c:\images\disk.iso\archive.zip\dir1`: the closest existing file must be detected as an image and the rest are directories or images. It is opened in a `NavigationContext` of its own, so a failure leaves the location unchanged;
- otherwise, with a non-local provider shown and an unrooted path, a path from the root of a new volume of that provider.

## Volume Provider Keys

The volume provider list of the left/right pane opens with Alt+F1/Alt+F2 or Shift+F1/Shift+F2, chosen in the settings. Until chosen, the setting is null and `VolumeProviderKeyBindings.Default` applies: Alt on Windows, Shift elsewhere, because Linux desktops take Alt+F1/Alt+F2 and macOS terminals only send Alt with Option set as Meta. Choosing the keys already in effect keeps the setting unset.

## Enter on Files

The `Enter on file` setting is `OpenAsVolume` (the default: detect images, otherwise start the file) or `StartWithHost` (always start local files with the host OS; files inside images are still opened as images when detected; Ctrl+PageDown still opens a file as a volume). Both front-ends set `NavigationContext.EnterFileAction` of both panes at startup and when the settings are saved.
