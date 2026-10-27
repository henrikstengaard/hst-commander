# Hst.Commander.Plugins.LhaLzx

The LhaLzx plugin adds lha and lzx archives, the common archive formats on the Amiga, to Hst Commander. It is included with Hst Commander by default and references only `Hst.Commander.Abstractions`, using `Hst.Compression` to read the archives.

## Features

- **Lha archives** (`-lh?-`, `-lz?-` and `-pm?-` compression methods) and **lzx archives** are opened as volumes with Enter and browsed like directories. They are detected by their contents, regardless of the extension.
- Directories are the archive's directory entries plus the directories in the file paths, so archives without directory entries are browsed as well.
- Amiga names, protection bits (`hsparwed`) and comments are kept: copying from an archive to an Amiga volume (e.g. an adf) keeps them as they are, and copying to other volumes stores them as UAE metadata (UAEFSDB or FS-UAE metafiles, see the UAE metadata setting). The properties dialog shows them.
- Files can be copied out of archives, viewed with F3 and opened as images, e.g. an adf inside an lha archive.
- Archives are read-only.

## Volumes

- `LhaFileVolumeProvider` and `LzxFileVolumeProvider` detect and open the archives.
- `LhaVolume` and `LzxVolume` share the directory tree of `ArchiveVolume<T>`.
