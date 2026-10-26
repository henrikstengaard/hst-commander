using Hst.Amiga.DataTypes.DiskObjects;
using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Plugins.Amiga;

namespace Hst.Commander.Core.Tests;

public class GivenWorkbenchNavigationMode : IDisposable
{
    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    private readonly WorkbenchNavigationMode _navigationMode = new();

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private async Task<(LocalVolume Volume, List<IEntry> Entries)> ListAsync()
    {
        var volume = new LocalVolume(_rootPath);
        return (volume, await volume.ListEntries().ToListAsync());
    }

    private async Task CreateEntriesAsync()
    {
        Directory.CreateDirectory(Path.Combine(_rootPath, "dir1"));
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file1"), "file1");
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "file1.info"),
            await AmigaIconTestData.CreatePlanarIcon(position: (10, 20)));
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file2"), "file2");
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "orphan.info"),
            await AmigaIconTestData.CreatePlanarIcon(true));
        // the icon of the volume, e.g. in the root of an adf
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "Disk.info"), await AmigaIconTestData.CreatePlanarIcon());
    }

    [Fact]
    public async Task When_Layout_Then_IconsHaveTheirPositionsAndInfoFilesOfEntriesAndDiskInfoAreHidden()
    {
        // arrange
        await CreateEntriesAsync();
        var (volume, entries) = await ListAsync();

        // act
        var layout = await _navigationMode.Layout(volume, entries);

        // assert
        Assert.Equal(["dir1", "file1", "file2", "orphan.info"], layout.Icons.Select(icon => icon.Name));
        var dir1 = layout.Icons[0];
        Assert.Null(dir1.X);
        Assert.Null(dir1.Y);
        Assert.NotNull(dir1.Image);
        Assert.NotNull(dir1.SelectedImage);
        var file1 = layout.Icons[1];
        Assert.Equal((10, 20), (file1.X, file1.Y));
        Assert.Equal((4, 2), (file1.Image!.Width, file1.Image.Height));
        // without a selected image the image is complemented: pen 0 (transparent) becomes pen 3
        Assert.NotNull(file1.SelectedImage);
        Assert.Equal(0, file1.Image.BgraPixels![3]);
        Assert.Equal(255, file1.SelectedImage!.BgraPixels![3]);
        Assert.NotNull(layout.Icons[2].Image);
        Assert.Null(layout.Icons[3].X);
    }

    [Fact]
    public async Task When_SavePositions_Then_InfoFilesAreUpdatedOrCreated()
    {
        // arrange
        await CreateEntriesAsync();
        var (volume, entries) = await ListAsync();

        // act
        await _navigationMode.SavePositions(volume, entries,
            [new NavigationIconPosition("dir1", 5, 6), new NavigationIconPosition("file1", 30, 40),
             new NavigationIconPosition("orphan.info", 50, 60)]);
        var (changedVolume, changedEntries) = await ListAsync();
        var layout = await _navigationMode.Layout(changedVolume, changedEntries);

        // assert - the drawer gets a drawer icon, the other icons keep their images
        Assert.True(File.Exists(Path.Combine(_rootPath, "dir1.info")));
        Assert.False(File.Exists(Path.Combine(_rootPath, "file2.info")));
        Assert.Equal(["dir1", "file1", "file2", "orphan.info"], layout.Icons.Select(icon => icon.Name));
        Assert.Equal([(5, 6), (30, 40), (null, null), (50, 60)],
            layout.Icons.Select(icon => (icon.X, icon.Y)));
        Assert.Equal((4, 2), (layout.Icons[1].Image!.Width, layout.Icons[1].Image!.Height));
        await using var stream = File.OpenRead(Path.Combine(_rootPath, "dir1.info"));
        var diskObject = (await AmigaIconHelper.ReadAmigaIcon(stream)).DiskObject;
        Assert.True(DiskObjectHelper.IsDrawerIcon(diskObject));
    }

    [Fact]
    public async Task When_SavePositionOfEntryWithoutIcon_Then_IconWithPlanarAndColorImagesIsCreated()
    {
        // arrange
        await CreateEntriesAsync();
        var (volume, entries) = await ListAsync();

        // act
        await _navigationMode.SavePositions(volume, entries, [new NavigationIconPosition("file2", 3, 4)]);

        // assert - the color (GlowIcon) images come first, the selected one with a glow, then the planar image
        await using var stream = File.OpenRead(Path.Combine(_rootPath, "file2.info"));
        var icons = await new AmigaIconProvider().ReadIcons("file2.info", stream);
        Assert.Equal(["Normal (GlowIcon)", "Selected (GlowIcon)", "Normal (planar)"], icons.Select(icon => icon.Name));
        Assert.Equal((26, 30), (icons[0].Images[0].Width, icons[0].Images[0].Height));
        Assert.Equal((24, 28), (icons[2].Images[0].Width, icons[2].Images[0].Height));
        // the glow is drawn on the pixel left of the page in the selected image only
        var leftOfPage = (15 * 26 + 0) * 4 + 3;
        Assert.Equal(0, icons[0].Images[0].BgraPixels![leftOfPage]);
        Assert.Equal(255, icons[1].Images[0].BgraPixels![leftOfPage]);
        stream.Position = 0;
        var properties = await new AmigaIconProvider().ReadIconProperties("file2.info", stream);
        Assert.Equal(["Project", "3, 4"], properties.Select(property => property.Value));
    }

    [Fact]
    public async Task When_IconPositionsAreSavedInNavigationContext_Then_LayoutHasThem()
    {
        // arrange
        await CreateEntriesAsync();
        using var navigationContext = new NavigationContext(_rootPath);
        var entries = await navigationContext.ListEntriesAsync();

        // act
        await navigationContext.SaveIconPositionsAsync(_navigationMode, entries,
            [new NavigationIconPosition("file2", 7, 8)]);
        var layout = await navigationContext.LayoutAsync(_navigationMode, await navigationContext.ListEntriesAsync());

        // assert - the parent entry is shown by the toolbar of the view
        Assert.True(navigationContext.CanSaveIconPositions);
        Assert.Equal(["dir1", "file1", "file2", "orphan.info"], layout.Icons.Select(icon => icon.Name));
        Assert.True(layout.Icons.All(icon => icon.IsMovable));
        var file2 = Assert.Single(layout.Icons, icon => icon.Name == "file2");
        Assert.Equal((7, 8), (file2.X, file2.Y));
    }

    [Fact]
    public async Task When_RdbPartitionsAreLaidOut_Then_DiskIconsAreShownAndTheirPositionsSaved()
    {
        // arrange
        await AmigaTestData.CreateRdbImage(Path.Combine(_rootPath, "disk.hdf"), AmigaTestData.Dos3DosType,
            AmigaTestData.Dos3DosType);
        using var navigationContext = new NavigationContext(_rootPath);
        await navigationContext.OpenEntryAsync(
            (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "disk.hdf"));
        await navigationContext.OpenEntryAsync(
            (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "rdb"));
        var path = navigationContext.CurrentPath;
        var entries = await navigationContext.ListEntriesAsync();

        // act
        var layout = await navigationContext.LayoutAsync(_navigationMode, entries);
        await navigationContext.SaveIconPositionsAsync(_navigationMode, entries,
            [new NavigationIconPosition("1", 12, 34)]);
        var savedLayout = await navigationContext.LayoutAsync(_navigationMode,
            await navigationContext.ListEntriesAsync());

        // assert - the partitions get the default disk icon, the pane stays in the partition list
        Assert.Equal(["1", "2"], layout.Icons.Select(icon => icon.Name));
        Assert.All(layout.Icons, icon =>
        {
            // the color image of the default disk icon, with a pixel for the glow of the selected image around it
            Assert.Equal((42, 20), (icon.Image!.Width, icon.Image.Height));
            Assert.Null(icon.X);
        });
        Assert.Equal(path, navigationContext.CurrentPath);
        Assert.Equal((12, 34), (savedLayout.Icons[0].X, savedLayout.Icons[0].Y));
        Assert.Null(savedLayout.Icons[1].X);
        var partition = (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "1");
        var lines = (await navigationContext.GetPropertiesAsync([partition])).GetSections()
            .SelectMany(section => section).ToDictionary(line => line.Label, line => line.Value);
        Assert.Equal("Disk", lines["Icon type"]);
        Assert.Equal("12, 34", lines["Icon position"]);
        await navigationContext.OpenEntryAsync(partition);
        var partitionEntries = await navigationContext.ListEntriesAsync();
        Assert.Contains(partitionEntries, entry => entry.Name == "Disk.info");
        // the Disk.info in the root of the partition isn't shown
        Assert.Empty((await navigationContext.LayoutAsync(_navigationMode, partitionEntries)).Icons);
    }

    [Theory]
    [InlineData("file1")]
    [InlineData("file1.info")]
    public async Task When_PropertiesOfEntryWithIconAreShown_Then_IconTypeAndPositionAreShown(string name)
    {
        // arrange
        await CreateEntriesAsync();
        using var navigationContext = new NavigationContext(_rootPath);
        var entries = await navigationContext.ListEntriesAsync();

        // act
        var properties = await navigationContext.GetPropertiesAsync([entries.First(entry => entry.Name == name)]);
        var file2Properties = await navigationContext.GetPropertiesAsync([entries.First(entry => entry.Name == "file2")]);

        // assert
        var lines = properties.GetSections().SelectMany(section => section)
            .ToDictionary(line => line.Label, line => line.Value);
        Assert.Equal("file1.info", lines["Icon file"]);
        Assert.Equal("Project", lines["Icon type"]);
        Assert.Equal("10, 20", lines["Icon position"]);
        Assert.DoesNotContain(file2Properties.GetSections().SelectMany(section => section),
            line => line.Label == "Icon position");
    }

    [Fact]
    public async Task When_SavingIconPositionsInDriveList_Then_NotSupportedExceptionIsThrown()
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);

        // act and assert
        Assert.False(navigationContext.CanSaveIconPositions);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            navigationContext.SaveIconPositionsAsync(_navigationMode, [], [new NavigationIconPosition("c", 1, 1)]));
    }

    [Fact]
    public async Task When_SavePositionOfAmigaExecutable_Then_ToolIconIsCreated()
    {
        // arrange - an executable starts with the hunk header
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "tool"), [0, 0, 3, 0xf3, 0, 0, 0, 0]);
        var (volume, entries) = await ListAsync();

        // act
        await _navigationMode.SavePositions(volume, entries, [new NavigationIconPosition("tool", 1, 2)]);

        // assert - WBTOOL is type 3
        await using var stream = File.OpenRead(Path.Combine(_rootPath, "tool.info"));
        var diskObject = (await AmigaIconHelper.ReadAmigaIcon(stream)).DiskObject;
        Assert.Equal(3, diskObject.Type);
        Assert.Equal((1, 2), (diskObject.CurrentX, diskObject.CurrentY));
    }
}
