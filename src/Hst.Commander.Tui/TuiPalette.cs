using Hst.Commander.Abstractions;
using Hst.Commander.Core;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     Colors of the TUI for a theme (<see cref="ThemeChoice" />), using the 16 legacy colors so they work in every
///     terminal: the closest of the 16 colors for each of the theme's colors (<see cref="From" />). The built-in themes
///     give the RGB values of <see cref="TuiTrueColors" /> in their <c>tui-</c> overrides, so they get exactly the 16
///     colors they were made with.
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
    ColorName16? MarkedItemBackground = null,
    TuiAttribute? Key = null)
{
    public TuiAttribute PaneNormal => new(Border, PaneBackground);

    /// <summary>Buttons of the bottom area: the theme's buttons, or the titles of the function key bar.</summary>
    public TuiAttribute ButtonTitle => Button ?? Menu;

    /// <summary>Keys shown before the button labels, e.g. "F5" of the function key bar.</summary>
    public TuiAttribute ButtonKey => Key ?? Button ?? FunctionKey;

    /// <summary>The palette of a theme, from its TUI colors (<see cref="ThemeChoice.TuiColors" />).</summary>
    public static TuiPalette For(ThemeChoice theme) => From(theme.TuiColors);

    /// <summary>
    ///     The palette of the colors of a theme, the closest of the 16 colors for each. Optional colors the theme doesn't
    ///     give fall back to the cursor's (menu and dialog focus), the menu's (key bars) and the marked entries' colors.
    /// </summary>
    public static TuiPalette From(ThemeColors colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var paneBackground = Closest(colors.PaneBackground);
        var menu = Attribute(colors.MenuForeground, colors.MenuBackground);
        var dialog = Attribute(colors.DialogForeground, colors.DialogBackground);
        var cursor = Attribute(colors.CursorForeground, colors.CursorBackground);
        var key = colors.KeyForeground != null || colors.KeyBackground != null
            ? Attribute(colors.KeyForeground ?? colors.ButtonForeground, colors.KeyBackground ?? colors.ButtonBackground)
            : (TuiAttribute?)null;
        // a marked entry under the cursor in the cursor's text when the marked color is the cursor's background here
        var markedCursor = Closest(colors.MarkedCursorForeground ?? colors.Marked);
        if (markedCursor == Closest(colors.CursorBackground))
        {
            markedCursor = Closest(colors.CursorForeground);
        }

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
            MarkedCursor: new TuiAttribute(markedCursor, Closest(colors.CursorBackground)),
            ActivePath: Attribute(colors.ActivePathForeground, colors.ActivePathBackground),
            Menu: menu,
            MenuFocus: Attribute(colors.MenuFocusForeground ?? colors.CursorForeground,
                colors.MenuFocusBackground ?? colors.CursorBackground),
            MenuHot: new TuiAttribute(Closest(colors.MenuHotForeground ?? colors.ActivePathBackground),
                Closest(colors.MenuBackground)),
            FunctionKey: key ?? menu,
            Dialog: dialog,
            DialogFocus: Attribute(colors.DialogFocusForeground ?? colors.CursorForeground,
                colors.DialogFocusBackground ?? colors.CursorBackground),
            Button: Attribute(colors.ButtonForeground, colors.ButtonBackground),
            MarkedContainerBackground: colors.MarkedContainerBackground == null ? null : Closest(colors.MarkedContainerBackground),
            MarkedItemBackground: colors.MarkedItemBackground == null ? null : Closest(colors.MarkedItemBackground),
            Key: key);
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
