using Hst.Commander.Plugins.Amiga;

namespace Hst.Commander.Core.Tests;

public class GivenAmigaIconProvider
{
    private readonly AmigaIconProvider iconProvider = new();

    [Theory]
    [InlineData("file", "file.info")]
    [InlineData("file.info", "file.info")]
    [InlineData("Disk.INFO", "Disk.INFO")]
    [InlineData("readme.txt", "readme.txt.info")]
    [InlineData(".info", null)]
    public void When_GetIconFileName_Then_InfoFileNameIsReturned(string name, string? expectedIconFileName)
    {
        // act
        var iconFileName = iconProvider.GetIconFileName(name);

        // assert
        Assert.Equal(expectedIconFileName, iconFileName);
    }

    [Fact]
    public async Task When_ReadPlanarIcon_Then_ImageIsDrawnWithFullAmigaOsPalette()
    {
        // arrange
        var data = await AmigaIconTestData.CreatePlanarIcon();

        // act
        var icon = await iconProvider.ReadIcon("file.info", new MemoryStream(data), 16);

        // assert
        Assert.NotNull(icon);
        Assert.False(icon.IsPng);
        Assert.Equal(4, icon.Width);
        Assert.Equal(2, icon.Height);
        var pixels = icon.BgraPixels!;
        Assert.Equal(4 * 2 * 4, pixels.Length);
        // pen 0 is transparent, pen 1 black, pen 2 white and pen 3 blue (bgra)
        Assert.Equal(0, pixels[3]);
        Assert.Equal(new byte[] { 17, 17, 17, 255 }, pixels[4..8]);
        Assert.Equal(new byte[] { 238, 238, 238, 255 }, pixels[8..12]);
        Assert.Equal(new byte[] { 175, 105, 75, 255 }, pixels[12..16]);
        Assert.Equal(new byte[] { 175, 105, 75, 255 }, pixels[16..20]);
        Assert.Equal(0, pixels[31]);
    }

    [Fact]
    public async Task When_ReadEightColorPlanarIcon_Then_UpperPensAreDrawnWithFullAmigaOsPalette()
    {
        // arrange
        var data = await AmigaIconTestData.CreateEightColorPlanarIcon();

        // act
        var icon = await iconProvider.ReadIcon("file.info", new MemoryStream(data), 16);

        // assert
        Assert.NotNull(icon);
        Assert.Equal(4, icon.Width);
        Assert.Equal(1, icon.Height);
        // pens 4 to 7: dark gray, light gray, beige and pink (bgra)
        Assert.Equal(
            new byte[] { 119, 119, 119, 255, 187, 187, 187, 255, 119, 170, 204, 255, 153, 102, 221, 255 },
            icon.BgraPixels);
    }

    [Fact]
    public async Task When_ReadIconWithNewIcon_Then_NewIconImageIsReturned()
    {
        // arrange
        var data = await AmigaIconTestData.CreateNewIcon();

        // act
        var icon = await iconProvider.ReadIcon("file.info", new MemoryStream(data), 16);

        // assert
        Assert.NotNull(icon);
        Assert.Equal(2, icon.Width);
        Assert.Equal(2, icon.Height);
        var pixels = icon.BgraPixels!;
        Assert.Equal(0, pixels[3]);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels[4..8]);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels[12..16]);
    }

    [Fact]
    public async Task When_ReadIconsOfPlanarIconWithSelectedImage_Then_NormalAndSelectedImagesAreReturned()
    {
        // arrange
        var data = await AmigaIconTestData.CreatePlanarIcon(withSelectedImage: true);

        // act
        var icons = await iconProvider.ReadIcons("file.info", new MemoryStream(data));

        // assert
        Assert.Equal(["Normal (planar)", "Selected (planar)"], icons.Select(icon => icon.Name));
        Assert.All(icons, icon => Assert.Equal(2, Assert.Single(icon.Images).BitsPerPixel));
        // the selected image uses the pens in reverse: pen 3 (blue) first
        Assert.Equal(new byte[] { 175, 105, 75, 255 }, icons[1].Images[0].BgraPixels![..4]);
    }

    [Fact]
    public async Task When_ReadIconsOfNewIcon_Then_NewIconImageIsFirstFollowedByPlanarImage()
    {
        // arrange
        var data = await AmigaIconTestData.CreateNewIcon();

        // act
        var icons = await iconProvider.ReadIcons("file.info", new MemoryStream(data));

        // assert
        Assert.Equal(["Normal (NewIcon)", "Normal (planar)"], icons.Select(icon => icon.Name));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x54, 0x65, 0x78, 0x74, 0x20, 0x66, 0x69, 0x6c, 0x65 })]
    [InlineData(new byte[] { 0xe3, 0x10, 0x00, 0x01, 0x00 })]
    public async Task When_ReadInvalidIcon_Then_NullIsReturned(byte[] data)
    {
        // act
        var icon = await iconProvider.ReadIcon("file.info", new MemoryStream(data), 16);

        // assert
        Assert.Null(icon);
    }
}
