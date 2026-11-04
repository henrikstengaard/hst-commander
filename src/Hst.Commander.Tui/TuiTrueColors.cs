using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     RGB values written for the 16 colors in terminals with true color, the Campbell scheme of Windows Terminal.
///     Terminal.Gui's own values make Gray 128,128,128, barely lighter than DarkGray, so black on gray (the cursor, the
///     active path, dialogs and text fields) is very dark, and Blue and Cyan fully saturated.
/// </summary>
/// <remarks>
///     A class of its own, not part of <see cref="TuiPalette" />: an attribute keeps the RGB values of its colors when
///     it's created, and reading a static field of <see cref="TuiPalette" /> would create its palettes before the
///     values are set.
/// </remarks>
public static class TuiTrueColors
{
    private static readonly Dictionary<ColorName16, string> Colors = new()
    {
        [ColorName16.Black] = "#0C0C0C",
        [ColorName16.Blue] = "#0037DA",
        [ColorName16.Green] = "#13A10E",
        [ColorName16.Cyan] = "#3A96DD",
        [ColorName16.Red] = "#C50F1F",
        [ColorName16.Magenta] = "#881798",
        [ColorName16.Yellow] = "#C19C00",
        [ColorName16.Gray] = "#CCCCCC",
        [ColorName16.DarkGray] = "#767676",
        [ColorName16.BrightBlue] = "#3B78FF",
        [ColorName16.BrightGreen] = "#16C60C",
        [ColorName16.BrightCyan] = "#61D6D6",
        [ColorName16.BrightRed] = "#E74856",
        [ColorName16.BrightMagenta] = "#B4009E",
        [ColorName16.BrightYellow] = "#F9F1A5",
        [ColorName16.White] = "#F2F2F2"
    };

    /// <summary>
    ///     Sets the RGB values of the 16 colors. Called before any palette is created, and after each
    ///     <c>Application.Init</c> in case a Terminal.Gui configuration file sets other values: with 16 colors the
    ///     nearest color is looked up in the same table, so it must match the attributes.
    /// </summary>
    public static void Apply() => Color.Colors16 = new Dictionary<ColorName16, string>(Colors);
}
