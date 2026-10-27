# Hst.Commander.Plugins.Amiga

The Amiga plugin adds Amiga disk images, file systems, icons and pictures to Hst Commander. It is included with Hst Commander by default and references only `Hst.Commander.Abstractions`, using [Hst.Amiga](https://github.com/henrikstengaard/hst-amiga) for the Amiga file systems and icons and `LTRData.DiscUtils.Vhd` for vhd images.

## Disk images

- **Amiga disk files (adf)**: double density (880 KB) and high density (1.76 MB) adfs with the Fast File System (`DOS\0` to `DOS\7`) are opened as volumes with Enter, detected by their contents regardless of the extension.
- **Hard disk images with a rigid disk block (RDB)**: raw images (e.g. `.hdf`, `.img`) and vhd images (fixed, dynamic and differencing). The root lists the partition table as `RDB` and its partitions shown with their number, drive and volume names, e.g. `1 - DH0 - Workbench`. Partitions with the Fast File System or PFS3 are opened as volumes; other file systems, e.g. SFS, can't be opened yet.
- PiStorm disks (an MBR or GPT partition holding a rigid disk block) and hybrid MBR/RDB disks open through the Disk plugin, which uses this plugin for the RDB partition.
- **Writable**: adfs and RDB images opened from a local directory can be written to: copying, moving, renaming, deleting and making directories work like in local directories, and both panes share the image safely. Images inside other images (e.g. an adf inside a zip archive) are read-only.
- Amiga names, protection bits (`hsparwed`) and comments are kept when copying between Amiga volumes and stored as UAE metadata (UAEFSDB or FS-UAE metafiles) when copying to other volumes, see the UAE metadata setting. The properties dialog (Ctrl+P) shows and changes the protection bits and comment.
- The properties of a partition show its partition number, drive name, volume name, file system, boot priority and used/free space.

## Creating adfs (F9)

`Amiga disk file` creates a new double density adf with a volume name, formatted with the Fast File System (`DOS\3`) or left empty (all zeros), and optionally bootable with the standard boot block.

## Icons

- `.info` files (disk objects) are shown as the icons of their entries in the GUI, e.g. `file.info` for `file` and the drawer `dir.info` for the directory `dir`, and `Disk.info` as the icon of a partition. Supported images: true color (AmigaOS 4 PNG icons), color icons (GlowIcons), NewIcons and planar images drawn with the AmigaOS palette.
- The properties dialog shows the icon type and its Workbench position; clicking the icon shows all images of the icon (normal and selected of each kind).

## Workbench navigation mode (GUI)

The View menu's `Workbench` mode shows a directory like a Workbench 3.1 drawer window: the icons on the gray Workbench background at the positions stored in their `.info` files, with black names in the Topaz font when installed.

- Partitions show the `Disk.info` in their root, like disks on the Workbench screen.
- Icons can be dragged to other positions and saved (Save/Reset below the pane), which writes the positions to the `.info` files and creates `.info` files for entries without one.
- **Icons only** shows only entries with a `.info` file, like Show Only Icons of the Workbench (unchecked by default, showing all entries).
- **Sort by name** places the icons sorted by name in rows, centered in line, ready to be saved.

### Def icons

Like the Workbench, entries without a `.info` file are shown with a def icon (default icon): a drawer for directories, a tool for Amiga executables, a project for other files and a disk for partitions. Without def icons of your own, built-in icons in the Workbench 3.1 style are used.

Put def icons in the Amiga plugin directory `~/.hst-commander/plugins/amiga` (e.g. `C:\Users\<name>\.hst-commander\plugins\amiga` on Windows), named like in `ENVARC:Sys` on the Amiga:

| File | Used for |
|---|---|
| `def_drawer` | Directories |
| `def_tool` | Amiga executables (files starting with a hunk header) |
| `def_project` | Other files |
| `def_disk` | Partitions and other volume entries without a `Disk.info` |

Each def icon is either an Amiga icon (`def_drawer.info`, e.g. copied from `ENVARC:Sys` of an Amiga installation) or an image of any format Hst Commander can view, e.g. `def_drawer.png`, `def_drawer.iff` (IFF ILBM), `.gif`, `.jpg`, `.bmp`, `.webp` or `.ico`. Names are case-insensitive; when there are several files for a def icon, the `.info` file is used first. Images are shown in their own size, the first image of a file with several. Changed def icons are shown the next time a directory is shown.

When the position of an entry without a `.info` file is saved, its new `.info` file is a copy of the `.info` def icon; for def icons of other image formats the built-in icon is written.

## Viewing (F3, GUI)

The viewer shows all images of Amiga icons and IFF pictures: FORM ILBM (1-8 bitplanes, EHB, HAM6, HAM8, 24 and 32-bit deep ILBMs, uncompressed or ByteRun1, with a mask or transparent color), FORM PBM, several pictures in a LIST or CAT and the first frame of an ANIM.
