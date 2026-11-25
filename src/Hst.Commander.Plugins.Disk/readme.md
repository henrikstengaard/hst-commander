# Hst.Commander.Plugins.Disk

The Disk plugin adds PC disk images with MBR and GPT partition tables to Hst Commander. It is included with Hst Commander by default and references only `Hst.Commander.Abstractions`, using `LTRData.DiscUtils` for the partition tables, file systems and virtual disk images.

## Disk images

- **MBR and GPT disk images**: raw images (e.g. `.img`), VHD images (fixed, dynamic and differencing), VHDX and VDI images (fixed and dynamic) and DMG images are opened as volumes with Enter, detected by their contents regardless of the extension.
- The root lists the partition table as `MBR` or `GPT` and its partitions as directories, shown with their number, e.g. `1`, so paths look like `disk.img\mbr\1\dir`.
- **FAT** (with long names), **exFAT** and **NTFS** partitions are opened as volumes. They can be written to when the image is: copying, moving, renaming, deleting and making directories work like in local directories, and the attributes can be changed in the properties dialog.
- **File systems without a partition table** (e.g. an image of a partition), raw or in a VHD, VHDX, VDI or DMG image, are opened as volumes too, writable for FAT, exFAT and NTFS.
- **VHDX and VDI images** are written like raw images; **DMG images** are read-only. A VHDX, VDI or DMG image holding a disk the plugin doesn't read is opened by the other plugins, e.g. an Amiga rigid disk block in a VHDX image.
- **F9** creates an exFAT file system without a partition table as a sparse file, with its size (a number with a B, KB, MB or GB unit), volume label and cluster size.
- **ext2, ext3 and ext4**, **XFS**, **Btrfs**, **HFS+**, **SquashFS** and **UDF** partitions and file systems are opened as read-only volumes (DiscUtils can't write them), with case-sensitive names except HFS+ (HFSX is case-sensitive) and UDF. SquashFS is only read when gzip compressed, and Btrfs files compressed with zstd can't be read (zlib and lzo can).
- **PiStorm disks**: an MBR partition of type `0x76` or a GPT partition of type `3F82EEBC-87C9-4097-8165-89D6540557C0` holding a rigid disk block is opened with the Amiga plugin, e.g. `pistorm.img\mbr\2\rdb\1`.
- **Hybrid MBR/RDB disks**: a disk with both an MBR and a rigid disk block lists both partition tables in its root, e.g. `hybrid.img\rdb\1`.
- Sector sizes of 512 and 4096 bytes are supported.
- The properties of a partition show its name, volume name, file system and location (e.g. active, primary or partition id, and sectors), and the properties of a partition table its type, number of partitions and sector size.

Partition tables can't be changed, the read-only file systems can't be written, and APFS, Apple partition maps (common in DMG images), differencing VHDX and VDI images and vmdk images can't be opened.

## Volumes

- `DiskFileVolumeProvider` detects and opens the disk images, virtual disk images through `VirtualDisks` (with `VirtualDiskStream` working around DiscUtils' VHDX stream, which reads and writes the wrong bytes with the asynchronous array overloads). Partitions without a file system the plugin reads, and virtual disks without a partition table or such a file system, are opened with the other plugins' file volume providers through `IPluginHost.OpenVolume` (the latter wrapped in `ResourceVolume`, disposing the virtual disk).
- `PartitionTableVolume` lists the partitions of an MBR or GPT partition table, `VolumeListVolume` the partition tables of a disk and `HybridDiskVolume` the partition tables of a hybrid disk.
- `FileSystems` detects and mounts the file systems, `FileSystemFileVolumeProvider` opens them without a partition table, `ExFatVolumeCreator` creates exFAT ones with `ExFatFormatter` (ported from Hst Imager, as DiscUtils' exFAT formatter writes an incomplete up-case table and too small a FAT above 2 TB).
- `DiscFileSystemVolume` is a file system mounted with DiscUtils; `ExFatVolumeLabel` reads the exFAT volume label, which DiscUtils doesn't.
