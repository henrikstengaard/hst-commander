using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Core;

/// <summary>
///     A volume of its own at a location, e.g. for copying without changing the volume shown in a pane.
///     Disposing it closes resources opened for the volume, like the stream of an ISO image.
/// </summary>
/// <param name="volume">Volume with its current directory at the location.</param>
/// <param name="path">Path of the location, like <see cref="NavigationContext.CurrentPath" />.</param>
/// <param name="resource">Resource owned by the volume, disposed with the location.</param>
public sealed class VolumeLocation(IVolume volume, string path, IDisposable? resource = null) : IDisposable
{
    public IVolume Volume { get; } = volume;

    public string Path { get; } = path;

    /// <summary>View of the shared image the location is in, e.g. of a physical disk; null when there's none.</summary>
    internal SharedImageVolume? SharedImage { get; init; }

    /// <summary>Reports the progress of committing the cached writes of the shared image while it's used.</summary>
    internal IProgress<DiskWriteProgress>? DiskWriteProgress
    {
        set
        {
            if (SharedImage != null)
            {
                SharedImage.DiskWriteProgress = value;
            }
        }
    }

    /// <summary>Commits the cached writes of the shared image (see <see cref="SharedImageVolume.CommitAsync" />).</summary>
    internal Task CommitAsync() => SharedImage?.CommitAsync() ?? Task.CompletedTask;

    public void Dispose() => resource?.Dispose();
}
