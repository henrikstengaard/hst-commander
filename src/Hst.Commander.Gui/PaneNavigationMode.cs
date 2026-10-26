using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Navigation mode of a pane: the built-in list (rows with size and modified date) or thumbnail (large icons with
///     the names below, row by row) mode, or a navigation mode of a plugin showing icons at positions (e.g. the Amiga
///     Workbench).
/// </summary>
/// <param name="Name">Name shown in the View menu and stored in the settings.</param>
/// <param name="Description">Description of the navigation mode.</param>
/// <param name="PluginMode">Navigation mode of a plugin, null for the built-in modes.</param>
public sealed record PaneNavigationMode(string Name, string Description, INavigationMode? PluginMode = null)
{
    public static PaneNavigationMode List { get; } = new("List", "Entries in rows with size and modified date");

    public static PaneNavigationMode Thumbnail { get; } =
        new("Thumbnail", "Large icons with the names below, row by row");

    /// <summary>The built-in modes followed by the navigation modes of the plugins.</summary>
    public static IReadOnlyList<PaneNavigationMode> All =>
    [
        List, Thumbnail,
        ..VolumeProviders.NavigationModes.Select(mode => new PaneNavigationMode(mode.Name, mode.Description, mode))
    ];

    /// <summary>The navigation mode with a name, e.g. stored in the settings; the list mode when there is none.</summary>
    public static PaneNavigationMode Find(string? name) =>
        All.FirstOrDefault(mode => mode.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? List;
}
