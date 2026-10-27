namespace Hst.Commander.Abstractions;

/// <summary>
/// Navigation mode a plugin adds to the panes of the GUI, next to the built-in list and thumbnail modes: the entries of
/// the current directory are shown as icons placed at positions on a background, e.g. the Amiga Workbench. Icons
/// without a position are placed by Hst Commander from the upper left corner, row by row. The user can drag icons to
/// other positions and save them with <see cref="SavePositions" />.
/// </summary>
public interface INavigationMode
{
    /// <summary>Name shown when choosing the navigation mode, e.g. "Workbench".</summary>
    string Name { get; }

    /// <summary>Description shown next to the name.</summary>
    string Description { get; }

    /// <summary>Colors and font of the pane in the navigation mode.</summary>
    NavigationModeStyle Style { get; }

    /// <summary>
    /// Options shown as check boxes next to the button going to the parent, e.g. showing only entries with icons.
    /// Their values are passed to <see cref="Layout" />.
    /// </summary>
    IReadOnlyList<NavigationModeOption> Options => [];

    /// <summary>
    /// Lays out the entries of the current directory of a volume: the icons of the entries shown, in order, with their
    /// images and positions. Entries without an icon are hidden, e.g. icon files. The parent entry ".." isn't included,
    /// Hst Commander shows a button going to the parent above the icons. Reads with the volume, which
    /// is the pane's volume, so it is called on the thread using the pane; a mode may change the volume's directory
    /// (e.g. to read the icon inside a volume entry) when it changes back before returning.
    /// </summary>
    /// <param name="volume">Volume at the directory.</param>
    /// <param name="entries">Entries of the directory.</param>
    /// <param name="options">
    /// Values of the <see cref="Options" /> by name, null or a missing option for its default value.
    /// </param>
    /// <param name="cancellationToken"></param>
    Task<NavigationLayout> Layout(IVolume volume, IReadOnlyList<IEntry> entries,
        IReadOnlyDictionary<string, bool>? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the positions of icons moved by the user in the current directory of a volume, e.g. in their icon files,
    /// creating icon files for entries without one. Only called for writable locations, with the volume at the
    /// directory, which must be at the directory again when it returns.
    /// </summary>
    /// <param name="volume">Volume at the directory.</param>
    /// <param name="entries">Entries of the directory, as passed to <see cref="Layout" />.</param>
    /// <param name="positions">New positions of the moved icons.</param>
    /// <param name="cancellationToken"></param>
    Task SavePositions(IVolume volume, IReadOnlyList<IEntry> entries, IReadOnlyList<NavigationIconPosition> positions,
        CancellationToken cancellationToken = default);
}

/// <summary>Colors and font of a pane in a navigation mode.</summary>
/// <param name="Background">Background color, "#RRGGBB".</param>
/// <param name="Foreground">Color of the icon names, "#RRGGBB".</param>
/// <param name="FontFamily">Font families of the icon names in order of preference, null for the pane's font.</param>
/// <param name="FontSize">Font size of the icon names, null for the pane's font size.</param>
public sealed record NavigationModeStyle(string Background, string Foreground, string? FontFamily = null,
    double? FontSize = null);

/// <summary>Option of a navigation mode shown as a check box, see <see cref="INavigationMode.Options" />.</summary>
/// <param name="Name">Name of the option in the values passed to <see cref="INavigationMode.Layout" />.</param>
/// <param name="Label">Text of the check box, e.g. "Icons only".</param>
/// <param name="Description">Description shown as tooltip.</param>
/// <param name="DefaultValue">Value until the user changes it.</param>
public sealed record NavigationModeOption(string Name, string Label, string Description, bool DefaultValue = false)
{
    /// <summary>The value of the option in values passed to <see cref="INavigationMode.Layout" />.</summary>
    public bool GetValue(IReadOnlyDictionary<string, bool>? options) =>
        options != null && options.TryGetValue(Name, out var value) ? value : DefaultValue;
}

/// <summary>Icons of the entries of a directory shown in a navigation mode, in order.</summary>
public sealed record NavigationLayout(IReadOnlyList<NavigationIcon> Icons);

/// <summary>
/// Icon of an entry in a navigation mode, drawn in its own size with the name centered below it.
/// </summary>
/// <param name="Name">Name of the entry (<see cref="IEntry.Name" />).</param>
/// <param name="Image">Image of the icon, null for Hst Commander's icon of the entry.</param>
/// <param name="SelectedImage">Image shown while the icon is selected, null to draw the image highlighted.</param>
/// <param name="X">Horizontal position of the image in pixels from the left, null when the icon has no position.</param>
/// <param name="Y">Vertical position of the image in pixels from the top, null when the icon has no position.</param>
/// <param name="IsMovable">False when the icon can't be dragged to another position, e.g. it has nowhere to store it.</param>
public sealed record NavigationIcon(string Name, IconImage? Image, IconImage? SelectedImage, int? X, int? Y,
    bool IsMovable = true);

/// <summary>Position of an icon moved by the user, of the image in pixels from the upper left corner.</summary>
public sealed record NavigationIconPosition(string Name, int X, int Y);
