using Hst.Commander.Abstractions;
using Hst.Commander.Core;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     Colors of the TUI for a theme (<see cref="ThemeChoice" />), using the 16 legacy colors so they work in every
///     terminal. Themes of plugins get the closest of the 16 colors for each of their colors (<see cref="From" />).
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
    TuiAttribute DialogFocus,
    TuiAttribute? Button = null,
    ColorName16? MarkedContainerBackground = null,
    ColorName16? MarkedItemBackground = null)
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

    public TuiAttribute PaneNormal => new(Border, PaneBackground);

    /// <summary>Buttons of the bottom area: the theme's buttons, or the titles of the function key bar.</summary>
    public TuiAttribute ButtonTitle => Button ?? Menu;

    /// <summary>Keys shown before the button labels, e.g. "F5" of the function key bar.</summary>
    public TuiAttribute ButtonKey => Button ?? FunctionKey;

    public static TuiPalette For(ThemeChoice theme) => theme.Plugin is { } pluginTheme
        ? From(pluginTheme.Colors)
        : theme.Id switch
        {
            Themes.LightId => Light,
            Themes.DarkId => Dark,
            _ => MidnightCommander
        };

    /// <summary>The palette of the colors of a plugin theme, the closest of the 16 colors for each.</summary>
    public static TuiPalette From(ThemeColors colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var paneBackground = Closest(colors.PaneBackground);
        var menu = Attribute(colors.MenuForeground, colors.MenuBackground);
        var dialog = Attribute(colors.DialogForeground, colors.DialogBackground);
        var cursor = Attribute(colors.CursorForeground, colors.CursorBackground);
        return new TuiPalette(
            PaneBackground: paneBackground,
            Border: Closest(colors.PaneBorder),
            Header: Closest(colors.Header),
            Container: Closest(colors.Container),
            Item: Closest(colors.Item),
            Executable: Closest(colors.Executable),
            Marked: Closest(colors.Marked),
            Path: Closest(colors.Path),
            Cursor: cursor,
            MarkedCursor: new TuiAttribute(Closest(colors.Marked), Closest(colors.CursorBackground)),
            ActivePath: Attribute(colors.ActivePathForeground, colors.ActivePathBackground),
            Menu: menu,
            MenuFocus: cursor,
            MenuHot: new TuiAttribute(Closest(colors.ActivePathBackground), Closest(colors.MenuBackground)),
            FunctionKey: menu,
            Dialog: dialog,
            DialogFocus: cursor,
            Button: Attribute(colors.ButtonForeground, colors.ButtonBackground),
            MarkedContainerBackground: colors.MarkedContainerBackground == null ? null : Closest(colors.MarkedContainerBackground),
            MarkedItemBackground: colors.MarkedItemBackground == null ? null : Closest(colors.MarkedItemBackground));
    }

    /// <summary>The closest of the 16 colors to a "#RRGGBB" color, gray when it is invalid.</summary>
    public static ColorName16 Closest(string? color) =>
        color != null && Color.TryParse(color, out Color? parsed) && parsed is { } value
            ? value.GetClosestNamedColor16()
            : ColorName16.Gray;

    /// <summary>An attribute of the closest of the 16 colors to "#RRGGBB" colors.</summary>
    public static TuiAttribute Attribute(string? foreground, string? background) =>
        new(Closest(foreground), Closest(background));

    /// <summary>
    ///     Creates a scheme with all the roles Terminal.Gui v2 draws with, so none are derived from the others. Like
    ///     earlier versions, text fields (<see cref="Scheme.Editable" />) are drawn in the focus color and selected text
    ///     and unfocused selected items of lists (<see cref="Scheme.Active" />) in the focus color inverted, unless
    ///     <paramref name="active" /> is given.
    /// </summary>
    public static Scheme CreateScheme(TuiAttribute normal, TuiAttribute focus, TuiAttribute? hotNormal = null,
        TuiAttribute? hotFocus = null, TuiAttribute? active = null)
    {
        var selected = active ?? new TuiAttribute(focus.Background, focus.Foreground);
        return new Scheme
        {
            Normal = normal,
            Focus = focus,
            HotNormal = hotNormal ?? normal,
            HotFocus = hotFocus ?? focus,
            Active = selected,
            HotActive = selected,
            Highlight = hotNormal ?? normal,
            Editable = focus,
            ReadOnly = focus,
            Disabled = normal
        };
    }

    public Scheme MenuColorScheme =>
        CreateScheme(Menu, MenuFocus, MenuHot, new TuiAttribute(MenuHot.Foreground, MenuFocus.Background));

    /// <summary>
    ///     Indicator in the menu bar shown when running as administrator or root, white on red in every theme like a
    ///     warning.
    /// </summary>
    public Scheme AdministratorColorScheme
    {
        get
        {
            var attribute = new TuiAttribute(ColorName16.White, ColorName16.Red);
            return CreateScheme(attribute, attribute);
        }
    }

    public Scheme DialogColorScheme => CreateScheme(Dialog, DialogFocus);

    /// <summary>Error message boxes, white on red with the focused button black on gray in every theme.</summary>
    public Scheme ErrorColorScheme
    {
        get
        {
            var normal = new TuiAttribute(ColorName16.White, ColorName16.Red);
            var focus = new TuiAttribute(ColorName16.Black, ColorName16.Gray);
            return CreateScheme(normal, focus);
        }
    }
}
