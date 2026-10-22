using System.Diagnostics;
using System.Text.RegularExpressions;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Gui.Icons;

/// <summary>
///     Icons registered on Linux and other freedesktop.org systems: the MIME type of a file extension from the
///     shared MIME database and its icon from the current icon theme (and the themes it inherits). Only PNG icons are
///     supported.
/// </summary>
internal sealed partial class FreedesktopFileIconProvider : IFileIconProvider
{
    private readonly Lazy<IReadOnlyList<string>> _dataDirectories = new(GetDataDirectories);
    private readonly Lazy<MimeDatabase> _mimeDatabase;
    private readonly Lazy<IReadOnlyList<IconTheme>> _themes;

    public FreedesktopFileIconProvider()
    {
        _mimeDatabase = new Lazy<MimeDatabase>(() => MimeDatabase.Load(_dataDirectories.Value));
        _themes = new Lazy<IReadOnlyList<IconTheme>>(LoadThemes);
    }

    public bool RequiresUiThread => false;

    public IconImage? GetFileTypeIcon(string extension, int size)
    {
        var mimeType = extension.Length > 1 ? _mimeDatabase.Value.GetMimeType(extension[1..]) : null;
        return FindIcon(_mimeDatabase.Value.GetIconNames(mimeType).Append("text-x-generic"), size);
    }

    public IconImage? GetFolderIcon(int size) => FindIcon(["folder", "inode-directory"], size);

    public IconImage? GetDriveIcon(string rootPath, int size) => FindIcon(["drive-harddisk", "folder"], size);

    public bool HasFileIcon(string name) => false;

    public IconImage? GetFileIcon(string path, int size) => null;

    private IconImage? FindIcon(IEnumerable<string> names, int size)
    {
        foreach (var name in names)
        {
            foreach (var theme in _themes.Value)
            {
                if (theme.FindIcon(name, size) is { } path)
                {
                    try
                    {
                        var data = File.ReadAllBytes(path);
                        return new IconImage(size, size, data, null);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<string> GetDataDirectories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdgDataHome
            ? xdgDataHome
            : Path.Combine(home, ".local", "share");
        var dataDirectories = Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } xdgDataDirs
            ? xdgDataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries)
            : ["/usr/local/share", "/usr/share"];
        return [dataHome, ..dataDirectories];
    }

    private IReadOnlyList<IconTheme> LoadThemes()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var iconDirectories = new[] { Path.Combine(home, ".icons") }
            .Concat(_dataDirectories.Value.Select(directory => Path.Combine(directory, "icons")))
            .Where(Directory.Exists)
            .ToList();

        // the current theme and the themes it inherits, then common themes and the hicolor fallback theme
        var themeNames = new List<string>();
        var pending = new Queue<string>(new[] { GetCurrentThemeName() }.OfType<string>()
            .Concat(["Adwaita", "Yaru", "breeze", "gnome", "elementary", "Papirus", "oxygen", "hicolor"]));
        while (pending.TryDequeue(out var themeName))
        {
            if (themeNames.Contains(themeName))
            {
                continue;
            }

            themeNames.Add(themeName);
            foreach (var inherited in ReadInherits(iconDirectories, themeName))
            {
                pending.Enqueue(inherited);
            }
        }

        return themeNames
            .Select(themeName => new IconTheme(iconDirectories
                .Select(directory => Path.Combine(directory, themeName))
                .Where(Directory.Exists)
                .ToList()))
            .Append(new IconTheme(_dataDirectories.Value
                .Select(directory => Path.Combine(directory, "pixmaps"))
                .Where(Directory.Exists)
                .ToList()))
            .ToList();
    }

    private static IEnumerable<string> ReadInherits(IEnumerable<string> iconDirectories, string themeName)
    {
        foreach (var directory in iconDirectories)
        {
            var indexPath = Path.Combine(directory, themeName, "index.theme");
            if (!File.Exists(indexPath))
            {
                continue;
            }

            var inherits = File.ReadLines(indexPath)
                .FirstOrDefault(line => line.StartsWith("Inherits=", StringComparison.Ordinal));
            return inherits?["Inherits=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        }

        return [];
    }

    /// <summary>Icon theme of GNOME-based desktops (gsettings) or KDE (kdeglobals), null when unknown.</summary>
    private static string? GetCurrentThemeName()
    {
        try
        {
            var kdeGlobals = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "kdeglobals");
            var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty;
            if (desktop.Contains("KDE", StringComparison.OrdinalIgnoreCase) && File.Exists(kdeGlobals))
            {
                var inIcons = false;
                foreach (var line in File.ReadLines(kdeGlobals))
                {
                    if (line.StartsWith('['))
                    {
                        inIcons = line == "[Icons]";
                    }
                    else if (inIcons && line.StartsWith("Theme=", StringComparison.Ordinal))
                    {
                        return line["Theme=".Length..].Trim();
                    }
                }

                return "breeze";
            }

            using var process = Process.Start(new ProcessStartInfo("gsettings",
                ["get", "org.gnome.desktop.interface", "icon-theme"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });
            if (process == null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim().Trim('\'');
            process.WaitForExit(2000);
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    ///     PNG icons of a theme by name with their sizes, from the size in the directory names, e.g. "16x16/mimetypes"
    ///     or "mimetypes/16". Directories are scanned on first use.
    /// </summary>
    private sealed partial class IconTheme(IReadOnlyList<string> directories)
    {
        private Dictionary<string, List<(int Size, string Path)>>? _icons;

        public string? FindIcon(string name, int size)
        {
            _icons ??= Scan();
            if (!_icons.TryGetValue(name, out var icons))
            {
                return null;
            }

            // the smallest icon at least the wanted size, otherwise the largest
            return (icons.Where(icon => icon.Size >= size).MinBy(icon => icon.Size) is { Path: not null } larger
                ? larger
                : icons.MaxBy(icon => icon.Size)).Path;
        }

        private Dictionary<string, List<(int Size, string Path)>> Scan()
        {
            var icons = new Dictionary<string, List<(int Size, string Path)>>(StringComparer.Ordinal);
            foreach (var directory in directories)
            {
                try
                {
                    foreach (var path in Directory.EnumerateFiles(directory, "*.png", SearchOption.AllDirectories))
                    {
                        var relativePath = Path.GetRelativePath(directory, path);
                        if (relativePath.Contains("symbolic", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var match = SizeRegex().Match(relativePath);
                        var size = match.Success ? int.Parse(match.Groups[1].Value) : 48;
                        if (match.Success && match.Groups[2].Success)
                        {
                            size *= int.Parse(match.Groups[2].Value);
                        }

                        var name = Path.GetFileNameWithoutExtension(path);
                        if (!icons.TryGetValue(name, out var list))
                        {
                            icons[name] = list = [];
                        }

                        list.Add((size, path));
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }

            return icons;
        }

        /// <summary>Size directory like "16x16", "16x16@2" or "16", optionally scaled.</summary>
        [GeneratedRegex(@"(?:^|[\\/])(\d+)(?:x\d+)?(?:@(\d+)x?)?(?:[\\/]|$)")]
        private static partial Regex SizeRegex();
    }

    /// <summary>Extension to MIME type mapping and icon names from the shared MIME database.</summary>
    private sealed class MimeDatabase
    {
        private readonly Dictionary<string, (int Weight, string MimeType)> _extensions =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _icons = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _genericIcons = new(StringComparer.Ordinal);

        public static MimeDatabase Load(IEnumerable<string> dataDirectories)
        {
            var database = new MimeDatabase();
            // the first data directory has the highest priority, so load it last
            foreach (var mimeDirectory in dataDirectories.Reverse().Select(directory => Path.Combine(directory, "mime")))
            {
                database.LoadGlobs(Path.Combine(mimeDirectory, "globs2"));
                LoadPairs(Path.Combine(mimeDirectory, "icons"), database._icons);
                LoadPairs(Path.Combine(mimeDirectory, "generic-icons"), database._genericIcons);
            }

            return database;
        }

        public string? GetMimeType(string extension) =>
            _extensions.TryGetValue(extension, out var mimeType) ? mimeType.MimeType : null;

        public IEnumerable<string> GetIconNames(string? mimeType)
        {
            if (mimeType == null)
            {
                yield break;
            }

            if (_icons.TryGetValue(mimeType, out var icon))
            {
                yield return icon;
            }

            yield return mimeType.Replace('/', '-');
            if (_genericIcons.TryGetValue(mimeType, out var genericIcon))
            {
                yield return genericIcon;
            }

            yield return $"{mimeType.Split('/')[0]}-x-generic";
        }

        /// <summary>Reads "weight:mime/type:*.ext" lines, keeping the highest weight for an extension.</summary>
        private void LoadGlobs(string path)
        {
            foreach (var line in ReadLines(path))
            {
                var parts = line.Split(':');
                if (parts.Length < 3 || !int.TryParse(parts[0], out var weight) || !parts[2].StartsWith("*.") ||
                    parts[2].IndexOfAny(['*', '?', '['], 2) >= 0)
                {
                    continue;
                }

                var extension = parts[2][2..];
                if (!_extensions.TryGetValue(extension, out var existing) || weight > existing.Weight)
                {
                    _extensions[extension] = (weight, parts[1]);
                }
            }
        }

        private static void LoadPairs(string path, Dictionary<string, string> pairs)
        {
            foreach (var line in ReadLines(path))
            {
                var separator = line.IndexOf(':');
                if (separator > 0)
                {
                    pairs[line[..separator]] = line[(separator + 1)..];
                }
            }
        }

        private static IEnumerable<string> ReadLines(string path)
        {
            try
            {
                return File.Exists(path)
                    ? File.ReadAllLines(path).Where(line => line.Length > 0 && !line.StartsWith('#'))
                    : [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }
}
