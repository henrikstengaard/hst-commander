using Hst.Commander.Core.Volumes;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Volume providers listed when choosing what a pane shows (Alt+F1/Alt+F2), the local volume provider first, and file
///     volume providers opening files as volumes, the built-in ones first followed by those of the <see cref="Plugins" />,
///     and the volume creators of the plugins creating new volumes as files.
/// </summary>
public static class VolumeProviders
{
    // the lists are created again when the plugins change, i.e. after plugins are loaded at startup
    private static Lists? _lists;

    public static IVolumeProvider Local { get; } = new LocalVolumeProvider();

    public static IFileVolumeProvider Iso9660 { get; } = new Iso9660FileVolumeProvider();

    public static IReadOnlyList<IVolumeProvider> All => GetLists().All;

    /// <summary>
    ///     File volume providers asked in order whether an opened file is a volume, e.g. an ISO image or a zip archive.
    /// </summary>
    public static IReadOnlyList<IFileVolumeProvider> Files => GetLists().Files;

    /// <summary>
    ///     Volume creators listed in the create dialog (F9): those of the plugins that can create volumes
    ///     (<see cref="IPlugin.CanCreateVolumes" />), in plugin order.
    /// </summary>
    public static IReadOnlyList<IVolumeCreator> Creators => GetLists().Creators;

    private static Lists GetLists()
    {
        var plugins = Plugins.All;
        if (_lists is { } lists && ReferenceEquals(lists.Plugins, plugins))
        {
            return lists;
        }

        lists = new Lists(plugins,
            [Local, ..plugins.SelectMany(plugin => plugin.VolumeProviders)],
            [Iso9660, ..plugins.SelectMany(plugin => plugin.FileVolumeProviders)],
            [..plugins.Where(plugin => plugin.CanCreateVolumes).SelectMany(plugin => plugin.VolumeCreators)]);
        _lists = lists;
        return lists;
    }

    private sealed record Lists(
        IReadOnlyList<IPlugin> Plugins,
        IReadOnlyList<IVolumeProvider> All,
        IReadOnlyList<IFileVolumeProvider> Files,
        IReadOnlyList<IVolumeCreator> Creators);
}
