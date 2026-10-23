using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// Plugin opening disk images with a master boot record (MBR) or a GUID partition table (GPT) as writable volumes
/// listing their partitions, included with Hst Commander by default. Partitions with FAT or NTFS are mounted by the
/// plugin, other partitions are opened by the file volume providers of Hst Commander and other plugins
/// (<see cref="IPluginHost.OpenVolume" />), e.g. PiStorm partitions with a rigid disk block by the Amiga plugin.
/// </summary>
public class DiskPlugin : IPlugin
{
    private readonly DiskFileVolumeProvider _diskFileVolumeProvider = new();

    public string Name => "Disk";

    public IEnumerable<IFileVolumeProvider> FileVolumeProviders => [_diskFileVolumeProvider];

    public void Initialize(IPluginHost host) => _diskFileVolumeProvider.Host = host;
}
