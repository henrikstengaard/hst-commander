using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Themes to choose from in the settings: the built-in themes (Midnight Commander, Light and Dark), drawn with the
///     colors of each front-end and the function key bar, followed by the themes of the plugins
///     (<see cref="IPlugin.Themes" />).
/// </summary>
public static class Themes
{
    public const string MidnightCommanderId = "MidnightCommander";
    public const string LightId = "Light";
    public const string DarkId = "Dark";

    /// <summary>
    ///     The built-in themes. Their identifiers are the names of the theme enum stored in the settings before themes
    ///     could be added by plugins, so those settings keep their theme.
    /// </summary>
    public static IReadOnlyList<ThemeChoice> BuiltIn { get; } =
    [
        new(MidnightCommanderId, "Midnight Commander"),
        new(LightId, "Light"),
        new(DarkId, "Dark")
    ];

    /// <summary>The built-in themes followed by the themes of all plugins.</summary>
    public static IReadOnlyList<ThemeChoice> All => List(Plugins.All);

    /// <summary>
    ///     The built-in themes followed by the themes of plugins, in plugin order. A theme with the identifier of a theme
    ///     before it is left out.
    /// </summary>
    public static IReadOnlyList<ThemeChoice> List(IEnumerable<IPlugin> plugins)
    {
        var themes = new List<ThemeChoice>(BuiltIn);
        foreach (var plugin in plugins)
        {
            foreach (var theme in plugin.Themes)
            {
                if (themes.All(choice => !string.Equals(choice.Id, theme.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    themes.Add(new ThemeChoice(theme.Id, theme.Name, theme));
                }
            }
        }

        return themes;
    }

    /// <summary>The theme with an identifier, Midnight Commander when none has it, e.g. its plugin isn't loaded.</summary>
    public static ThemeChoice Find(string? id) => Find(All, id);

    /// <summary>The theme of a list with an identifier, the first theme when none has it.</summary>
    public static ThemeChoice Find(IReadOnlyList<ThemeChoice> themes, string? id) =>
        themes.FirstOrDefault(theme => string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase)) ?? themes[0];
}

/// <summary>Theme to choose from, see <see cref="Themes" />.</summary>
/// <param name="Id">Identifier stored in the settings, e.g. "MidnightCommander".</param>
/// <param name="Name">Name shown in the settings, e.g. "Midnight Commander".</param>
/// <param name="Plugin">Theme of a plugin, null for a built-in theme.</param>
public sealed record ThemeChoice(string Id, string Name, ITheme? Plugin = null)
{
    /// <summary>Buttons in the bottom area: the plugin theme's, or the function key bar.</summary>
    public ButtonPanel GetBottomPanel() => Plugin?.GetBottomPanel() ?? FunctionKeyBar.Default;

    /// <summary>True when the theme shows parent buttons at the outer side of the panes.</summary>
    public bool ShowParentButtons => Plugin?.ShowParentButtons ?? false;

    /// <summary>The editable buttons of the theme, null when the user can't change them.</summary>
    public ICustomizableButtonPanel? CustomizableButtons => Plugin as ICustomizableButtonPanel;
}
