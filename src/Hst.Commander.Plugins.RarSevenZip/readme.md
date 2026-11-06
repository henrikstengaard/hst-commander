# Hst.Commander.Plugins.RarSevenZip

The RarSevenZip plugin adds rar and 7z archives to Hst Commander. It is included with Hst Commander by default and references only `Hst.Commander.Abstractions`, using `SharpCompress` (MIT) to read the archives.

## Features

- **Rar archives** (rar 1.5 to 4 and rar 5) and **7z archives** are opened as volumes with Enter and browsed like directories. They are detected by their contents, regardless of the extension.
- Directories are the archive's directory entries plus the directories in the file paths, so archives without directory entries are browsed as well.
- Files can be copied out of archives, viewed with F3 and opened as images, e.g. an adf inside a 7z archive. Files of solid archives are copied in the archive's order in one pass (see below), so the solid block is decompressed once; opening a single file (e.g. with F3) still decompresses the files before it in its block, which takes long when a large file comes before it.
- Damaged archives: files are checked against their size and CRC, and errors decompressing them are shown as "File '<name>' can't be decompressed: the archive is damaged", so a copy stops with an error instead of leaving damaged files. In a solid archive, the files after the damage in the same block can't be read either.
- Archives are read-only.
- Encrypted files are listed but can't be read (there is no way to enter a password), and archives with encrypted headers can't be opened.
- Only the opened volume of a multi-volume rar archive is read: files continuing in the next volume can't be read.

## Volumes

- `RarFileVolumeProvider` detects rar archives by the signature `Rar!` 1A 07 followed by 00 (rar 4) or 01 00 (rar 5), and `SevenZipFileVolumeProvider` 7z archives by `7z` BC AF 27 1C.
- `SharpCompressVolume` derives from `ArchiveVolume<T>` in `Hst.Commander.Abstractions` (without Amiga metadata), browsing the entries of a SharpCompress archive. It disposes the archive with the volume, leaving the stream Hst Commander owns open (`LeaveStreamOpen`).
- `ArchiveEntryStream` wraps the stream of each file:
  - It reads synchronously, also when read asynchronously: SharpCompress decompresses much slower when read asynchronously (17 times slower for lzma in 7z archives), and Hst Commander copies with asynchronous reads, so copying from an archive took minutes instead of seconds.
  - It checks the size and CRC32 (slicing-by-8) of the file when read to the end; files without a CRC in the archive (0) only have their size checked.
  - It throws decompression errors of damaged archives (e.g. `IndexOutOfRangeException` of the lzma decoder) as `InvalidDataException` naming the file, and its `Dispose` never throws (SharpCompress decompresses the rest of a file on dispose, which fails again in a damaged archive).
- `SharpCompressVolume` returns the archive's order from `GetReadOrder` for solid archives, so copying reads the files in that order and the sequential reader passes no files. Copying `AmigaOS41F` (2.2 GB in three solid blocks, with a 2 GB disk image before small files) went from restarting the reader 24 times to one pass.
- `SolidArchiveReader` reads the files of solid archives. SharpCompress decompresses a solid block from its start for each file opened, which makes copying many files take quadratic time (minutes before the first percent of a large archive). The reader keeps one sequential reader and only restarts it when a file before its position is opened. Files it passes in the parent directory of the opened file's directory (or in its own directory at the root and in top-level directories) are cached in temporary files, up to 1 GiB, so files copied in another order than the archive's don't restart it; the rest of the archive isn't cached. Cached files are deleted when read and when the volume is disposed. A file still being read when the reader moves on is moved to a temporary file first. After a decompression error the reader is reset, so the next file opened starts a new one.
