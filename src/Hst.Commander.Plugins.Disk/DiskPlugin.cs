using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// Plugin opening disk images with a master boot record (MBR) or a GUID partition table (GPT) as writable volumes
/// listing their partitions, raw or in VHD, VHDX, VDI and DMG images, included with Hst Commander by default. Partitions
/// with FAT, exFAT or NTFS, or read-only ext2/3/4, XFS, Btrfs, HFS+, SquashFS or UDF are mounted by the plugin (see
/// <see cref="FileSystems" />), other partitions are opened by the file volume providers of Hst Commander and other
/// plugins (<see cref="IPluginHost.OpenVolume" />), e.g. PiStorm partitions with a rigid disk block by the Amiga plugin.
/// The file systems without a partition table are opened too, and exFAT ones created with F9.
/// </summary>
public class DiskPlugin : IPlugin
{
    private readonly DiskFileVolumeProvider _diskFileVolumeProvider = new();

    public string Name => "Disk";

    public IEnumerable<IFileVolumeProvider> FileVolumeProviders { get; }

    public IEnumerable<IVolumeCreator> VolumeCreators { get; } = [new ExFatVolumeCreator()];

    public DiskPlugin()
    {
        FileVolumeProviders = [_diskFileVolumeProvider, new FileSystemFileVolumeProvider()];
    }

    public void Initialize(IPluginHost host) => _diskFileVolumeProvider.Host = host;
}
