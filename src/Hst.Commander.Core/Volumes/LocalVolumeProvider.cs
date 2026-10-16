using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Volumes;

/// <summary>
/// Volume provider of the local file system, creating a <see cref="LocalVolume" /> at the drive list.
/// </summary>
public class LocalVolumeProvider : IVolumeProvider
{
    public string Name => "Local";

    public string Description => "Local drives and directories";

    public Task<IVolume> CreateVolume(CancellationToken cancellationToken = default) =>
        Task.FromResult<IVolume>(new LocalVolume(string.Empty));
}
