namespace Hst.Commander.Abstractions;

/// <summary>
/// Buttons in the bottom area of the window, in columns sharing the width equally, e.g. the function key bar with ten
/// columns of one button. Each column lists its buttons from top to bottom, and the area is as high as the column with
/// the most buttons.
/// </summary>
/// <param name="Columns">Columns from left to right.</param>
public sealed record ButtonPanel(IReadOnlyList<ButtonColumn> Columns)
{
    /// <summary>Number of rows: the most buttons in a column.</summary>
    public int Rows => Columns.Count == 0 ? 0 : Columns.Max(column => column.Buttons.Count);
}

/// <summary>Column of a <see cref="ButtonPanel" />.</summary>
/// <param name="Buttons">Buttons from top to bottom.</param>
public sealed record ButtonColumn(IReadOnlyList<PanelButton> Buttons);

/// <summary>Button of a <see cref="ButtonPanel" /> running a command of Hst Commander when clicked.</summary>
/// <param name="Label">Text of the button, e.g. "Copy".</param>
/// <param name="Action">Command run when clicked, one of <see cref="CommanderActions" />, e.g. "Copy".</param>
/// <param name="Key">Key shown before the label, e.g. "F5", null for none.</param>
/// <param name="Background">Background "#RRGGBB", null for the theme's button background.</param>
/// <param name="Foreground">Text color "#RRGGBB", null for the theme's button text color.</param>
public sealed record PanelButton(string Label, string Action, string? Key = null, string? Background = null,
    string? Foreground = null);
