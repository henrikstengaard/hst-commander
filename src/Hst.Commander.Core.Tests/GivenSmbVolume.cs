using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Smb;

namespace Hst.Commander.Core.Tests;

public class GivenSmbVolume : IDisposable
{
    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    private readonly TestSmbServer _server = new();
    private readonly string _sharePath;

    public GivenSmbVolume()
    {
        _sharePath = Directory.CreateDirectory(Path.Combine(_rootPath, "share")).FullName;
        Directory.CreateDirectory(Path.Combine(_rootPath, "hidden"));
        _server.AddShare("share", _sharePath);
        _server.AddShare("c$", Path.Combine(_rootPath, "hidden"));
        Directory.CreateDirectory(Path.Combine(_sharePath, "dir1"));
        File.WriteAllText(Path.Combine(_sharePath, "file1.txt"), "Hello SMB world");
    }

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private SmbVolume OpenVolume() => SmbVolume.Open(_server, "server", null);

    private static async Task<IReadOnlyList<string>> ListNamesAsync(IVolume volume) =>
        (await volume.ListEntries().ToListAsync()).Select(entry => entry.Name).ToList();

    [Fact]
    public async Task When_ListingRoot_Then_SharesExceptHiddenSharesAreListed()
    {
        // arrange
        using var volume = OpenVolume();

        // act
        var entries = await volume.ListEntries().ToListAsync();

        // assert
        var entry = Assert.Single(entries);
        Assert.Equal("share", entry.Name);
        Assert.Equal(EntryType.Container, entry.Type);
        Assert.True(entry.IsVolume);
        Assert.Equal(SmbPath.GetPathRoot("server"), volume.Name);
    }

    [Fact]
    public async Task When_ListingShare_Then_EntriesAreListedWithoutDotEntries()
    {
        // arrange
        using var volume = OpenVolume();
        await volume.ChangeDirectory(["share"]);

        // act
        var entries = await volume.ListEntries().ToListAsync();

        // assert
        Assert.Equal(["dir1", "file1.txt"], entries.Select(entry => entry.Name));
        Assert.Equal(EntryType.Container, entries[0].Type);
        Assert.Null(entries[0].Size);
        Assert.Equal(EntryType.Item, entries[1].Type);
        Assert.Equal(15, entries[1].Size);
        Assert.Equal(DateTimeKind.Utc, entries[1].ModifiedDate?.Kind);
        Assert.Equal(EntryAttributes.Archive, entries[1].Attributes);
        Assert.Equal(["share"], volume.CurrentPathComponents);
    }

    [Fact]
    public async Task When_ChangingToHiddenShare_Then_ItIsOpened()
    {
        // arrange
        using var volume = OpenVolume();

        // act
        await volume.ChangeDirectory(["c$"]);

        // assert
        Assert.Equal(["c$"], volume.CurrentPathComponents);
        Assert.Empty(await ListNamesAsync(volume));
    }

    [Fact]
    public async Task When_ChangingToMissingDirectory_Then_DirectoryIsUnchanged()
    {
        // arrange
        using var volume = OpenVolume();
        await volume.ChangeDirectory(["share"]);

        // act and assert
        await Assert.ThrowsAnyAsync<IOException>(() => volume.ChangeDirectory(["missing"]));
        await Assert.ThrowsAnyAsync<IOException>(() => volume.ChangeDirectory([Paths.Root, "missing"]));
        Assert.Equal(["share"], volume.CurrentPathComponents);
    }

    [Fact]
    public async Task When_ChangingToParentAndRoot_Then_PathComponentsChange()
    {
        // arrange
        using var volume = OpenVolume();
        await volume.ChangeDirectory(["share", "dir1"]);

        // act
        await volume.ChangeDirectory([Paths.Parent]);
        var parent = volume.CurrentPathComponents.ToList();
        await volume.ChangeDirectory(["DIR1"]);
        await volume.ChangeDirectory([Paths.Root]);

        // assert
        Assert.Equal(["share"], parent);
        Assert.Empty(volume.CurrentPathComponents);
    }

    [Fact]
    public async Task When_OpeningEntries_Then_ContainersAreChangedTo()
    {
        // arrange
        using var volume = OpenVolume();

        // act
        var share = await volume.OpenEntry("share");
        var file = await volume.OpenEntry("file1.txt");
        var dir = await volume.OpenEntry("dir1");

        // assert
        Assert.True(share.IsVolume);
        Assert.Equal(EntryType.Item, file.Type);
        Assert.Equal(15, file.Size);
        Assert.Equal(EntryType.Container, dir.Type);
        Assert.Equal(["share", "dir1"], volume.CurrentPathComponents);
        await Assert.ThrowsAnyAsync<IOException>(() => volume.OpenEntry("missing"));
    }

    [Fact]
    public async Task When_ReadingFile_Then_ContentIsReadInRequestsOfMaxReadSize()
    {
        // arrange
        using var volume = OpenVolume();
        await volume.ChangeDirectory(["share"]);

        // act
        await using var stream = await volume.OpenRead("file1.txt");
        var buffer = new byte[100];
        var firstRead = await stream.ReadAsync(buffer);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();

        // assert
        Assert.True(stream.CanSeek);
        Assert.Equal(7, firstRead);
        Assert.Equal("Hello SMB world", text);
    }

    [Fact]
    public async Task When_WritingFile_Then_FileIsCreatedAndOverwritten()
    {
        // arrange
        using var volume = OpenVolume();
        await volume.ChangeDirectory(["share", "dir1"]);
        var data = Enumerable.Range(0, 100).Select(value => (byte)value).ToArray();

        // act
        await using (var stream = await volume.OpenWrite("new.bin"))
        {
            await stream.WriteAsync(data);
        }

        await using (var stream = await volume.OpenWrite("new.bin"))
        {
            await stream.WriteAsync(data.AsMemory(0, 10));
        }

        // assert
        Assert.Equal(data[..10], await File.ReadAllBytesAsync(Path.Combine(_sharePath, "dir1", "new.bin")));
    }

    [Fact]
    public async Task When_CreatingDeletingAndRenaming_Then_ShareIsChanged()
    {
        // arrange
        using var volume = OpenVolume();
        await volume.ChangeDirectory(["share"]);

        // act
        await volume.CreateContainer("dir2");
        await volume.CreateContainer("dir2");
        await volume.Rename("file1.txt", "renamed.txt");
        await volume.Rename("dir2", "DIR2");
        await volume.Delete("dir1");

        // assert
        Assert.Equal(["DIR2", "renamed.txt"], await ListNamesAsync(volume));
    }

    [Fact]
    public async Task When_RenamingToExistingName_Then_IOExceptionIsThrown()
    {
        // arrange
        using var volume = OpenVolume();
        await volume.ChangeDirectory(["share"]);

        // act and assert
        await Assert.ThrowsAsync<IOException>(() => volume.Rename("file1.txt", "dir1"));
    }

    [Fact]
    public async Task When_DeletingDirectoryWithEntries_Then_IOExceptionIsThrown()
    {
        // arrange
        File.WriteAllText(Path.Combine(_sharePath, "dir1", "file2.txt"), "file2");
        using var volume = OpenVolume();
        await volume.ChangeDirectory(["share"]);

        // act and assert
        await Assert.ThrowsAsync<IOException>(() => volume.Delete("dir1"));
        Assert.True(Directory.Exists(Path.Combine(_sharePath, "dir1")));
    }

    [Fact]
    public async Task When_ChangingShareList_Then_NotSupportedExceptionIsThrown()
    {
        // arrange
        using var volume = OpenVolume();

        // act and assert
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.CreateContainer("new"));
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.Delete("share"));
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.OpenWrite("file"));
        Assert.Equal(EntryAttributes.None, await volume.GetChangeableAttributes());
        Assert.Null(await volume.GetSpace());
    }

    [Fact]
    public async Task When_SettingModifiedDateAndAttributes_Then_EntryIsChanged()
    {
        // arrange
        using var volume = OpenVolume();
        await volume.ChangeDirectory(["share"]);
        var modifiedDate = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Unspecified);

        // act
        await volume.SetModifiedDate("file1.txt", modifiedDate);
        await volume.SetAttributes("file1.txt", EntryAttributes.ReadOnly | EntryAttributes.Hidden);
        var entry = (await volume.ListEntries().ToListAsync()).Single(entry => entry.Name == "file1.txt");
        await volume.SetAttributes("file1.txt", EntryAttributes.None);
        var cleared = (await volume.ListEntries().ToListAsync()).Single(entry => entry.Name == "file1.txt");

        // assert
        Assert.Equal(DateTime.SpecifyKind(modifiedDate, DateTimeKind.Utc), entry.ModifiedDate);
        Assert.Equal(EntryAttributes.ReadOnly | EntryAttributes.Hidden, entry.Attributes);
        Assert.Equal(EntryAttributes.None, cleared.Attributes);
    }

    [Fact]
    public async Task When_GettingSpaceOfShare_Then_FreeAndTotalAreReturned()
    {
        // arrange
        using var volume = OpenVolume();
        await volume.ChangeDirectory(["share"]);

        // act
        var space = await volume.GetSpace();

        // assert
        Assert.Equal(new VolumeSpace(250 * 4096, 1000 * 4096), space);
    }

    [Fact]
    public async Task When_ConnectionIsClosedByServer_Then_VolumeConnectsAgain()
    {
        // arrange
        using var volume = OpenVolume();
        await volume.ChangeDirectory(["share"]);
        _server.Connections[0].IsConnected = false;

        // act
        var names = await ListNamesAsync(volume);

        // assert
        Assert.Equal(["dir1", "file1.txt"], names);
        Assert.Equal(2, _server.Connections.Count);
        Assert.True(_server.Connections[0].IsDisposed);
    }

    [Fact]
    public void When_Disposing_Then_ConnectionIsDisposed()
    {
        // arrange
        var volume = OpenVolume();

        // act
        volume.Dispose();

        // assert
        Assert.True(Assert.Single(_server.Connections).IsDisposed);
    }
}
