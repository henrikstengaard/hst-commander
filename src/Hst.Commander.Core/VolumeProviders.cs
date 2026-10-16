using Hst.Commander.Core.Volumes;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Volume providers listed when choosing what a pane shows (Alt+F1/Alt+F2), the local volume provider first, and file
///     volume providers opening files as volumes, the built-in ones first followed by those of the <see cref="Plugins" />.
/// </summary>
public static class VolumeProviders
{
    public static IVolumeProvider Local { get; } = new LocalVolumeProvider();

    public static IFileVolumeProvider Iso9660 { get; } = new Iso9660FileVolumeProvider();

    public static IReadOnlyList<IVolumeProvider> All { get; } =
        [Local, ..Plugins.All.SelectMany(plugin => plugin.VolumeProviders)];

    /// <summary>
    ///     File volume providers asked in order whether an opened file is a volume, e.g. an ISO image or a zip archive.
    /// </summary>
    public static IReadOnlyList<IFileVolumeProvider> Files { get; } =
        [Iso9660, ..Plugins.All.SelectMany(plugin => plugin.FileVolumeProviders)];
}
