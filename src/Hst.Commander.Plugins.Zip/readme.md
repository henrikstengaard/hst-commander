# Hst.Commander.Plugins.Zip

The Zip plugin adds zip archives to Hst Commander. It is included with Hst Commander by default and references only `Hst.Commander.Abstractions`, using `System.IO.Compression` to read the archives and compress the files written.

## Features

- **Zip archives** are opened as volumes with Enter and browsed like directories. They are detected by their contents (a local file header, an empty archive or a spanned archive marker), regardless of the extension, so e.g. `.nupkg`, `.jar` and `.docx` files open as zip archives too.
- Directories are the archive's directory entries plus the directories in the file paths, so archives without directory entries are browsed as well. Both `/` and `\` separate directories.
- Files can be copied out of archives, viewed with F3 and opened as images, e.g. an adf or ISO image inside a zip archive.
- **Zip archives are writable** when the file is (a local file or a file on an SMB share, not one inside another image): files and directories copied or moved into an archive are added compressed with deflate (or stored when that isn't smaller), and entries can be renamed, deleted and edited. Archives larger than 4 GB or with more than 65535 entries are written with zip64 records.
- **Creating zip archives** with F9 (`Zip archive`): an empty archive is created, which files are then copied into.

## Volumes

- `ZipPlugin` implements `IPlugin` and returns the file volume provider and the volume creator.
- `ZipFileVolumeProvider` implements `IFileVolumeProvider`, detecting zip archives with `IsMatch` and opening them with `CreateVolume`.
- `ZipVolume` derives from `ArchiveVolume<T>` in `Hst.Commander.Abstractions`, reading entries with `ZipArchive` and overriding the write members when the archive's stream is writable; `ZipFormat` reads and writes the central directory and local headers (see [Writable zip archives](../../docs/volumes.md#writable-zip-archives)).
- `ZipVolumeCreator` creates an empty zip archive with F9.
