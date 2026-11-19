using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// Plugin opening Amiga disk files (adf), hard disk images with a rigid disk block (RDB) and partition images without
/// one (e.g. UAE hardfiles) as writable volumes and
/// creating new adfs (F9), showing the icons of .info files, showing directories like the Workbench in the GUI and
/// viewing Amiga icons and IFF ILBM pictures (F3) and adding the Comment and Tooltypes tabs to the properties
/// dialog, included with Hst Commander by default. The Workbench shows def
/// icons from the plugin's directory (<see cref="IPluginHost.GetPluginDirectory" />) for entries without a .info file.
/// </summary>
public class AmigaPlugin : IPlugin
{
    public const string PluginName = "Amiga";

    private readonly DefIcons _defIcons = new();

    public AmigaPlugin()
    {
        NavigationModes = [new WorkbenchNavigationMode(_defIcons)];
    }

    public string Name => PluginName;

    public IEnumerable<IFileVolumeProvider> FileVolumeProviders { get; } =
        [new AdfFileVolumeProvider(), new RdbFileVolumeProvider(), new AmigaPartitionFileVolumeProvider()];

    public bool CanCreateVolumes => true;

    public IEnumerable<IVolumeCreator> VolumeCreators { get; } = [new AdfVolumeCreator()];

    public IEnumerable<IIconProvider> IconProviders { get; } = [new AmigaIconProvider()];

    public IEnumerable<INavigationMode> NavigationModes { get; }

    public IEnumerable<IImageProvider> ImageProviders { get; } = [new AmigaImageProvider()];

    public IEnumerable<IPropertiesTabProvider> PropertiesTabProviders { get; } = [new AmigaPropertiesTabProvider()];

    public void Initialize(IPluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        _defIcons.Host = host;
        _defIcons.Directory = host.GetPluginDirectory(Name);
    }
}
