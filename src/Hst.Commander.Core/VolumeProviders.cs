using Hst.Commander.Core.Volumes;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Volume providers listed when choosing what a pane shows (Alt+F1/Alt+F2), the local volume provider first.
///     Volume providers found as plugins at startup are intended to be added after the built-in ones.
/// </summary>
public static class VolumeProviders
{
    public static IVolumeProvider Local { get; } = new LocalVolumeProvider();

    public static IReadOnlyList<IVolumeProvider> All { get; } = [Local];
}
