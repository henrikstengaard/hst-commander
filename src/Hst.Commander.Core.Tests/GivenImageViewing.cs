using System.Buffers.Binary;
using System.Text;
using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Amiga;
using SkiaSharp;

namespace Hst.Commander.Core.Tests;

public class GivenImageViewing
{
    private static readonly byte[] FourColorMap = [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255];

    private static (int R, int G, int B, int A) Pixel(IconImage image, int x, int y)
    {
        var pixels = ImagePixels.ToBgra(image)!;
        var index = (y * image.Width + x) * 4;
        return (pixels[index + 2], pixels[index + 1], pixels[index], pixels[index + 3]);
    }

    private static Task<IReadOnlyList<EntryImage>> ReadAsync(IImageProvider provider, string name, byte[] data) =>
        provider.ReadImages(name, new MemoryStream(data));

    private static byte[] CreatePng(int width, int height, SKColor color, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var data = bitmap.Encode(format, 100);
        return data.ToArray();
    }

    private static byte[] CreateIco(params byte[][] pngs)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)pngs.Length);
        var offset = 6 + pngs.Length * 16;
        foreach (var png in pngs)
        {
            var size = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var png in pngs)
        {
            writer.Write(png);
        }

        return stream.ToArray();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task When_ReadIlbm_Then_BitplanesAreDecodedWithColorMap(bool compress)
    {
        // arrange
        var ilbm = IlbmTestData.CreateIlbm(4, 2, 2, [0, 1, 2, 3, 3, 3, 3, 1], FourColorMap, compress);

        // act
        var images = await ReadAsync(new AmigaImageProvider(), "picture.iff", ilbm);

        // assert
        var image = Assert.Single(images);
        Assert.Equal("ILBM", image.Name);
        Assert.Equal((4, 2, 2), (image.Image.Width, image.Image.Height, image.Image.BitsPerPixel));
        Assert.Equal((0, 0, 0, 255), Pixel(image.Image, 0, 0));
        Assert.Equal((255, 0, 0, 255), Pixel(image.Image, 1, 0));
        Assert.Equal((0, 255, 0, 255), Pixel(image.Image, 2, 0));
        Assert.Equal((0, 0, 255, 255), Pixel(image.Image, 3, 0));
        Assert.Equal((255, 0, 0, 255), Pixel(image.Image, 3, 1));
    }

    [Fact]
    public async Task When_ReadIlbmWithFourBitColors_Then_ColorsAreScaledUp()
    {
        // arrange
        var ilbm = IlbmTestData.CreateIlbm(1, 1, 1, [1], [0, 0, 0, 0xF0, 0x80, 0x00]);

        // act
        var image = (await ReadAsync(new AmigaImageProvider(), "picture.iff", ilbm))[0].Image;

        // assert
        Assert.Equal((0xFF, 0x88, 0, 255), Pixel(image, 0, 0));
    }

    [Fact]
    public async Task When_ReadExtraHalfBriteIlbm_Then_UpperColorsAreHalfAsBright()
    {
        // arrange
        var colorMap = new byte[32 * 3];
        colorMap[3] = 200;
        colorMap[4] = 100;
        colorMap[5] = 50;
        var ilbm = IlbmTestData.CreateIlbm(2, 1, 6, [1, 33], colorMap, viewModes: 0x80);

        // act
        var image = (await ReadAsync(new AmigaImageProvider(), "picture.iff", ilbm))[0].Image;

        // assert
        Assert.Equal((200, 100, 50, 255), Pixel(image, 0, 0));
        Assert.Equal((100, 50, 25, 255), Pixel(image, 1, 0));
    }

    [Fact]
    public async Task When_ReadHam6Ilbm_Then_ColorsAreHeldAndModified()
    {
        // arrange: a color of the color map, then red, green and blue changed one at a time
        var colorMap = new byte[16 * 3];
        colorMap[3] = 0x11;
        colorMap[4] = 0x22;
        colorMap[5] = 0x33;
        var ilbm = IlbmTestData.CreateIlbm(4, 1, 6, [0x01, 0x2F, 0x38, 0x10], colorMap, viewModes: 0x800);

        // act
        var image = (await ReadAsync(new AmigaImageProvider(), "picture.ham", ilbm))[0].Image;

        // assert
        Assert.Equal((0x11, 0x22, 0x33, 255), Pixel(image, 0, 0));
        Assert.Equal((0xFF, 0x22, 0x33, 255), Pixel(image, 1, 0));
        Assert.Equal((0xFF, 0x88, 0x33, 255), Pixel(image, 2, 0));
        Assert.Equal((0xFF, 0x88, 0x00, 255), Pixel(image, 3, 0));
    }

    [Fact]
    public async Task When_ReadIlbmWithMaskPlane_Then_MaskedPixelsAreTransparent()
    {
        // arrange
        var ilbm = IlbmTestData.CreateIlbm(2, 1, 2, [1, 2], FourColorMap, true, mask: [true, false]);

        // act
        var image = (await ReadAsync(new AmigaImageProvider(), "picture.iff", ilbm))[0].Image;

        // assert
        Assert.Equal(255, Pixel(image, 0, 0).A);
        Assert.Equal(0, Pixel(image, 1, 0).A);
    }

    [Fact]
    public async Task When_ReadListOfIlbms_Then_EachPictureIsAnImage()
    {
        // arrange
        var list = IlbmTestData.CreateList(
            IlbmTestData.CreateIlbm(1, 1, 2, [1], FourColorMap),
            IlbmTestData.CreateIlbm(2, 1, 2, [2, 3], FourColorMap));

        // act
        var images = await ReadAsync(new AmigaImageProvider(), "pictures.iff", list);

        // assert
        Assert.Equal(["ILBM 1", "ILBM 2"], images.Select(image => image.Name));
        Assert.Equal([1, 2], images.Select(image => image.Image.Width));
    }

    [Fact]
    public async Task When_ReadAmigaIconWithSelectedImage_Then_NormalAndSelectedImagesAreRead()
    {
        // arrange
        var icon = await AmigaIconTestData.CreatePlanarIcon(true);

        // act
        var images = await ReadAsync(new AmigaImageProvider(), "file.info", icon);

        // assert
        Assert.Equal(["Normal (planar)", "Selected (planar)"], images.Select(image => image.Name));
        Assert.All(images, image => Assert.Equal((4, 2), (image.Image.Width, image.Image.Height)));
    }

    [Fact]
    public async Task When_ReadPngNotNamedInfo_Then_AmigaImageProviderSkipsIt()
    {
        // arrange
        var png = CreatePng(2, 2, SKColors.Red, SKEncodedImageFormat.Png);

        // act
        var images = await ReadAsync(new AmigaImageProvider(), "picture.png", png);

        // assert
        Assert.Empty(images);
    }

    [Fact]
    public async Task When_ReadWindowsIconFile_Then_AllImagesAreReadLargestFirst()
    {
        // arrange
        var ico = CreateIco(CreatePng(16, 16, SKColors.Red, SKEncodedImageFormat.Png),
            CreatePng(32, 32, SKColors.Blue, SKEncodedImageFormat.Png));

        // act
        var images = await ReadAsync(new WindowsIconImageProvider(), "app.ico", ico);

        // assert
        Assert.Equal([32, 16], images.Select(image => image.Image.Width));
        Assert.Equal((0, 0, 255, 255), Pixel(images[0].Image, 5, 5));
        Assert.Equal((255, 0, 0, 255), Pixel(images[1].Image, 5, 5));
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "PNG")]
    [InlineData(SKEncodedImageFormat.Jpeg, "JPEG")]
    [InlineData(SKEncodedImageFormat.Webp, "WEBP")]
    public async Task When_ReadStandardImage_Then_ItIsDecodedToPixels(SKEncodedImageFormat format, string name)
    {
        // arrange
        var data = CreatePng(8, 4, new SKColor(0, 255, 0), format);

        // act
        var images = await ReadAsync(new StandardImageProvider(), "picture", data);

        // assert
        var image = Assert.Single(images);
        Assert.Equal(name, image.Name);
        Assert.Equal((8, 4), (image.Image.Width, image.Image.Height));
        var (r, g, b, a) = Pixel(image.Image, 3, 2);
        Assert.True(r < 16 && g > 240 && b < 16 && a == 255, $"{r}, {g}, {b}, {a}");
    }

    [Fact]
    public async Task When_ReadTextWithImageProviders_Then_NoImagesAreRead()
    {
        // arrange
        var text = Encoding.ASCII.GetBytes("FORM is not an image");

        // assert
        Assert.Empty(await ReadAsync(new StandardImageProvider(), "a.txt", text));
        Assert.Empty(await ReadAsync(new WindowsIconImageProvider(), "a.txt", text));
        Assert.Empty(await ReadAsync(new AmigaImageProvider(), "a.txt", text));
    }

    [Fact]
    public async Task When_ViewImageFile_Then_ImageViewShowsImagesAndHexViewToggles()
    {
        // arrange
        var ico = CreateIco(CreatePng(16, 16, SKColors.Red, SKEncodedImageFormat.Png),
            CreatePng(32, 32, SKColors.Blue, SKEncodedImageFormat.Png));
        var document = ViewerDocument.Open(new MemoryStream(ico));
        using var session = new ViewerSession(document, await document.ReadImagesAsync("app.ico", VolumeProviders.Images));

        // act & assert
        Assert.True(session.IsImage);
        Assert.Equal(32, session.CurrentImage!.Image.Width);
        Assert.Equal($"Image 1 of 2: Icon image 1, 32 × 32, 32-bit, {ico.Length:N0} bytes", session.GetStatusText(10));

        session.Scroll(10, 10);
        Assert.Equal(1, session.ImageIndex);
        session.Scroll(1, 10);
        Assert.Equal(1, session.ImageIndex);
        session.ScrollToStart();
        Assert.Equal(0, session.ImageIndex);

        session.ToggleHex();
        Assert.True(session.IsHex);
        Assert.False(session.IsImage);
        session.ToggleHex();
        Assert.True(session.IsImage);

        // searching shows the text found in the hex view
        Assert.True(await session.FindAsync(new ViewerSearchOptions("PNG"), 10, 80));
        Assert.True(session.IsHex);
    }

    [Fact]
    public async Task When_OpenViewerOfImageAndTextFiles_Then_OnlyImagesStartInImageView()
    {
        // arrange
        var rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(rootPath);
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "picture.iff"),
            IlbmTestData.CreateIlbm(4, 2, 2, [0, 1, 2, 3, 3, 3, 3, 1], FourColorMap, true));
        await File.WriteAllTextAsync(Path.Combine(rootPath, "text.txt"), "FORM text");
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            var entries = await navigationContext.ListEntriesAsync();

            // act
            using var image = await navigationContext.OpenViewerAsync(entries.First(e => e.Name == "picture.iff"));
            using var text = await navigationContext.OpenViewerAsync(entries.First(e => e.Name == "text.txt"));

            // assert
            Assert.True(image.IsImage);
            Assert.Equal("ILBM", image.CurrentImage!.Name);
            Assert.False(text.IsImage);
            Assert.Empty(text.Images);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public void When_ScaleAndFitImages_Then_SmallImagesAreEnlargedByWholeFactorsAndLargeOnesAveraged()
    {
        // arrange: a white and a transparent pixel
        byte[] pixels = [255, 255, 255, 255, 0, 0, 0, 0];

        // act
        var scaled = ImagePixels.Render(pixels, 2, 1, 0.5, 0, 0, 1, 1);
        var enlarged = ImagePixels.Render(pixels, 2, 1, 2, 0, 0, 4, 2);
        var moved = ImagePixels.Render(pixels, 2, 1, 2, -2, 1, 3, 2);

        // assert
        Assert.Equal([255, 255, 255, 127], scaled);
        Assert.Equal(255, enlarged[(1 * 4 + 1) * 4 + 3]);
        Assert.Equal(0, enlarged[(0 * 4 + 2) * 4 + 3]);
        // the white pixel is outside on the left, the transparent pixel and the area outside the image are transparent
        Assert.All(moved, value => Assert.Equal(0, value));
        Assert.Equal((32, 16), ImagePixels.GetFitSize(16, 8, 50, 50, 2));
        Assert.Equal((48, 24), ImagePixels.GetFitSize(16, 8, 50, 50));
        Assert.Equal((50, 25), ImagePixels.GetFitSize(100, 50, 50, 50));
    }

    [Fact]
    public void When_ZoomAndPanImage_Then_LayoutIsZoomedAndMovedWithinTheView()
    {
        // arrange: a 100 × 50 image in a 50 × 50 view
        var image = new IconImage(100, 50, null, new byte[100 * 50 * 4], 32);
        using var session = new ViewerSession(ViewerDocument.Open(new MemoryStream([1, 0, 2])),
            [new EntryImage("Image", image)]);

        // act & assert: fitted, centered vertically, nothing to move
        Assert.Equal(new ViewerImageLayout(0.5, 0, 12.5, 50, 25, true), session.GetImageLayout(50, 50));
        Assert.False(session.PanImage(10, 0, 50, 50));
        Assert.EndsWith(", Fit 50%", session.GetStatusText(10, session.GetImageLayout(50, 50)));

        // zoomed in to 100%, the center is shown and can be moved to the edges
        session.ZoomIn(50, 50);
        Assert.Equal(1, session.ImageZoom);
        Assert.Equal(new ViewerImageLayout(1, -25, 0, 100, 50, false), session.GetImageLayout(50, 50));
        Assert.EndsWith(", 100%", session.GetStatusText(10, session.GetImageLayout(50, 50)));
        Assert.True(session.PanImage(10, 0, 50, 50));
        Assert.Equal(-35, session.GetImageLayout(50, 50)!.X);
        Assert.True(session.PanImage(1000, 0, 50, 50));
        Assert.Equal(-50, session.GetImageLayout(50, 50)!.X);
        Assert.True(session.PanImage(-1000, 0, 50, 50));
        Assert.Equal(0, session.GetImageLayout(50, 50)!.X);

        // showing an image centers it again
        session.ShowImage(0);
        Assert.Equal(-25, session.GetImageLayout(50, 50)!.X);

        session.ZoomIn(50, 50);
        Assert.Equal(2, session.ImageZoom);
        session.ZoomOut(50, 50);
        Assert.Equal(1, session.ImageZoom);
        session.SetZoom(null);
        session.ZoomOut(50, 50);
        Assert.Equal(0.25, session.ImageZoom);
        session.SetZoom(100);
        session.ZoomIn(50, 50);
        Assert.Equal(16, session.ImageZoom);
    }
}
