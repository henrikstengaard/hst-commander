# Hst.Commander.Plugins.LhaLzx

The LhaLzx plugin adds lha and lzx archives, the common archive formats on the Amiga, to Hst Commander. It is included with Hst Commander by default and references only `Hst.Commander.Abstractions`, using `Hst.Compression` to read the archives and write lha archives.

## Features

- **Lha archives** (`-lh?-`, `-lz?-` and `-pm?-` compression methods) and **lzx archives** are opened as volumes with Enter and browsed like directories. They are detected by their contents, regardless of the extension.
- Directories are the archive's directory entries plus the directories in the file paths, so archives without directory entries are browsed as well.
- Amiga names, protection bits (`hsparwed`) and comments are kept: copying from an archive to an Amiga volume (e.g. an adf) keeps them as they are, and copying to other volumes stores them as UAE metadata (UAEFSDB or FS-UAE metafiles, see the UAE metadata setting). The properties dialog shows them.
- Files can be copied out of archives, viewed with F3 and opened as images, e.g. an adf inside an lha archive.
- **Lha archives are writable** when the file is (a local file or a file on an SMB share, not one inside another image): files and directories copied or moved into an archive are added as Amiga entries with their protection bits and comments, compressed with `-lh5-`, and entries can be renamed, deleted and changed in the properties dialog. Lzx archives are read-only.
- **Creating lha archives** with F9 (`Lha archive`): an empty archive is created, which files are then copied into.

## Volumes

- `LhaFileVolumeProvider` and `LzxFileVolumeProvider` detect and open the archives.
- `LhaVolume` and `LzxVolume` derive from `ArchiveVolume<T>` in `Hst.Commander.Abstractions`, which builds the directory tree. `LhaVolume` overrides its write members when the archive's stream is writable (see [Writable lha archives](../../docs/volumes.md#writable-lha-archives)).
- `LhaVolumeCreator` creates an empty lha archive with F9.
