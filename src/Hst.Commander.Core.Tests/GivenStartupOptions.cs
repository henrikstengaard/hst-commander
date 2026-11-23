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

    private static readonly PaneLocations Saved = new(new PaneStartup(null, "/saved/left"),
        new PaneStartup(PhysicalDiskVolume.VolumeName, "PhysicalDrive1"), true);

    [Fact]
    public void When_GuiRestoresSavedPanes_Then_BothPanesAndActivePaneAreRestored()
    {
        // act
        var restored = StartupOptions.Parse([]).WithSavedPanes(Saved, true, "/home/me");

        // assert
        Assert.Equal(Saved.Left, restored.Left);
        Assert.Equal(Saved.Right, restored.Right);
        Assert.True(restored.IsRightActive);
    }

    [Fact]
    public void When_TuiRestoresSavedPanes_Then_OnlyRightPaneIsRestored()
    {
        // act
        var restored = StartupOptions.Parse([]).WithSavedPanes(Saved, false, "/home/me");

        // assert - the left pane stays in the current directory, and is active
        Assert.Equal(new PaneStartup(null, null), restored.Left);
        Assert.Equal(Saved.Right, restored.Right);
        Assert.False(restored.IsRightActive);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void When_PanesAreGiven_Then_SavedPanesAreNotRestored(bool restoreLeft)
    {
        // arrange - e.g. restarted as administrator
        var options = StartupOptions.Parse(["--left-path", "/elevated"]);

        // act
        var restored = options.WithSavedPanes(Saved, restoreLeft, "/home/me");

        // assert
        Assert.Equal(options, restored);
    }

    [Fact]
    public void When_PaneWasOnServer_Then_ItStartsInHomeDirectory()
    {
        // arrange - a pane connected to an SMB server is saved without a location, as it needs the password
        var saved = new PaneLocations(new PaneStartup(null, null), new PaneStartup(null, null), false);

        // act
        var restored = StartupOptions.Parse([]).WithSavedPanes(saved, true, "/home/me");

        // assert
        Assert.Equal(new PaneStartup(null, "/home/me"), restored.Left);
        Assert.Equal(new PaneStartup(null, "/home/me"), restored.Right);
    }

    [Fact]
    public void When_NothingWasSaved_Then_PanesStartInCurrentDirectory()
    {
        // arrange
        var options = StartupOptions.Parse([]);

        // act
        var restored = options.WithSavedPanes(null, true, "/home/me");

        // assert
        Assert.Equal(options, restored);
    }

    [Fact]
    public void When_PaneLocationsAreSavedAndLoaded_Then_TheyAreTheSame()
    {
        // arrange
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };

        // act
        var json = System.Text.Json.JsonSerializer.Serialize(Saved, options);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<PaneLocations>(json, options);

        // assert
        Assert.Equal(Saved, loaded);
        Assert.DoesNotContain("IsEmpty", json);
    }

    [Fact]
    public void When_PaneLocationsAreTakenFromPanes_Then_LocalPaneHasItsPath()
    {
        // arrange
        using var left = new NavigationContext(_rootPath);
        using var right = new NavigationContext(_rootPath);

        // act
        var locations = PaneLocations.From(left, right, false);

        // assert
        Assert.Equal(new PaneLocations(new PaneStartup(null, _rootPath), new PaneStartup(null, _rootPath), false),
            locations);
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

    [Fact]
    public void When_AdministratorScriptIsMadeForMacOs_Then_ProgramAndArgumentsAreQuotedForShellAndAppleScript()
    {
        // act
        var script = Elevation.GetAdministratorScript("/Applications/Hst Commander.app/Contents/MacOS/hst.commander.gui",
            ["--left-path", "/Users/me/it's \"here\"", "--active", "right"]);

        // assert
        Assert.StartsWith(
            "do shell script \"'/Applications/Hst Commander.app/Contents/MacOS/hst.commander.gui' '--left-path' " +
            "'/Users/me/it'\\\\''s \\\"here\\\"' '--active' 'right' > /dev/null 2>&1 &\" with prompt \"", script);
        Assert.EndsWith("\" with administrator privileges", script);
    }
}
