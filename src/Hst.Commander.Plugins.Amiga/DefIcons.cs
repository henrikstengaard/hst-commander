using Hst.Amiga.DataTypes.DiskObjects;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>Kinds of entries getting a def icon when they have no icon of their own.</summary>
public enum DefIconType
{
    /// <summary>Directories, <c>def_drawer</c>.</summary>
    Drawer,

    /// <summary>Amiga executables (hunk header), <c>def_tool</c>.</summary>
    Tool,

    /// <summary>Other files, <c>def_project</c>.</summary>
    Project,

    /// <summary>Volume entries, e.g. partitions, without a Disk.info, <c>def_disk</c>.</summary>
    Disk
}

/// <summary>Images of a def icon, with the bytes of the .info file when it is one.</summary>
/// <param name="Image">Image of the icon.</param>
/// <param name="SelectedImage">Image while selected, null to draw the image highlighted.</param>
/// <param name="InfoData">Bytes of a .info def icon, used for the .info files created when positions are saved.</param>
internal sealed record DefIcon(IconImage Image, IconImage? SelectedImage, byte[]? InfoData);

/// <summary>
/// Def icons, the default icons the Workbench shows for entries without a .info file (like ENVARC:Sys/def_*.info on
/// the Amiga): files named <c>def_drawer</c>, <c>def_tool</c>, <c>def_project</c> and <c>def_disk</c> in the directory of
/// the plugin (e.g. "~/.hst-commander/plugins/amiga"), either Amiga icons (<c>.info</c>) or images of any format an
/// image provider reads (<see cref="IPluginHost.ReadImages" />), e.g. <c>.png</c>, <c>.iff</c>, <c>.gif</c> or
/// <c>.bmp</c>. Names are case-insensitive and a .info file is used before other files. Icons are read again when their
/// file changes, so def icons can be added or changed while Hst Commander runs.
/// </summary>
public sealed class DefIcons
{
    /// <summary>Most bytes of a def icon file.</summary>
    public const long MaxFileLength = 4 * 1024 * 1024;

    private readonly Dictionary<string, (DateTime Modified, long Length, DefIcon? Icon)> _cache =
        new(StringComparer.Ordinal);

    private readonly Lock _lock = new();

    /// <summary>Directory holding the def icons, null for none.</summary>
    public string? Directory { get; set; }

    /// <summary>Host reading images of other formats than Amiga icons, only .info files are read when null.</summary>
    public IPluginHost? Host { get; set; }

    /// <summary>File name of a kind of def icon without extension, e.g. "def_drawer".</summary>
    public static string GetName(DefIconType type) => type switch
    {
        DefIconType.Drawer => "def_drawer",
        DefIconType.Tool => "def_tool",
        DefIconType.Project => "def_project",
        _ => "def_disk"
    };

    /// <summary>
    /// The def icon of a kind of entry: the first def icon file of it that can be read, null when there is none.
    /// </summary>
    internal async Task<DefIcon?> GetAsync(DefIconType type, CancellationToken cancellationToken = default)
    {
        foreach (var path in FindFiles(type))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await ReadCachedAsync(path, cancellationToken) is { } defIcon)
            {
                return defIcon;
            }
        }

        return null;
    }

    /// <summary>Files of a def icon in the directory, .info files first.</summary>
    private IReadOnlyList<string> FindFiles(DefIconType type)
    {
        if (Directory is not { } directory || !System.IO.Directory.Exists(directory))
        {
            return [];
        }

        var name = GetName(type);
        try
        {
            // matched here, as file name patterns are case-sensitive on some systems
            return System.IO.Directory.EnumerateFiles(directory)
                .Where(path => Path.GetFileNameWithoutExtension(path).Equals(name, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => Path.GetExtension(path).Equals(AmigaIconProvider.InfoExtension,
                    StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(path => path, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async Task<DefIcon?> ReadCachedAsync(string path, CancellationToken cancellationToken)
    {
        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(path);
            if (!fileInfo.Exists || fileInfo.Length == 0 || fileInfo.Length > MaxFileLength)
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        lock (_lock)
        {
            if (_cache.TryGetValue(path, out var cached) && cached.Modified == fileInfo.LastWriteTimeUtc &&
                cached.Length == fileInfo.Length)
            {
                return cached.Icon;
            }
        }

        var defIcon = await ReadAsync(path, cancellationToken);
        lock (_lock)
        {
            _cache[path] = (fileInfo.LastWriteTimeUtc, fileInfo.Length, defIcon);
        }

        return defIcon;
    }

    /// <summary>Reads a def icon file, null when it isn't an icon or image that can be read.</summary>
    private async Task<DefIcon?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        byte[] data;
        try
        {
            data = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var name = Path.GetFileName(path);
        if (Path.GetExtension(path).Equals(AmigaIconProvider.InfoExtension, StringComparison.OrdinalIgnoreCase))
        {
            AmigaIcon? amigaIcon;
            try
            {
                using var stream = new MemoryStream(data, false);
                amigaIcon = await AmigaIconHelper.ReadAmigaIcon(stream);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Hst.Amiga throws various exceptions for invalid icon files
                return null;
            }

            if (amigaIcon?.DiskObject == null)
            {
                return null;
            }

            var (image, selectedImage) = WorkbenchNavigationMode.GetWorkbenchImages(amigaIcon);
            return image != null ? new DefIcon(image, selectedImage, data) : null;
        }

        if (Host == null)
        {
            return null;
        }

        using var imageStream = new MemoryStream(data, false);
        var images = await Host.ReadImages(name, imageStream, cancellationToken);
        return images.Count > 0 ? new DefIcon(images[0].Image, null, null) : null;
    }
}
