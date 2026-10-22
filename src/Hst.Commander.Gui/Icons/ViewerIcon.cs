using Avalonia.Media;

namespace Hst.Commander.Gui.Icons;

/// <summary>
///     Icon shown in the icon viewer: one icon of a file (e.g. the selected image of an Amiga icon or an icon of a
///     Windows program) with its images, largest first.
/// </summary>
internal sealed class ViewerIcon(string name, IReadOnlyList<ViewerIconImage> images)
{
    /// <summary>Size of the thumbnails in the icon list in pixels.</summary>
    private const int ThumbnailSize = 32;

    public string Name { get; } = name;

    public IReadOnlyList<ViewerIconImage> Images { get; } = images;

    /// <summary>Size of the single image, otherwise the number of sizes, shown below the name in the icon list.</summary>
    public string Description => Images.Count == 1 ? Images[0].ToString() : $"{Images.Count} images";

    /// <summary>
    ///     Thumbnail in the icon list: the smallest image at least the thumbnail size, otherwise the largest.
    /// </summary>
    public IImage? Thumbnail => (Images.Where(image => image.Width >= ThumbnailSize).MinBy(image => image.Width) ??
                                 Images[0]).Image;
}

/// <summary>Image of an icon in the icon viewer, its bitmap created when first shown.</summary>
internal sealed class ViewerIconImage(int width, int height, int? bitsPerPixel, Func<IImage?> createImage)
{
    private readonly Lazy<IImage?> _image = new(createImage);

    public int Width { get; } = width;

    public int Height { get; } = height;

    public int? BitsPerPixel { get; } = bitsPerPixel;

    public IImage? Image => _image.Value;

    /// <summary>Size and color depth, e.g. "32 × 32, 8-bit", shown in the size list.</summary>
    public override string ToString() =>
        BitsPerPixel is { } bitsPerPixel ? $"{Width} × {Height}, {bitsPerPixel}-bit" : $"{Width} × {Height}";
}
