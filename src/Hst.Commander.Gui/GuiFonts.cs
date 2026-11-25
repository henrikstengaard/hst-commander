using Avalonia.Media;
using Avalonia.Media.Fonts;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Font of the panes and the bottom area: the font chosen in the settings, otherwise the theme's font when it's
///     installed (<see cref="Abstractions.ITheme.FontFamily" />), otherwise the default monospace fonts. Fonts are the
///     ones Avalonia's font manager lists on Windows, macOS and Linux plus the fonts downloaded for themes
///     (<see cref="FontDownloader.DefaultDirectory" />), which only Hst Commander loads.
/// </summary>
public static class GuiFonts
{
    /// <summary>Monospace fonts of Windows, macOS and Linux, the first installed one is used.</summary>
    public const string DefaultFamily = "Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace";

    public const double DefaultSize = 14;

    public const double MinimumSize = 6;

    public const double MaximumSize = 48;

    private static IReadOnlyList<string>? _installed;

    private static LocalFontCollection? _local;

    /// <summary>Names of the installed font families and the downloaded ones, sorted.</summary>
    public static IReadOnlyList<string> Installed => _installed ??= FontManager.Current.SystemFonts
        .Select(family => family.Name)
        .Concat(LocalFamilies)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static IReadOnlyCollection<string> LocalFamilies => _local?.FamilyNames ?? [];

    /// <summary>
    ///     Loads the fonts downloaded for themes, again after downloading, so they can be used like installed fonts.
    ///     Fonts that can't be read are left out.
    /// </summary>
    public static void LoadDownloadedFonts()
    {
        if (_local != null)
        {
            FontManager.Current.RemoveFontCollection(_local.Key);
        }

        _local = new LocalFontCollection(FontDownloader.DefaultDirectory);
        FontManager.Current.AddFontCollection(_local);
        _installed = null;
    }

    /// <summary>True when one of the comma separated font families is installed or downloaded.</summary>
    public static bool IsInstalled(string families) => SplitFamilies(families)
        .Any(name => Installed.Contains(name, StringComparer.OrdinalIgnoreCase));

    /// <summary>Font family and size for a theme and the font chosen in the settings (null for none).</summary>
    public static (FontFamily Family, double Size) Resolve(ThemeChoice theme, string? fontFamily, double? fontSize)
    {
        var themeFamily = theme.Theme.FontFamily is { } family && !string.IsNullOrWhiteSpace(family) &&
                          IsInstalled(family)
            ? family
            : null;
        var themeSize = themeFamily != null ? theme.Theme.FontSize : null;
        var chosen = !string.IsNullOrWhiteSpace(fontFamily) ? fontFamily.Trim() : themeFamily;
        var size = Math.Clamp(fontSize ?? themeSize ?? DefaultSize, MinimumSize, MaximumSize);
        var families = chosen == null ? DefaultFamily : $"{Qualify(chosen)}, {DefaultFamily}";
        return (new FontFamily(families), size);
    }

    /// <summary>
    ///     The comma separated font families with the downloaded ones as "fonts:hst-commander#Name", the way Avalonia
    ///     finds fonts of a font collection.
    /// </summary>
    private static string Qualify(string families) => string.Join(", ", SplitFamilies(families)
        .Select(name => LocalFamilies.Contains(name, StringComparer.OrdinalIgnoreCase)
            ? $"{LocalFontCollection.CollectionKey.OriginalString}#{name}"
            : name));

    private static IEnumerable<string> SplitFamilies(string families) => families
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The TrueType and OpenType fonts in a directory.</summary>
    private sealed class LocalFontCollection : FontCollectionBase
    {
        public static readonly Uri CollectionKey = new("fonts:hst-commander");

        public LocalFontCollection(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(directory)
                         .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".ttf" or ".otf"))
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    if (TryAddGlyphTypeface(stream, out var glyphTypeface))
                    {
                        FamilyNames.Add(glyphTypeface.FamilyName);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                      or InvalidOperationException or ArgumentException)
                {
                    // a font that can't be read is left out
                }
            }
        }

        public override Uri Key => CollectionKey;

        public HashSet<string> FamilyNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
