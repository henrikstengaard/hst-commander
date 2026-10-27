# Hst.Commander.Plugins.Disk

The Disk plugin adds PC disk images with MBR and GPT partition tables to Hst Commander. It is included with Hst Commander by default and references only `Hst.Commander.Abstractions`, using `LTRData.DiscUtils` for the partition tables, FAT and NTFS file systems and vhd images.

## Disk images

- **MBR and GPT disk images**: raw images (e.g. `.img`) and vhd images (fixed, dynamic and differencing) are opened as volumes with Enter, detected by their contents regardless of the extension.
- The root lists the partition table as `MBR` or `GPT` and its partitions as directories, shown with their number, e.g. `1`, so paths look like `disk.img\mbr\1\dir`.
- **FAT** (with long names) and **NTFS** partitions are opened as volumes. They can be written to when the image is: copying, moving, renaming, deleting and making directories work like in local directories, and the attributes can be changed in the properties dialog.
- **PiStorm disks**: an MBR partition of type `0x76` or a GPT partition of type `3F82EEBC-87C9-4097-8165-89D6540557C0` holding a rigid disk block is opened with the Amiga plugin, e.g. `pistorm.img\mbr\2\rdb\1`.
- **Hybrid MBR/RDB disks**: a disk with both an MBR and a rigid disk block lists both partition tables in its root, e.g. `hybrid.img\rdb\1`.
- Sector sizes of 512 and 4096 bytes are supported.
- The properties of a partition show its name, volume name, file system and location (e.g. active, primary or partition id, and sectors), and the properties of a partition table its type, number of partitions and sector size.

Partition tables can't be changed, and other file systems (e.g. exFAT, ext) can't be opened yet.

## Volumes

- `DiskFileVolumeProvider` detects and opens the disk images. Partitions that aren't FAT or NTFS are opened with the other plugins' file volume providers through `IPluginHost.OpenVolume`.
- `PartitionTableVolume` lists the partitions of an MBR or GPT partition table, `VolumeListVolume` the partition tables of a disk and `HybridDiskVolume` the partition tables of a hybrid disk.
- `DiscFileSystemVolume` is a FAT or NTFS partition.
