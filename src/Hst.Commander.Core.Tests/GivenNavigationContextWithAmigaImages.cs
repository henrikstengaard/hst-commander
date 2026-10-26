using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextWithAmigaImages : IDisposable
{
    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private static async Task OpenAsync(NavigationContext navigationContext, string name) =>
        await navigationContext.OpenEntryAsync(
            (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == name));

    private static async Task<IReadOnlyList<string>> ListNamesAsync(NavigationContext navigationContext) =>
        (await navigationContext.ListEntriesAsync()).Select(entry => entry.Name).ToList();

    /// <summary>True when the file is closed, so it can be opened without sharing.</summary>
    private static bool IsClosed(string path)
    {
        try
        {
            using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    [Fact]
    public async Task When_AdfIsChanged_Then_ChangesAreWrittenToTheFile()
    {
        // arrange
        var adfPath = Path.Combine(_rootPath, "disk.adf");
        await AmigaTestData.CreateAdf(adfPath);
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file1.txt"), "Amiga");
        var startedFiles = new List<string>();

        using (var adfContext = new NavigationContext(_rootPath, startedFiles.Add))
        using (var localContext = new NavigationContext(_rootPath))
        {
            // act
            await OpenAsync(adfContext, "disk.adf");
            Assert.Null(adfContext.CreateDirectoryNotSupportedMessage);
            Assert.Null(adfContext.MoveNotSupportedMessage);
            await adfContext.CreateDirectoryAsync("dir1");
            var localEntries = await localContext.ListEntriesAsync();
            await localContext.CopyAsync([localEntries.First(entry => entry.Name == "file1.txt")], adfContext,
                adfContext.CurrentPath, "*");
            var entries = await adfContext.ListEntriesAsync();
            await adfContext.RenameAsync(entries.First(entry => entry.Name == "dir1"), "Dir2");
            var names = await ListNamesAsync(adfContext);

            // assert
            Assert.Empty(startedFiles);
            Assert.True(adfContext.IsInImage);
            Assert.Equal([Paths.Parent, "Dir2", "file1.txt"], names);
            var copiedEntry = (await adfContext.ListEntriesAsync()).First(entry => entry.Name == "file1.txt");
            Assert.Equal(5, copiedEntry.Size);
            Assert.Equal(File.GetLastWriteTimeUtc(Path.Combine(_rootPath, "file1.txt")),
                copiedEntry.ModifiedDate!.Value, TimeSpan.FromSeconds(1));
            Assert.False(IsClosed(adfPath));
        }

        Assert.True(IsClosed(adfPath));
        Assert.Equal(["Dir2", "file1.txt"], await AmigaTestData.ListAdfNames(adfPath));
    }

    [Fact]
    public async Task When_ViewingFileLargerThanABlockInAdf_Then_ItIsReadAndStreamsReadIntoBufferOffsetsAndSeek()
    {
        // arrange: a file of many blocks, as the Amiga file system reads a block at a time
        var adfPath = Path.Combine(_rootPath, "disk.adf");
        await AmigaTestData.CreateAdf(adfPath);
        var data = Enumerable.Range(0, 100_000).Select(index => (byte)index).ToArray();
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "data.bin"), data);

        using var adfContext = new NavigationContext(_rootPath);
        using var localContext = new NavigationContext(_rootPath);
        await OpenAsync(adfContext, "disk.adf");
        var localEntries = await localContext.ListEntriesAsync();
        await localContext.CopyAsync([localEntries.First(entry => entry.Name == "data.bin")], adfContext,
            adfContext.CurrentPath, "*");
        var entry = (await adfContext.ListEntriesAsync()).First(e => e.Name == "data.bin");

        // act
        byte[] viewed;
        using (var session = await adfContext.OpenViewerAsync(entry))
        {
            viewed = session.Document.ReadBytes(70_000, 1000);
        }

        var buffer = new byte[2000];
        var afterSeek = new byte[1000];
        var afterSeekBack = new byte[1000];
        await using (var stream = await adfContext.OpenReadAsync(entry))
        {
            stream.ReadExactly(buffer, 1000, 1000);

            // past the 72 data blocks of the file header, into the extension blocks
            Assert.Equal(90_000, stream.Seek(90_000, SeekOrigin.Begin));
            stream.ReadExactly(afterSeek);
            stream.Position = 488 * 3;
            stream.ReadExactly(afterSeekBack);
        }

        // assert
        Assert.Equal(data[70_000..71_000], viewed);
        Assert.Equal(data[..1000], buffer[1000..]);
        Assert.Equal(data[90_000..91_000], afterSeek);
        Assert.Equal(data[(488 * 3)..(488 * 3 + 1000)], afterSeekBack);
    }

    [Fact]
    public async Task When_AdfIsOpenInBothPanes_Then_ChangesInOnePaneAreSeenInTheOther()
    {
        // arrange
        var adfPath = Path.Combine(_rootPath, "disk.adf");
        await AmigaTestData.CreateAdf(adfPath);
        using var leftContext = new NavigationContext(_rootPath);
        using var rightContext = new NavigationContext(_rootPath);
        await OpenAsync(leftContext, "disk.adf");
        await OpenAsync(rightContext, "disk.adf");

        // act
        await leftContext.CreateDirectoryAsync("dir1");
        await rightContext.CreateDirectoryAsync("dir2");
        await OpenAsync(leftContext, "dir1");
        await leftContext.CreateDirectoryAsync("dir3");
        var leftEntries = await leftContext.ListEntriesAsync();
        await leftContext.CopyAsync([leftEntries.First(entry => entry.Name == "dir3")], rightContext,
            rightContext.CurrentPath, "*");
        var rightNames = await ListNamesAsync(rightContext);
        var leftNames = await ListNamesAsync(leftContext);

        // assert
        Assert.Equal([Paths.Parent, "dir1", "dir2", "dir3"], rightNames);
        Assert.Equal([Paths.Parent, "dir3"], leftNames);
        Assert.Equal(Path.Combine(_rootPath, "disk.adf", "dir1"), leftContext.CurrentPath);
    }

    [Fact]
    public async Task When_EntriesAreDeletedAndMovedInAdf_Then_TheyAreRemoved()
    {
        // arrange
        var adfPath = Path.Combine(_rootPath, "disk.adf");
        await AmigaTestData.CreateAdf(adfPath);
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file1.txt"), "file1");
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file2.txt"), "file2");
        using var adfContext = new NavigationContext(_rootPath);
        using var localContext = new NavigationContext(_rootPath);
        await OpenAsync(adfContext, "disk.adf");
        await adfContext.CreateDirectoryAsync("dir1");
        var localEntries = await localContext.ListEntriesAsync();
        await localContext.MoveAsync(
            localEntries.Where(entry => entry.Name is "file1.txt" or "file2.txt").ToList(), adfContext,
            adfContext.CurrentPath, "*");

        // act
        var entries = await adfContext.ListEntriesAsync();
        await adfContext.DeleteAsync(entries.Where(entry => entry.Name is "dir1" or "file1.txt").ToList());
        entries = await adfContext.ListEntriesAsync();
        await adfContext.MoveAsync(entries.Where(entry => entry.Name == "file2.txt").ToList(), localContext,
            localContext.CurrentPath, "*");

        // assert
        Assert.Equal([Paths.Parent], await ListNamesAsync(adfContext));
        Assert.Equal("file2", await File.ReadAllTextAsync(Path.Combine(_rootPath, "file2.txt")));
        Assert.False(File.Exists(Path.Combine(_rootPath, "file1.txt")));
    }

    [Fact]
    public async Task When_AdfFileIsReadOnly_Then_AdfIsReadOnly()
    {
        // arrange
        var adfPath = Path.Combine(_rootPath, "disk.adf");
        await AmigaTestData.CreateAdf(adfPath);
        File.SetAttributes(adfPath, FileAttributes.ReadOnly);
        try
        {
            using var navigationContext = new NavigationContext(_rootPath);

            // act
            await OpenAsync(navigationContext, "disk.adf");

            // assert
            Assert.True(navigationContext.IsInImage);
            Assert.NotNull(navigationContext.CreateDirectoryNotSupportedMessage);
            Assert.NotNull(navigationContext.MoveNotSupportedMessage);
            await Assert.ThrowsAsync<NotSupportedException>(() => navigationContext.CreateDirectoryAsync("dir1"));
        }
        finally
        {
            File.SetAttributes(adfPath, FileAttributes.Normal);
        }
    }

    [Theory]
    [InlineData("disk.hdf", false)]
    [InlineData("disk.vhd", true)]
    public async Task When_RdbImageIsOpened_Then_PartitionsAreListedAndWritable(string name, bool isVhd)
    {
        // arrange
        var imagePath = Path.Combine(_rootPath, name);
        if (isVhd)
        {
            await AmigaTestData.CreateVhdRdbImage(imagePath, AmigaTestData.Dos3DosType, AmigaTestData.Pfs3DosType);
        }
        else
        {
            await AmigaTestData.CreateRdbImage(imagePath, AmigaTestData.Dos3DosType, AmigaTestData.Pfs3DosType);
        }

        var startedFiles = new List<string>();
        using (var navigationContext = new NavigationContext(_rootPath, startedFiles.Add))
        {
            // act
            await OpenAsync(navigationContext, name);
            var tableNames = await ListNamesAsync(navigationContext);
            await OpenAsync(navigationContext, "rdb");
            var partitionNames = await ListNamesAsync(navigationContext);
            var partitionEntries = await navigationContext.ListEntriesAsync();
            await OpenAsync(navigationContext, "1");
            await navigationContext.CreateDirectoryAsync("ffs");
            await navigationContext.GoParentAsync();
            await OpenAsync(navigationContext, "2");
            await navigationContext.CreateDirectoryAsync("pfs3");

            // assert
            Assert.Empty(startedFiles);
            Assert.Equal([Paths.Parent, "rdb"], tableNames);
            Assert.Equal([Paths.Parent, "1", "2"], partitionNames);
            Assert.All(partitionEntries.Skip(1), entry => Assert.Equal(EntryType.Container, entry.Type));
            Assert.Equal(["1 - DH0 - Workbench", "2 - DH1 - Work"], partitionEntries.Skip(1).Select(entry => entry.DisplayName));
            Assert.All(partitionEntries.Skip(1), entry => Assert.True(entry.IsVolume));
            Assert.Equal([Paths.Parent, "pfs3"], await ListNamesAsync(navigationContext));
            Assert.Equal(Path.Combine(imagePath, "rdb", "2"), navigationContext.CurrentPath);
        }

        // the changes are read again from the file
        using var reopenedContext = new NavigationContext(_rootPath);
        await OpenAsync(reopenedContext, name);
        await OpenAsync(reopenedContext, "rdb");
        await OpenAsync(reopenedContext, "1");
        Assert.Equal([Paths.Parent, "ffs"], await ListNamesAsync(reopenedContext));
        await reopenedContext.ChangeLocationAsync(Path.Combine(imagePath, "rdb", "2", "pfs3"));
        Assert.Equal(Path.Combine(imagePath, "rdb", "2", "pfs3"), reopenedContext.CurrentPath);
    }

    [Fact]
    public async Task When_PropertiesOfRdbPartitionAreShown_Then_PartitionDetailsAreShown()
    {
        // arrange
        var imagePath = Path.Combine(_rootPath, "disk.hdf");
        await AmigaTestData.CreateRdbImage(imagePath, AmigaTestData.Dos3DosType, AmigaTestData.Pfs3DosType);
        using var navigationContext = new NavigationContext(_rootPath);
        await OpenAsync(navigationContext, "disk.hdf");
        await OpenAsync(navigationContext, "rdb");
        var partitions = await navigationContext.ListEntriesAsync();

        // act
        var properties = navigationContext.GetProperties([partitions.First(entry => entry.Name == "2")]);

        // assert
        var lines = properties.GetSections().SelectMany(section => section)
            .ToDictionary(line => line.Label, line => line.Value);
        Assert.Equal("2 - DH1 - Work", properties.Name);
        Assert.Equal("Amiga partition", lines["Type"]);
        Assert.Equal("2", lines["Partition"]);
        Assert.Equal("DH1", lines["Drive name"]);
        Assert.Equal("Work", lines["Volume name"]);
        Assert.Equal("Professional File System 3 (PFS\\3)", lines["File system"]);
        Assert.Equal("No", lines["Bootable"]);
        Assert.Contains("bytes", lines["Free space"]);
        Assert.Contains("bytes", lines["Capacity"]);
        Assert.NotNull(properties.UsedFraction);
        Assert.InRange(properties.UsedFraction.Value, 0, 1);
    }

    [Fact]
    public async Task When_RdbPartitionCantBeMounted_Then_ItIsShownWithoutVolumeName()
    {
        // arrange
        var imagePath = Path.Combine(_rootPath, "disk.hdf");
        await AmigaTestData.CreateRdbImage(imagePath, AmigaTestData.Dos3DosType, AmigaTestData.SfsDosType);
        using var navigationContext = new NavigationContext(_rootPath);
        await OpenAsync(navigationContext, "disk.hdf");
        await OpenAsync(navigationContext, "rdb");

        // act
        var partitions = await navigationContext.ListEntriesAsync();
        var properties = navigationContext.GetProperties([partitions.First(entry => entry.Name == "2")]);

        // assert
        Assert.Equal(["1 - DH0 - Workbench", "2 - DH1"], partitions.Skip(1).Select(entry => entry.DisplayName));
        var lines = properties.GetSections().SelectMany(section => section)
            .ToDictionary(line => line.Label, line => line.Value);
        Assert.Equal("DH1", lines["Drive name"]);
        Assert.Equal("Smart File System (SFS\\0)", lines["File system"]);
        Assert.False(lines.ContainsKey("Volume name"));
        Assert.False(lines.ContainsKey("Free space"));
        Assert.Null(properties.UsedFraction);
    }

    [Fact]
    public async Task When_RdbPartitionHasDiskIcon_Then_IconIsReadFromInsideThePartition()
    {
        // arrange
        var imagePath = Path.Combine(_rootPath, "disk.hdf");
        await AmigaTestData.CreateRdbImage(imagePath, AmigaTestData.Dos3DosType, AmigaTestData.Pfs3DosType);
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "Disk.info"), await AmigaIconTestData.CreatePlanarIcon());
        using var localContext = new NavigationContext(_rootPath);
        using var rdbContext = new NavigationContext(_rootPath);
        await OpenAsync(rdbContext, "disk.hdf");
        await OpenAsync(rdbContext, "rdb");
        await OpenAsync(rdbContext, "1");
        var localEntries = await localContext.ListEntriesAsync();
        await localContext.CopyAsync([localEntries.First(entry => entry.Name == "Disk.info")], rdbContext,
            rdbContext.CurrentPath, "*");
        await rdbContext.GoParentAsync();
        var partitions = await rdbContext.ListEntriesAsync();

        // act
        var icon = await rdbContext.ReadIconAsync(partitions.First(entry => entry.Name == "1"), 16);
        var icons = await rdbContext.ReadIconsAsync(partitions.First(entry => entry.Name == "1"));
        var noIcon = await rdbContext.ReadIconAsync(partitions.First(entry => entry.Name == "2"), 16);

        // assert
        Assert.True(rdbContext.HasIcon(partitions.First(entry => entry.Name == "1")));
        Assert.Null(rdbContext.GetIconFile(partitions.First(entry => entry.Name == "1")));
        Assert.NotNull(icon);
        Assert.Equal(4, icon.Width);
        Assert.Equal(["Normal (planar)"], icons.Select(entryIcon => entryIcon.Name));
        Assert.Null(noIcon);
        // the pane stays in the partition list
        Assert.Equal(Path.Combine(imagePath, "rdb"), rdbContext.CurrentPath);
        Assert.Equal([Paths.Parent, "1", "2"], await ListNamesAsync(rdbContext));
    }

    [Fact]
    public async Task When_RdbPartitionIconsAreReadOnUiThread_Then_ItDoesNotDeadlock()
    {
        // arrange
        var imagePath = Path.Combine(_rootPath, "disk.hdf");
        await AmigaTestData.CreateRdbImage(imagePath, AmigaTestData.Dos3DosType, AmigaTestData.Pfs3DosType);

        // act and assert
        SingleThreadSynchronizationContext.Run(async () =>
        {
            using var navigationContext = new NavigationContext(_rootPath);
            await OpenAsync(navigationContext, "disk.hdf");
            await OpenAsync(navigationContext, "rdb");
            var partitions = await navigationContext.ListEntriesAsync();
            foreach (var partition in partitions.Skip(1))
            {
                Assert.Null(await navigationContext.ReadIconAsync(partition, 16));
            }

            // listing again closes the location icons were read from
            await navigationContext.ListEntriesAsync();
        }, TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task When_RdbImageIsClosedOnUiThread_Then_ItDoesNotDeadlock(bool writeFile)
    {
        // arrange
        var imagePath = Path.Combine(_rootPath, "disk.hdf");
        await AmigaTestData.CreateRdbImage(imagePath, AmigaTestData.Dos3DosType, AmigaTestData.Pfs3DosType);
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "file1.txt"), "Amiga");

        // act, like a front-end on its UI thread: closing PFS3 flushes it, which must not wait for the UI thread
        SingleThreadSynchronizationContext.Run(async () =>
        {
            using var navigationContext = new NavigationContext(_rootPath);
            using var localContext = new NavigationContext(_rootPath);
            await OpenAsync(navigationContext, "disk.hdf");
            await OpenAsync(navigationContext, "rdb");
            await OpenAsync(navigationContext, "2");
            if (writeFile)
            {
                var localEntries = await localContext.ListEntriesAsync();
                await localContext.CopyAsync([localEntries.First(entry => entry.Name == "file1.txt")],
                    navigationContext, navigationContext.CurrentPath, "*");
            }

            await navigationContext.GoParentAsync();
            await navigationContext.GoParentAsync();
            await navigationContext.GoParentAsync();

            // assert
            Assert.False(navigationContext.IsInImage);
            Assert.Equal(_rootPath, navigationContext.CurrentPath);
        }, TimeSpan.FromSeconds(30));

        Assert.True(IsClosed(imagePath));
    }

    [Fact]
    public async Task When_RdbHasNoPartitions_Then_OpeningItThrowsIOException()
    {
        // arrange
        var imagePath = Path.Combine(_rootPath, "disk.hdf");
        await AmigaTestData.CreateRdbImage(imagePath);
        var startedFiles = new List<string>();
        using var navigationContext = new NavigationContext(_rootPath, startedFiles.Add);
        var entry = (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "disk.hdf");

        // act
        var exception = await Assert.ThrowsAsync<IOException>(() => navigationContext.OpenEntryAsync(entry));

        // assert
        Assert.Contains("no partitions", exception.Message);
        Assert.Empty(startedFiles);
        Assert.False(navigationContext.IsInImage);
        Assert.True(IsClosed(imagePath));
    }

    [Fact]
    public async Task When_FileHasAdfSignatureButNotAdfSize_Then_FileIsStarted()
    {
        // arrange
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "notes.txt"), "DOS\u0003 not an adf"u8.ToArray());
        var startedFiles = new List<string>();
        using var navigationContext = new NavigationContext(_rootPath, startedFiles.Add);

        // act
        await OpenAsync(navigationContext, "notes.txt");

        // assert
        Assert.False(navigationContext.IsInImage);
        Assert.Equal([Path.Combine(_rootPath, "notes.txt")], startedFiles);
    }

    [Fact]
    public async Task When_CopyingBetweenAdfAndLocalDirectory_Then_UaeMetadataIsWrittenAndRead()
    {
        // arrange - an adf with an item with a name and protection bits that can't be stored in local directories
        var adfPath = Path.Combine(_rootPath, "disk.adf");
        await AmigaTestData.CreateAdf(adfPath);
        await using (var stream = File.Open(adfPath, FileMode.Open, FileAccess.ReadWrite))
        using (var volume = await Hst.Commander.Plugins.Amiga.AmigaFileSystems.MountAdf(stream))
        {
            await using (var itemStream = await volume.OpenWrite("file*1"))
            {
                await itemStream.WriteAsync("Amiga"u8.ToArray());
            }

            await volume.SetAmigaMetadata("file*1", new AmigaMetadata(0x40, "Comment"));
        }

        var localPath = Directory.CreateDirectory(Path.Combine(_rootPath, "local")).FullName;
        await AmigaTestData.CreateAdf(Path.Combine(_rootPath, "disk2.adf"));

        using var adfContext = new NavigationContext(_rootPath);
        using var localContext = new NavigationContext(localPath);
        await OpenAsync(adfContext, "disk.adf");

        // act - copy from the adf to the local directory and from there to another adf
        await adfContext.CopyAsync([(await adfContext.ListEntriesAsync()).First(entry => entry.Name == "file*1")],
            localContext, localContext.CurrentPath, "*");
        var localNames = await ListNamesAsync(localContext);
        var adf2Context = new NavigationContext(_rootPath);
        using (adf2Context)
        {
            await OpenAsync(adf2Context, "disk2.adf");
            await localContext.CopyAsync((await localContext.ListEntriesAsync()).Where(entry => entry.Name != Paths.Parent)
                .ToList(), adf2Context, adf2Context.CurrentPath, "*");
            var copiedEntries = await adf2Context.ListEntriesAsync();

            // assert
            Assert.Equal([Paths.Parent, "_UAEFSDB.___", "__uae___file_1"], localNames.Order(StringComparer.Ordinal));
            var copiedEntry = Assert.Single(copiedEntries, entry => entry.Name != Paths.Parent);
            Assert.Equal("file*1", copiedEntry.Name);
            Assert.Equal(new AmigaMetadata(0x40, "Comment"), copiedEntry.AmigaMetadata);
        }
    }

    /// <summary>Creates an adf with the item "file*1" with the script protection bit and a comment.</summary>
    private static async Task CreateAdfWithItemAsync(string adfPath)
    {
        await AmigaTestData.CreateAdf(adfPath);
        await using var stream = File.Open(adfPath, FileMode.Open, FileAccess.ReadWrite);
        using var volume = await Hst.Commander.Plugins.Amiga.AmigaFileSystems.MountAdf(stream);
        await using (var itemStream = await volume.OpenWrite("file*1"))
        {
            await itemStream.WriteAsync("Amiga"u8.ToArray());
        }

        await volume.SetAmigaMetadata("file*1", new AmigaMetadata(0x40, "Comment"));
    }

    private static Dictionary<string, string> GetLines(EntryProperties properties) =>
        properties.GetSections().SelectMany(section => section).ToDictionary(line => line.Label, line => line.Value);

    private static PropertyFlagGroup GetProtectionBits(EntryProperties properties) =>
        Assert.Single(properties.FlagGroups, group => group.Label == "Protection bits");

    /// <summary>The checked protection bits like the Amiga List command, e.g. "----rwed".</summary>
    private static string FormatFlags(PropertyFlagGroup group) =>
        string.Concat(group.Flags.Select(flag => flag.IsSet ? flag.Label : "-"));

    /// <summary>Check boxes of the protection bits checked for the letters, e.g. "-sp-rwe-".</summary>
    private static IReadOnlyList<IReadOnlyList<bool>> CheckProtectionBits(EntryProperties properties, string letters) =>
        properties.FlagGroups
            .Select(group => (IReadOnlyList<bool>)group.Flags
                .Select((flag, index) => group.Label == "Protection bits" ? letters[index] != '-' : flag.IsSet)
                .ToList())
            .ToList();

    [Fact]
    public async Task When_PropertiesOfAdfEntryAreShown_Then_ProtectionBitsAndCommentAreShown()
    {
        // arrange
        await CreateAdfWithItemAsync(Path.Combine(_rootPath, "disk.adf"));
        using var navigationContext = new NavigationContext(_rootPath);
        await OpenAsync(navigationContext, "disk.adf");
        var entry = (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "file*1");

        // act
        var properties = await navigationContext.GetPropertiesAsync([entry]);

        // assert
        var lines = GetLines(properties);
        Assert.Equal("-s--rwed", FormatFlags(GetProtectionBits(properties)));
        Assert.True(GetProtectionBits(properties).CanChange);
        Assert.Equal("Comment", lines["Comment"]);
        Assert.DoesNotContain("Amiga name", lines.Keys);
        Assert.DoesNotContain(properties.FlagGroups, group => group.Label == "Attributes");
    }

    [Fact]
    public async Task When_ProtectionBitsOfAdfEntryAreChanged_Then_TheyAreWrittenWithTheComment()
    {
        // arrange
        await CreateAdfWithItemAsync(Path.Combine(_rootPath, "disk.adf"));
        using var navigationContext = new NavigationContext(_rootPath);
        await OpenAsync(navigationContext, "disk.adf");
        var entry = (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "file*1");
        var properties = await navigationContext.GetPropertiesAsync([entry]);

        // act
        var changed = await navigationContext.ChangeFlagsAsync(properties, CheckProtectionBits(properties, "-sp-rwe-"));

        // assert
        Assert.True(changed);
        var changedEntry = (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == "file*1");
        Assert.Equal(new AmigaMetadata(0x61, "Comment"), changedEntry.AmigaMetadata);
        Assert.Equal("-sp-rwe-", FormatFlags(GetProtectionBits(await navigationContext.GetPropertiesAsync([changedEntry]))));
    }

    [Fact]
    public async Task When_PropertiesOfLhaEntryAreShown_Then_ProtectionBitsCantBeChanged()
    {
        // arrange
        File.Copy(ArchiveTestData.GetPath("special_chars.lha"), Path.Combine(_rootPath, "special_chars.lha"));
        using var navigationContext = new NavigationContext(_rootPath);
        await OpenAsync(navigationContext, "special_chars.lha");
        var entry = (await navigationContext.ListEntriesAsync()).First(entry => entry.Name != Paths.Parent);

        // act
        var properties = await navigationContext.GetPropertiesAsync([entry]);

        // assert
        Assert.False(GetProtectionBits(properties).CanChange);
        Assert.False(properties.CanChangeFlags);
    }

    [Theory]
    [InlineData(UaeMetadataFormat.UaeFsDb)]
    [InlineData(UaeMetadataFormat.UaeMetafile)]
    public async Task When_PropertiesOfLocalEntryWithUaeMetadataAreShown_Then_AmigaNameAndMetadataAreShown(
        UaeMetadataFormat format)
    {
        // arrange - an Amiga item copied to a local directory with UAE metadata
        await CreateAdfWithItemAsync(Path.Combine(_rootPath, "disk.adf"));
        var localPath = Directory.CreateDirectory(Path.Combine(_rootPath, "local")).FullName;
        using var adfContext = new NavigationContext(_rootPath) { UaeMetadata = format };
        using var localContext = new NavigationContext(localPath);
        await OpenAsync(adfContext, "disk.adf");
        await adfContext.CopyAsync([(await adfContext.ListEntriesAsync()).First(entry => entry.Name == "file*1")],
            localContext, localContext.CurrentPath, "*");
        var localEntries = await localContext.ListEntriesAsync();
        var localEntry = Assert.Single(localEntries, entry => entry.Name != Paths.Parent &&
                                                              entry.Name != "_UAEFSDB.___" &&
                                                              !entry.Name.EndsWith(".uaem"));
        var plainProperties = await localContext.GetPropertiesAsync(
            [localEntries.First(entry => entry.Name == Paths.Parent)]);

        // act
        var properties = await localContext.GetPropertiesAsync([localEntry]);

        // assert - the pane shows the normal name, the properties the Amiga name and metadata
        Assert.NotEqual("file*1", localEntry.Name);
        Assert.Equal(localEntry.Name, properties.Name);
        var lines = GetLines(properties);
        Assert.Equal("file*1", lines["Amiga name"]);
        Assert.Equal("-s--rwed", FormatFlags(GetProtectionBits(properties)));
        Assert.True(GetProtectionBits(properties).CanChange);
        Assert.Equal("Comment", lines["Comment"]);
        Assert.Contains(properties.FlagGroups, group => group.Label == "Attributes");
        Assert.DoesNotContain(plainProperties.FlagGroups, group => group.Label == "Protection bits");
    }

    [Theory]
    [InlineData(UaeMetadataFormat.UaeFsDb)]
    [InlineData(UaeMetadataFormat.UaeMetafile)]
    public async Task When_ProtectionBitsOfLocalEntryWithUaeMetadataAreChanged_Then_MetadataIsWritten(
        UaeMetadataFormat format)
    {
        // arrange - an Amiga item copied to a local directory with UAE metadata
        await CreateAdfWithItemAsync(Path.Combine(_rootPath, "disk.adf"));
        var localPath = Directory.CreateDirectory(Path.Combine(_rootPath, "local")).FullName;
        using var adfContext = new NavigationContext(_rootPath) { UaeMetadata = format };
        using var localContext = new NavigationContext(localPath);
        await OpenAsync(adfContext, "disk.adf");
        await adfContext.CopyAsync([(await adfContext.ListEntriesAsync()).First(entry => entry.Name == "file*1")],
            localContext, localContext.CurrentPath, "*");
        var localNames = await ListNamesAsync(localContext);
        var localEntry = (await localContext.ListEntriesAsync()).Single(entry => entry.Name != Paths.Parent &&
            entry.Name != "_UAEFSDB.___" && !entry.Name.EndsWith(".uaem"));
        var properties = await localContext.GetPropertiesAsync([localEntry]);

        // act
        var changed = await localContext.ChangeFlagsAsync(properties, CheckProtectionBits(properties, "-sp-rwe-"));

        // assert - the metadata is changed in the same metadata file
        Assert.True(changed);
        Assert.Equal(localNames, await ListNamesAsync(localContext));
        var changedProperties = await localContext.GetPropertiesAsync(
            [(await localContext.ListEntriesAsync()).Single(entry => entry.Name == localEntry.Name)]);
        var lines = GetLines(changedProperties);
        Assert.Equal("file*1", lines["Amiga name"]);
        Assert.Equal("Comment", lines["Comment"]);
        Assert.Equal("-sp-rwe-", FormatFlags(GetProtectionBits(changedProperties)));
    }

    [Theory]
    [InlineData("special_chars.lha")]
    [InlineData("special_chars.lzx")]
    public async Task When_CopyingFromLhaOrLzxArchive_Then_AmigaNamesAndMetadataAreKept(string name)
    {
        // arrange - archives from Hst Imager with names, protection bits and comments local directories can't store
        File.Copy(ArchiveTestData.GetPath(name), Path.Combine(_rootPath, name));
        await AmigaTestData.CreateAdf(Path.Combine(_rootPath, "disk.adf"));
        var localPath = Directory.CreateDirectory(Path.Combine(_rootPath, "local")).FullName;
        using var archiveContext = new NavigationContext(_rootPath);
        using var adfContext = new NavigationContext(_rootPath);
        using var localContext = new NavigationContext(localPath);
        await OpenAsync(archiveContext, name);
        await OpenAsync(adfContext, "disk.adf");
        var archiveEntries = (await archiveContext.ListEntriesAsync())
            .Where(entry => entry.Name != Paths.Parent)
            .ToList();

        // act
        await archiveContext.CopyAsync(archiveEntries, adfContext, adfContext.CurrentPath, "*");
        await archiveContext.CopyAsync(archiveEntries, localContext, localContext.CurrentPath, "*");

        // assert - copied to the adf as they are and to the local directory with UAE metadata
        var adfEntries = await adfContext.ListEntriesAsync();
        Assert.Equal(archiveEntries.Select(entry => entry.Name).Order(StringComparer.Ordinal),
            adfEntries.Where(entry => entry.Name != Paths.Parent).Select(entry => entry.Name)
                .Order(StringComparer.Ordinal));
        Assert.Equal(new AmigaMetadata(0x40, ""), adfEntries.First(entry => entry.Name == "file1*").AmigaMetadata);
        Assert.Equal(new AmigaMetadata(0, "comment on file2"),
            adfEntries.First(entry => entry.Name == "file2<").AmigaMetadata);
        Assert.Equal(new AmigaMetadata(0x40, ""), adfEntries.First(entry => entry.Name == "dir2").AmigaMetadata);

        var localEntries = await localContext.ListEntriesAsync();
        Assert.Contains(localEntries, entry => entry.Name == "_UAEFSDB.___");
        Assert.DoesNotContain(localEntries, entry => entry.Name.IndexOfAny(['*', '<']) >= 0);
        var lines = GetLines(await localContext.GetPropertiesAsync(
            [localEntries.First(entry => entry.Name.StartsWith("__uae___file2"))]));
        Assert.Equal("file2<", lines["Amiga name"]);
        Assert.Equal("comment on file2", lines["Comment"]);
    }
}
