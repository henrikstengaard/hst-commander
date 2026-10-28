using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.PhysicalDisk;

/// <summary>
/// Plugin listing the physical disks of the computer, internal and external (e.g. attached with USB), as a volume
/// provider, included with Hst Commander by default. A disk is opened like a disk image by the file volume providers
/// of Hst Commander and other plugins, e.g. MBR and GPT disks by the Disk plugin and RDB disks by the Amiga plugin.
/// The size of the disk cache is read from "settings.json" in the plugin's directory (see
/// <see cref="PhysicalDiskSettings" />).
/// </summary>
public class PhysicalDiskPlugin : IPlugin
{
    private PhysicalDiskSettings _settings = new();

    public string Name => "PhysicalDisk";

    public IEnumerable<IVolumeProvider> VolumeProviders =>
        PhysicalDisks.ForCurrentPlatform() is { } disks
            ? [new PhysicalDiskVolumeProvider(disks, _settings.CacheSizeInBytes)]
            : [];

    public void Initialize(IPluginHost host) =>
        _settings = PhysicalDiskSettings.Load(host.GetPluginDirectory(Name));
}
