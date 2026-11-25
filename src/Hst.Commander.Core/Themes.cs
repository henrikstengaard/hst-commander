using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Themes to choose from in the settings: the built-in themes (Midnight Commander, Light and Dark), the themes of
///     the plugins (<see cref="IPlugin.Themes" />), then the included themes adapted from opencode and the user's own
///     themes in <see cref="UserDirectory" />, sorted by name. Built-in, included and user themes are JSON theme files
///     (<see cref="JsonTheme" />); a user theme with the identifier of a built-in or included theme replaces it.
/// </summary>
public static class Themes
{
    public const string MidnightCommanderId = "MidnightCommander";
    public const string LightId = "Light";
    public const string DarkId = "Dark";

    /// <summary>Prefix of the names of the embedded theme files of the built-in themes.</summary>
    private const string BuiltInResourcePrefix = "Themes.BuiltIn.";

    /// <summary>Prefix of the names of the embedded theme files adapted from opencode.</summary>
    private const string IncludedResourcePrefix = "Themes.Included.";

    /// <summary>The built-in theme files in the order they are listed.</summary>
    private static readonly string[] BuiltInFiles = ["midnight-commander.json", "light.json", "dark.json"];

    private static readonly Lazy<IReadOnlyList<JsonTheme>> LazyBuiltIn = new(() =>
        BuiltInFiles.SelectMany(file => ReadResource(BuiltInResourcePrefix + file)).ToList());

    private static readonly Lazy<IReadOnlyList<JsonTheme>> LazyIncluded = new(() =>
        typeof(Themes).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(IncludedResourcePrefix, StringComparison.Ordinal) &&
                           name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .SelectMany(ReadResource)
            .ToList());

    private static readonly Lazy<IReadOnlyList<JsonTheme>> LazyUser = new(() => LoadDirectory(UserDirectory));

    /// <summary>
    ///     Directory of the user's JSON theme files, <c>themes</c> in the settings directory. Read once when the themes
    ///     are first listed, so new and changed files are used after a restart.
    /// </summary>
    public static string UserDirectory => Path.Combine(SettingsPaths.Directory, "themes");

    /// <summary>
    ///     The built-in themes. Their identifiers are the names of the theme enum stored in the settings before themes
    ///     could be added by plugins, so those settings keep their theme.
    /// </summary>
    public static IReadOnlyList<JsonTheme> BuiltIn => LazyBuiltIn.Value;

    /// <summary>The themes adapted from opencode included with Hst Commander, a dark and a light theme per file.</summary>
    public static IReadOnlyList<JsonTheme> Included => LazyIncluded.Value;

    /// <summary>The user's themes read from <see cref="UserDirectory" />.</summary>
    public static IReadOnlyList<JsonTheme> User => LazyUser.Value;

    /// <summary>All themes: built-in, of all plugins, included and the user's.</summary>
    public static IReadOnlyList<ThemeChoice> All => List(Plugins.All, User);

    /// <summary>
    ///     The built-in themes, then the themes of plugins in plugin order, then the included and the user's themes
    ///     sorted by name. A user theme with the identifier of a built-in or included theme replaces it; any other theme
    ///     with the identifier of a theme before it is left out.
    /// </summary>
    public static IReadOnlyList<ThemeChoice> List(IEnumerable<IPlugin> plugins, IEnumerable<JsonTheme>? userThemes = null)
    {
        var user = (userThemes ?? []).ToList();
        var replaced = new HashSet<JsonTheme>();

        ITheme Replace(JsonTheme theme)
        {
            var replacement = user.FirstOrDefault(userTheme => SameId(userTheme, theme));
            if (replacement == null)
            {
                return theme;
            }

            replaced.Add(replacement);
            return replacement;
        }

        var themes = new List<ThemeChoice>();
        void Add(ITheme theme)
        {
            if (themes.All(choice => !SameId(choice.Theme, theme)))
            {
                themes.Add(new ThemeChoice(theme));
            }
        }

        foreach (var theme in BuiltIn)
        {
            Add(Replace(theme));
        }

        foreach (var theme in plugins.SelectMany(plugin => plugin.Themes))
        {
            Add(theme);
        }

        var others = Included.Select(Replace).Concat(user.Where(theme => !replaced.Contains(theme)))
            .OrderBy(theme => theme.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var theme in others)
        {
            Add(theme);
        }

        return themes;
    }

    /// <summary>The theme with an identifier, Midnight Commander when none has it, e.g. its plugin isn't loaded.</summary>
    public static ThemeChoice Find(string? id) => Find(All, id);

    /// <summary>The theme of a list with an identifier, the first theme when none has it.</summary>
    public static ThemeChoice Find(IReadOnlyList<ThemeChoice> themes, string? id) =>
        themes.FirstOrDefault(theme => string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase)) ?? themes[0];

    /// <summary>
    ///     Reads the JSON theme files (<c>*.json</c>) of a directory, sorted by file name. A file that can't be read or
    ///     isn't a valid theme is logged and left out; a missing directory has no themes.
    /// </summary>
    public static IReadOnlyList<JsonTheme> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var themes = new List<JsonTheme>();
        try
        {
            foreach (var file in Directory.GetFiles(directory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    themes.AddRange(JsonTheme.Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file)));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                      or FormatException)
                {
                    ErrorLog.Error($"Theme file '{file}' isn't used", exception);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Error($"Themes in '{directory}' can't be listed", exception);
        }

        return themes;
    }

    private static bool SameId(ITheme first, ITheme second) =>
        string.Equals(first.Id, second.Id, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<JsonTheme> ReadResource(string name)
    {
        using var stream = typeof(Themes).Assembly.GetManifestResourceStream(name) ??
                           throw new InvalidOperationException($"Embedded theme '{name}' not found");
        using var reader = new StreamReader(stream);
        var fileName = name[(name.LastIndexOf('.', name.Length - 6) + 1)..^5];
        return JsonTheme.Parse(reader.ReadToEnd(), fileName);
    }
}

/// <summary>Theme to choose from, see <see cref="Themes" />.</summary>
/// <param name="Theme">The theme: a <see cref="JsonTheme" /> or a theme of a plugin.</param>
public sealed record ThemeChoice(ITheme Theme)
{
    /// <summary>Identifier stored in the settings, e.g. "MidnightCommander".</summary>
    public string Id => Theme.Id;

    /// <summary>Name shown in the settings, e.g. "Midnight Commander".</summary>
    public string Name => Theme.Name;

    /// <summary>Buttons in the bottom area: the plugin theme's, or the function key bar.</summary>
    public ButtonPanel GetBottomPanel() => Theme.GetBottomPanel();

    /// <summary>True when the labels of the bottom area's buttons are centered, for the button banks of plugin themes.</summary>
    public bool CenterButtonLabels => Theme is not JsonTheme;

    /// <summary>True when the theme shows parent buttons at the outer side of the panes.</summary>
    public bool ShowParentButtons => Theme.ShowParentButtons;

    /// <summary>True when the whole panel above the active pane has the active path's colors, not only the path.</summary>
    public bool HighlightActivePathPanel => Theme.HighlightActivePathPanel;

    /// <summary>The editable buttons of the theme, null when the user can't change them.</summary>
    public ICustomizableButtonPanel? CustomizableButtons => Theme as ICustomizableButtonPanel;

    /// <summary>Colors used by the GUI.</summary>
    public ThemeColors GuiColors => Theme.Colors;

    /// <summary>Colors used by the TUI, which a JSON theme can set apart from the GUI's.</summary>
    public ThemeColors TuiColors => (Theme as JsonTheme)?.TuiColors ?? Theme.Colors;

    /// <summary>True when the GUI keeps the system look of the menu bar, bottom area and buttons.</summary>
    public bool SystemChrome => (Theme as JsonTheme)?.SystemChrome ?? false;
}
