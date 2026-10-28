using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.PhysicalDisk;

/// <summary>Volume provider listing the physical disks (see <see cref="PhysicalDiskVolume" />).</summary>
/// <param name="disks">Physical disks of the operating system.</param>
/// <param name="cacheSize">Most bytes cached for each disk opened, 0 to not cache.</param>
public class PhysicalDiskVolumeProvider(IPhysicalDisks disks, long cacheSize = PhysicalDiskSettings.DefaultCacheSize)
    : IVolumeProvider
{
    public string Name => PhysicalDiskVolume.VolumeName;

    public string Description => "Internal and external physical disks";

    /// <summary>Disks are only opened as administrator (Windows) or root (Linux and macOS).</summary>
    public bool RequiresAdministrator => true;

    public Task<IVolume> CreateVolume(CancellationToken cancellationToken = default) =>
        Task.FromResult<IVolume>(new PhysicalDiskVolume(disks, cacheSize));
}
