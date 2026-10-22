using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Amiga;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextWithIconFiles
{
    private static readonly IReadOnlyList<IIconProvider> IconProviders = [new AmigaIconProvider()];

    [Fact]
    public async Task When_ListEntries_Then_IconFilesAreFoundForEntriesAndIconFiles()
    {
        // arrange
        using var navigationContext = await CreateNavigationContext(await AmigaIconTestData.CreatePlanarIcon());

        // act
        var entries = await navigationContext.ListEntriesAsync();

        // assert
        string? GetIconFileName(string name) =>
            navigationContext.GetIconFile(entries.Single(entry => entry.Name == name))?.Name;
        Assert.Equal("file.info", GetIconFileName("file"));
        Assert.Equal("file.info", GetIconFileName("file.info"));
        Assert.Equal("Drawer.info", GetIconFileName("drawer"));
        Assert.Equal("Drawer.info", GetIconFileName("Drawer.info"));
        Assert.Null(GetIconFileName("other.txt"));
    }

    [Fact]
    public async Task When_ReadIcon_Then_IconOfIconFileIsReturnedForEntryAndIconFile()
    {
        // arrange
        using var navigationContext = await CreateNavigationContext(await AmigaIconTestData.CreatePlanarIcon());
        var entries = await navigationContext.ListEntriesAsync();

        // act
        var fileIcon = await navigationContext.ReadIconAsync(entries.Single(entry => entry.Name == "file"), 16);
        var infoIcon = await navigationContext.ReadIconAsync(entries.Single(entry => entry.Name == "file.info"), 16);
        var otherIcon = await navigationContext.ReadIconAsync(entries.Single(entry => entry.Name == "other.txt"), 16);

        // assert
        Assert.NotNull(fileIcon);
        Assert.Equal(4, fileIcon.Width);
        Assert.NotNull(infoIcon);
        Assert.Equal(fileIcon.BgraPixels, infoIcon.BgraPixels);
        Assert.Null(otherIcon);
    }

    [Fact]
    public async Task When_ReadIcons_Then_AllIconsOfIconFileAreReturned()
    {
        // arrange
        using var navigationContext =
            await CreateNavigationContext(await AmigaIconTestData.CreatePlanarIcon(withSelectedImage: true));
        var entries = await navigationContext.ListEntriesAsync();

        // act
        var fileIcons = await navigationContext.ReadIconsAsync(entries.Single(entry => entry.Name == "file"));
        var otherIcons = await navigationContext.ReadIconsAsync(entries.Single(entry => entry.Name == "other.txt"));

        // assert
        Assert.Equal(["Normal (planar)", "Selected (planar)"], fileIcons.Select(icon => icon.Name));
        Assert.Empty(otherIcons);
    }

    [Fact]
    public async Task When_ReadIconOfInvalidIconFile_Then_NullIsReturned()
    {
        // arrange
        using var navigationContext = await CreateNavigationContext("not an icon"u8.ToArray());
        var entries = await navigationContext.ListEntriesAsync();

        // act
        var icon = await navigationContext.ReadIconAsync(entries.Single(entry => entry.Name == "file"), 16);

        // assert
        Assert.Null(icon);
    }

    [Fact]
    public async Task When_ReadIconOfLocalFile_Then_IconIsReturned()
    {
        // arrange
        var rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(rootPath, "file"), "file");
            await File.WriteAllBytesAsync(Path.Combine(rootPath, "file.info"),
                await AmigaIconTestData.CreatePlanarIcon());
            using var navigationContext = new NavigationContext(rootPath, iconProviders: IconProviders);
            var entries = await navigationContext.ListEntriesAsync();

            // act
            var icon = await navigationContext.ReadIconAsync(entries.Single(entry => entry.Name == "file"), 16);

            // assert
            Assert.NotNull(icon);
            Assert.Equal(4, icon.Width);
            Assert.Equal(2, icon.Height);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    private static async Task<NavigationContext> CreateNavigationContext(byte[] iconData)
    {
        var volumeProvider = new TestVolumeProvider(volume =>
        {
            volume.AddEntry([], new TestEntry("drawer", EntryType.Container));
            volume.AddEntry([], new TestEntry("Drawer.info", EntryType.Item, iconData.Length, data: iconData));
            volume.AddEntry([], new TestEntry("file", EntryType.Item, 4, data: "file"u8.ToArray()));
            volume.AddEntry([], new TestEntry("file.info", EntryType.Item, iconData.Length, data: iconData));
            volume.AddEntry([], new TestEntry("other.txt", EntryType.Item, 5, data: "other"u8.ToArray()));
        });
        var navigationContext = new NavigationContext(string.Empty, iconProviders: IconProviders);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);
        return navigationContext;
    }
}
