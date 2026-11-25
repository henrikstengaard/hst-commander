using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Archives;

/// <summary>
/// Plugin opening rar, 7z and tar archives and gzip, xz, bzip2, zstd and compress (.Z) files (including compressed tar
/// archives) as volumes using SharpCompress, included with Hst Commander by default.
/// </summary>
public class ArchivesPlugin : IPlugin
{
    public string Name => "Archives";

    public IEnumerable<IFileVolumeProvider> FileVolumeProviders { get; } =
        [
            new RarFileVolumeProvider(), new SevenZipFileVolumeProvider(), new TarFileVolumeProvider(),
            new GzipFileVolumeProvider(), new XzFileVolumeProvider(), new Bzip2FileVolumeProvider(),
            new ZstdFileVolumeProvider(), new CompressFileVolumeProvider()
        ];
}
