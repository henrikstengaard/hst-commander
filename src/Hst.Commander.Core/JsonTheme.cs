using System.Globalization;
using System.Text.Json;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     A theme read from a JSON theme file in the format of opencode's themes (see docs/themes.md): a light and/or dark
///     variant, each with a <c>palette</c> of a few seed colors the theme's colors are derived from and
///     <c>overrides</c> setting colors by name. Overrides prefixed with <c>tui-</c> or <c>gui-</c> only apply to that
///     front-end. A file with both variants gives two themes, e.g. "Dracula Dark" and "Dracula Light". JSON themes show
///     the function key bar in the bottom area.
/// </summary>
public sealed class JsonTheme : ITheme
{
    /// <summary>Prefix of overrides only used by the TUI.</summary>
    public const string TuiPrefix = "tui-";

    /// <summary>Prefix of overrides only used by the GUI.</summary>
    public const string GuiPrefix = "gui-";

    /// <summary>Names of the colors overrides can set, the names of <see cref="ThemeColors" /> in kebab case.</summary>
    public static IReadOnlyList<string> ColorNames { get; } =
    [
        "pane-background", "pane-border", "header", "container", "item", "executable", "marked", "path",
        "active-path-background", "active-path-foreground", "cursor-background", "cursor-foreground",
        "menu-background", "menu-foreground", "bottom-background", "button-background", "button-foreground",
        "dialog-background", "dialog-foreground", "marked-container-background", "marked-item-background",
        "marked-cursor-foreground", "menu-focus-background", "menu-focus-foreground", "menu-hot-foreground",
        "key-background", "key-foreground", "dialog-focus-background", "dialog-focus-foreground",
        "viewer-selection-background", "viewer-selection-foreground"
    ];

    private JsonTheme(string id, string name, bool isDark, ThemeColors colors, ThemeColors tuiColors,
        JsonThemeOptions options)
    {
        Id = id;
        Name = name;
        IsDark = isDark;
        Colors = colors;
        TuiColors = tuiColors;
        SystemChrome = options.SystemChrome;
        ShowParentButtons = options.ShowParentButtons;
        HighlightActivePathPanel = options.HighlightActivePathPanel;
        FontFamily = options.FontFamily;
        FontSize = options.FontSize;
    }

    public string Id { get; }

    public string Name { get; }

    public bool IsDark { get; }

    /// <summary>Colors of the GUI: derived from the palette, with the overrides and the <c>gui-</c> overrides.</summary>
    public ThemeColors Colors { get; }

    /// <summary>Colors of the TUI: derived from the palette, with the overrides and the <c>tui-</c> overrides.</summary>
    public ThemeColors TuiColors { get; }

    /// <summary>
    ///     True when the GUI keeps the system (Fluent) look of the menu bar, the bottom area and its buttons, like the
    ///     built-in themes; their colors are then only used by the TUI.
    /// </summary>
    public bool SystemChrome { get; }

    public bool ShowParentButtons { get; }

    public bool HighlightActivePathPanel { get; }

    public string? FontFamily { get; }

    public double? FontSize { get; }

    public ButtonPanel GetBottomPanel() => FunctionKeyBar.Default;

    /// <summary>
    ///     Reads the themes of a JSON theme file: one per variant, the dark variant first. Throws
    ///     <see cref="FormatException" /> when the file isn't a valid theme.
    /// </summary>
    /// <param name="json">Contents of the file.</param>
    /// <param name="fallbackId">Identifier used when the file has none, e.g. the file name without its extension.</param>
    public static IReadOnlyList<JsonTheme> Parse(string json, string fallbackId)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException exception)
        {
            throw new FormatException($"Invalid JSON: {exception.Message}", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("A theme must be a JSON object");
            }

            var id = GetString(root, "id") ?? fallbackId;
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new FormatException("The theme has no id");
            }

            var name = GetString(root, "name") ?? id;
            var options = new JsonThemeOptions(
                SystemChrome: false,
                ShowParentButtons: GetBoolean(root, "showParentButtons"),
                HighlightActivePathPanel: GetBoolean(root, "highlightActivePathPanel"),
                FontFamily: GetString(root, "fontFamily"),
                FontSize: root.TryGetProperty("fontSize", out var size) && size.ValueKind == JsonValueKind.Number
                    ? size.GetDouble()
                    : null);

            var variants = new[] { (Key: "dark", IsDark: true, Suffix: "Dark"), (Key: "light", IsDark: false, Suffix: "Light") }
                .Where(variant => root.TryGetProperty(variant.Key, out var element) &&
                                  element.ValueKind == JsonValueKind.Object)
                .ToList();
            if (variants.Count == 0)
            {
                throw new FormatException("The theme has neither a \"dark\" nor a \"light\" variant");
            }

            return variants.Select(variant =>
            {
                var element = root.GetProperty(variant.Key);
                var palette = GetColors(element, "palette", null);
                var background = palette.GetValueOrDefault("neutral") ?? (variant.IsDark ? "#1E1E1E" : "#FFFFFF");
                var overrides = GetColors(element, "overrides", background);
                var variantOptions = options with { SystemChrome = GetBoolean(element, "systemChrome") };
                return new JsonTheme(
                    variants.Count == 1 ? id : $"{id}-{variant.Key}",
                    variants.Count == 1 ? name : $"{name} {variant.Suffix}",
                    variant.IsDark,
                    ToThemeColors(Merge(Derive(palette, overrides, variant.IsDark, false), overrides, GuiPrefix)),
                    ToThemeColors(Merge(Derive(palette, overrides, variant.IsDark, true), overrides, TuiPrefix)),
                    variantOptions);
            }).ToList();
        }
    }

    /// <summary>
    ///     The colors derived from the palette: the pane in the neutral (background) color with directories in ink
    ///     (text) and files a little dimmer, the cursor and active path in the primary color, the column headers in the
    ///     accent color, executables in the success color and marked entries in the warning color. Menus, buttons and
    ///     dialogs are drawn in the neutral color mixed with a little ink. opencode's <c>text-weak</c> and
    ///     <c>border-weak-base</c> overrides are used for files and the border. The TUI's border (also the color of
    ///     the column headers) has more ink, so it doesn't become the light gray of the 16 colors on a light background.
    /// </summary>
    private static Dictionary<string, string> Derive(IReadOnlyDictionary<string, string> palette,
        IReadOnlyDictionary<string, string> overrides, bool isDark, bool isTui)
    {
        string Seed(string name, string fallback) => palette.TryGetValue(name, out var value) ? value : fallback;

        var neutral = Seed("neutral", isDark ? "#1E1E1E" : "#FFFFFF");
        var ink = Seed("ink", isDark ? "#D4D4D4" : "#1E1E1E");
        var primary = Seed("primary", isDark ? "#4CC2FF" : "#0067C0");
        var accent = Seed("accent", primary);
        var success = Seed("success", isDark ? "#6CCB5F" : "#008000");
        var warning = Seed("warning", isDark ? "#FFD700" : "#B07800");
        var weak = overrides.TryGetValue("text-weak", out var textWeak) ? textWeak : ThemeColor.Mix(ink, neutral, 0.75);
        var primaryText = ThemeColor.Contrasting(primary, neutral, ink);
        return new Dictionary<string, string>
        {
            ["pane-background"] = neutral,
            ["pane-border"] = overrides.TryGetValue("border-weak-base", out var border)
                ? border
                : ThemeColor.Mix(ink, neutral, isTui ? 0.5 : 0.3),
            ["header"] = accent,
            ["container"] = ink,
            ["item"] = weak,
            ["executable"] = success,
            ["marked"] = warning,
            ["path"] = weak,
            ["active-path-background"] = primary,
            ["active-path-foreground"] = primaryText,
            ["cursor-background"] = primary,
            ["cursor-foreground"] = primaryText,
            ["menu-background"] = ThemeColor.Mix(ink, neutral, 0.1),
            ["menu-foreground"] = ink,
            ["bottom-background"] = neutral,
            ["button-background"] = ThemeColor.Mix(ink, neutral, 0.15),
            ["button-foreground"] = ink,
            ["dialog-background"] = ThemeColor.Mix(ink, neutral, 0.08),
            ["dialog-foreground"] = ink,
            ["viewer-selection-background"] = ThemeColor.Mix(primary, neutral, 0.35),
            ["viewer-selection-foreground"] = ink
        };
    }

    /// <summary>The derived colors with the overrides and then the overrides of one front-end.</summary>
    private static Dictionary<string, string> Merge(Dictionary<string, string> derived,
        IReadOnlyDictionary<string, string> overrides, string prefix)
    {
        var colors = new Dictionary<string, string>(derived, StringComparer.OrdinalIgnoreCase);
        foreach (var name in ColorNames)
        {
            if (overrides.TryGetValue(name, out var color))
            {
                colors[name] = color;
            }
        }

        foreach (var name in ColorNames)
        {
            if (overrides.TryGetValue(prefix + name, out var color))
            {
                colors[name] = color;
            }
        }

        return colors;
    }

    private static ThemeColors ToThemeColors(Dictionary<string, string> colors)
    {
        string? Optional(string name) => colors.GetValueOrDefault(name);

        return new ThemeColors(colors["pane-background"], colors["pane-border"], colors["header"],
            colors["container"], colors["item"], colors["executable"], colors["marked"], colors["path"],
            colors["active-path-background"], colors["active-path-foreground"], colors["cursor-background"],
            colors["cursor-foreground"], colors["menu-background"], colors["menu-foreground"],
            colors["bottom-background"], colors["button-background"], colors["button-foreground"],
            colors["dialog-background"], colors["dialog-foreground"])
        {
            MarkedContainerBackground = Optional("marked-container-background"),
            MarkedItemBackground = Optional("marked-item-background"),
            MarkedCursorForeground = Optional("marked-cursor-foreground"),
            MenuFocusBackground = Optional("menu-focus-background"),
            MenuFocusForeground = Optional("menu-focus-foreground"),
            MenuHotForeground = Optional("menu-hot-foreground"),
            KeyBackground = Optional("key-background"),
            KeyForeground = Optional("key-foreground"),
            DialogFocusBackground = Optional("dialog-focus-background"),
            DialogFocusForeground = Optional("dialog-focus-foreground"),
            ViewerSelectionBackground = Optional("viewer-selection-background"),
            ViewerSelectionForeground = Optional("viewer-selection-foreground")
        };
    }

    /// <summary>
    ///     The colors of an object of a variant, normalized to "#RRGGBB". Colors with an alpha ("#RRGGBBAA") are drawn
    ///     over the background (the alpha is ignored without one). Values that aren't colors (e.g. opencode's
    ///     "var(--...)" references) are left out, so the derived color is used.
    /// </summary>
    private static Dictionary<string, string> GetColors(JsonElement element, string name, string? background)
    {
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!element.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object)
        {
            return colors;
        }

        foreach (var property in obj.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String &&
                ThemeColor.TryParse(property.Value.GetString(), background, out var color))
            {
                colors[property.Name] = color;
            }
        }

        return colors;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static bool GetBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private sealed record JsonThemeOptions(
        bool SystemChrome,
        bool ShowParentButtons,
        bool HighlightActivePathPanel,
        string? FontFamily,
        double? FontSize);
}

/// <summary>"#RRGGBB" colors of themes: parsing, mixing and contrast.</summary>
public static class ThemeColor
{
    /// <summary>
    ///     Normalizes "#RGB", "#RRGGBB" or "#RRGGBBAA" (the alpha is ignored) to "#RRGGBB" in upper case; false for
    ///     anything else.
    /// </summary>
    public static bool TryNormalize(string? text, out string color)
    {
        color = string.Empty;
        var value = text?.Trim();
        if (value is not { Length: 4 or 7 or 9 } || value[0] != '#' ||
            !value.Skip(1).All(Uri.IsHexDigit))
        {
            return false;
        }

        var hex = value.Length == 4
            ? string.Concat(value[1], value[1], value[2], value[2], value[3], value[3])
            : value.Substring(1, 6);
        color = "#" + hex.ToUpperInvariant();
        return true;
    }

    /// <summary>
    ///     Parses "#RGB", "#RRGGBB" or "#RRGGBBAA" to "#RRGGBB", drawing a color with an alpha over a background
    ///     ("#RRGGBB", the alpha is ignored when it's null); false for anything else.
    /// </summary>
    public static bool TryParse(string? text, string? background, out string color)
    {
        if (!TryNormalize(text, out color))
        {
            return false;
        }

        var value = text!.Trim();
        if (value.Length == 9 && background != null && TryNormalize(background, out var solidBackground))
        {
            var alpha = int.Parse(value.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            color = Mix(color, solidBackground, alpha);
        }

        return true;
    }

    /// <summary>A color mixed from two colors, <paramref name="amount" /> of the first (0 to 1).</summary>
    public static string Mix(string first, string second, double amount)
    {
        var (r1, g1, b1) = ToRgb(first);
        var (r2, g2, b2) = ToRgb(second);
        int Channel(int a, int b) => (int)Math.Round(a * amount + b * (1 - amount));
        return ToHex(Channel(r1, r2), Channel(g1, g2), Channel(b1, b2));
    }

    /// <summary>
    ///     The one of the candidates with the most contrast to a background, or black or white when none of them has
    ///     a contrast ratio of 4.5 (WCAG AA for text).
    /// </summary>
    public static string Contrasting(string background, params string[] candidates)
    {
        var best = candidates.MaxBy(candidate => ContrastRatio(background, candidate));
        return best != null && ContrastRatio(background, best) >= 4.5
            ? best
            : new[] { "#000000", "#FFFFFF" }.MaxBy(candidate => ContrastRatio(background, candidate))!;
    }

    /// <summary>WCAG contrast ratio of two colors, 1 to 21.</summary>
    public static double ContrastRatio(string first, string second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string color)
    {
        var (r, g, b) = ToRgb(color);
        static double Linear(int channel)
        {
            var value = channel / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linear(r) + 0.7152 * Linear(g) + 0.0722 * Linear(b);
    }

    private static (int R, int G, int B) ToRgb(string color)
    {
        if (!TryNormalize(color, out var normalized))
        {
            throw new FormatException($"Invalid color \"{color}\"");
        }

        return (Parse(normalized, 1), Parse(normalized, 3), Parse(normalized, 5));
    }

    private static int Parse(string color, int index) =>
        int.Parse(color.AsSpan(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string ToHex(int r, int g, int b) => $"#{r:X2}{g:X2}{b:X2}";
}
