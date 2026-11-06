using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.RarSevenZip;

/// <summary>
/// Plugin opening rar and 7z archives as volumes using SharpCompress, included with Hst Commander by default.
/// </summary>
public class RarSevenZipPlugin : IPlugin
{
    public string Name => "RarSevenZip";

    public IEnumerable<IFileVolumeProvider> FileVolumeProviders { get; } =
        [new RarFileVolumeProvider(), new SevenZipFileVolumeProvider()];
}
