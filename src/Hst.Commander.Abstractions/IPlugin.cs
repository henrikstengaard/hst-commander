namespace Hst.Commander.Abstractions;

/// <summary>
/// Interface for a plugin extending Hst Commander with volume providers and file volume providers.
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// Name of the plugin, e.g. "Zip".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Volume providers listed when choosing what a pane shows (Alt+F1/Alt+F2), after the built-in ones.
    /// </summary>
    IEnumerable<IVolumeProvider> VolumeProviders => [];

    /// <summary>
    /// File volume providers registering files that open as volumes, e.g. zip archives, asked after the built-in ones.
    /// </summary>
    IEnumerable<IFileVolumeProvider> FileVolumeProviders => [];

    /// <summary>
    /// True when the plugin can create volumes with its <see cref="VolumeCreators" />, listed in the create dialog
    /// (F9). Defaults to false: the volume creators aren't used.
    /// </summary>
    bool CanCreateVolumes => false;

    /// <summary>
    /// Volume creators creating new volumes as files, e.g. an empty Amiga disk file, listed in the create dialog (F9)
    /// when <see cref="CanCreateVolumes" /> is true. A plugin can create several kinds of volumes.
    /// </summary>
    IEnumerable<IVolumeCreator> VolumeCreators => [];
}
