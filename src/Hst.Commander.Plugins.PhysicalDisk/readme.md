# Hst.Commander.Plugins.PhysicalDisk

The PhysicalDisk plugin adds the physical disks of the computer, internal and external (e.g. USB sticks, SD cards and USB drives), to Hst Commander. It is included with Hst Commander by default and references only `Hst.Commander.Abstractions`; it has no dependencies of its own.

## Physical disks

- **Physical disks** is listed in the volume provider list (Alt+F1/Alt+F2). Its root lists the disks with media, named by the device and shown with their model, e.g. `PhysicalDrive2 - SanDisk Ultra` on Windows, `sdb - SanDisk Ultra` on Linux or `disk4 - SanDisk Ultra` on macOS. The properties dialog shows the model, bus (e.g. USB, SATA, NVMe), whether it's removable or a system disk, the device, the sector size and the size. A disk that's asleep, e.g. an external USB drive, can take more than ten seconds to wake up before it's listed; the pane shows "Reading Physical disks..." with a progress indicator meanwhile.
- System disks, the disks the operating system runs from (with the Windows volume on Windows, `/` or `/boot` on Linux, the root volume on macOS), are listed but not accessible: opening, viewing or copying one shows the error "Physical disk '...' is a system disk and not accessible".
- Pressing Enter on a disk opens it like a disk image with the other plugins: MBR and GPT disks with the Disk plugin, RDB disks with the Amiga plugin, so paths start with the disk, e.g. `PhysicalDrive2\mbr\1\dir` or `PhysicalDrive2\rdb\1`. The path can also be entered in the location dialog while the physical disks are shown.
- A disk is opened for reading and writing once and shared by both panes and file operations, like writable images. It is opened read-only when it can't be written, e.g. when a volume on it is in use.
- F3 views and F5 copies a disk like a file, e.g. to make an image of it.

## Disk cache

Disks are read and written through a cache of 64 KiB blocks, so the many small reads and writes of file systems (e.g. of allocation tables and directories) read and write the disk in large blocks. Changes are written when the file system is consistent: between file operations, about a second after the last operation, or right away when half the cache holds changes. They are only written in the middle of an operation when the whole cache holds changes, e.g. when writing a file larger than the cache, and always when the disk is closed. Copy, move and delete write the changes before they finish, and their progress dialog shows "Writing to disk" with the progress while changes are written.

The cache is 32 MB for each disk opened by default. Change it with `CacheSize` in megabytes in `~/.hst-commander/plugins/physicaldisk/settings.json`, e.g. `{ "CacheSize": 64 }`, or turn it off with `0`; it is read when Hst Commander starts.

## Access

Choosing Physical disks when Hst Commander isn't running as administrator (root on Linux and macOS) offers to restart it as administrator with the panes where they were: with UAC on Windows, with sudo in the terminal in the TUI on Linux and macOS. The GUI on Linux and macOS says to start it as root. Both front-ends show `Administrator` or `root` in the menu bar while running with these rights.

- **Windows**: disks are listed by any user, but only opened as administrator. Windows doesn't allow writing to the sectors of its mounted volumes, so opening a disk for writing locks and dismounts its volumes (e.g. the FAT partition of a USB stick) until the disk is closed; when one can't be locked, e.g. the system drive or a drive with open files, the disk is opened read-only.
- **Linux**: disks (`/dev/sd*`, `/dev/nvme*`, `/dev/mmcblk*`, `/dev/vd*`, ...) are listed from `/sys/block` and opened as root or as a member of the `disk` group. A disk with a mounted partition is opened read-only.
- **macOS**: disks are listed with `diskutil` and opened as raw disks (`/dev/rdiskN`) as root. A disk with a mounted volume is opened read-only; unmount it with `diskutil unmountDisk` to write to it.

## Classes

- `PhysicalDiskPlugin` returns the `PhysicalDiskVolumeProvider` for the current operating system (`PhysicalDisks.ForCurrentPlatform`).
- `PhysicalDiskVolume` lists the disks as items with an id (`IVolume.GetWritableImageId`, the device path) and opens them with `OpenRead` and `OpenReadWrite`.
- `IPhysicalDisks` lists and opens disks, implemented by `WindowsPhysicalDisks`, `LinuxPhysicalDisks` and `MacPhysicalDisks`.
- `DiskStream` reads and writes a device through a buffer aligned to the sectors, as devices only read and write whole sectors.
- `CachedDiskStream` caches blocks of a `DiskStream` and implements `IWriteCache`, so Hst Commander commits it when the file system is consistent; `Flush` doesn't write the cache, as file systems such as PFS3 flush after each change.
- `PhysicalDiskSettings` reads the cache size from the plugin's settings file.
