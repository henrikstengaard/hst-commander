using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.LhaLzx;

/// <summary>
/// Plugin opening lha and lzx archives as volumes and creating lha archives using Hst.Compression, included with Hst
/// Commander by default.
/// </summary>
public class LhaLzxPlugin : IPlugin
{
    public string Name => "LhaLzx";

    public IEnumerable<IFileVolumeProvider> FileVolumeProviders { get; } =
        [new LhaFileVolumeProvider(), new LzxFileVolumeProvider()];

    public IEnumerable<IVolumeCreator> VolumeCreators { get; } = [new LhaVolumeCreator()];
}
