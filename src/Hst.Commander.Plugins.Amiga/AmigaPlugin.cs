using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// Plugin opening Amiga disk files (adf) and hard disk images with a rigid disk block (RDB) as writable volumes and
/// creating new adfs (F9), showing the icons of .info files and showing directories like the Workbench in the GUI,
/// included with Hst Commander by default.
/// </summary>
public class AmigaPlugin : IPlugin
{
    public string Name => "Amiga";

    public IEnumerable<IFileVolumeProvider> FileVolumeProviders { get; } =
        [new AdfFileVolumeProvider(), new RdbFileVolumeProvider()];

    public bool CanCreateVolumes => true;

    public IEnumerable<IVolumeCreator> VolumeCreators { get; } = [new AdfVolumeCreator()];

    public IEnumerable<IIconProvider> IconProviders { get; } = [new AmigaIconProvider()];

    public IEnumerable<INavigationMode> NavigationModes { get; } = [new WorkbenchNavigationMode()];
}
