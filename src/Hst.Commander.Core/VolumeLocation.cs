using Hst.Commander.Extensions.Volume.Abstractions;

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

    public void Dispose() => resource?.Dispose();
}
