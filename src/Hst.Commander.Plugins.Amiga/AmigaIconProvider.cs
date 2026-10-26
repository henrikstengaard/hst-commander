using Hst.Amiga.DataTypes.DiskObjects;
using Hst.Amiga.DataTypes.DiskObjects.NewIcons;
using Hst.Commander.Abstractions;
using Hst.Imaging;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// Icon provider reading Amiga icons (disk objects) from .info files, e.g. "file.info" holds the icon of "file" and of
/// itself. The best image of the icon is used: a true color (PNG, AmigaOS 4) icon, a color icon (GlowIcons), a
/// NewIcon or the planar image drawn with the full AmigaOS palette.
/// </summary>
public class AmigaIconProvider : IIconProvider
{
    public const string InfoExtension = ".info";

    /// <summary>Icon of an Amiga volume, in its root.</summary>
    public const string DiskIconFileName = "Disk.info";

    /// <summary>Position of a disk object without a position on the Workbench, NO_ICON_POSITION.</summary>
    internal const int NoIconPosition = unchecked((int)0x80000000);

    private static readonly byte[] DiskObjectMagic = [0xe3, 0x10];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4e, 0x47];

    public string Name => "Amiga icons";

    public string Description => "Amiga .info icon files";

    public string? GetIconFileName(string name)
    {
        if (name.EndsWith(InfoExtension, StringComparison.OrdinalIgnoreCase))
        {
            return name.Length > InfoExtension.Length ? name : null;
        }

        return name.Length == 0 ? null : name + InfoExtension;
    }

    /// <summary>The icon of an Amiga volume (e.g. an RDB partition) is the Disk.info file in its root.</summary>
    public string? GetVolumeIconFileName(string name) => DiskIconFileName;

    public async Task<IconImage?> ReadIcon(string name, Stream stream, int size,
        CancellationToken cancellationToken = default) =>
        (await ReadIcons(name, stream, cancellationToken)).FirstOrDefault()?.Images[0];

    /// <summary>
    /// The type of the icon (disk object), e.g. "Drawer", and its position on the Workbench, e.g. "10, 20", or "None"
    /// when the Workbench places it.
    /// </summary>
    public async Task<IReadOnlyList<EntryProperty>> ReadIconProperties(string name, Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (await ReadAmigaIconAsync(stream, cancellationToken) is not { DiskObject: { } diskObject })
        {
            return [];
        }

        var hasPosition = diskObject.CurrentX != NoIconPosition && diskObject.CurrentY != NoIconPosition;
        return
        [
            new EntryProperty("Icon type", GetTypeName(diskObject.Type)),
            new EntryProperty("Icon position", hasPosition ? $"{diskObject.CurrentX}, {diskObject.CurrentY}" : "None")
        ];
    }

    /// <summary>Name of a disk object type, WBDISK to WBAPPICON.</summary>
    private static string GetTypeName(int type) => type switch
    {
        1 => "Disk",
        2 => "Drawer",
        3 => "Tool",
        4 => "Project",
        5 => "Trashcan",
        6 => "Device",
        7 => "Kickstart",
        8 => "AppIcon",
        _ => $"Unknown ({type})"
    };

    /// <summary>
    /// Reads all images of an Amiga icon, best first: the true color (PNG) images, the color icon (GlowIcon) images,
    /// the NewIcon images and the planar images, each normal and selected when present.
    /// </summary>
    public async Task<IReadOnlyList<EntryIcon>> ReadIcons(string name, Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return GetIcons(await ReadAmigaIconAsync(stream, cancellationToken)).ToList();
    }

    /// <summary>Reads an Amiga icon, null when the stream isn't a valid icon.</summary>
    private static async Task<AmigaIcon?> ReadAmigaIconAsync(Stream stream, CancellationToken cancellationToken)
    {
        var magic = new byte[4];
        if (await stream.ReadAtLeastAsync(magic, magic.Length, false, cancellationToken) < magic.Length ||
            !(magic.AsSpan(0, 2).SequenceEqual(DiskObjectMagic) || magic.AsSpan().SequenceEqual(PngMagic)))
        {
            return null;
        }

        try
        {
            stream.Position = 0;
            return await AmigaIconHelper.ReadAmigaIcon(stream);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Hst.Amiga throws various exceptions for invalid icon files, which just have no icon
            return null;
        }
    }

    /// <summary>
    /// Normal and selected image of the best kind of image of an icon (see <see cref="ReadIcons" />), null when the
    /// icon has no image or no selected image of that kind.
    /// </summary>
    internal static (IconImage? Normal, IconImage? Selected) GetImages(AmigaIcon amigaIcon)
    {
        var icons = GetIcons(amigaIcon).ToList();
        if (icons.Count == 0)
        {
            return (null, null);
        }

        // the icons are named by image and kind, e.g. "Normal (planar)" and "Selected (planar)"
        var kind = icons[0].Name[icons[0].Name.IndexOf('(')..];
        var selected = icons.FirstOrDefault(icon => icon.Name == $"{GetImageName(1)} {kind}");
        return (icons[0].Images[0], selected?.Images[0]);
    }

    /// <summary>Planar image of a disk object drawn with the full AmigaOS palette, null when it has none.</summary>
    internal static IconImage? DecodePlanarImage(ImageData? imageData) =>
        imageData is { Width: > 0, Height: > 0 }
            ? ToIconImage(ImageDataDecoder.Decode(imageData, AmigaOsPalette.FullPalette(), true), imageData.Depth)
            : null;

    private static IEnumerable<EntryIcon> GetIcons(AmigaIcon? amigaIcon)
    {
        if (amigaIcon == null)
        {
            yield break;
        }

        var trueColorIcons = amigaIcon.TrueColorIcons ?? [];
        for (var index = 0; index < trueColorIcons.Count; index++)
        {
            var trueColorIcon = trueColorIcons[index];
            yield return new EntryIcon($"{GetImageName(index)} (true color)",
                [new IconImage(trueColorIcon.Image.Width, trueColorIcon.Image.Height, trueColorIcon.PngData, null, 32)]);
        }

        var colorIconImages = amigaIcon.ColorIcon?.Images ?? [];
        for (var index = 0; index < colorIconImages.Length; index++)
        {
            if (colorIconImages[index].Image is { } image &&
                ToIconImage(image, colorIconImages[index].Depth) is { } iconImage)
            {
                yield return new EntryIcon($"{GetImageName(index)} (GlowIcon)", [iconImage]);
            }
        }

        var diskObject = amigaIcon.DiskObject;
        if (diskObject == null)
        {
            yield break;
        }

        for (var imageNumber = 1; imageNumber <= 2; imageNumber++)
        {
            if (NewIconHelper.GetNewIconImage(diskObject, imageNumber) is { Width: > 0, Height: > 0 } newIcon &&
                ToIconImage(NewIconConverter.ToImage(newIcon), newIcon.Depth) is { } iconImage)
            {
                yield return new EntryIcon($"{GetImageName(imageNumber - 1)} (NewIcon)", [iconImage]);
            }
        }

        ImageData?[] planarImages = [diskObject.FirstImageData, diskObject.SecondImageData];
        for (var index = 0; index < planarImages.Length; index++)
        {
            // the 8 colors of the full AmigaOS palette, so both 4 color and 8 color (e.g. MagicWB) icons are drawn
            // with the colors they were designed for
            if (DecodePlanarImage(planarImages[index]) is { } iconImage)
            {
                yield return new EntryIcon($"{GetImageName(index)} (planar)", [iconImage]);
            }
        }
    }

    /// <summary>Amiga icons have a normal image and an optional selected image, shown while the icon is selected.</summary>
    private static string GetImageName(int index) => index switch
    {
        0 => "Normal",
        1 => "Selected",
        _ => $"Image {index + 1}"
    };

    /// <summary>Converts an Hst.Imaging image to BGRA pixels.</summary>
    private static IconImage? ToIconImage(Image image, int? bitsPerPixel)
    {
        if (image.Width <= 0 || image.Height <= 0)
        {
            return null;
        }

        var pixels = new byte[image.Width * image.Height * 4];
        if (image.BitsPerPixel <= 8)
        {
            var colors = image.Palette.Colors;
            for (var i = 0; i < image.Width * image.Height && i < image.PixelData.Length; i++)
            {
                var index = image.PixelData[i];
                if (colors.Count == 0)
                {
                    continue;
                }

                // pens beyond the palette wrap around, as the Workbench would draw them with other screen colors
                var color = colors[index % colors.Count];
                var alpha = index == image.Palette.TransparentColor ? 0 : color.A;
                SetPixel(pixels, i, color.R, color.G, color.B, alpha);
            }
        }
        else
        {
            // 24 and 32 bits per pixel images store rgb(a)
            var iterator = new ImagePixelDataIterator(image);
            for (var i = 0; i < image.Width * image.Height && iterator.Next(); i++)
            {
                var pixel = iterator.Current;
                SetPixel(pixels, i, pixel.R, pixel.G, pixel.B, pixel.A);
            }
        }

        return new IconImage(image.Width, image.Height, null, pixels, bitsPerPixel);
    }

    private static void SetPixel(byte[] pixels, int index, int r, int g, int b, int a)
    {
        pixels[index * 4] = (byte)b;
        pixels[index * 4 + 1] = (byte)g;
        pixels[index * 4 + 2] = (byte)r;
        pixels[index * 4 + 3] = (byte)a;
    }
}
