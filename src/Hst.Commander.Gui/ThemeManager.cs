using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Applies a theme (<see cref="ThemeChoice" />) to the application. Light and Dark use the Fluent control styling,
///     while Midnight Commander is a custom theme variant based on Dark with additional styles. A theme of a plugin
///     (<see cref="ITheme" />) gets a theme variant of its own based on Light or Dark, with brushes made from its colors
///     and <see cref="PluginThemeStyles" /> coloring the current row, the menu bar and the buttons.
/// </summary>
public static class ThemeManager
{
    public static readonly ThemeVariant MidnightCommanderVariant = new("MidnightCommander", ThemeVariant.Dark);

    private static readonly Dictionary<string, ThemeVariant> PluginVariants = new(StringComparer.OrdinalIgnoreCase);
    private static MidnightCommanderStyles? _midnightCommanderStyles;
    private static PluginThemeStyles? _pluginThemeStyles;

    public static void Apply(ThemeChoice theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var application = Application.Current ?? throw new InvalidOperationException("Application is not initialized.");

        application.RequestedThemeVariant = theme.Plugin is { } pluginTheme
            ? GetPluginVariant(application, pluginTheme)
            : theme.Id switch
            {
                Themes.LightId => ThemeVariant.Light,
                Themes.DarkId => ThemeVariant.Dark,
                _ => MidnightCommanderVariant
            };

        SetStyles(application, _midnightCommanderStyles ??= new MidnightCommanderStyles(),
            theme.Plugin == null && theme.Id == Themes.MidnightCommanderId);
        SetStyles(application, _pluginThemeStyles ??= new PluginThemeStyles(), theme.Plugin != null);
    }

    private static void SetStyles(Application application, Styles styles, bool isUsed)
    {
        if (isUsed)
        {
            if (!application.Styles.Contains(styles))
            {
                application.Styles.Add(styles);
            }
        }
        else
        {
            application.Styles.Remove(styles);
        }
    }

    /// <summary>
    ///     The theme variant of a plugin theme, created the first time with a theme dictionary holding the brushes of its
    ///     colors under the keys of the built-in themes in App.axaml.
    /// </summary>
    private static ThemeVariant GetPluginVariant(Application application, ITheme theme)
    {
        if (PluginVariants.TryGetValue(theme.Id, out var variant))
        {
            return variant;
        }

        variant = new ThemeVariant($"Plugin:{theme.Id}", theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light);
        var colors = theme.Colors;
        var dictionary = new ResourceDictionary
        {
            ["PaneBackground"] = Brush(colors.PaneBackground),
            ["PaneBorder"] = Brush(colors.PaneBorder),
            ["HeaderForeground"] = Brush(colors.Header),
            ["ContainerForeground"] = Brush(colors.Container),
            ["ItemForeground"] = Brush(colors.Item),
            ["ExecutableForeground"] = Brush(colors.Executable),
            ["PathForeground"] = Brush(colors.Path),
            ["ActivePathBackground"] = Brush(colors.ActivePathBackground),
            ["ActivePathForeground"] = Brush(colors.ActivePathForeground),
            ["SelectedEntryForeground"] = Brush(colors.CursorForeground),
            ["MarkedEntryForeground"] = Brush(colors.Marked),
            ["MarkedContainerBackground"] = colors.MarkedContainerBackground == null
                ? Brushes.Transparent
                : Brush(colors.MarkedContainerBackground),
            ["MarkedItemBackground"] = colors.MarkedItemBackground == null
                ? Brushes.Transparent
                : Brush(colors.MarkedItemBackground),
            ["AdministratorBackground"] = Brush("#C62828"),
            ["AdministratorForeground"] = Brush("#FFFFFF"),
            ["ViewerSelectionBackground"] = Brush(colors.CursorBackground),
            ["ViewerSelectionForeground"] = Brush(colors.CursorForeground),
            ["CursorBackground"] = Brush(colors.CursorBackground),
            ["MenuBackground"] = Brush(colors.MenuBackground),
            ["MenuForeground"] = Brush(colors.MenuForeground),
            ["BottomBackground"] = Brush(colors.BottomBackground),
            ["ButtonBackground"] = Brush(colors.ButtonBackground),
            ["ButtonForeground"] = Brush(colors.ButtonForeground)
        };
        application.Resources.ThemeDictionaries[variant] = dictionary;
        PluginVariants[theme.Id] = variant;
        return variant;
    }

    /// <summary>A brush of a "#RRGGBB" color, gray when the color is invalid.</summary>
    public static IBrush Brush(string? color) =>
        new SolidColorBrush(Color.TryParse(color, out var parsed) ? parsed : Colors.Gray);
}
