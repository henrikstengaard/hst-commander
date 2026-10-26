namespace Hst.Commander.Abstractions;

/// <summary>
/// Interface for a plugin extending Hst Commander with volume providers, file volume providers, volume creators, icon
/// providers, navigation modes and image providers.
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

    /// <summary>
    /// Icon providers reading icons from icon files next to entries, e.g. Amiga .info files, shown left of the entry
    /// names in the GUI. Asked in plugin order; the first provider with an icon file listed for an entry is used.
    /// </summary>
    IEnumerable<IIconProvider> IconProviders => [];

    /// <summary>
    /// Navigation modes added to the panes of the GUI after the built-in list and thumbnail modes, e.g. the Amiga
    /// Workbench showing icons at their positions.
    /// </summary>
    IEnumerable<INavigationMode> NavigationModes => [];

    /// <summary>
    /// Image providers reading the images of files viewed with F3, e.g. Amiga icons and IFF ILBM pictures. Asked in
    /// plugin order before the built-in ones (standard image formats and Windows icon files); the first provider
    /// reading images from a file is used.
    /// </summary>
    IEnumerable<IImageProvider> ImageProviders => [];

    /// <summary>
    /// Called once by Hst Commander when the plugin is loaded, before it is used, with the services of Hst Commander,
    /// e.g. to open partitions with the file volume providers of other plugins. Defaults to doing nothing.
    /// </summary>
    void Initialize(IPluginHost host)
    {
    }
}
