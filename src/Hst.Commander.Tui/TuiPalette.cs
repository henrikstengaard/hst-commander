using Terminal.Gui;
using TuiAttribute = Terminal.Gui.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     Colors of the TUI for a <see cref="TuiTheme" />, using the 16 legacy colors so they work in every terminal.
/// </summary>
public sealed record TuiPalette(
    ColorName16 PaneBackground,
    ColorName16 Border,
    ColorName16 Header,
    ColorName16 Container,
    ColorName16 Item,
    ColorName16 Executable,
    ColorName16 Marked,
    ColorName16 Path,
    TuiAttribute Cursor,
    TuiAttribute MarkedCursor,
    TuiAttribute ActivePath,
    TuiAttribute Menu,
    TuiAttribute MenuFocus,
    TuiAttribute MenuHot,
    TuiAttribute FunctionKey,
    TuiAttribute Dialog,
    TuiAttribute DialogFocus)
{
    /// <summary>Blue panes with directories in white, files in gray, executables in green and cyan menu and function key bars.</summary>
    public static TuiPalette MidnightCommander { get; } = new(
        PaneBackground: ColorName16.Blue,
        Border: ColorName16.White,
        Header: ColorName16.BrightYellow,
        Container: ColorName16.White,
        Item: ColorName16.Gray,
        Executable: ColorName16.BrightGreen,
        Marked: ColorName16.BrightYellow,
        Path: ColorName16.BrightYellow,
        Cursor: new TuiAttribute(ColorName16.Black, ColorName16.Gray),
        MarkedCursor: new TuiAttribute(ColorName16.BrightYellow, ColorName16.Gray),
        ActivePath: new TuiAttribute(ColorName16.Black, ColorName16.Gray),
        Menu: new TuiAttribute(ColorName16.Black, ColorName16.Cyan),
        MenuFocus: new TuiAttribute(ColorName16.White, ColorName16.Black),
        MenuHot: new TuiAttribute(ColorName16.BrightYellow, ColorName16.Cyan),
        FunctionKey: new TuiAttribute(ColorName16.White, ColorName16.Black),
        Dialog: new TuiAttribute(ColorName16.Black, ColorName16.Gray),
        DialogFocus: new TuiAttribute(ColorName16.Black, ColorName16.BrightCyan));

    public static TuiPalette Light { get; } = new(
        PaneBackground: ColorName16.White,
        Border: ColorName16.DarkGray,
        Header: ColorName16.Blue,
        Container: ColorName16.Black,
        Item: ColorName16.DarkGray,
        Executable: ColorName16.Green,
        Marked: ColorName16.Red,
        Path: ColorName16.Black,
        Cursor: new TuiAttribute(ColorName16.White, ColorName16.Blue),
        MarkedCursor: new TuiAttribute(ColorName16.BrightYellow, ColorName16.Blue),
        ActivePath: new TuiAttribute(ColorName16.White, ColorName16.Blue),
        Menu: new TuiAttribute(ColorName16.Black, ColorName16.Gray),
        MenuFocus: new TuiAttribute(ColorName16.White, ColorName16.Blue),
        MenuHot: new TuiAttribute(ColorName16.Blue, ColorName16.Gray),
        FunctionKey: new TuiAttribute(ColorName16.White, ColorName16.DarkGray),
        Dialog: new TuiAttribute(ColorName16.Black, ColorName16.White),
        DialogFocus: new TuiAttribute(ColorName16.White, ColorName16.Blue));

    public static TuiPalette Dark { get; } = new(
        PaneBackground: ColorName16.Black,
        Border: ColorName16.DarkGray,
        Header: ColorName16.Gray,
        Container: ColorName16.White,
        Item: ColorName16.Gray,
        Executable: ColorName16.BrightGreen,
        Marked: ColorName16.BrightYellow,
        Path: ColorName16.Gray,
        Cursor: new TuiAttribute(ColorName16.White, ColorName16.DarkGray),
        MarkedCursor: new TuiAttribute(ColorName16.BrightYellow, ColorName16.DarkGray),
        ActivePath: new TuiAttribute(ColorName16.Black, ColorName16.BrightCyan),
        Menu: new TuiAttribute(ColorName16.White, ColorName16.DarkGray),
        MenuFocus: new TuiAttribute(ColorName16.Black, ColorName16.BrightCyan),
        MenuHot: new TuiAttribute(ColorName16.BrightCyan, ColorName16.DarkGray),
        FunctionKey: new TuiAttribute(ColorName16.Gray, ColorName16.Black),
        Dialog: new TuiAttribute(ColorName16.White, ColorName16.DarkGray),
        DialogFocus: new TuiAttribute(ColorName16.Black, ColorName16.BrightCyan));

    /// <summary>
    ///     RGB values written for the 16 colors in terminals with true color, the Campbell scheme of Windows Terminal.
    ///     Terminal.Gui's own values make Gray 128,128,128, barely lighter than DarkGray, so black on gray (the cursor,
    ///     the active path and text fields) is very dark, and Blue and Cyan fully saturated.
    /// </summary>
    private static readonly Dictionary<ColorName16, string> TrueColors = new()
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
    ///     Sets the RGB values of the 16 colors. Called before any palette is created, as an attribute keeps the RGB
    ///     values of its colors, and after each <c>Application.Init</c>, which may reload Terminal.Gui's own values:
    ///     with 16 colors the nearest color is looked up in the same table, so it must match the attributes.
    /// </summary>
    public static void ApplyTrueColors() => Color.Colors16 = new Dictionary<ColorName16, string>(TrueColors);

    public TuiAttribute PaneNormal => new(Border, PaneBackground);

    public static TuiPalette For(TuiTheme theme) => theme switch
    {
        TuiTheme.Light => Light,
        TuiTheme.Dark => Dark,
        _ => MidnightCommander
    };

    public ColorScheme MenuColorScheme => new()
    {
        Normal = Menu,
        Focus = MenuFocus,
        HotNormal = MenuHot,
        HotFocus = new TuiAttribute(MenuHot.Foreground, MenuFocus.Background),
        Disabled = Menu
    };

    /// <summary>
    ///     Indicator in the menu bar shown when running as administrator or root, white on red in every theme like a
    ///     warning.
    /// </summary>
    public ColorScheme AdministratorColorScheme
    {
        get
        {
            var attribute = new TuiAttribute(ColorName16.White, ColorName16.Red);
            return new ColorScheme
            {
                Normal = attribute, Focus = attribute, HotNormal = attribute, HotFocus = attribute,
                Disabled = attribute
            };
        }
    }

    public ColorScheme DialogColorScheme => new()
    {
        Normal = Dialog,
        Focus = DialogFocus,
        HotNormal = Dialog,
        HotFocus = DialogFocus,
        Disabled = Dialog
    };

    /// <summary>Error message boxes, white on red with the focused button black on gray in every theme.</summary>
    public ColorScheme ErrorColorScheme
    {
        get
        {
            var normal = new TuiAttribute(ColorName16.White, ColorName16.Red);
            var focus = new TuiAttribute(ColorName16.Black, ColorName16.Gray);
            return new ColorScheme
            {
                Normal = normal, Focus = focus, HotNormal = normal, HotFocus = focus, Disabled = normal
            };
        }
    }
}
