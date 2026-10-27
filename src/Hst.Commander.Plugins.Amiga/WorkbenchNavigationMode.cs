using Hst.Amiga.DataTypes.DiskObjects;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// Navigation mode showing a directory like a drawer window of the Amiga Workbench 3.1: the icons of the .info files
/// (disk objects) on the Workbench gray background with black names, at the positions stored in the disk objects.
/// Entries without a .info file get the def icon of a drawer, tool (Amiga executables), project or disk (volume
/// entries) without a position (<see cref="DefIcons" />, otherwise the built-in default icons), and the .info files of
/// entries and Disk.info (the icon of the volume) aren't shown themselves. Volume entries, e.g. the partitions of an
/// RDB disk, show the Disk.info in their root like disks on the Workbench screen. Saving positions writes them to the
/// .info files (the Disk.info of volume entries), creating icons for entries without one from the .info def icon, or
/// default icons with a planar and a color (GlowIcon) image. The "Icons only" option shows only entries with a .info
/// file, like "Show Only Icons" of the Workbench (volume entries are always shown, like disks).
/// </summary>
/// <param name="defIcons">Def icons of entries without a .info file, the built-in default icons when null.</param>
public class WorkbenchNavigationMode(DefIcons? defIcons = null) : INavigationMode
{
    /// <summary>First long word of an Amiga executable, the hunk header.</summary>
    private static readonly byte[] HunkHeaderMagic = [0x00, 0x00, 0x03, 0xf3];

    private static readonly Lazy<(IconImage? Normal, IconImage? Selected)> DefaultToolImages =
        new(() => GetDefaultImages(DefaultIcons.CreateTool()));

    private static readonly Lazy<(IconImage? Normal, IconImage? Selected)> DefaultDrawerImages =
        new(() => GetDefaultImages(DefaultIcons.CreateDrawer()));

    private static readonly Lazy<(IconImage? Normal, IconImage? Selected)> DefaultProjectImages =
        new(() => GetDefaultImages(DefaultIcons.CreateProject()));

    private static readonly Lazy<(IconImage? Normal, IconImage? Selected)> DefaultDiskImages =
        new(() => GetDefaultImages(DefaultIcons.CreateDisk()));

    public string Name => "Workbench";

    public string Description => "Amiga Workbench 3.1 icons at their positions";

    /// <summary>The gray background and black text of the Workbench 3.1 default palette.</summary>
    public NavigationModeStyle Style { get; } = new("#AAAAAA", "#000000",
        "Topaz New, Topaz, Courier New, Courier, monospace", 12);

    /// <summary>Shows only entries with a .info file, unchecked by default (all entries are shown).</summary>
    public static readonly NavigationModeOption IconsOnlyOption = new("IconsOnly", "Icons only",
        "Show only entries with a .info icon file, like Show Only Icons of the Workbench");

    public IReadOnlyList<NavigationModeOption> Options { get; } = [IconsOnlyOption];

    /// <summary>Def icons of entries without a .info file, null for the built-in default icons.</summary>
    public DefIcons? DefIcons { get; } = defIcons;

    public async Task<NavigationLayout> Layout(IVolume volume, IReadOnlyList<IEntry> entries,
        IReadOnlyDictionary<string, bool>? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(entries);

        var iconsOnly = IconsOnlyOption.GetValue(options);
        // each kind of def icon is looked up once per layout
        var defaultImages = new Dictionary<DefIconType, (IconImage? Normal, IconImage? Selected)>();
        var icons = new List<NavigationIcon>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsHidden(entry, entries))
            {
                continue;
            }

            var infoEntry = entry.IsVolume ? null : GetInfoEntry(entry, entries);
            if (iconsOnly && !entry.IsVolume && infoEntry == null)
            {
                continue;
            }

            var amigaIcon = entry.IsVolume
                ? await ReadVolumeIconAsync(volume, entry, cancellationToken)
                : infoEntry != null
                    ? await ReadIconAsync(volume, infoEntry.Name, cancellationToken)
                    : null;
            if (amigaIcon?.DiskObject is not { } diskObject)
            {
                var type = await GetDefIconTypeAsync(volume, entry, cancellationToken);
                if (!defaultImages.TryGetValue(type, out var images))
                {
                    images = await GetDefaultImagesAsync(type, cancellationToken);
                    defaultImages[type] = images;
                }

                icons.Add(new NavigationIcon(entry.Name, images.Normal, images.Selected, null, null));
                continue;
            }

            var (image, selectedImage) = GetWorkbenchImages(amigaIcon);
            var hasPosition = diskObject.CurrentX != AmigaIconProvider.NoIconPosition &&
                              diskObject.CurrentY != AmigaIconProvider.NoIconPosition;
            icons.Add(new NavigationIcon(entry.Name, image, selectedImage,
                hasPosition ? diskObject.CurrentX : null, hasPosition ? diskObject.CurrentY : null));
        }

        return new NavigationLayout(icons);
    }

    public async Task SavePositions(IVolume volume, IReadOnlyList<IEntry> entries,
        IReadOnlyList<NavigationIconPosition> positions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(positions);

        foreach (var position in positions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entries.FirstOrDefault(entry => entry.Name.Equals(position.Name, StringComparison.Ordinal)) is not
                    { } entry || entry.Name == Paths.Parent)
            {
                continue;
            }

            if (entry.IsVolume)
            {
                await SaveVolumeIconPositionAsync(volume, entry, position, cancellationToken);
                continue;
            }

            // the icon is written once it's built, so an invalid icon file isn't overwritten
            var infoEntry = GetInfoEntry(entry, entries);
            var bytes = infoEntry != null
                ? await UpdatePositionAsync(volume, infoEntry.Name, position, cancellationToken)
                : await CreateIconAsync(volume, entry, position, cancellationToken);
            await WriteAsync(volume, infoEntry?.Name ?? entry.Name + AmigaIconProvider.InfoExtension, bytes,
                cancellationToken);
        }
    }

    private static async Task WriteAsync(IVolume volume, string name, byte[] bytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await volume.OpenWrite(name, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
    }

    /// <summary>
    /// Reads the Disk.info in the root of a volume entry, e.g. an RDB partition, changing back to the directory
    /// after. Null when it has none or the entry can't be opened, e.g. a partition that can't be mounted.
    /// </summary>
    private static async Task<AmigaIcon?> ReadVolumeIconAsync(IVolume volume, IEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            await volume.ChangeDirectory([entry.Name], cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            return null;
        }

        try
        {
            return await FindDiskIconAsync(volume, cancellationToken) is { } diskIconName
                ? await ReadIconAsync(volume, diskIconName, cancellationToken)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            return null;
        }
        finally
        {
            await volume.ChangeDirectory([Paths.Parent], CancellationToken.None);
        }
    }

    /// <summary>
    /// Saves the position in the Disk.info in the root of a volume entry, creating a disk icon from the def icon (or
    /// a default disk icon) when it has none, changing back to the directory after.
    /// </summary>
    private async Task SaveVolumeIconPositionAsync(IVolume volume, IEntry entry,
        NavigationIconPosition position, CancellationToken cancellationToken)
    {
        await volume.ChangeDirectory([entry.Name], cancellationToken);
        try
        {
            var diskIconName = await FindDiskIconAsync(volume, cancellationToken);
            var bytes = diskIconName != null
                ? await UpdatePositionAsync(volume, diskIconName, position, cancellationToken)
                : await WriteIconAsync(await CreateIconAsync(DefIconType.Disk, cancellationToken), position);
            await WriteAsync(volume, diskIconName ?? AmigaIconProvider.DiskIconFileName, bytes, cancellationToken);
        }
        finally
        {
            await volume.ChangeDirectory([Paths.Parent], CancellationToken.None);
        }
    }

    /// <summary>Name of the Disk.info in the current directory (case-insensitive), null when there is none.</summary>
    private static async Task<string?> FindDiskIconAsync(IVolume volume, CancellationToken cancellationToken)
    {
        await foreach (var entry in volume.ListEntries(cancellationToken))
        {
            if (entry.Type == EntryType.Item &&
                entry.Name.Equals(AmigaIconProvider.DiskIconFileName, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Name;
            }
        }

        return null;
    }

    /// <summary>
    /// True for entries not shown as icons: .info files of listed entries (case-insensitive like Amiga file systems)
    /// and Disk.info, the icon of the volume (e.g. an adf or an Amiga partition) shown where the volume is. Disk.info is
    /// hidden in any directory, as the volume passed is the pane's, which doesn't tell the root of an image or a
    /// partition.
    /// </summary>
    private static bool IsHidden(IEntry entry, IReadOnlyList<IEntry> entries)
    {
        if (entry.Type == EntryType.Container ||
            !entry.Name.EndsWith(AmigaIconProvider.InfoExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (entry.Name.Equals(AmigaIconProvider.DiskIconFileName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = entry.Name[..^AmigaIconProvider.InfoExtension.Length];
        return name.Length > 0 && entries.Any(other => !ReferenceEquals(other, entry) &&
                                                       other.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The .info file holding the icon of an entry: "name.info", or the entry itself for a .info file without an
    /// entry. Null when the entry has no icon.
    /// </summary>
    private static IEntry? GetInfoEntry(IEntry entry, IReadOnlyList<IEntry> entries)
    {
        if (entry.Name == Paths.Parent)
        {
            return null;
        }

        var infoName = entry.Name + AmigaIconProvider.InfoExtension;
        return entries.FirstOrDefault(other => other.Type == EntryType.Item &&
                                               other.Name.Equals(infoName, StringComparison.Ordinal)) ??
               entries.FirstOrDefault(other => other.Type == EntryType.Item &&
                                               other.Name.Equals(infoName, StringComparison.OrdinalIgnoreCase)) ??
               (entry.Type == EntryType.Item &&
                entry.Name.EndsWith(AmigaIconProvider.InfoExtension, StringComparison.OrdinalIgnoreCase)
                   ? entry
                   : null);
    }

    /// <summary>Reads the icon of a .info file, null when it can't be read or isn't a valid icon.</summary>
    private static async Task<AmigaIcon?> ReadIconAsync(IVolume volume, string name,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await ReadAllAsync(volume, name, cancellationToken);
            return await AmigaIconHelper.ReadAmigaIcon(stream);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Hst.Amiga throws various exceptions for invalid icon files, which are shown with a default icon
            return null;
        }
    }

    private static async Task<MemoryStream> ReadAllAsync(IVolume volume, string name,
        CancellationToken cancellationToken)
    {
        var memoryStream = new MemoryStream();
        await using (var stream = await volume.OpenRead(name, cancellationToken))
        {
            await stream.CopyToAsync(memoryStream, cancellationToken);
        }

        memoryStream.Position = 0;
        return memoryStream;
    }

    /// <summary>The .info file with the position changed, keeping its images and other data.</summary>
    private static async Task<byte[]> UpdatePositionAsync(IVolume volume, string infoName,
        NavigationIconPosition position, CancellationToken cancellationToken)
    {
        AmigaIcon? amigaIcon;
        using (var stream = await ReadAllAsync(volume, infoName, cancellationToken))
        {
            try
            {
                amigaIcon = await AmigaIconHelper.ReadAmigaIcon(stream);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                amigaIcon = null;
            }
        }

        if (amigaIcon?.DiskObject is not { } diskObject)
        {
            throw new IOException($"'{infoName}' is not a valid icon, its position can't be saved");
        }

        diskObject.CurrentX = position.X;
        diskObject.CurrentY = position.Y;
        if (amigaIcon.Kind == AmigaIcon.IconKind.TrueColor && amigaIcon.TrueColorIcons is { Count: > 0 } trueColorIcons)
        {
            // true color (PNG) icons store the disk object in their chunks
            await DiskObjectHelper.UpdateTrueColorIcons(diskObject, trueColorIcons);
        }

        using var output = new MemoryStream();
        await AmigaIconHelper.WriteAmigaIcon(amigaIcon, output);
        return output.ToArray();
    }

    /// <summary>
    /// A new .info file with the def icon of an entry at the position: a drawer icon for directories, a tool icon for
    /// Amiga executables and a project icon for other files.
    /// </summary>
    private async Task<byte[]> CreateIconAsync(IVolume volume, IEntry entry, NavigationIconPosition position,
        CancellationToken cancellationToken) =>
        await WriteIconAsync(await CreateIconAsync(await GetDefIconTypeAsync(volume, entry, cancellationToken),
            cancellationToken), position);

    /// <summary>
    /// A new icon of a kind of entry: the .info def icon when there is one, otherwise the default icon with a planar
    /// and a color image.
    /// </summary>
    private async Task<AmigaIcon> CreateIconAsync(DefIconType type, CancellationToken cancellationToken)
    {
        if (DefIcons != null && await DefIcons.GetAsync(type, cancellationToken) is { InfoData: { } infoData })
        {
            try
            {
                // read again, so the cached def icon isn't changed
                using var stream = new MemoryStream(infoData, false);
                if (await AmigaIconHelper.ReadAmigaIcon(stream) is { DiskObject: not null } amigaIcon)
                {
                    return amigaIcon;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // the def icon was read before, so this isn't expected; the default icon is used
            }
        }

        return type switch
        {
            DefIconType.Drawer => DefaultIcons.CreateDrawer(),
            DefIconType.Tool => DefaultIcons.CreateTool(),
            DefIconType.Disk => DefaultIcons.CreateDisk(),
            _ => DefaultIcons.CreateProject()
        };
    }

    /// <summary>Kind of def icon of an entry without an icon: disk, drawer, tool (Amiga executables) or project.</summary>
    private static async Task<DefIconType> GetDefIconTypeAsync(IVolume volume, IEntry entry,
        CancellationToken cancellationToken) =>
        entry.IsVolume
            ? DefIconType.Disk
            : entry.Type == EntryType.Container
                ? DefIconType.Drawer
                : await IsExecutableAsync(volume, entry.Name, cancellationToken)
                    ? DefIconType.Tool
                    : DefIconType.Project;

    /// <summary>Images of a kind of entry without an icon: the def icon, otherwise the built-in default icon.</summary>
    private async Task<(IconImage? Normal, IconImage? Selected)> GetDefaultImagesAsync(DefIconType type,
        CancellationToken cancellationToken)
    {
        if (DefIcons != null && await DefIcons.GetAsync(type, cancellationToken) is { } defIcon)
        {
            return (defIcon.Image, defIcon.SelectedImage);
        }

        return type switch
        {
            DefIconType.Drawer => DefaultDrawerImages.Value,
            DefIconType.Tool => DefaultToolImages.Value,
            DefIconType.Disk => DefaultDiskImages.Value,
            _ => DefaultProjectImages.Value
        };
    }

    /// <summary>
    /// Normal and selected image of an icon shown on the Workbench: the best kind of image, planar icons without a
    /// selected image are complemented while selected, like the Workbench draws them.
    /// </summary>
    internal static (IconImage? Normal, IconImage? Selected) GetWorkbenchImages(AmigaIcon amigaIcon)
    {
        var (image, selectedImage) = AmigaIconProvider.GetImages(amigaIcon);
        if (image != null && selectedImage == null && amigaIcon.ColorIcon == null &&
            (amigaIcon.TrueColorIcons?.Count ?? 0) == 0)
        {
            selectedImage = AmigaIconProvider.DecodePlanarImage(Complement(amigaIcon.DiskObject.FirstImageData));
        }

        return (image, selectedImage);
    }

    /// <summary>A new icon with its images at the position.</summary>
    private static async Task<byte[]> WriteIconAsync(AmigaIcon amigaIcon, NavigationIconPosition position)
    {
        amigaIcon.DiskObject.CurrentX = position.X;
        amigaIcon.DiskObject.CurrentY = position.Y;
        if (amigaIcon.Kind == AmigaIcon.IconKind.TrueColor && amigaIcon.TrueColorIcons is { Count: > 0 } trueColorIcons)
        {
            // true color (PNG) icons, e.g. a def icon, store the disk object in their chunks
            await DiskObjectHelper.UpdateTrueColorIcons(amigaIcon.DiskObject, trueColorIcons);
        }

        using var output = new MemoryStream();
        await AmigaIconHelper.WriteAmigaIcon(amigaIcon, output);
        return output.ToArray();
    }

    private static async Task<bool> IsExecutableAsync(IVolume volume, string name,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await volume.OpenRead(name, cancellationToken);
            var magic = new byte[HunkHeaderMagic.Length];
            return await stream.ReadAtLeastAsync(magic, magic.Length, false, cancellationToken) == magic.Length &&
                   magic.AsSpan().SequenceEqual(HunkHeaderMagic);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>The images of a default icon, the color images as when the icon is saved.</summary>
    private static (IconImage? Normal, IconImage? Selected) GetDefaultImages(AmigaIcon amigaIcon) =>
        AmigaIconProvider.GetImages(amigaIcon);

    /// <summary>
    /// Planar image with its pens complemented, like the Workbench draws selected icons without a selected image:
    /// the bit planes stored are inverted and the planes not stored (filled with PlaneOnOff) are flipped.
    /// </summary>
    private static ImageData? Complement(ImageData? imageData)
    {
        if (imageData is not { Width: > 0, Height: > 0 } || imageData.Data == null)
        {
            return null;
        }

        var planeMask = (byte)((1 << imageData.Depth) - 1);
        return new ImageData
        {
            LeftEdge = imageData.LeftEdge,
            TopEdge = imageData.TopEdge,
            Width = imageData.Width,
            Height = imageData.Height,
            Depth = imageData.Depth,
            ImageDataPointer = imageData.ImageDataPointer,
            PlanePick = imageData.PlanePick,
            PlaneOnOff = (byte)(imageData.PlaneOnOff ^ (~imageData.PlanePick & planeMask)),
            NextPointer = imageData.NextPointer,
            Data = imageData.Data.Select(value => (byte)~value).ToArray()
        };
    }
}
