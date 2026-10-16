using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Zip;

/// <summary>
/// Plugin opening zip archives as volumes, included with Hst Commander by default.
/// </summary>
public class ZipPlugin : IPlugin
{
    public string Name => "Zip";

    public IEnumerable<IFileVolumeProvider> FileVolumeProviders { get; } = [new ZipFileVolumeProvider()];
}
