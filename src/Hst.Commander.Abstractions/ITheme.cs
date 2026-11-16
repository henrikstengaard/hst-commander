namespace Hst.Commander.Abstractions;

/// <summary>
/// Theme a plugin adds to Hst Commander, chosen in the settings next to the built-in themes: the colors of the TUI and
/// GUI, the buttons shown in the bottom area of the window (<see cref="GetBottomPanel" />) and optional parent buttons
/// at the outer side of the panes. The terminal user interface uses the closest of its 16 colors for each color. A
/// theme implementing <see cref="ICustomizableButtonPanel" /> lets the user change its buttons in the settings.
/// </summary>
public interface ITheme
{
    /// <summary>Identifier of the theme stored in the settings, unique and never changed, e.g. "DirectoryOpus4".</summary>
    string Id { get; }

    /// <summary>Name shown when choosing the theme, e.g. "Directory Opus 4".</summary>
    string Name { get; }

    /// <summary>
    /// True when the theme is dark, so the GUI draws the controls the theme doesn't color (e.g. text fields) dark.
    /// </summary>
    bool IsDark { get; }

    /// <summary>Colors of the theme.</summary>
    ThemeColors Colors { get; }

    /// <summary>
    /// True to show a narrow button going to the parent directory at the outer side of each pane: left of the left pane
    /// and right of the right pane. Defaults to false.
    /// </summary>
    bool ShowParentButtons => false;

    /// <summary>
    /// Font of the panes and the bottom area in the GUI, null for the default. Fonts aren't included with the theme: it
    /// names fonts the user may have installed, separated by commas (e.g. "Topaz a600a1200a400, MicroKnight"), and is
    /// only used when one of them is installed. The user's font setting overrides it; the TUI uses the terminal's font.
    /// </summary>
    string? FontFamily => null;

    /// <summary>Font size used with <see cref="FontFamily" /> when one of its fonts is installed, null for the default.</summary>
    double? FontSize => null;

    /// <summary>
    /// Buttons shown in the bottom area of the window, in columns. Called when the theme is applied and again after the
    /// settings are changed, e.g. after the user changed the buttons.
    /// </summary>
    ButtonPanel GetBottomPanel();
}

/// <summary>Colors of a <see cref="ITheme" />, each "#RRGGBB".</summary>
/// <param name="PaneBackground">Background of the panes.</param>
/// <param name="PaneBorder">Border of the panes.</param>
/// <param name="Header">Column headers of the panes.</param>
/// <param name="Container">Names of directories and other containers.</param>
/// <param name="Item">Names of files.</param>
/// <param name="Executable">Names of executable files.</param>
/// <param name="Marked">Names of marked entries.</param>
/// <param name="Path">Path of the inactive pane.</param>
/// <param name="ActivePathBackground">Background of the path of the active pane.</param>
/// <param name="ActivePathForeground">Text of the path of the active pane.</param>
/// <param name="CursorBackground">Background of the entry under the cursor.</param>
/// <param name="CursorForeground">Text of the entry under the cursor.</param>
/// <param name="MenuBackground">Background of the menu bar.</param>
/// <param name="MenuForeground">Text of the menu bar.</param>
/// <param name="BottomBackground">Background of the bottom area around the buttons.</param>
/// <param name="ButtonBackground">Background of buttons without their own colors.</param>
/// <param name="ButtonForeground">Text of buttons without their own colors.</param>
/// <param name="DialogBackground">Background of dialogs in the TUI.</param>
/// <param name="DialogForeground">Text of dialogs in the TUI.</param>
/// <param name="MarkedContainerBackground">
/// Background of marked directories and other containers, null for the pane background (only the text is colored).
/// </param>
/// <param name="MarkedItemBackground">Background of marked files, null for the pane background.</param>
public sealed record ThemeColors(
    string PaneBackground,
    string PaneBorder,
    string Header,
    string Container,
    string Item,
    string Executable,
    string Marked,
    string Path,
    string ActivePathBackground,
    string ActivePathForeground,
    string CursorBackground,
    string CursorForeground,
    string MenuBackground,
    string MenuForeground,
    string BottomBackground,
    string ButtonBackground,
    string ButtonForeground,
    string DialogBackground,
    string DialogForeground,
    string? MarkedContainerBackground = null,
    string? MarkedItemBackground = null);
