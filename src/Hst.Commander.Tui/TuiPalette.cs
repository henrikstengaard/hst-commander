using Hst.Commander.Abstractions;
using Hst.Commander.Core;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     Colors of a theme converted for the terminal's effective color mode.
/// </summary>
public sealed record TuiPalette(
    Color PaneBackground,
    Color Border,
    Color Header,
    Color Container,
    Color Item,
    Color Executable,
    Color Marked,
    Color Path,
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
    Color? MarkedContainerBackground = null,
    Color? MarkedItemBackground = null,
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
    ///     The palette of the colors of a theme in the effective mode. Optional colors the theme doesn't
    ///     give fall back to the cursor's (menu and dialog focus), the menu's (key bars) and the marked entries' colors.
    /// </summary>
    public static TuiPalette From(ThemeColors colors, TuiColorMode? mode = null)
    {
        ArgumentNullException.ThrowIfNull(colors);
        Color ConvertColor(string? color) => TuiColors.Parse(color, mode);
        TuiAttribute Attribute(string? foreground, string? background) => new(ConvertColor(foreground), ConvertColor(background));
        var paneBackground = ConvertColor(colors.PaneBackground);
        var menu = Attribute(colors.MenuForeground, colors.MenuBackground);
        var dialog = Attribute(colors.DialogForeground, colors.DialogBackground);
        var cursor = Attribute(colors.CursorForeground, colors.CursorBackground);
        var key = colors.KeyForeground != null || colors.KeyBackground != null
            ? Attribute(colors.KeyForeground ?? colors.ButtonForeground, colors.KeyBackground ?? colors.ButtonBackground)
            : (TuiAttribute?)null;
        // a marked entry under the cursor in the cursor's text when the marked color is the cursor's background here
        var markedCursor = ConvertColor(colors.MarkedCursorForeground ?? colors.Marked);
        if (markedCursor == ConvertColor(colors.CursorBackground))
        {
            markedCursor = ConvertColor(colors.CursorForeground);
        }

        return new TuiPalette(
            PaneBackground: paneBackground,
            Border: ConvertColor(colors.PaneBorder),
            Header: ConvertColor(colors.Header),
            Container: ConvertColor(colors.Container),
            Item: ConvertColor(colors.Item),
            Executable: ConvertColor(colors.Executable),
            Marked: ConvertColor(colors.Marked),
            Path: ConvertColor(colors.Path),
            Cursor: cursor,
            MarkedCursor: new TuiAttribute(markedCursor, ConvertColor(colors.CursorBackground)),
            ActivePath: Attribute(colors.ActivePathForeground, colors.ActivePathBackground),
            Menu: menu,
            MenuFocus: Attribute(colors.MenuFocusForeground ?? colors.CursorForeground,
                colors.MenuFocusBackground ?? colors.CursorBackground),
            MenuHot: new TuiAttribute(ConvertColor(colors.MenuHotForeground ?? colors.ActivePathBackground),
                ConvertColor(colors.MenuBackground)),
            FunctionKey: key ?? menu,
            Dialog: dialog,
            DialogFocus: Attribute(colors.DialogFocusForeground ?? colors.CursorForeground,
                colors.DialogFocusBackground ?? colors.CursorBackground),
            Button: Attribute(colors.ButtonForeground, colors.ButtonBackground),
            MarkedContainerBackground: colors.MarkedContainerBackground == null ? null : ConvertColor(colors.MarkedContainerBackground),
            MarkedItemBackground: colors.MarkedItemBackground == null ? null : ConvertColor(colors.MarkedItemBackground),
            Key: key);
    }

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
