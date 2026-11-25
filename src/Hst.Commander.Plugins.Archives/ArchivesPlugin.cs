using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Archives;

/// <summary>
/// Plugin opening rar, 7z and tar archives and gzip and xz files (including compressed tar archives) as volumes using
/// SharpCompress, included with Hst Commander by default.
/// </summary>
public class ArchivesPlugin : IPlugin
{
    public string Name => "Archives";

    public IEnumerable<IFileVolumeProvider> FileVolumeProviders { get; } =
        [
            new RarFileVolumeProvider(), new SevenZipFileVolumeProvider(), new TarFileVolumeProvider(),
            new GzipFileVolumeProvider(), new XzFileVolumeProvider()
        ];
}
