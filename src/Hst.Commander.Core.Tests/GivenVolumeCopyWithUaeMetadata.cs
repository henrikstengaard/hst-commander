using System.Text;
using Hst.Amiga.DataTypes.UaeFsDbs;
using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Plugins.Amiga;

namespace Hst.Commander.Core.Tests;

public class GivenVolumeCopyWithUaeMetadata : IDisposable
{
    // "-s--rwed": script flag set, reading, writing, executing and deleting allowed
    private static readonly AmigaMetadata ScriptMetadata = new(0x40, "Comment 1");

    // "----rw-d": executing not allowed
    private static readonly AmigaMetadata NotExecutableMetadata = new(0x02, "Comment 2");

    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    private readonly Stack<IDisposable> _disposables = new();

    public void Dispose()
    {
        while (_disposables.Count > 0)
        {
            _disposables.Pop().Dispose();
        }

        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private async Task<AmigaFileSystemVolume> CreateAdfVolumeAsync(string name)
    {
        var path = Path.Combine(_rootPath, name);
        await AmigaTestData.CreateAdf(path);
        var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite);
        _disposables.Push(stream);
        var volume = await AmigaFileSystems.MountAdf(stream);
        _disposables.Push(volume);
        return volume;
    }

    private LocalVolume CreateLocalVolume(string name) =>
        new(Directory.CreateDirectory(Path.Combine(_rootPath, name)).FullName);

    /// <summary>Creates an adf with an item with a special name, a directory with an item and a plain item.</summary>
    private async Task<AmigaFileSystemVolume> CreateAdfWithEntriesAsync(string name)
    {
        var volume = await CreateAdfVolumeAsync(name);
        await WriteItemAsync(volume, "file*1", "Amiga", ScriptMetadata);
        await WriteItemAsync(volume, "plain.txt", "Plain", null);
        await volume.CreateContainer("dir#1");
        await volume.SetAmigaMetadata("dir#1", new AmigaMetadata(0, "Directory"));
        await volume.ChangeDirectory(["dir#1"]);
        await WriteItemAsync(volume, "a?b", "Inside", NotExecutableMetadata);
        await volume.ChangeDirectory([Paths.Parent]);
        return volume;
    }

    private static async Task WriteItemAsync(IVolume volume, string name, string content, AmigaMetadata? metadata)
    {
        await using (var stream = await volume.OpenWrite(name))
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(content));
        }

        if (metadata != null)
        {
            await volume.SetAmigaMetadata(name, metadata);
        }
    }

    private static async Task<string> ReadItemAsync(IVolume volume, string name)
    {
        await using var stream = await volume.OpenRead(name);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static async Task<List<IEntry>> ListAsync(IVolume volume) => await volume.ListEntries().ToListAsync();

    private static async Task<IEntry> GetEntryAsync(IVolume volume, string name) =>
        (await ListAsync(volume)).Single(entry => entry.Name == name);

    private static IReadOnlyList<string> ListLocalNames(string path) =>
        Directory.EnumerateFileSystemEntries(path).Select(Path.GetFileName).OfType<string>()
            .Order(StringComparer.Ordinal).ToList();

    private static async Task<List<UaeFsDbNode>> ReadFsDbNodesAsync(string directoryPath) =>
        (await UaeFsDbReader.ReadFromFile(Path.Combine(directoryPath, "_UAEFSDB.___"))).ToList();

    /// <summary>Asserts the adf has the entries of <see cref="CreateAdfWithEntriesAsync" /> with their metadata.</summary>
    private static async Task AssertAdfEntriesAsync(IVolume volume)
    {
        Assert.Equal(["dir#1", "file*1", "plain.txt"],
            (await ListAsync(volume)).Select(entry => entry.Name).Order(StringComparer.Ordinal));
        Assert.Equal(ScriptMetadata, (await GetEntryAsync(volume, "file*1")).AmigaMetadata);
        Assert.Equal("Amiga", await ReadItemAsync(volume, "file*1"));
        Assert.Equal(new AmigaMetadata(0, string.Empty), (await GetEntryAsync(volume, "plain.txt")).AmigaMetadata);
        Assert.Equal(new AmigaMetadata(0, "Directory"), (await GetEntryAsync(volume, "dir#1")).AmigaMetadata);
        await volume.ChangeDirectory(["dir#1"]);
        Assert.Equal(["a?b"], (await ListAsync(volume)).Select(entry => entry.Name));
        Assert.Equal(NotExecutableMetadata, (await GetEntryAsync(volume, "a?b")).AmigaMetadata);
        Assert.Equal("Inside", await ReadItemAsync(volume, "a?b"));
        await volume.ChangeDirectory([Paths.Parent]);
    }

    [Fact]
    public async Task When_CopyingBetweenAmigaVolumes_Then_NamesProtectionBitsAndCommentsAreKept()
    {
        // arrange
        var source = await CreateAdfWithEntriesAsync("source.adf");
        var destination = await CreateAdfVolumeAsync("destination.adf");

        // act
        await source.CopyToAsync(await ListAsync(source), destination);

        // assert
        Assert.True(await source.SupportsAmigaMetadata());
        await AssertAdfEntriesAsync(destination);
    }

    [Fact]
    public async Task When_CopyingFromAmigaToLocalWithUaeFsDb_Then_NamesAndMetadataAreWrittenToUaeFsDbFiles()
    {
        // arrange
        var source = await CreateAdfWithEntriesAsync("source.adf");
        var destination = CreateLocalVolume("local");

        // act
        await source.CopyToAsync(await ListAsync(source), destination, new CopyOptions());

        // assert - special names get safe names, the plain item needs no metadata
        var localPath = Path.Combine(_rootPath, "local");
        Assert.Equal(["_UAEFSDB.___", "__uae___dir_1", "__uae___file_1", "plain.txt"], ListLocalNames(localPath));
        Assert.Equal("Amiga", await File.ReadAllTextAsync(Path.Combine(localPath, "__uae___file_1")));
        var nodes = await ReadFsDbNodesAsync(localPath);
        Assert.Equal(["dir#1", "file*1"], nodes.Select(node => node.AmigaName).Order(StringComparer.Ordinal));
        var fileNode = nodes.Single(node => node.AmigaName == "file*1");
        Assert.Equal("__uae___file_1", fileNode.NormalName);
        Assert.Equal(0x40U, fileNode.Mode);
        Assert.Equal("Comment 1", fileNode.Comment);

        var directoryPath = Path.Combine(localPath, "__uae___dir_1");
        Assert.Equal(["_UAEFSDB.___", "__uae___a_b"], ListLocalNames(directoryPath));
        var directoryNode = Assert.Single(await ReadFsDbNodesAsync(directoryPath));
        Assert.Equal("a?b", directoryNode.AmigaName);
        Assert.Equal(0x02U, directoryNode.Mode);
    }

    [Fact]
    public async Task When_CopyingFromAmigaToLocalWithUaeMetafiles_Then_NamesAreEncodedAndMetafilesWritten()
    {
        // arrange
        var source = await CreateAdfWithEntriesAsync("source.adf");
        var destination = CreateLocalVolume("local");

        // act
        await source.CopyToAsync(await ListAsync(source), destination,
            new CopyOptions(UaeMetadata: UaeMetadataFormat.UaeMetafile));

        // assert
        var localPath = Path.Combine(_rootPath, "local");
        Assert.Equal(["dir%231", "dir%231.uaem", "file%2a1", "file%2a1.uaem", "plain.txt"],
            ListLocalNames(localPath));
        var metafile = await File.ReadAllTextAsync(Path.Combine(localPath, "file%2a1.uaem"));
        Assert.StartsWith("-s--rwed ", metafile);
        Assert.EndsWith(" Comment 1\n", metafile);
        Assert.Equal(["a%3fb", "a%3fb.uaem"], ListLocalNames(Path.Combine(localPath, "dir%231")));
        Assert.StartsWith("----rw-d ", await File.ReadAllTextAsync(Path.Combine(localPath, "dir%231", "a%3fb.uaem")));
    }

    [Fact]
    public async Task When_CopyingFromAmigaToLocalWithoutMetadata_Then_InvalidCharactersAreReplaced()
    {
        // arrange
        var source = await CreateAdfWithEntriesAsync("source.adf");
        var destination = CreateLocalVolume("local");

        // act
        await source.CopyToAsync(await ListAsync(source), destination,
            new CopyOptions(UaeMetadata: UaeMetadataFormat.None));

        // assert
        var localPath = Path.Combine(_rootPath, "local");
        Assert.Equal(["dir#1", "file_1", "plain.txt"], ListLocalNames(localPath));
        Assert.Equal(["a_b"], ListLocalNames(Path.Combine(localPath, "dir#1")));
    }

    [Theory]
    [InlineData(UaeMetadataFormat.UaeFsDb)]
    [InlineData(UaeMetadataFormat.UaeMetafile)]
    public async Task When_CopyingFromLocalWithUaeMetadataToAmiga_Then_NamesAndMetadataAreRestored(
        UaeMetadataFormat format)
    {
        // arrange - copy an adf to a local directory with uae metadata
        var source = await CreateAdfWithEntriesAsync("source.adf");
        var local = CreateLocalVolume("local");
        await source.CopyToAsync(await ListAsync(source), local, new CopyOptions(UaeMetadata: format));
        var destination = await CreateAdfVolumeAsync("destination.adf");

        // act
        await local.CopyToAsync(await ListAsync(local), destination);

        // assert - metadata files aren't copied
        await AssertAdfEntriesAsync(destination);
    }

    [Fact]
    public async Task When_CopyingFromAmigaToLocalAgain_Then_ExistingNormalNamesAreUsed()
    {
        // arrange
        var source = await CreateAdfWithEntriesAsync("source.adf");
        var destination = CreateLocalVolume("local");
        await source.CopyToAsync(await ListAsync(source), destination);

        // act
        await source.CopyToAsync(await ListAsync(source), destination);

        // assert - no new normal names and nodes
        var localPath = Path.Combine(_rootPath, "local");
        Assert.Equal(["_UAEFSDB.___", "__uae___dir_1", "__uae___file_1", "plain.txt"], ListLocalNames(localPath));
        Assert.Equal(2, (await ReadFsDbNodesAsync(localPath)).Count);
    }

    [Fact]
    public async Task When_CopyingBetweenLocalDirectories_Then_UaeMetadataFilesAreCopiedAsTheyAre()
    {
        // arrange
        var source = await CreateAdfWithEntriesAsync("source.adf");
        var local = CreateLocalVolume("local");
        await source.CopyToAsync(await ListAsync(source), local);
        var destination = CreateLocalVolume("local2");

        // act
        await local.CopyToAsync(await ListAsync(local), destination);

        // assert
        Assert.Equal(ListLocalNames(Path.Combine(_rootPath, "local")), ListLocalNames(Path.Combine(_rootPath, "local2")));
    }

    [Fact]
    public async Task When_MovingFromLocalWithUaeMetadataToAmiga_Then_MetadataFilesAreRemovedFromTheSource()
    {
        // arrange
        var source = await CreateAdfWithEntriesAsync("source.adf");
        var local = CreateLocalVolume("local");
        await source.CopyToAsync(await ListAsync(source), local);
        var destination = await CreateAdfVolumeAsync("destination.adf");

        // act
        await local.MoveToAsync(await ListAsync(local), destination);

        // assert - moved directories are deleted, as their metadata files are removed
        await AssertAdfEntriesAsync(destination);
        Assert.Empty(ListLocalNames(Path.Combine(_rootPath, "local")));
    }

    [Fact]
    public async Task When_MovingSomeEntriesFromLocalWithUaeMetadata_Then_MetadataOfOtherEntriesIsKept()
    {
        // arrange
        var source = await CreateAdfWithEntriesAsync("source.adf");
        var local = CreateLocalVolume("local");
        await source.CopyToAsync(await ListAsync(source), local);
        var destination = await CreateAdfVolumeAsync("destination.adf");

        // act
        await local.MoveToAsync([await GetEntryAsync(local, "__uae___file_1")], destination);

        // assert
        Assert.Equal(["file*1"], (await ListAsync(destination)).Select(entry => entry.Name));
        Assert.Equal(ScriptMetadata, (await GetEntryAsync(destination, "file*1")).AmigaMetadata);
        var localPath = Path.Combine(_rootPath, "local");
        Assert.Equal(["_UAEFSDB.___", "__uae___dir_1", "plain.txt"], ListLocalNames(localPath));
        Assert.Equal(["dir#1"], (await ReadFsDbNodesAsync(localPath)).Select(node => node.AmigaName));
    }
}
