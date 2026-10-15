using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Hst.Commander.Core;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Gui.Icons;

/// <summary>
///     Loads the icons shown left of the entry names: the icon contained in the file (e.g. a program or an .ico file,
///     also inside images), otherwise the icon the host OS has registered for the file type, folders or drives, and
///     a built-in icon when the OS has none. Used on the UI thread; icons are cached.
/// </summary>
internal static class FileIcons
{
    private const int MaxFileIcons = 1000;

    private static readonly IFileIconProvider? Provider = CreateProvider();
    private static readonly Dictionary<string, IImage> TypeIcons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, IImage?> FileIconCache = new(StringComparer.Ordinal);

    /// <summary>Built-in folder icon, used until the OS icon is loaded or when the OS has none.</summary>
    public static IImage DefaultFolderIcon { get; } = CreateDrawing(
        "M1,3.5 L1,13 L15,13 L15,5 L7.5,5 L6,3 L1.5,3 Z", "#E8B84A", "#A07818");

    /// <summary>Built-in file icon, used until the OS icon is loaded or when the OS has none.</summary>
    public static IImage DefaultFileIcon { get; } = CreateDrawing(
        "M3,1 L10,1 L13,4 L13,15 L3,15 Z", "#F4F4F4", "#808080");

    /// <summary>Icon shown right away for an entry, before <see cref="LoadAsync" /> has loaded the actual icon.</summary>
    public static IImage GetInitialIcon(IEntry entry) => entry.Type == EntryType.Container
        ? TypeIcons.GetValueOrDefault("folder") ?? DefaultFolderIcon
        : TypeIcons.GetValueOrDefault($"file{Path.GetExtension(entry.Name)}") ?? DefaultFileIcon;

    /// <summary>
    ///     Loads the icons of the listed entries, first the type icons and then the icons contained in files, which
    ///     are read from the pane's volume. Stops when cancelled, e.g. when the pane lists other entries.
    /// </summary>
    public static async Task LoadAsync(IReadOnlyList<EntryItem> items, NavigationContext navigationContext, int size,
        CancellationToken cancellationToken)
    {
        var isDriveList = navigationContext.IsDriveList;
        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var localPath = navigationContext.GetLocalPath(item.Entry);
            item.Icon = item.IsContainer
                ? isDriveList && localPath != null
                    ? await GetTypeIconAsync($"drive:{localPath}", provider => provider.GetDriveIcon(localPath, size),
                        DefaultFolderIcon)
                    : await GetTypeIconAsync("folder", provider => provider.GetFolderIcon(size), DefaultFolderIcon)
                : await GetTypeIconAsync($"file{Path.GetExtension(item.Name)}",
                    provider => provider.GetFileTypeIcon(Path.GetExtension(item.Name), size), DefaultFileIcon);
        }

        foreach (var item in items.Where(item => !item.IsContainer))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (await GetFileIconAsync(item.Entry, navigationContext, size, cancellationToken) is { } fileIcon &&
                !cancellationToken.IsCancellationRequested)
            {
                item.Icon = fileIcon;
            }
        }
    }

    private static async Task<IImage> GetTypeIconAsync(string key, Func<IFileIconProvider, IconImage?> getIcon,
        IImage defaultIcon)
    {
        if (TypeIcons.TryGetValue(key, out var icon))
        {
            return icon;
        }

        icon = CreateBitmap(await RunProviderAsync(getIcon)) ?? defaultIcon;
        TypeIcons[key] = icon;
        return icon;
    }

    /// <summary>Icon of the file itself, null when it has none and its type icon is shown.</summary>
    private static async Task<IImage?> GetFileIconAsync(IEntry entry, NavigationContext navigationContext, int size,
        CancellationToken cancellationToken)
    {
        var localPath = navigationContext.GetLocalPath(entry);
        var hasEmbeddedIcon = EmbeddedIconReader.HasEmbeddedIcon(entry.Name);
        var hasOsFileIcon = localPath != null && Provider?.HasFileIcon(entry.Name) == true;
        if (!hasEmbeddedIcon && !hasOsFileIcon)
        {
            return null;
        }

        var key = $"{localPath ?? navigationContext.CurrentPath + "|" + entry.Name}|{entry.Size}|{entry.ModifiedDate:O}|{size}";
        if (FileIconCache.TryGetValue(key, out var cachedIcon))
        {
            return cachedIcon;
        }

        IconImage? iconImage = null;
        try
        {
            if (hasEmbeddedIcon && localPath != null)
            {
                iconImage = await Task.Run(() =>
                {
                    using var stream = File.OpenRead(localPath);
                    return EmbeddedIconReader.ReadIcon(entry.Name, stream, size);
                }, cancellationToken);
            }
            else if (hasEmbeddedIcon)
            {
                // files inside images are read on the UI thread, as the pane's volume is not thread safe
                await using var stream = await navigationContext.OpenReadAsync(entry, cancellationToken);
                iconImage = EmbeddedIconReader.ReadIcon(entry.Name, stream, size);
            }

            if (iconImage == null && hasOsFileIcon)
            {
                iconImage = await RunProviderAsync(provider => provider.GetFileIcon(localPath!, size));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException or ObjectDisposedException
                                              or OperationCanceledException)
        {
            return null;
        }

        if (FileIconCache.Count >= MaxFileIcons)
        {
            FileIconCache.Clear();
        }

        var icon = CreateBitmap(iconImage);
        FileIconCache[key] = icon;
        return icon;
    }

    private static async Task<IconImage?> RunProviderAsync(Func<IFileIconProvider, IconImage?> getIcon)
    {
        if (Provider == null)
        {
            return null;
        }

        try
        {
            return Provider.RequiresUiThread ? getIcon(Provider) : await Task.Run(() => getIcon(Provider));
        }
        catch (Exception)
        {
            // a missing icon is not an error worth showing
            return null;
        }
    }

    private static IFileIconProvider? CreateProvider()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsFileIconProvider();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacFileIconProvider();
        }

        return OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD() ? new FreedesktopFileIconProvider() : null;
    }

    private static Bitmap? CreateBitmap(IconImage? iconImage)
    {
        try
        {
            if (iconImage?.PngData is { } pngData)
            {
                return new Bitmap(new MemoryStream(pngData));
            }

            if (iconImage?.BgraPixels is not { } pixels)
            {
                return null;
            }

            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, handle.AddrOfPinnedObject(),
                    new PixelSize(iconImage.Width, iconImage.Height), new Vector(96, 96), iconImage.Width * 4);
            }
            finally
            {
                handle.Free();
            }
        }
        catch (Exception)
        {
            // e.g. invalid PNG data
            return null;
        }
    }

    private static DrawingImage CreateDrawing(string path, string fill, string stroke) => new(new GeometryDrawing
    {
        Geometry = Geometry.Parse(path),
        Brush = new SolidColorBrush(Color.Parse(fill)),
        Pen = new Pen(new SolidColorBrush(Color.Parse(stroke)))
    });
}
