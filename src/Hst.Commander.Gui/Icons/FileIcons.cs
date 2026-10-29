using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Hst.Commander.Core;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Gui.Icons;

/// <summary>
///     Loads the icons shown left of the entry names: the icon of a plugin's icon file next to the entry (e.g. an Amiga
///     .info file), the icon contained in the file (e.g. a program or an .ico file, also inside images), otherwise the icon the host OS has registered for the file type, folders or drives, and
///     a built-in icon when the OS has none. Used on the UI thread; icons are cached.
/// </summary>
internal static class FileIcons
{
    private const int MaxFileIcons = 1000;

    private static readonly IFileIconProvider? Provider = CreateProvider();
    private static readonly Dictionary<string, IImage> TypeIcons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, IImage?> FileIconCache = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim TypeIconLock = new(1, 1);

    /// <summary>Built-in folder icon, used until the OS icon is loaded or when the OS has none.</summary>
    public static IImage DefaultFolderIcon { get; } = CreateDrawing(
        "M1,3.5 L1,13 L15,13 L15,5 L7.5,5 L6,3 L1.5,3 Z", "#E8B84A", "#A07818");

    /// <summary>Built-in file icon, used until the OS icon is loaded or when the OS has none.</summary>
    public static IImage DefaultFileIcon { get; } = CreateDrawing(
        "M3,1 L10,1 L13,4 L13,15 L3,15 Z", "#F4F4F4", "#808080");

    /// <summary>Folder icons with an arrow up for the parent entry (".."), by the folder icon they are made from.</summary>
    private static readonly Dictionary<IImage, IImage> ParentIcons = new(ReferenceEqualityComparer.Instance);

    /// <summary>Icon shown right away for an entry, before <see cref="LoadAsync" /> has loaded the actual icon.</summary>
    public static IImage GetInitialIcon(IEntry entry) => IsParent(entry)
        ? GetParentIcon(TypeIcons.GetValueOrDefault("folder") ?? DefaultFolderIcon)
        : entry.Type == EntryType.Container
            ? TypeIcons.GetValueOrDefault("folder") ?? DefaultFolderIcon
            : TypeIcons.GetValueOrDefault($"file{Path.GetExtension(entry.Name)}") ?? DefaultFileIcon;

    private static bool IsParent(IEntry entry) => entry.Name == Paths.Parent;

    /// <summary>
    ///     Folder icon with a green arrow up drawn over its lower right part, showing the parent entry ("..") navigates
    ///     a directory up. Cached per folder icon.
    /// </summary>
    private static IImage GetParentIcon(IImage folderIcon)
    {
        if (ParentIcons.TryGetValue(folderIcon, out var parentIcon))
        {
            return parentIcon;
        }

        // the arrow is drawn in a 16x16 box scaled to the folder icon's size
        var size = folderIcon.Size;
        var scale = Math.Min(size.Width, size.Height) / 16;
        var arrow = Geometry.Parse("M11,5 L15,9.5 L12.75,9.5 L12.75,15 L9.25,15 L9.25,9.5 L7,9.5 Z");
        arrow.Transform = new ScaleTransform(scale, scale);
        parentIcon = new DrawingImage(new DrawingGroup
        {
            Children =
            {
                new ImageDrawing { ImageSource = folderIcon, Rect = new Rect(size) },
                new GeometryDrawing
                {
                    Geometry = arrow,
                    Brush = new SolidColorBrush(Color.Parse("#3CB043")),
                    Pen = new Pen(new SolidColorBrush(Color.Parse("#1E6B23")), Math.Max(1, scale * 0.75))
                }
            }
        });
        ParentIcons[folderIcon] = parentIcon;
        return parentIcon;
    }

    /// <summary>
    ///     Loads the icons of the listed entries, first the type icons and then the icons contained in files, which
    ///     are read from the pane's volume. Stops when cancelled, e.g. when the pane lists other entries. Type icons are
    ///     cached without their size, unless <paramref name="isLarge" /> (e.g. thumbnails), which are cached by size.
    /// </summary>
    public static async Task LoadAsync(IReadOnlyList<EntryItem> items, NavigationContext navigationContext, int size,
        CancellationToken cancellationToken, bool isLarge = false)
    {
        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            item.Icon = await GetEntryTypeIconAsync(item.Entry, navigationContext, size,
                isLarge ? $"@{size}" : string.Empty);
        }

        foreach (var item in items.Where(item =>
                     !IsParent(item.Entry) && (!item.IsContainer || navigationContext.HasIcon(item.Entry))))
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

    /// <summary>
    ///     Loads the icon of a single entry in another size than the pane icons, e.g. for the properties dialog: the
    ///     icon contained in the file, otherwise its type icon. Only the type icon when <paramref name="typeOnly" />,
    ///     e.g. for several entries of the same type.
    /// </summary>
    public static async Task<IImage> LoadIconAsync(IEntry entry, NavigationContext navigationContext, int size,
        bool typeOnly, CancellationToken cancellationToken)
    {
        // type icons of the panes are cached without their size, so cache other sizes separately
        var typeIcon = await GetEntryTypeIconAsync(entry, navigationContext, size, $"@{size}");
        return !typeOnly && await GetFileIconAsync(entry, navigationContext, size, cancellationToken) is { } fileIcon
            ? fileIcon
            : typeIcon;
    }

    /// <summary>Sizes the OS icons are asked for when viewing all icons, the OS has them in some of these sizes.</summary>
    private static readonly int[] ViewerIconSizes = [16, 24, 32, 48, 64, 96, 128, 256];

    /// <summary>
    ///     Loads all icons of a single entry for the icon viewer, each with its images: the icons of its plugin icon
    ///     file (e.g. the normal and selected image of an Amiga icon), the icons contained in the file (e.g. all icons
    ///     of a Windows program or all images of an .ico file), otherwise the OS icon of the file or its type in the
    ///     sizes the OS has. Only the type icon when <paramref name="typeOnly" />. Falls back to the built-in icon.
    /// </summary>
    public static async Task<IReadOnlyList<ViewerIcon>> LoadIconsAsync(IEntry entry,
        NavigationContext navigationContext, bool typeOnly, CancellationToken cancellationToken)
    {
        var localPath = navigationContext.GetLocalPath(entry);
        if (!typeOnly)
        {
            if (navigationContext.HasIcon(entry) &&
                await navigationContext.ReadIconsAsync(entry, cancellationToken) is { Count: > 0 } iconFileIcons)
            {
                return ToViewerIcons(iconFileIcons);
            }

            if (entry.Type != EntryType.Container && EmbeddedIconReader.HasEmbeddedIcon(entry.Name) &&
                await ReadEmbeddedIconsAsync(entry, navigationContext, localPath, cancellationToken) is
                    { Count: > 0 } embeddedIcons)
            {
                return ToViewerIcons(embeddedIcons);
            }

            if (entry.Type != EntryType.Container && localPath != null && Provider?.HasFileIcon(entry.Name) == true &&
                await LoadOsIconAsync(size => provider => provider.GetFileIcon(localPath, size)) is { } fileIcon)
            {
                return [fileIcon];
            }
        }

        var extension = Path.GetExtension(entry.Name);
        Func<int, Func<IFileIconProvider, IconImage?>> getTypeIcon = entry.Type != EntryType.Container
            ? size => provider => provider.GetFileTypeIcon(extension, size)
            : navigationContext.IsDriveList && localPath != null
                ? size => provider => provider.GetDriveIcon(localPath, size)
                : size => provider => provider.GetFolderIcon(size);
        if (await LoadOsIconAsync(getTypeIcon) is { } typeIcon)
        {
            return [typeIcon];
        }

        var defaultIcon = entry.Type == EntryType.Container ? DefaultFolderIcon : DefaultFileIcon;
        return
        [
            new ViewerIcon("Icon", [new ViewerIconImage((int)defaultIcon.Size.Width, (int)defaultIcon.Size.Height,
                null, () => defaultIcon)])
        ];
    }

    private static async Task<IReadOnlyList<EntryIcon>> ReadEmbeddedIconsAsync(IEntry entry,
        NavigationContext navigationContext, string? localPath, CancellationToken cancellationToken)
    {
        try
        {
            if (localPath != null)
            {
                return await Task.Run(() =>
                {
                    using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    return EmbeddedIconReader.ReadIcons(entry.Name, stream);
                }, cancellationToken);
            }

            // files inside images are read on the UI thread, as the pane's volume is not thread safe
            await using var imageStream = await navigationContext.OpenReadAsync(entry, cancellationToken);
            var data = new MemoryStream();
            await imageStream.CopyToAsync(data, cancellationToken);
            data.Position = 0;
            return await Task.Run(() => EmbeddedIconReader.ReadIcons(entry.Name, data), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException or ObjectDisposedException)
        {
            return [];
        }
    }

    /// <summary>OS icon in the sizes the OS has (distinct pixel sizes), null when the OS has none.</summary>
    private static async Task<ViewerIcon?> LoadOsIconAsync(Func<int, Func<IFileIconProvider, IconImage?>> getIcon)
    {
        var images = new List<IconImage>();
        foreach (var size in ViewerIconSizes)
        {
            if (await RunProviderAsync(getIcon(size)) is { } image &&
                !images.Any(other => other.Width == image.Width && other.Height == image.Height))
            {
                images.Add(image);
            }
        }

        return images.Count == 0 ? null : ToViewerIcons([new EntryIcon("Icon", images)])[0];
    }

    private static IReadOnlyList<ViewerIcon> ToViewerIcons(IEnumerable<EntryIcon> icons) => icons
        .Select(icon => new ViewerIcon(icon.Name, icon.Images
            .OrderByDescending(image => image.Width * image.Height)
            .ThenByDescending(image => image.BitsPerPixel ?? 0)
            .Select(image => new ViewerIconImage(image.Width, image.Height, image.BitsPerPixel,
                () => CreateBitmap(image)))
            .ToList()))
        .ToList();

    /// <summary>
    ///     Type icon of an entry: the folder icon with an arrow up for the parent entry (".."), the drive icon in the
    ///     drive list, the folder icon or the file type icon.
    /// </summary>
    private static async Task<IImage> GetEntryTypeIconAsync(IEntry entry, NavigationContext navigationContext,
        int size, string keySuffix)
    {
        if (IsParent(entry))
        {
            return GetParentIcon(await GetTypeIconAsync($"folder{keySuffix}", provider => provider.GetFolderIcon(size),
                DefaultFolderIcon));
        }

        var localPath = navigationContext.GetLocalPath(entry);
        if (entry.Type != EntryType.Container)
        {
            var extension = Path.GetExtension(entry.Name);
            return await GetTypeIconAsync($"file{extension}{keySuffix}",
                provider => provider.GetFileTypeIcon(extension, size), DefaultFileIcon);
        }

        return navigationContext.IsDriveList && localPath != null
            ? await GetTypeIconAsync($"drive:{localPath}{keySuffix}", provider => provider.GetDriveIcon(localPath, size),
                DefaultFolderIcon)
            : await GetTypeIconAsync($"folder{keySuffix}", provider => provider.GetFolderIcon(size), DefaultFolderIcon);
    }

    private static async Task<IImage> GetTypeIconAsync(string key, Func<IFileIconProvider, IconImage?> getIcon,
        IImage defaultIcon)
    {
        if (TypeIcons.TryGetValue(key, out var icon))
        {
            return icon;
        }

        // both panes load their icons at the same time (e.g. at startup), so type icons are loaded one at a time and
        // a pane waiting for another pane's load gets the same icon instead of loading it again
        await TypeIconLock.WaitAsync();
        try
        {
            if (TypeIcons.TryGetValue(key, out icon))
            {
                return icon;
            }

            icon = CreateBitmap(await RunProviderAsync(getIcon)) ?? defaultIcon;
            TypeIcons[key] = icon;
            return icon;
        }
        finally
        {
            TypeIconLock.Release();
        }
    }

    /// <summary>
    ///     Icon of the entry itself, null when it has none and its type icon is shown: the icon of its icon file (also
    ///     for directories), otherwise for files the icon contained in the file or the OS icon of the file.
    /// </summary>
    private static async Task<IImage?> GetFileIconAsync(IEntry entry, NavigationContext navigationContext, int size,
        CancellationToken cancellationToken)
    {
        if (navigationContext.HasIcon(entry) &&
            await GetIconFileIconAsync(entry, navigationContext, size, cancellationToken) is { } iconFileIcon)
        {
            return iconFileIcon;
        }

        if (entry.Type == EntryType.Container)
        {
            return null;
        }

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

        var icon = CreateBitmap(iconImage);
        AddFileIcon(key, icon);
        return icon;
    }

    /// <summary>
    ///     Icon read from the icon file of an entry by the icon provider of a plugin, cached by the icon file, so an
    ///     entry and its icon file (e.g. "file" and "file.info") share it. The icon of a volume entry (e.g. the
    ///     Disk.info of a partition) is read each time, as its icon file isn't listed.
    /// </summary>
    private static async Task<IImage?> GetIconFileIconAsync(IEntry entry, NavigationContext navigationContext,
        int size, CancellationToken cancellationToken)
    {
        if (navigationContext.GetIconFile(entry) is not { } iconFile)
        {
            var volumeIcon = CreateBitmap(await navigationContext.ReadIconAsync(entry, size, cancellationToken));
            return cancellationToken.IsCancellationRequested ? null : volumeIcon;
        }

        var location = navigationContext.GetLocalPath(iconFile) ?? navigationContext.CurrentPath + "|" + iconFile.Name;
        var key = $"icon:{location}|{iconFile.Size}|{iconFile.ModifiedDate:O}|{size}";
        if (FileIconCache.TryGetValue(key, out var cachedIcon))
        {
            return cachedIcon;
        }

        var icon = CreateBitmap(await navigationContext.ReadIconAsync(entry, size, cancellationToken));
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        AddFileIcon(key, icon);
        return icon;
    }

    private static void AddFileIcon(string key, IImage? icon)
    {
        if (FileIconCache.Count >= MaxFileIcons)
        {
            FileIconCache.Clear();
        }

        FileIconCache[key] = icon;
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

    /// <summary>Bitmap of an icon image (PNG data or BGRA pixels), null when there is none or it's invalid.</summary>
    public static Bitmap? CreateBitmap(IconImage? iconImage)
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
