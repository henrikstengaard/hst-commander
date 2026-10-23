using System.Text;
using DiscUtils;
using DiscUtils.Fat;
using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Amiga;
using Hst.Commander.Plugins.Disk;

namespace Hst.Commander.Core.Tests;

public class GivenDiskFileVolumeProvider : IDisposable
{
    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    // a host with the rigid disk block provider of the Amiga plugin, opening PiStorm partitions and hybrid disks
    private readonly DiskFileVolumeProvider _provider = new(new PluginHost(() => [new RdbFileVolumeProvider()]));

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private static async Task<IReadOnlyList<IEntry>> ListAsync(IVolume volume) =>
        await volume.ListEntries().ToListAsync();

    private static async Task WriteTextAsync(IVolume volume, string name, string text)
    {
        await using var stream = await volume.OpenWrite(name);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(text));
    }

    private static async Task<string> ReadTextAsync(IVolume volume, string name)
    {
        await using var stream = await volume.OpenRead(name);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task When_DiskHasMbrOrGpt_Then_ItIsMatched()
    {
        // arrange
        await using var mbrDisk = DiskTestData.CreateMbrDisk();
        await using var gptDisk = DiskTestData.CreateGptDisk();

        // act
        var mbrMatch = await _provider.IsMatch("disk.img", mbrDisk);
        var gptMatch = await _provider.IsMatch("disk.img", gptDisk);

        // assert
        Assert.True(mbrMatch);
        Assert.True(gptMatch);
    }

    [Fact]
    public async Task When_DiskIsRdbFloppyOrEmpty_Then_ItIsNotMatched()
    {
        // arrange
        var rdbPath = Path.Combine(_rootPath, "disk.hdf");
        await AmigaTestData.CreateRdbImage(rdbPath, AmigaTestData.Dos3DosType);
        await using var rdbDisk = File.OpenRead(rdbPath);
        await using var floppy = new MemoryStream();
        FatFileSystem.FormatFloppy(floppy, FloppyDiskType.HighDensity, "FLOPPY").Dispose();
        floppy.Position = 0;
        await using var empty = new MemoryStream(new byte[4096]);

        // act
        var rdbMatch = await _provider.IsMatch("disk.hdf", rdbDisk);
        var floppyMatch = await _provider.IsMatch("floppy.img", floppy);
        var emptyMatch = await _provider.IsMatch("empty.img", empty);

        // assert
        Assert.False(rdbMatch);
        Assert.False(floppyMatch);
        Assert.False(emptyMatch);
    }

    [Fact]
    public async Task When_MbrDiskIsOpened_Then_PartitionsAreListedWithDetails()
    {
        // arrange
        await using var disk = DiskTestData.CreateMbrDisk();

        // act
        using var volume = (VolumeListVolume)await _provider.CreateVolume(disk);
        var tableEntries = await ListAsync(volume);
        await volume.ChangeDirectory(["mbr"]);
        var entries = await ListAsync(volume);

        // assert
        var tableEntry = Assert.Single(tableEntries);
        Assert.Equal("mbr", tableEntry.Name);
        Assert.Equal("MBR", tableEntry.DisplayName);
        Assert.Equal(EntryType.Container, tableEntry.Type);
        Assert.False(tableEntry.IsVolume);
        Assert.Equal("Partition table", tableEntry.Details!.TypeName);
        Assert.Contains(tableEntry.Details.Sections.SelectMany(section => section),
            property => property is { Label: "Partitions", Value: "1" });
        Assert.Equal(["mbr"], volume.CurrentPathComponents);
        var entry = Assert.Single(entries);
        Assert.Equal("1", entry.Name);
        Assert.Equal($"1 - {DiskTestData.FatLabel}", entry.DisplayName);
        Assert.Equal(EntryType.Container, entry.Type);
        Assert.True(entry.IsVolume);
        Assert.Equal(16 * 1024 * 1024, entry.Size);
        Assert.NotNull(entry.Details);
        Assert.Equal("MBR partition", entry.Details.TypeName);
        var properties = entry.Details.Sections.SelectMany(section => section).ToList();
        Assert.Contains(properties, property => property is { Label: "Volume name", Value: DiskTestData.FatLabel });
        Assert.Contains(properties, property => property is { Label: "Active", Value: "Yes" });
        Assert.Contains(properties, property => property.Label == "Capacity" && property.Bytes > 0);
        Assert.NotNull(entry.Details.UsedFraction);
    }

    [Fact]
    public async Task When_FilesAreWrittenToFatPartition_Then_TheyAreReadAfterOpeningAgain()
    {
        // arrange
        await using var disk = DiskTestData.CreateMbrDisk();

        // act
        using (var volume = (VolumeListVolume)await _provider.CreateVolume(disk))
        {
            await volume.ChangeDirectory(["mbr", "1"]);
            await volume.CreateContainer("dir1");
            await volume.ChangeDirectory(["dir1"]);
            await WriteTextAsync(volume, "a long file name.txt", "Hello");
            await volume.SetModifiedDate("a long file name.txt", new DateTime(2024, 5, 6, 7, 8, 10, DateTimeKind.Utc));
            Assert.Equal(["mbr", "1", "dir1"], volume.CurrentPathComponents);
            await volume.ChangeDirectory([Paths.Parent, Paths.Parent, Paths.Parent]);
            Assert.Empty(volume.CurrentPathComponents);
        }

        disk.Position = 0;
        using var reopened = (VolumeListVolume)await _provider.CreateVolume(disk);
        await reopened.ChangeDirectory(["mbr", "1", "DIR1"]);
        var entry = Assert.Single(await ListAsync(reopened));
        var text = await ReadTextAsync(reopened, "a long file name.txt");
        await reopened.Rename("a long file name.txt", "A LONG FILE NAME.TXT");
        var renamed = Assert.Single(await ListAsync(reopened));
        await reopened.Delete("A LONG FILE NAME.TXT");
        var entriesAfterDelete = await ListAsync(reopened);

        // assert
        Assert.Equal("a long file name.txt", entry.Name);
        Assert.Equal(5, entry.Size);
        Assert.Equal(new DateTime(2024, 5, 6, 7, 8, 10, DateTimeKind.Utc), entry.ModifiedDate!.Value,
            TimeSpan.FromSeconds(2));
        Assert.Equal("Hello", text);
        Assert.Equal("A LONG FILE NAME.TXT", renamed.Name);
        Assert.Empty(entriesAfterDelete);
    }

    [Theory]
    [InlineData(true, "GPT partition")]
    [InlineData(false, "MBR partition")]
    public async Task When_DiskHasLargeSectors_Then_FatPartitionIsMountedAndWritten(bool gpt, string typeName)
    {
        // arrange
        await using var disk = DiskTestData.CreateLargeSectorDisk(gpt);

        // act
        var isMatch = await _provider.IsMatch("disk.img", disk);
        disk.Position = 0;
        using var volume = (VolumeListVolume)await _provider.CreateVolume(disk);
        await volume.ChangeDirectory([typeName[..3].ToLowerInvariant()]);
        var entry = Assert.Single(await ListAsync(volume));
        await volume.ChangeDirectory(["1"]);
        await WriteTextAsync(volume, "file1.txt", "4Kn");
        var text = await ReadTextAsync(volume, "file1.txt");

        // assert
        Assert.True(isMatch);
        Assert.Equal($"1 - {DiskTestData.FatLabel}", entry.DisplayName);
        Assert.Equal(DiskTestData.LargeSectorPartitionSize, entry.Size);
        Assert.Equal(typeName, entry.Details!.TypeName);
        Assert.Contains(entry.Details.Sections.SelectMany(section => section),
            property => property is { Label: "Sector size", Value: "4096 bytes" });
        Assert.Equal("4Kn", text);
    }

    [Fact]
    public async Task When_SmallPartitionsFitWithLargeSectors_Then_SectorsOf512BytesAreUsed()
    {
        // arrange
        await using var disk = DiskTestData.CreateSmallPartitionMbrDisk();

        // act
        using var volume = (VolumeListVolume)await _provider.CreateVolume(disk);
        await volume.ChangeDirectory(["mbr"]);
        var entry = Assert.Single(await ListAsync(volume));

        // assert
        Assert.Equal($"1 - {DiskTestData.FatLabel}", entry.DisplayName);
        Assert.Equal(8 * 1024 * 1024, entry.Size);
        Assert.Contains(entry.Details!.Sections.SelectMany(section => section),
            property => property is { Label: "Sector size", Value: "512 bytes" });
    }

    [Fact]
    public async Task When_GptDiskIsOpened_Then_FatAndNtfsPartitionsAreMounted()
    {
        // arrange
        await using var disk = DiskTestData.CreateGptDisk();

        // act
        using var volume = (VolumeListVolume)await _provider.CreateVolume(disk);
        var tableEntries = await ListAsync(volume);
        await volume.ChangeDirectory(["gpt"]);
        var entries = await ListAsync(volume);
        await volume.ChangeDirectory(["2"]);
        await volume.CreateContainer("dir1");
        await WriteTextAsync(volume, "file1.txt", "NTFS");
        var ntfsNames = (await ListAsync(volume)).Select(entry => entry.Name).ToList();
        var text = await ReadTextAsync(volume, "file1.txt");

        // assert
        Assert.Equal(["gpt"], tableEntries.Select(entry => entry.Name));
        Assert.Equal(["1", "2"], entries.Select(entry => entry.Name));
        Assert.Equal($"1 - {DiskTestData.FatLabel}", entries[0].DisplayName);
        Assert.Equal($"2 - {DiskTestData.NtfsLabel}", entries[1].DisplayName);
        Assert.Equal("GPT partition", entries[0].Details!.TypeName);
        Assert.Contains(entries[0].Details!.Sections.SelectMany(section => section),
            property => property is { Label: "Name", Value: DiskTestData.GptFatName });
        Assert.Contains("dir1", ntfsNames);
        Assert.Contains("file1.txt", ntfsNames);
        Assert.Equal("NTFS", text);
    }

    [Theory]
    [InlineData(false, "mbr")]
    [InlineData(true, "gpt")]
    public async Task When_PiStormDiskIsOpened_Then_RdbPartitionIsOpenedByHost(bool gpt, string tableName)
    {
        // arrange
        await using var disk = gpt
            ? await DiskTestData.CreateGptPiStormDisk(AmigaTestData.Dos3DosType)
            : await DiskTestData.CreatePiStormDisk(AmigaTestData.Dos3DosType);

        // act
        using var volume = (VolumeListVolume)await _provider.CreateVolume(disk);
        await volume.ChangeDirectory([tableName]);
        var entries = await ListAsync(volume);
        await volume.ChangeDirectory(["2"]);
        var rdbTableEntries = await ListAsync(volume);
        await volume.ChangeDirectory(["rdb"]);
        var rdbEntries = await ListAsync(volume);
        await volume.ChangeDirectory(["1"]);
        await WriteTextAsync(volume, "file1.txt", "Amiga");
        var amigaNames = (await ListAsync(volume)).Select(entry => entry.Name).ToList();
        var pathComponents = volume.CurrentPathComponents.ToList();
        await volume.ChangeDirectory([Paths.Parent, Paths.Parent, Paths.Parent]);
        var mbrEntries = await ListAsync(volume);

        // assert
        Assert.Equal(["1", "2"], entries.Select(entry => entry.Name));
        Assert.Equal("2 - PiStorm RDB", entries[1].DisplayName);
        var properties = entries[1].Details!.Sections.SelectMany(section => section).ToList();
        Assert.Contains(properties, property => property is { Label: "File system", Value: "Rdb" });
        Assert.Contains(properties, property => property.Label == "Partition type" &&
                                                property.Value.StartsWith("PiStorm RDB (", StringComparison.Ordinal));
        Assert.Equal(["rdb"], rdbTableEntries.Select(entry => entry.Name));
        var rdbEntry = Assert.Single(rdbEntries);
        Assert.Equal("1", rdbEntry.Name);
        Assert.Equal("1 - DH0 - Workbench", rdbEntry.DisplayName);
        Assert.Equal(["file1.txt"], amigaNames);
        Assert.Equal([tableName, "2", "rdb", "1"], pathComponents);
        Assert.Equal([tableName], volume.CurrentPathComponents);
        Assert.Equal(2, mbrEntries.Count);
    }

    [Fact]
    public async Task When_PartitionCantBeMounted_Then_ItIsListedAndChangingToItFails()
    {
        // arrange
        var provider = new DiskFileVolumeProvider();
        await using var disk = await DiskTestData.CreatePiStormDisk(AmigaTestData.Dos3DosType);

        // act
        using var volume = (VolumeListVolume)await provider.CreateVolume(disk);
        await volume.ChangeDirectory(["mbr"]);
        var entries = await ListAsync(volume);

        // assert
        Assert.Equal("2 - PiStorm RDB", entries[1].DisplayName);
        Assert.DoesNotContain(entries[1].Details!.Sections.SelectMany(section => section),
            property => property.Label == "File system");
        await Assert.ThrowsAsync<IOException>(() => volume.ChangeDirectory(["2"]));
        Assert.Equal(["mbr"], volume.CurrentPathComponents);
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.CreateContainer("dir1"));
    }

    [Fact]
    public async Task When_HybridDiskIsOpened_Then_MbrAndRdbAreListed()
    {
        // arrange
        var path = Path.Combine(_rootPath, "hybrid.img");
        await DiskTestData.CreateHybridDisk(path, AmigaTestData.Dos3DosType);
        await using var disk = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);

        // act
        var isMatch = await _provider.IsMatch("hybrid.img", disk);
        disk.Position = 0;
        using var volume = (HybridDiskVolume)await _provider.CreateVolume(disk);
        var entries = await ListAsync(volume);
        await volume.ChangeDirectory(["mbr"]);
        var mbrEntries = await ListAsync(volume);
        await volume.ChangeDirectory([Paths.Root, "rdb", "1"]);
        await volume.CreateContainer("dir1");
        var amigaNames = (await ListAsync(volume)).Select(entry => entry.Name).ToList();
        var pathComponents = volume.CurrentPathComponents.ToList();
        await volume.ChangeDirectory([Paths.Parent, Paths.Parent]);
        var rootEntries = await ListAsync(volume);

        // assert
        Assert.True(isMatch);
        Assert.Equal(["mbr", "rdb"], entries.Select(entry => entry.Name));
        Assert.All(entries, entry => Assert.Equal("Partition table", entry.Details!.TypeName));
        Assert.Equal($"1 - {DiskTestData.FatLabel}", Assert.Single(mbrEntries).DisplayName);
        Assert.Equal(["dir1"], amigaNames);
        Assert.Equal(["rdb", "1"], pathComponents);
        Assert.Empty(volume.CurrentPathComponents);
        Assert.Equal(["mbr", "rdb"], rootEntries.Select(entry => entry.Name));
    }

    [Fact]
    public async Task When_HybridDiskIsOpenedWithoutHost_Then_OnlyMbrPartitionsAreListed()
    {
        // arrange
        var path = Path.Combine(_rootPath, "hybrid.img");
        await DiskTestData.CreateHybridDisk(path, AmigaTestData.Dos3DosType);
        await using var disk = File.OpenRead(path);

        // act
        using var volume = (VolumeListVolume)await new DiskFileVolumeProvider().CreateVolume(disk);
        var tableEntries = await ListAsync(volume);
        await volume.ChangeDirectory(["mbr"]);
        var entries = await ListAsync(volume);

        // assert
        Assert.Equal(["mbr"], tableEntries.Select(entry => entry.Name));
        Assert.Equal($"1 - {DiskTestData.FatLabel}", Assert.Single(entries).DisplayName);
    }
}
