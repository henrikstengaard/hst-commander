namespace Hst.Commander.Abstractions;

/// <summary>
/// Icon image read from a file: either PNG data or 32-bit BGRA pixels (not premultiplied, top-down rows).
/// </summary>
/// <param name="BitsPerPixel">Color depth of the image as stored in the file, e.g. 2 for a 4 color Amiga icon, null when unknown.</param>
public sealed record IconImage(int Width, int Height, byte[]? PngData, byte[]? BgraPixels, int? BitsPerPixel = null)
{
    public bool IsPng => PngData != null;
}

/// <summary>
/// One icon of a file with its images, e.g. the normal or the selected image of an Amiga icon (one image each), or an
/// icon of a Windows program with images in several sizes and color depths.
/// </summary>
/// <param name="Name">Name of the icon, e.g. "Normal (GlowIcon)" or the resource name of a Windows icon.</param>
/// <param name="Images">Images of the icon, at least one.</param>
public sealed record EntryIcon(string Name, IReadOnlyList<IconImage> Images);
