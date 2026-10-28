using Hst.Commander.Plugins.PhysicalDisk;

namespace Hst.Commander.Core.Tests;

public class GivenStartupOptions : IDisposable
{
    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void When_OptionsAreConvertedToArguments_Then_TheyAreParsedAgain()
    {
        // arrange
        var options = new StartupOptions
        {
            Left = new PaneStartup(null, @"C:\dir with spaces\"),
            Right = new PaneStartup(PhysicalDiskVolume.VolumeName, null),
            IsRightActive = true
        };

        // act
        var arguments = options.ToArguments();
        var parsed = StartupOptions.Parse(arguments);

        // assert
        Assert.Equal(options, parsed);
    }

    [Fact]
    public void When_ArgumentsAreUnknown_Then_TheyAreIgnored()
    {
        // act
        var options = StartupOptions.Parse(["--unknown", "--left-path", "/tmp", "--right-volume"]);

        // assert
        Assert.Equal(new PaneStartup(null, "/tmp"), options.Left);
        Assert.Equal(new PaneStartup(null, null), options.Right);
        Assert.False(options.IsRightActive);
    }

    [Fact]
    public void When_OptionsAreCreatedFromPanes_Then_ThePaneChangingVolumeProviderStartsAtItsRoot()
    {
        // arrange
        using var left = new NavigationContext(_rootPath);
        using var right = new NavigationContext(_rootPath);
        var volumeProvider = new PhysicalDiskVolumeProvider(new TestPhysicalDisks());

        // act
        var options = StartupOptions.FromPanes(left, right, true, true, volumeProvider);

        // assert
        Assert.Equal(new PaneStartup(null, _rootPath), options.Left);
        Assert.Equal(new PaneStartup(PhysicalDiskVolume.VolumeName, null), options.Right);
        Assert.True(options.IsRightActive);
    }

    [Fact]
    public async Task When_PaneStartupIsApplied_Then_PaneShowsTheVolumeProviderAndLocation()
    {
        // arrange
        Directory.CreateDirectory(Path.Combine(_rootPath, "dir1"));
        using var local = new NavigationContext(string.Empty);
        using var physicalDisks = new NavigationContext(_rootPath);

        // act
        await new PaneStartup(null, Path.Combine(_rootPath, "dir1")).ApplyAsync(local);
        await new PaneStartup(PhysicalDiskVolume.VolumeName, null).ApplyAsync(physicalDisks);

        // assert
        Assert.Equal(Path.Combine(_rootPath, "dir1"), local.CurrentPath);
        if (PhysicalDisks.ForCurrentPlatform() != null)
        {
            Assert.Equal(PhysicalDiskVolume.VolumeName, physicalDisks.CurrentPath);
            Assert.True(physicalDisks.VolumeProvider.RequiresAdministrator);
        }
    }

    [Fact]
    public async Task When_PaneStartupLocationDoesNotExist_Then_PaneStaysWhereItIs()
    {
        // arrange
        using var navigationContext = new NavigationContext(_rootPath);

        // act
        await new PaneStartup("No such volumes", Path.Combine(_rootPath, "missing")).ApplyAsync(navigationContext);

        // assert
        Assert.Equal(_rootPath, navigationContext.CurrentPath);
    }

    [Theory]
    [InlineData(new[] { "--left-path", @"C:\" }, @"--left-path C:\")]
    [InlineData(new[] { "--left-path", @"C:\my dir\" }, @"--left-path ""C:\my dir\\""")]
    [InlineData(new[] { "a\"b", "" }, @"""a\""b"" """"")]
    public void When_ArgumentsAreJoinedForWindows_Then_TheyAreQuoted(string[] arguments, string expected) =>
        Assert.Equal(expected, Elevation.JoinWindowsArguments(arguments));
}
