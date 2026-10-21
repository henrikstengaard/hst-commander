using Hst.Amiga.FileSystems.FastFileSystem;
using Hst.Amiga.FileSystems.FastFileSystem.Blocks;
using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Amiga;
using Directory = System.IO.Directory;
using File = System.IO.File;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextCreatingVolume
{
    private readonly AdfVolumeCreator adfVolumeCreator = new();

    private readonly TestVolumeProvider volumeProvider = new(volume =>
    {
        volume.AddEntry([], new TestEntry("file1.txt", EntryType.Item, 5, data: "file1"u8.ToArray()));
    });

    [Fact]
    public void When_ListingCreators_Then_AdfVolumeCreatorOfAmigaPluginIsListed()
    {
        // act
        var creators = VolumeProviders.Creators;

        // assert
        Assert.Contains(creators, creator => creator is AdfVolumeCreator);
    }

    [Fact]
    public void When_PluginCanNotCreateVolumes_Then_VolumeCreatorsAreNotListed()
    {
        // arrange
        IPlugin plugin = new NoCreatePlugin();

        // act & assert
        Assert.False(plugin.CanCreateVolumes);
        Assert.DoesNotContain(VolumeProviders.Creators, creator => creator is TestVolumeCreator);
    }

    [Fact]
    public async Task When_CreateFormattedAdfInLocalDirectory_Then_AdfIsCreatedWithVolumeNameAndSelected()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            await navigationContext.ListEntriesAsync();
            var previousPath = navigationContext.CurrentPath;

            // act
            await navigationContext.CreateVolumeAsync(adfVolumeCreator, " disk ",
                AdfInput("Workbench", format: true, bootable: false));
            var entries = await navigationContext.ListEntriesAsync();
            var selectedIndex = navigationContext.GetSelectedIndex(entries, previousPath, 0);

            // assert
            var path = Path.Combine(rootPath, "disk.adf");
            Assert.Equal("disk.adf", entries[selectedIndex].Name);
            Assert.Equal(AmigaTestData.AdfSize, new FileInfo(path).Length);
            await using var stream = File.OpenRead(path);
            var volume = await FastFileSystemVolume.MountAdf(stream);
            Assert.Equal("Workbench", volume.Name);
            Assert.Empty(await volume.ListEntries());
            var bootBlock = new byte[1024];
            stream.Position = 0;
            await stream.ReadExactlyAsync(bootBlock);
            Assert.Equal("DOS\u0003"u8.ToArray(), bootBlock[..4]);
            Assert.All(bootBlock[12..], value => Assert.Equal(0, value));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_CreateBootableAdf_Then_BootBlockHasBootCodeAndValidChecksum()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        try
        {
            using var navigationContext = new NavigationContext(rootPath);

            // act
            await navigationContext.CreateVolumeAsync(adfVolumeCreator, "boot.adf",
                AdfInput("Boot", format: true, bootable: true));

            // assert
            var path = Path.Combine(rootPath, "boot.adf");
            var bootBlock = (await File.ReadAllBytesAsync(path))[..1024];
            Assert.Equal("DOS\u0003"u8.ToArray(), bootBlock[..4]);
            Assert.Equal(Hst.Amiga.FloppyDiskConstants.BootableBootBlockBytes,
                bootBlock[12..(12 + Hst.Amiga.FloppyDiskConstants.BootableBootBlockBytes.Length)]);
            var checksum = BootBlockChecksumHelper.CalculateChecksum(bootBlock);
            Assert.Equal(checksum, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bootBlock.AsSpan(4)));
            var volume = await FastFileSystemVolume.MountAdf(new MemoryStream(await File.ReadAllBytesAsync(path)));
            Assert.Equal("Boot", volume.Name);
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_CreateUnformattedAdf_Then_AdfIsEmpty()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        try
        {
            using var navigationContext = new NavigationContext(rootPath);

            // act
            await navigationContext.CreateVolumeAsync(adfVolumeCreator, "blank",
                AdfInput("Empty", format: false, bootable: true));

            // assert
            var data = await File.ReadAllBytesAsync(Path.Combine(rootPath, "blank.adf"));
            Assert.Equal(AmigaTestData.AdfSize, data.Length);
            Assert.All(data, value => Assert.Equal(0, value));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a:b")]
    [InlineData("1234567890123456789012345678901")]
    public async Task When_CreateAdfWithInvalidVolumeName_Then_IOExceptionIsThrownAndNoFileIsLeft(string volumeName)
    {
        // arrange
        var rootPath = CreateTempDirectory();
        try
        {
            using var navigationContext = new NavigationContext(rootPath);

            // act & assert
            await Assert.ThrowsAsync<IOException>(() => navigationContext.CreateVolumeAsync(adfVolumeCreator,
                "disk", AdfInput(volumeName, format: true, bootable: false)));
            Assert.Empty(Directory.GetFileSystemEntries(rootPath));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_CreateVolumeWithExistingName_Then_IOExceptionIsThrownAndFileIsKept()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        try
        {
            var path = Path.Combine(rootPath, "disk.adf");
            await File.WriteAllTextAsync(path, "keep");
            using var navigationContext = new NavigationContext(rootPath);

            // act & assert
            await Assert.ThrowsAsync<IOException>(() => navigationContext.CreateVolumeAsync(adfVolumeCreator,
                "disk", AdfInput("Empty", format: true, bootable: false)));
            Assert.Equal("keep", await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Fact]
    public async Task When_CreateVolumeInDriveList_Then_NotSupportedExceptionIsThrown()
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);

        // act & assert
        Assert.NotNull(navigationContext.GetCreateVolumeNotSupportedMessage([adfVolumeCreator]));
        await Assert.ThrowsAsync<NotSupportedException>(() => navigationContext.CreateVolumeAsync(adfVolumeCreator,
            "disk", AdfInput("Empty", format: true, bootable: false)));
    }

    [Fact]
    public async Task When_NoCreators_Then_CreateVolumeIsNotSupported()
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);

        // act & assert
        Assert.NotNull(navigationContext.GetCreateVolumeNotSupportedMessage([]));
        Assert.Null(navigationContext.GetCreateVolumeNotSupportedMessage([adfVolumeCreator]));
    }

    [Fact]
    public async Task When_CreateVolumeInVolume_Then_VolumeIsCopiedToVolumeAndSelected()
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);
        var previousPath = navigationContext.CurrentPath;
        await navigationContext.ListEntriesAsync();

        // act
        await navigationContext.CreateVolumeAsync(adfVolumeCreator, "disk.ADF",
            AdfInput("Empty", format: true, bootable: false));
        var entries = await navigationContext.ListEntriesAsync();
        var selectedIndex = navigationContext.GetSelectedIndex(entries, previousPath, 0);

        // assert
        Assert.Equal("disk.ADF", entries[selectedIndex].Name);
        await using var stream = await navigationContext.OpenReadAsync(entries[selectedIndex]);
        var data = new MemoryStream();
        await stream.CopyToAsync(data);
        Assert.Equal(AmigaTestData.AdfSize, data.Length);
    }

    [Fact]
    public async Task When_CreatorFailsInVolume_Then_PartialItemIsDeleted()
    {
        // arrange
        using var navigationContext = new NavigationContext(string.Empty);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);

        // act & assert
        await Assert.ThrowsAsync<IOException>(() => navigationContext.CreateVolumeAsync(new TestVolumeCreator(true),
            "fail", ValidTestInput));
        var entries = await navigationContext.ListEntriesAsync();
        Assert.DoesNotContain(entries, entry => entry.Name.StartsWith("fail", StringComparison.Ordinal));
    }

    [Fact]
    public async Task When_CreatorFailsInLocalDirectory_Then_PartialFileIsDeleted()
    {
        // arrange
        var rootPath = CreateTempDirectory();
        try
        {
            using var navigationContext = new NavigationContext(rootPath);

            // act & assert
            await Assert.ThrowsAsync<IOException>(() => navigationContext.CreateVolumeAsync(
                new TestVolumeCreator(true), "fail", ValidTestInput));
            Assert.Empty(Directory.GetFileSystemEntries(rootPath));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Theory]
    [InlineData("disk", "disk.test")]
    [InlineData("disk.TEST", "disk.TEST")]
    [InlineData(" disk.txt ", "disk.txt.test")]
    public void When_GetFileName_Then_ExtensionIsAddedWhenMissing(string name, string expectedName)
    {
        // act & assert
        Assert.Equal(expectedName, VolumeCreation.GetFileName(new TestVolumeCreator(false), name));
    }

    [Fact]
    public void When_GetValues_Then_InputIsConvertedAndDefaultsAreUsed()
    {
        // act
        var values = VolumeCreation.GetValues(new TestVolumeCreator(false), new Dictionary<string, object?>
        {
            ["Text"] = "  text  ",
            ["Number"] = " 42 "
        });

        // assert
        Assert.Equal("text", values.GetText("Text"));
        Assert.Equal(42, values.GetNumber("Number"));
        Assert.True(values.GetBoolean("Boolean"));
        Assert.Null(values.GetNumber("Optional"));
    }

    [Theory]
    [InlineData("", "1")]
    [InlineData("text", "")]
    [InlineData("text", "abc")]
    public void When_GetValuesWithMissingOrInvalidInput_Then_IOExceptionIsThrown(string text, string number)
    {
        // act & assert
        Assert.Throws<IOException>(() => VolumeCreation.GetValues(new TestVolumeCreator(false),
            new Dictionary<string, object?> { ["Text"] = text, ["Number"] = number }));
    }

    private static readonly Dictionary<string, object?> ValidTestInput = new() { ["Text"] = "a", ["Number"] = "1" };

    private static Dictionary<string, object?> AdfInput(string volumeName, bool format, bool bootable) => new()
    {
        [AdfVolumeCreator.VolumeNameField] = volumeName,
        [AdfVolumeCreator.FormatField] = format,
        [AdfVolumeCreator.BootableField] = bootable
    };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class NoCreatePlugin : IPlugin
    {
        public string Name => "No create";

        public IEnumerable<IVolumeCreator> VolumeCreators { get; } = [new TestVolumeCreator(false)];
    }

    private sealed class TestVolumeCreator(bool fail) : IVolumeCreator
    {
        public string Name => "Test";

        public string Description => "Test volumes";

        public string FileExtension => ".test";

        public IReadOnlyList<VolumeCreatorField> Fields { get; } =
        [
            new("Text", "Text", VolumeCreatorFieldType.Text, true),
            new("Number", "Number", VolumeCreatorFieldType.Number, true),
            new("Boolean", "Boolean", VolumeCreatorFieldType.Boolean, false, true),
            new("Optional", "Optional", VolumeCreatorFieldType.Number)
        ];

        public async Task Create(Stream stream, VolumeCreatorValues values,
            CancellationToken cancellationToken = default)
        {
            await stream.WriteAsync("partial"u8.ToArray(), cancellationToken);
            if (fail)
            {
                throw new IOException("Creating failed");
            }
        }
    }
}
