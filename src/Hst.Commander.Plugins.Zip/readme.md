# Hst.Commander.Plugins.Zip

The Zip plugin adds zip archives to Hst Commander. It is included with Hst Commander by default and references only `Hst.Commander.Abstractions`, using `System.IO.Compression` to read the archives.

## Features

- **Zip archives** are opened as volumes with Enter and browsed like directories. They are detected by their contents (a local file header, an empty archive or a spanned archive marker), regardless of the extension, so e.g. `.nupkg`, `.jar` and `.docx` files open as zip archives too.
- Directories are the archive's directory entries plus the directories in the file paths, so archives without directory entries are browsed as well. Both `/` and `\` separate directories.
- Files can be copied out of archives, viewed with F3 and opened as images, e.g. an adf or ISO image inside a zip archive.
- Archives are read-only.

## Example

The plugin is also the smallest example of a plugin:

- `ZipPlugin` implements `IPlugin` and returns the file volume provider.
- `ZipFileVolumeProvider` implements `IFileVolumeProvider`, detecting zip archives with `IsMatch` and opening them with `CreateVolume`.
- `ZipVolume` implements `IVolume` over a `ZipArchive`.
