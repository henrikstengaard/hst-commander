using DiscUtils.Iso9660;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextChangingLocation
{
    [Fact]
    public async Task When_GoHome_Then_HomeDirectoryIsShown()
    {
        // arrange
        var homeDirectory = NavigationContext.HomeDirectory;
        Assert.NotNull(homeDirectory);
        using var navigationContext = new NavigationContext(string.Empty);

        // act
        await navigationContext.GoHomeAsync();

        // assert
        Assert.Equal(Path.GetFullPath(homeDirectory), navigationContext.CurrentPath);
        Assert.Same(VolumeProviders.Local, navigationContext.VolumeProvider);
    }

    [Fact]
    public async Task When_ChangeLocationToLocalDirectory_Then_DirectoryIsListed()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        var dirPath = Path.Combine(rootPath, "dir1");
        Directory.CreateDirectory(dirPath);
        await File.WriteAllTextAsync(Path.Combine(dirPath, "file1.txt"), "file1");
        try
        {
            using var navigationContext = new NavigationContext(string.Empty);

            // act
            await navigationContext.ChangeLocationAsync(dirPath);
            var entries = await navigationContext.ListEntriesAsync();

            // assert
            Assert.Equal(dirPath, navigationContext.CurrentPath);
            Assert.Equal([Paths.Parent, "file1.txt"], entries.Select(entry => entry.Name));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_ChangeLocationToRelativePath_Then_PathIsRelativeToCurrentDirectory()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        var dirPath = Path.Combine(rootPath, "dir1");
        Directory.CreateDirectory(dirPath);
        try
        {
            using var navigationContext = new NavigationContext(rootPath);

            // act
            await navigationContext.ChangeLocationAsync("dir1");

            // assert
            Assert.Equal(dirPath, navigationContext.CurrentPath);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_ChangeLocationToEmptyPath_Then_DrivesAreListed()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        try
        {
            using var navigationContext = new NavigationContext(rootPath);

            // act
            await navigationContext.ChangeLocationAsync(" ");

            // assert
            Assert.Equal("Computer", navigationContext.CurrentPath);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_ChangeLocationToMissingDirectory_Then_ExceptionIsThrownAndLocationIsUnchanged()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        try
        {
            using var navigationContext = new NavigationContext(rootPath);

            // act & assert
            await Assert.ThrowsAsync<IOException>(() =>
                navigationContext.ChangeLocationAsync(Path.Combine(rootPath, "missing", "dir")));
            Assert.Equal(rootPath, navigationContext.CurrentPath);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_ChangeLocationIntoIsoImage_Then_DirectoryInImageIsListed()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        var isoPath = Path.Combine(rootPath, "image.iso");
        var builder = new CDBuilder { UseJoliet = true };
        builder.AddDirectory("dir1");
        builder.AddFile(@"dir1\file1.txt", "iso-file1"u8.ToArray());
        builder.Build(isoPath);
        try
        {
            using var navigationContext = new NavigationContext(string.Empty);
            var dirPath = Path.Combine(isoPath, "dir1");

            // act
            await navigationContext.ChangeLocationAsync(dirPath);
            var entries = await navigationContext.ListEntriesAsync();

            // go up to the image root and out of the image
            await navigationContext.GoParentAsync();
            await navigationContext.GoParentAsync();

            // assert
            Assert.Equal([Paths.Parent, "file1.txt"], entries.Select(entry => entry.Name));
            Assert.Equal(rootPath, navigationContext.CurrentPath);
            Assert.Equal("image.iso", navigationContext.PreviousEntryName);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_ChangeLocationInOtherVolume_Then_PathIsFromVolumeRoot()
    {
        // arrange
        var volumeProvider = new TestVolumeProvider(volume =>
        {
            volume.AddEntry([], new TestEntry("dir1", EntryType.Container));
            volume.AddEntry(["dir1"], new TestEntry("dir2", EntryType.Container));
            volume.AddEntry(["dir1", "dir2"], new TestEntry("file1.txt", EntryType.Item));
        });
        using var navigationContext = new NavigationContext(string.Empty);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);

        // act
        await navigationContext.ChangeLocationAsync($"dir1{Path.DirectorySeparatorChar}dir2");
        var entries = await navigationContext.ListEntriesAsync();
        await navigationContext.ChangeLocationAsync("Test");

        // assert
        Assert.Equal([Paths.Parent, "file1.txt"], entries.Select(entry => entry.Name));
        Assert.Same(volumeProvider, navigationContext.VolumeProvider);
        Assert.Equal("Test", navigationContext.CurrentPath);
    }

    [Fact]
    public async Task When_ChangeLocationToOtherContextInsideZip_Then_SameLocationIsShownInOwnVolumes()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(rootPath, "dir1"));
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "dir1", "archive.zip"),
            ZipTestData.CreateZip(("dir2/file1.txt", "file1")));
        try
        {
            using var other = new NavigationContext(rootPath);
            await other.ChangeLocationAsync(Path.Combine(rootPath, "dir1", "archive.zip", "dir2"));
            using var navigationContext = new NavigationContext(rootPath) { UaeMetadata = UaeMetadataFormat.None };

            // act
            await navigationContext.ChangeLocationAsync(other);
            var entries = await navigationContext.ListEntriesAsync();
            // going up in one context leaves the other where it is
            await navigationContext.GoParentAsync();

            // assert
            Assert.Equal([Paths.Parent, "file1.txt"], entries.Select(entry => entry.Name));
            Assert.True(navigationContext.IsInImage);
            Assert.Equal(UaeMetadataFormat.None, navigationContext.UaeMetadata);
            Assert.Equal(Path.Combine(rootPath, "dir1", "archive.zip"), navigationContext.CurrentPath);
            Assert.Equal(Path.Combine(rootPath, "dir1", "archive.zip", "dir2"), other.CurrentPath);
            Assert.Equal([Paths.Parent, "file1.txt"],
                (await other.ListEntriesAsync()).Select(entry => entry.Name));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_ChangeLocationToOtherContextInOtherVolumeProvider_Then_ProviderAndPathAreShown()
    {
        // arrange
        var volumeProvider = new TestVolumeProvider(volume =>
        {
            volume.AddEntry([], new TestEntry("dir1", EntryType.Container));
            volume.AddEntry(["dir1"], new TestEntry("file1.txt", EntryType.Item));
        });
        using var other = new NavigationContext(string.Empty);
        await other.ChangeVolumeProviderAsync(volumeProvider);
        await other.ChangeLocationAsync("dir1");
        using var navigationContext = new NavigationContext(Path.GetTempPath());

        // act
        await navigationContext.ChangeLocationAsync(other);
        var entries = await navigationContext.ListEntriesAsync();

        // assert
        Assert.Same(volumeProvider, navigationContext.VolumeProvider);
        Assert.Equal(other.CurrentPath, navigationContext.CurrentPath);
        Assert.Equal([Paths.Parent, "file1.txt"], entries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task When_ChangeLocationToOtherContextFails_Then_LocationIsUnchanged()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        var dirPath = Directory.CreateDirectory(Path.Combine(rootPath, "dir1")).FullName;
        try
        {
            using var other = new NavigationContext(dirPath);
            using var navigationContext = new NavigationContext(rootPath);
            Directory.Delete(dirPath);

            // act & assert
            await Assert.ThrowsAnyAsync<IOException>(() => navigationContext.ChangeLocationAsync(other));
            Assert.Equal(rootPath, navigationContext.CurrentPath);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(path);
        return path;
    }
}
