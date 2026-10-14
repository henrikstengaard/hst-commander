namespace Hst.Commander.Extensions.Volume.Abstractions;

/// <summary>
/// Interface for providing volumes to Hst Commander, e.g. local drives. Volume providers are listed when choosing
/// what a pane shows (Alt+F1 for the left pane, Alt+F2 for the right pane) and are intended to be loaded as plugins.
/// </summary>
public interface IVolumeProvider
{
    /// <summary>
    /// Name of the volume provider shown in the list, e.g. "Local".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Short description of the volumes provided, shown next to the name.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Create a new volume at its root. Each call returns a volume of its own, so e.g. a copy can use one on a
    /// background thread without changing the volume shown in a pane. A volume implementing <see cref="IDisposable"/>
    /// is disposed when it is no longer used.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns>Volume at its root.</returns>
    Task<IVolume> CreateVolume(CancellationToken cancellationToken = default);
}
