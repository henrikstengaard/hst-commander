using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Applies a theme (<see cref="ThemeChoice" />) to the application. Each theme gets a theme variant of its own based
///     on Light or Dark (<see cref="ITheme.IsDark" />), which colors the controls the theme doesn't, with a theme
///     dictionary of brushes made from its GUI colors (<see cref="ThemeChoice.GuiColors" />) under the keys the views
///     use. Unless the theme keeps the system look (<see cref="ThemeChoice.SystemChrome" />, like the built-in themes),
///     <see cref="ThemeChromeStyles" /> colors the menu bar, the bottom area and the buttons with them too.
/// </summary>
public static class ThemeManager
{
    private static readonly Dictionary<string, ThemeVariant> Variants = new(StringComparer.OrdinalIgnoreCase);
    private static ThemeChromeStyles? _chromeStyles;

    public static void Apply(ThemeChoice theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var application = Application.Current ?? throw new InvalidOperationException("Application is not initialized.");
        application.RequestedThemeVariant = GetVariant(application, theme);

        var chromeStyles = _chromeStyles ??= new ThemeChromeStyles();
        if (!theme.SystemChrome)
        {
            if (!application.Styles.Contains(chromeStyles))
            {
                application.Styles.Add(chromeStyles);
            }
        }
        else
        {
            application.Styles.Remove(chromeStyles);
        }
    }

    /// <summary>
    ///     The theme variant of a theme, created the first time with a theme dictionary holding the brushes of its
    ///     colors.
    /// </summary>
    private static ThemeVariant GetVariant(Application application, ThemeChoice theme)
    {
        if (Variants.TryGetValue(theme.Id, out var variant))
        {
            return variant;
        }

        variant = new ThemeVariant($"Theme:{theme.Id}", theme.Theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light);
        var colors = theme.GuiColors;
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
            ["ViewerSelectionBackground"] = Brush(colors.ViewerSelectionBackground ?? colors.CursorBackground),
            ["ViewerSelectionForeground"] = Brush(colors.ViewerSelectionForeground ?? colors.CursorForeground),
            ["CursorBackground"] = Brush(colors.CursorBackground)
        };
        // the chrome's colors, only for themes coloring it: ButtonBackground and ButtonForeground are also the keys of
        // Fluent's buttons, so they color all buttons, including those of dialogs
        if (!theme.SystemChrome)
        {
            dictionary["MenuBackground"] = Brush(colors.MenuBackground);
            dictionary["MenuForeground"] = Brush(colors.MenuForeground);
            dictionary["BottomBackground"] = Brush(colors.BottomBackground);
            dictionary["ButtonBackground"] = Brush(colors.ButtonBackground);
            dictionary["ButtonForeground"] = Brush(colors.ButtonForeground);
        }

        application.Resources.ThemeDictionaries[variant] = dictionary;
        Variants[theme.Id] = variant;
        return variant;
    }

    /// <summary>A brush of a "#RRGGBB" color, gray when the color is invalid.</summary>
    public static IBrush Brush(string? color) =>
        new SolidColorBrush(Color.TryParse(color, out var parsed) ? parsed : Colors.Gray);
}
