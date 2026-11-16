using Avalonia.Media;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Font of the panes and the bottom area: the font chosen in the settings, otherwise the theme's font when it's
///     installed (<see cref="Abstractions.ITheme.FontFamily" />), otherwise the default monospace fonts. Fonts are listed
///     by Avalonia's font manager on Windows, macOS and Linux.
/// </summary>
public static class GuiFonts
{
    /// <summary>Monospace fonts of Windows, macOS and Linux, the first installed one is used.</summary>
    public const string DefaultFamily = "Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace";

    public const double DefaultSize = 14;

    public const double MinimumSize = 6;

    public const double MaximumSize = 48;

    private static IReadOnlyList<string>? _installed;

    /// <summary>Names of the installed font families, sorted.</summary>
    public static IReadOnlyList<string> Installed => _installed ??= FontManager.Current.SystemFonts
        .Select(family => family.Name)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToList();

    /// <summary>True when one of the comma separated font families is installed.</summary>
    public static bool IsInstalled(string families) => SplitFamilies(families)
        .Any(name => Installed.Contains(name, StringComparer.OrdinalIgnoreCase));

    /// <summary>Font family and size for a theme and the font chosen in the settings (null for none).</summary>
    public static (FontFamily Family, double Size) Resolve(ThemeChoice theme, string? fontFamily, double? fontSize)
    {
        var themeFamily = theme.Plugin?.FontFamily is { } family && !string.IsNullOrWhiteSpace(family) &&
                          IsInstalled(family)
            ? family
            : null;
        var themeSize = themeFamily != null ? theme.Plugin?.FontSize : null;
        var chosen = !string.IsNullOrWhiteSpace(fontFamily) ? fontFamily.Trim() : themeFamily;
        var size = Math.Clamp(fontSize ?? themeSize ?? DefaultSize, MinimumSize, MaximumSize);
        return (new FontFamily(chosen == null ? DefaultFamily : $"{chosen}, {DefaultFamily}"), size);
    }

    private static IEnumerable<string> SplitFamilies(string families) => families
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
