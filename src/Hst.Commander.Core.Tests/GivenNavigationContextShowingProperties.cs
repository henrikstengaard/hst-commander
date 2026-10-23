using Hst.Commander.Core.Volumes;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextShowingProperties
{
    private static readonly DateTime ModifiedDate = new(2026, 10, 15, 12, 30, 0, DateTimeKind.Utc);

    private readonly TestVolumeProvider volumeProvider = new(volume =>
    {
        volume.AddEntry([], new TestEntry("dir1", EntryType.Container, 0, ModifiedDate));
        volume.AddEntry(["dir1"], new TestEntry("a.txt", EntryType.Item, 100));
        volume.AddEntry(["dir1"], new TestEntry("dir2", EntryType.Container));
        volume.AddEntry(["dir1", "dir2"], new TestEntry("b.txt", EntryType.Item, 200));
        volume.AddEntry([], new TestEntry("file1.txt", EntryType.Item, 5, ModifiedDate));
    });

    [Theory]
    [InlineData(0x00, "----rwed")]
    [InlineData(0x40, "-s--rwed")]
    [InlineData(0x02, "----rw-d")]
    [InlineData(0x0f, "--------")]
    [InlineData(0xf0, "hsparwed")]
    public void When_ProtectionBitsAreFormatted_Then_TheyAreShownLikeTheAmigaListCommand(int protectionBits,
        string expected) =>
        Assert.Equal(expected, EntryProperties.FormatProtectionBits(protectionBits));

    [Fact]
    public async Task When_GetPropertiesOfFileInVolume_Then_TypeLocationSizeAndModifiedAreShown()
    {
        // arrange
        using var navigationContext = await CreateVolumeContextAsync();
        var entries = await navigationContext.ListEntriesAsync();
        var file = entries.Single(entry => entry.Name == "file1.txt");

        // act
        var properties = navigationContext.GetProperties([file]);
        var contents = await navigationContext.CountContentsAsync([file]);
        var lines = GetLines(properties, contents);

        // assert
        Assert.Equal("file1.txt", properties.Name);
        Assert.True(properties.HasContents);
        Assert.Null(properties.UsedFraction);
        Assert.EndsWith("(.txt)", lines["Type"]);
        Assert.Equal(navigationContext.CurrentPath, lines["Location"]);
        Assert.Equal("5 bytes", lines["Size"]);
        Assert.Equal(EntryProperties.FormatDate(ModifiedDate), lines["Modified"]);
        Assert.False(lines.ContainsKey("Contains"));
        Assert.False(lines.ContainsKey("Size on disk"));
        Assert.False(lines.ContainsKey("Attributes"));
    }

    [Fact]
    public async Task When_CountContentsOfDirectoryInVolume_Then_ContentsAreCountedRecursively()
    {
        // arrange
        using var navigationContext = await CreateVolumeContextAsync();
        var entries = await navigationContext.ListEntriesAsync();
        var directory = entries.Single(entry => entry.Name == "dir1");
        var reported = new List<EntryContents>();

        // act
        var properties = navigationContext.GetProperties([directory]);
        var contents = await navigationContext.CountContentsAsync([directory], new SyncProgress<EntryContents>(reported.Add));
        var lines = GetLines(properties, contents);

        // assert
        Assert.Equal(new EntryContents(300, null, 2, 1, true), contents);
        Assert.Equal(contents, reported[^1]);
        Assert.Equal("300 bytes", lines["Size"]);
        Assert.Equal("2 Files, 1 Folder", lines["Contains"]);
    }

    [Fact]
    public async Task When_CountContentsOfMultipleEntries_Then_EntriesAreCountedToo()
    {
        // arrange
        using var navigationContext = await CreateVolumeContextAsync();
        var entries = (await navigationContext.ListEntriesAsync())
            .Where(entry => entry.Name != Paths.Parent)
            .ToList();

        // act
        var properties = navigationContext.GetProperties(entries);
        var contents = await navigationContext.CountContentsAsync(entries);
        var lines = GetLines(properties, contents);

        // assert
        Assert.Equal("1 File, 1 Folder", properties.Name);
        Assert.Equal("Multiple types", lines["Type"]);
        Assert.Equal($"All in {navigationContext.CurrentPath}", lines["Location"]);
        Assert.Equal("305 bytes", lines["Size"]);
        Assert.Equal("3 Files, 2 Folders", lines["Contains"]);
    }

    [Fact]
    public async Task When_GetSectionsBeforeCounting_Then_ContentsAreCalculating()
    {
        // arrange
        using var navigationContext = await CreateVolumeContextAsync();
        var entries = await navigationContext.ListEntriesAsync();
        var directory = entries.Single(entry => entry.Name == "dir1");

        // act
        var properties = navigationContext.GetProperties([directory]);
        var lines = GetLines(properties, null);
        var countedLines = GetLines(properties, new EntryContents(1, null, 1, 0, false));

        // assert
        Assert.Equal(EntryProperties.Calculating, lines["Size"]);
        Assert.Equal(EntryProperties.Calculating, lines["Contains"]);
        Assert.Equal(lines.Keys, countedLines.Keys);
    }

    [Fact]
    public async Task When_GetPropertiesOfLocalDirectory_Then_DatesAttributesAndContentsAreShown()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(rootPath, "dir1", "dir2"));
            await File.WriteAllBytesAsync(Path.Combine(rootPath, "dir1", "a.bin"), new byte[1000]);
            await File.WriteAllBytesAsync(Path.Combine(rootPath, "dir1", "dir2", "b.bin"), new byte[24]);
            using var navigationContext = new NavigationContext(rootPath);
            var entries = await navigationContext.ListEntriesAsync();
            var directory = entries.Single(entry => entry.Name == "dir1");

            // act
            var properties = navigationContext.GetProperties([directory]);
            var contents = await navigationContext.CountContentsAsync([directory]);
            var lines = GetLines(properties, contents);

            // assert
            Assert.Equal("dir1", properties.Name);
            Assert.Equal(rootPath, lines["Location"]);
            Assert.Equal("1,024 bytes", lines["Size"].Split('(', ')')[1]);
            Assert.Equal("2 Files, 1 Folder", lines["Contains"]);
            Assert.Contains("Created", lines.Keys);
            Assert.Contains("Modified", lines.Keys);
            Assert.Contains("Accessed", lines.Keys);
            Assert.Contains("Attributes", lines.Keys);
            Assert.Equal(OperatingSystem.IsWindows(), lines.ContainsKey("Size on disk"));
            Assert.Equal(!OperatingSystem.IsWindows(), lines.ContainsKey("Permissions"));
            if (OperatingSystem.IsWindows())
            {
                Assert.True(contents.SizeOnDisk >= 0);
            }
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_GetPropertiesOfDrive_Then_SpaceIsShown()
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);
        var drive = DriveInfo.GetDrives().First(drive => drive.IsReady);
        var entries = await navigationContext.ListEntriesAsync();
        var entry = entries.Single(entry => entry.Name == drive.Name);

        // act
        var properties = navigationContext.GetProperties([entry]);
        var lines = GetLines(properties, null);

        // assert
        Assert.False(properties.HasContents);
        Assert.NotNull(properties.UsedFraction);
        Assert.Equal(drive.Name, lines["Drive"]);
        Assert.Equal(drive.DriveFormat, lines["File system"]);
        Assert.Contains("Capacity", lines.Keys);
        Assert.Contains("Free space", lines.Keys);
    }

    [Theory]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead |
                UnixFileMode.OtherExecute, "rwxr-xr-x (755)")]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead, "rw-r----- (640)")]
    public void When_FormatUnixMode_Then_PermissionsAndOctalModeAreReturned(UnixFileMode mode, string expected) =>
        Assert.Equal(expected, FileSystemDetails.FormatUnixMode(mode));

    [Fact]
    public void When_FormatAttributes_Then_SetAttributesAreListed()
    {
        Assert.Equal("Read-only, Hidden", FileSystemDetails.FormatAttributes(FileAttributes.ReadOnly | FileAttributes.Hidden));
        Assert.Equal("None", FileSystemDetails.FormatAttributes(FileAttributes.Normal));
    }

    private async Task<NavigationContext> CreateVolumeContextAsync()
    {
        var navigationContext = new NavigationContext(string.Empty);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);
        return navigationContext;
    }

    private static Dictionary<string, string> GetLines(EntryProperties properties, EntryContents? contents) =>
        properties.GetSections(contents)
            .SelectMany(section => section)
            .ToDictionary(line => line.Label, line => line.Value);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(path);
        return Path.GetFullPath(path);
    }

    /// <summary>Progress calling the handler on the reporting thread, so reports are received before awaiting ends.</summary>
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
