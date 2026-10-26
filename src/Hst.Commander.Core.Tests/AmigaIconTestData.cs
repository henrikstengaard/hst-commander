using Hst.Amiga.DataTypes.DiskObjects;
using Hst.Amiga.DataTypes.DiskObjects.NewIcons;
using Hst.Imaging;

namespace Hst.Commander.Core.Tests;

/// <summary>Creates Amiga icon (.info) files for tests.</summary>
public static class AmigaIconTestData
{
    /// <summary>
    /// Creates a project icon with a 4x2 planar image using the Workbench pens 0, 1, 2, 3 on the first row and 3, 2,
    /// 1, 0 on the second, and a selected image with the pens in reverse order when <paramref name="withSelectedImage" />,
    /// at a position on the Workbench when given.
    /// </summary>
    public static async Task<byte[]> CreatePlanarIcon(bool withSelectedImage = false, (int X, int Y)? position = null)
    {
        var image = new Image(4, 2, 8, AmigaOsPalette.FourColors(), [0, 1, 2, 3, 3, 2, 1, 0]);
        var diskObject = DiskObjectHelper.CreateProjectInfo();
        if (position is { } iconPosition)
        {
            diskObject.CurrentX = iconPosition.X;
            diskObject.CurrentY = iconPosition.Y;
        }

        DiskObjectHelper.SetFirstImage(diskObject, ImageDataEncoder.Encode(image, 2));
        if (withSelectedImage)
        {
            var selectedImage = new Image(4, 2, 8, AmigaOsPalette.FourColors(), [3, 2, 1, 0, 0, 1, 2, 3]);
            DiskObjectHelper.SetSecondImage(diskObject, ImageDataEncoder.Encode(selectedImage, 2));
        }

        return await Write(diskObject);
    }

    /// <summary>Creates a project icon with a 4x1 planar image of depth 3 using the pens 4, 5, 6 and 7.</summary>
    public static async Task<byte[]> CreateEightColorPlanarIcon()
    {
        var image = new Image(4, 1, 8, AmigaOsPalette.FullPalette(), [4, 5, 6, 7]);
        var diskObject = DiskObjectHelper.CreateProjectInfo();
        DiskObjectHelper.SetFirstImage(diskObject, ImageDataEncoder.Encode(image, 3));
        return await Write(diskObject);
    }

    /// <summary>
    /// Creates a project icon with a planar image and a 2x2 NewIcon image: a transparent pixel and three red pixels.
    /// </summary>
    public static async Task<byte[]> CreateNewIcon()
    {
        var planarImage = new Image(2, 2, 8, AmigaOsPalette.FourColors(), [1, 1, 1, 1]);
        var diskObject = DiskObjectHelper.CreateProjectInfo();
        DiskObjectHelper.SetFirstImage(diskObject, ImageDataEncoder.Encode(planarImage, 2));
        var palette = new Palette([new Color(0, 0, 0, 0), new Color(255, 0, 0)], 0);
        NewIconHelper.SetFirstImage(diskObject,
            NewIconConverter.ToNewIcon(new Image(2, 2, 8, palette, [0, 1, 1, 1])));
        return await Write(diskObject);
    }

    private static async Task<byte[]> Write(DiskObject diskObject)
    {
        using var stream = new MemoryStream();
        await DiskObjectWriter.Write(diskObject, stream);
        return stream.ToArray();
    }
}
