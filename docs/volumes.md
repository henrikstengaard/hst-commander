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

Plugin volumes:

| Class | Notes |
|---|---|
| `ZipVolume` (Zip) | Directories are the archive's directory entries plus the directories in file paths; `/` and `\` both separate, empty, `.` and `..` components are skipped. Names match exactly, otherwise case-insensitively |
| `ArchiveVolume<T>`, `LhaVolume`, `LzxVolume` (LhaLzx) | Same directory tree rules as zip. Entries carry `AmigaMetadata` and `SupportsAmigaMetadata` is true, so copying from them works like copying from an Amiga volume. Lha names are cut at a zero character (the comment follows); `-lhd-` entries without a trailing separator (symbolic links) are skipped; lzx dates are local time. The archives aren't disposed, as that would dispose the stream Hst Commander owns |
| `AmigaFileSystemVolume` (Amiga) | Fast File System of an adf or an RDB partition, or PFS3 of an RDB partition; other file systems (e.g. SFS) throw `IOException`. See below |
| `RdbVolume` (Amiga) | Hard disk images with a rigid disk block (raw, fixed or dynamic vhd). See below |
| `VolumeListVolume`, `PartitionTableVolume` (Disk) | MBR and GPT disk images. See below |
| `HybridDiskVolume` (Disk) | A disk with both an MBR and another volume on the whole disk (e.g. a rigid disk block in blocks 1-15): the root lists both partition tables, e.g. `hybrid.img\rdb\1` |
| `DiscFileSystemVolume` (Disk) | FAT (long names) and NTFS via DiscUtils, writable when the file system is; dates are UTC; attributes can be changed; free/total space shown in the path panel (`GetSpace`) |
| `PhysicalDiskVolume` (PhysicalDisk) | The physical disks of the computer, listed as items opened like images. See below |
| `SmbVolume` (Smb, external plugin) | Windows file shares and other SMB 2/3 servers: the root lists the shares of a server, writable as the user's rights allow. See below |

ISO images and zip, lha and lzx archives are read-only. `LocalVolume.CreateContainer` throws `NotSupportedException` in the drive list. The drive list shows the size of each drive in the size column, like the physical disk list; a drive without media (e.g. an empty card reader) has no size. Local directories have no size (`<DIR>`).

### Amiga file systems

- Writable when the stream is. Changes are flushed after each operation and when a written stream is closed (PFS3 caches).
- Names are case-insensitive and can't contain `/` or `:`. Names longer than the file system allows throw `IOException`, because Hst.Amiga would silently truncate them. Case-only renames go through a temporary name.
- Amiga dates are local time; dates before 1978 become 1978-01-01. Delete and overwrite ignore protection bits. Comments are cut to 79 characters.
- `OpenRead` returns Hst.Amiga's seekable stream, so e.g. the viewer reads it directly.
- Free/total space of an adf or an RDB partition is shown in the path panel (`GetSpace`, forwarded by `RdbVolume` inside a partition).
- Disposing and closing a written stream wait on the thread pool, because Hst.Amiga waits for async writes there and would deadlock on a UI thread.

### Partition tables

- The root of an RDB, MBR or GPT disk lists the partition table as a single directory (`rdb`, `mbr` or `gpt` in paths, shown in upper case), so paths always show it, e.g. `disk.hdf\rdb\1` or `pistorm.img\mbr\2\rdb\1`. It lists the partitions as directories `1`..`n` marked `IsVolume`, with a `DisplayName` (e.g. `1 - DH0 - Workbench`, `2 - PiStorm RDB`), the partition size as `Size` (shown in the size column instead of `<DIR>`) and `Details` for the properties dialog. The partition table directory has the disk size as `Size`, also shown in the size column and as `Disk size` in its properties: the disk's length for MBR and GPT (MBR doesn't store the disk size, and the partition table covers the whole disk), and cylinders * heads * sectors * block size of the rigid disk block for RDB.
- A partition is mounted when listed or opened and stays mounted until disposed; one that can't be mounted is only tried once when listed and throws `IOException` when opened. Partition lists are read-only.
- MBR/GPT partitions are mounted as FAT or NTFS, otherwise through `IPluginHost.OpenVolume` (e.g. a PiStorm partition, MBR type `0x76` or its GPT type, opens as an `RdbVolume`).
- Sector sizes of 512 and 4096 bytes are supported. GPT tells by the header offset. MBR doesn't store it, so 4096 is used only when the partitions only fit with 4096-byte sectors, or also fit and more of them start with a recognizable volume there.
- `DiskFileVolumeProvider` doesn't match the boot sector of an unpartitioned FAT/exFAT/NTFS volume, which also ends with `0x55 0xAA`.

### Physical disks

- The `Physical disks` volume provider of the PhysicalDisk plugin lists the disks with media as items named by the device (`PhysicalDrive2` on Windows, `sdb` on Linux, `disk4` on macOS) with the model in `DisplayName`, e.g. `PhysicalDrive2 - SanDisk Ultra`, and the bus, sector size and size in `Details`. The list is read again each time it's listed, so attached disks show up when refreshing. Disks are queried at the same time (Windows and macOS), as a sleeping disk, e.g. a USB drive, only answers after waking up, which can take more than ten seconds; both front-ends show a progress indicator in the pane when listing takes longer than 2 seconds (`ListingProgress`).
- System disks (`PhysicalDisk.IsSystem`) aren't accessible, so the operating system can't be damaged: `OpenRead` and `OpenReadWrite` throw `UnauthorizedAccessException` ("... is a system disk and not accessible"), shown as an error when opening, viewing or copying one. They are the disks with the Windows volume (Windows), with `/`, `/boot` or `/boot/efi` through partitions, device mapper and RAID devices (Linux), and with the root volume and its APFS physical stores (macOS). The properties show `System disk`.
- A disk is an item, so Enter opens it as an image with the file volume providers (MBR/GPT with the Disk plugin, RDB with the Amiga plugin), and paths start with the disk, e.g. `PhysicalDrive2\mbr\1`. F3 and F5 read the whole disk.
- Each disk has a writable image id (its device path), so it is shared like a local writable image (see below). `OpenReadWrite` fails, making the disk read-only, when the OS uses a volume on it: Windows locks and dismounts the disk's volumes and fails when one can't be locked (e.g. the system drive); Linux and macOS fail when a partition is mounted.
- Reading needs administrator rights (Windows) or root (Linux: or the `disk` group). The provider has `RequiresAdministrator`, so choosing it when not elevated offers to restart as administrator, see [Running as administrator](application.md#running-as-administrator). Read errors of files that aren't local are thrown when opening them, so the error is shown instead of nothing happening.
- `DiskStream` reads and writes whole sectors through a buffer aligned in memory, as Windows physical drives and macOS raw disks require, and flushes writes when closed.
- Disks are read and written through `CachedDiskStream`, a least-recently-used cache of 64 KiB blocks, because file systems read and write many small sectors (allocation tables, directories), and each read or write of a physical disk, especially a USB drive, is slow. Small reads read a whole block once, and small writes change the cached block, so changes are written later, in order of position, with consecutive blocks written together (up to 1 MiB at a time). Reads and writes of whole blocks that aren't cached, e.g. reading a whole disk with F3/F5 or a large file's data, go to the disk directly so they don't push the cached blocks out. When the cache is full, the least recently used block without writes is dropped, and when every block holds writes they are all written.
- Cached writes are written when the file system is consistent, so a disk removed or a crash while files are copied is less likely to leave a partly written change (e.g. a directory entry without its allocation table): the stream implements `IWriteCache` and the shared image commits it between operations (see [Writable images](#writable-images)). `Flush` doesn't write the cached writes, because file systems flush their stream after each change: PFS3 (`Pfs3Volume.Flush`, called by the Amiga plugin after each operation and file written) flushes its own caches and then the stream, which would write the cache and flush the disk after each file and each date set. FFS writes its caches to the stream without flushing it. Writes are only written in the middle of an operation when the whole cache holds writes, e.g. when writing a file larger than the cache.
- The cache size is per disk opened (a disk viewed while it's open in a pane has a cache of its own), 32 MB by default, set with `CacheSize` in megabytes in `~/.hst-commander/plugins/physicaldisk/settings.json`, e.g. `{ "CacheSize": 64 }`; 0 turns the cache off (`PhysicalDiskSettings`, read when the plugin is initialized).

### SMB shares

- The SMB plugin is an external plugin, installed separately (see [Licenses](application.md#licenses)): it uses SMBLibrary (LGPL-3.0-or-later), so it isn't included with Hst Commander. The tests reference it directly.
- The `SMB` volume provider of the Smb plugin is an `ILocationVolumeProvider`: choosing it asks for an SMB path (`VolumeLocationDialog`, showing the last path entered while Hst Commander runs) and connects with `VolumeConnector.ConnectAsync`, which asks for a user name and password (`CredentialsDialog`) when `Connect` throws `CredentialsRequiredException`, again each time they're rejected, until connected or cancelled. "Connecting to ..." is shown in the pane when connecting takes longer than 2 seconds.
- `SmbPath.Parse` takes UNC paths (`\\server\share\dir`) and URLs (`smb://server/share/dir`, with `%` escapes and optional `user:password@`); `\` and `/` both separate, so `//server/share` and mixed separators work, and the leading separators can be left out. A server alone (`\\server`) lists its shares. `..` is rejected, and so is a port other than 445 (SMBLibrary only connects to 445). User names can be `DOMAIN\user` or `user@domain`.
- Connecting first logs in as guest (or with the URL's credentials). A failed login (`STATUS_LOGON_FAILURE`, `STATUS_ACCESS_DENIED`, ...) or access denied to the share or its directories asks for credentials, e.g. Windows accepts an anonymous login and then denies the share. `Connect` opens the path to check it and disconnects; the pane gets a new volume from the returned `SmbServerVolumeProvider`.
- `SmbServerVolumeProvider` (name `\\server`, `//server` on Linux and macOS) keeps the server and credentials in memory, and each `CreateVolume` connects and logs in again, so file operations get a connection of their own. Its `PathRoot` makes paths UNC paths, e.g. `\\server\share\dir`, which can be entered in the location dialog. It isn't in the volume provider list, so restarting as administrator starts such a pane locally.
- The root lists the disk shares (`IsVolume`), except hidden shares ending with `$`, which can still be entered by path, e.g. `\\server\c$`. Shares can't be created, deleted or renamed. Directories are opened before changing to them, so a directory that can't be read leaves the volume where it is.
- Requests are synchronous (SMBLibrary), so `SmbVolume` runs them on the thread pool. Reads and writes go to the server in requests of at most 1 MiB (`SmbFileStream`, seekable and unbuffered). Dates are UTC; read-only, hidden, system and archive attributes can be changed; free/total space of the share is shown in the path panel. A connection closed by the server (e.g. when idle) is connected again on the next request. Files on shares have no writable image id, so images on shares are read-only.
- Only SMB 2 and 3 are supported (no SMB 1).

## Navigation Model

Each pane's `NavigationContext` owns a `NestedVolume` over a root volume from a volume provider (initially `Local` at the startup directory). `ChangeVolumeProviderAsync` creates a new root volume, closes opened images and the previous root volume, and clears the marks. `CurrentPath` at the root is `Computer` for the local drive list and the provider's `Name` otherwise. Local-only features (starting files, executables) need a `LocalVolume` root. The disk space (free/total) in the path panel is the local drive's, or inside an image or another root volume the space the current volume returns from `IVolume.GetSpace`, read when the pane is listed so the path panel doesn't read the volume (on FAT it scans the allocation table).

### Images

Opening a file (Enter or double-click, `OpenEntryAsync`) first checks whether it is an image by asking every file volume provider, regardless of extension, so e.g. a `.nupkg`, `.jar` or `.docx` opens as a zip archive. A file no provider detects is started. `OpenAsVolumeAsync` (Ctrl+PageDown, File > Open as volume) does the same detection but throws `IOException` when nothing matches. Images are detected in any location, including inside other images.

1. Providers are asked in order with `IsMatch`, reading through the current volume's `OpenRead`. A seekable stream is reset for the next provider, otherwise the file is opened again. Local files that can't be read (`IOException`, `UnauthorizedAccessException`, `NotSupportedException`) aren't images, so they are started; for other files (and with `OpenAsVolumeAsync`) the error is thrown, e.g. a physical disk without access.
2. The first match gets the file opened again as a seekable stream; a stream that isn't seekable (e.g. inside a zip) is copied to a temporary file deleted on close. See [Temporary copies](#temporary-copies).
3. The volume is pushed on the `NestedVolume` (wrapped in `EntryVolumeProvider`) and the image (provider, file name, the directory inside its parent image, volume and stream) on the context's image stack, which `OpenLocationAsync` uses to open the images again.

Going up from an image's root pops and disposes its volume and stream. Changing the volume provider or location closes all images.

### Temporary Copies

Files that can't be read seekably, e.g. a zip archive inside a zip archive, are copied to a temporary file to open them as images or view them (F3). `NavigationContext.TemporaryCopyLimit` (default 1 GB, `DefaultTemporaryCopyLimit`) caps the copy: a larger file fails with an `IOException` saying the limit can be changed in Settings, shown as an error by the front-ends. The entry's size is checked before copying when it's known; otherwise the size is counted while copying, as a stream inside an archive doesn't know its length. 0 means no limit. The copy runs on the thread pool: awaiting each read and write on the UI thread made it crawl in the TUI, where every continuation waits for a Terminal.Gui main loop iteration. Both front-ends have a `Copy limit` setting in megabytes on the Panes tab (`TemporaryCopyLimit` in the settings file) and set it on both panes at startup and when the settings are saved. The limit applies wherever a temporary copy is made, including file operations and volume file detection opening the images again (`OpenLocationAsync`).

When the pane opens an image or views a file and the copy takes longer than `ListingProgress.Delay` (2 seconds), the pane's `ProgressIndicator` (`IProgressIndicator`) shows "Copying <name> to a temporary file..." in the same box as slow listings, with a progress bar (the fraction of the entry's size, reported at most every 100 ms) and a Cancel button, which has the focus until the box is hidden. Cancelling throws `OperationCanceledException`, which the front-ends ignore, leaving the pane where it was. Copies made by `OpenLocationAsync` for background work don't show it.

### Detecting Volume Files

After listing, both front-ends call `DetectVolumeFilesAsync` in the background to find the files that can be opened as volumes, using the same detection as opening images (every file volume provider's `IsMatch`, regardless of extension). The TUI shows them with a `>` marker before the name, the GUI with a blue arrow right over the icon. Local files are read directly; files in images or other root volumes are read from a volume of their own (`OpenLocationAsync`), so the pane's volume isn't used. Files are read on the thread pool one at a time and reported on the calling thread; `IsVolumeFile(entry)` returns the result until the entries are listed again. Files that can't be read or make a provider fail aren't volumes. The front-ends cancel detection before listing again. Directories and the drive list are skipped.

### Writable Images

- A provider with `CanWrite` (the Amiga adf and RDB providers and the Disk provider) gets a writable stream for a file of the root volume with an id from `IVolume.GetWritableImageId`: a local file (its full path, `LocalVolume`) or a physical disk (its device path, `PhysicalDiskVolume`).
- `SharedImages` opens such a file once per id (case-insensitive on Windows and macOS) with `IVolume.OpenReadWrite` (read/write sharing, no buffering), falling back to `OpenRead` (read-only) when it throws `UnauthorizedAccessException` or `IOException`. The volume is shared by both panes and the volumes of file operations, because two volumes writing one file would each keep their own file system state (e.g. free blocks) and damage it.
- Each user gets a `SharedImageVolume` view with its own current directory. Every operation, and each read/write of its streams (`LockedStream`), holds the image's semaphore and first changes the shared volume to the view's directory. Operations run on the thread pool while holding the lock, so the lock is never held by work waiting for a blocked UI thread. The file is closed when the last view is disposed.
- A stream that caches writes (`IWriteCache`, e.g. a physical disk) is committed (`IWriteCache.Commit`) at points where the file system is consistent: after an operation, or after a file opened for writing is closed, as long as no file is open for writing. The volumes have written their own caches to the stream by then (e.g. the Amiga plugin flushes the FFS and PFS3 volumes after each operation and file written). It is committed right away when the cache asks for it (`ShouldFlush`, half the cache holds writes), otherwise when no operation has been done for `SharedImages.IdleFlushDelay` (1 second). Errors of the idle commit are ignored; the writes stay cached and the next commit (an operation or closing the image) throws the error.
- Copy, move and delete commit the shared images of their locations (`VolumeLocation.SharedImage`) as their last step, also when they fail or are cancelled, so the writes are written while the progress dialog is open instead of by the idle commit after it closes, which made the panes wait for the image's lock. Commits during and at the end of the operation report `DiskWriteProgress` (bytes written of the cached writes, from `IWriteCache.CachedWriteBytes` and the progress of `Commit`) through the operation's progress as `CopyProgress.DiskWrite` and `DeleteProgress.DiskWrite`, shown by the progress dialogs as "Writing to disk 12M/16M" (see [Common rules](file-operations.md#common-rules)). Commits after other operations (e.g. making a directory) and closing the image aren't shown, as they write little.
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
- otherwise, with a non-local provider shown and an unrooted path, a path from the root of a new volume of that provider, where components are directories or images, e.g. `PhysicalDrive2\mbr\1`;
- with a non-local provider with a `PathRoot` shown, a path starting with it is a path in its volumes the same way, e.g. `\\server\share\dir` of an SMB server (`CurrentPath` shows paths with it too).

## Volume Provider Keys

The volume provider list of the left/right pane opens with Alt+F1/Alt+F2 or Shift+F1/Shift+F2, chosen in the settings. Until chosen, the setting is null and `VolumeProviderKeyBindings.Default` applies: Alt on Windows, Shift elsewhere, because Linux desktops take Alt+F1/Alt+F2 and macOS terminals only send Alt with Option set as Meta. Choosing the keys already in effect keeps the setting unset.

## Enter on Files

The `Enter on file` setting is `OpenAsVolume` (the default: detect images, otherwise start the file) or `StartWithHost` (always start local files with the host OS; files inside images are still opened as images when detected; Ctrl+PageDown still opens a file as a volume). Both front-ends set `NavigationContext.EnterFileAction` of both panes at startup and when the settings are saved.
