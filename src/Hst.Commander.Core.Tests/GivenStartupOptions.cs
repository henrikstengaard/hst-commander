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

    private static string? NoEnvironmentVariable(string name) => null;

    [Theory]
    [InlineData(new[] { "--restore-panes" }, null, true)]
    [InlineData(new[] { "--left-path", "/tmp", "--restore-panes" }, null, true)]
    [InlineData(new string[0], "1", true)]
    [InlineData(new string[0], null, false)]
    [InlineData(new string[0], "0", false)]
    public void When_ArgumentsOrEnvironmentMarkAShortcut_Then_LaunchedFromShortcutIsTrue(string[] args,
        string? launchedFromApp, bool expected)
    {
        // act
        var options = StartupOptions.Parse(args,
            name => name == StartupOptions.LaunchedFromAppVariable ? launchedFromApp : null);

        // assert
        Assert.Equal(expected, options.IsLaunchedFromShortcut);
    }

    [Fact]
    public void When_RestorePanesIsFollowedByAnOption_Then_TheOptionIsParsed()
    {
        // act
        var options = StartupOptions.Parse(["--restore-panes", "--right-path", "/tmp"], NoEnvironmentVariable);

        // assert
        Assert.Equal(new PaneStartup(null, "/tmp"), options.Right);
        Assert.DoesNotContain(StartupOptions.RestorePanesOption, options.ToArguments());
    }

    [Fact]
    public void When_LaunchedFromShortcut_Then_BothPanesAndActivePaneAreRestored()
    {
        // arrange
        var options = StartupOptions.Parse(["--restore-panes"], NoEnvironmentVariable);

        // act
        var restored = options.WithSavedPanes(Saved, "/home/me");

        // assert
        Assert.Equal(Saved.Left, restored.Left);
        Assert.Equal(Saved.Right, restored.Right);
        Assert.True(restored.IsRightActive);
        Assert.Equal("/home/me", restored.GetInitialDirectory("/opt/app", "/home/me"));
    }

    [Fact]
    public void When_LaunchedFromTerminal_Then_LeftPaneIsCurrentDirectoryAndRightPaneIsRestored()
    {
        // arrange
        var options = StartupOptions.Parse([], NoEnvironmentVariable);

        // act
        var restored = options.WithSavedPanes(Saved, "/home/me");

        // assert
        Assert.Equal(new PaneStartup(null, null), restored.Left);
        Assert.Equal(Saved.Right, restored.Right);
        Assert.False(restored.IsRightActive);
        Assert.Equal("/work", restored.GetInitialDirectory("/work", "/home/me"));
    }

    [Fact]
    public void When_PanesAreGiven_Then_SavedPanesAreNotRestored()
    {
        // arrange - e.g. restarted as administrator from a shortcut start
        var options = StartupOptions.Parse(["--restore-panes", "--left-path", "/elevated"], NoEnvironmentVariable);

        // act
        var restored = options.WithSavedPanes(Saved, "/home/me");

        // assert
        Assert.Equal(options, restored);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void When_PaneWasOnServer_Then_ItStartsInHomeDirectory(bool fromShortcut)
    {
        // arrange - a pane connected to an SMB server is saved without a location, as it needs the password
        var saved = new PaneLocations(new PaneStartup(null, null), new PaneStartup(null, null), false);
        var options = new StartupOptions { IsLaunchedFromShortcut = fromShortcut };

        // act
        var restored = options.WithSavedPanes(saved, "/home/me");

        // assert
        Assert.Equal(new PaneStartup(null, fromShortcut ? "/home/me" : null), restored.Left);
        Assert.Equal(new PaneStartup(null, "/home/me"), restored.Right);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void When_NothingWasSaved_Then_ShortcutStartsInHomeAndTerminalInCurrentDirectory(bool fromShortcut)
    {
        // arrange
        var options = new StartupOptions { IsLaunchedFromShortcut = fromShortcut };

        // act
        var restored = options.WithSavedPanes(null, "/home/me");

        // assert
        var expected = new PaneStartup(null, fromShortcut ? "/home/me" : null);
        Assert.Equal(expected, restored.Left);
        Assert.Equal(expected, restored.Right);
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
