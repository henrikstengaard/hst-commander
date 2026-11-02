using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Smb;
using Hst.Commander.Plugins.Zip;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextBookmarks : IDisposable
{
    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    private readonly TestCredentialStore _credentialStore = new();
    private readonly BookmarkStore _store;
    private readonly TestSmbServer _server = new();
    private readonly SmbVolumeProvider _smbVolumeProvider;
    private readonly IFileVolumeProvider[] _fileVolumeProviders = [new ZipFileVolumeProvider()];
    private readonly VolumeCredentials _credentials = new(@"DOMAIN\user", "secret");

    private readonly TestVolumeProvider _testVolumeProvider = new(volume =>
    {
        volume.AddEntry([], new TestEntry("dir1", EntryType.Container));
        volume.AddEntry(["dir1"], new TestEntry("dir2", EntryType.Container));
    });

    public GivenNavigationContextBookmarks()
    {
        _store = new BookmarkStore(Path.Combine(_rootPath, "bookmarks.json"), _credentialStore);
        var sharePath = Directory.CreateDirectory(Path.Combine(_rootPath, "share")).FullName;
        var dirPath = Directory.CreateDirectory(Path.Combine(sharePath, "dir1")).FullName;
        File.WriteAllBytes(Path.Combine(dirPath, "archive.zip"),
            ZipTestData.CreateZip(("inner/file.txt", "content")));
        _server.AddShare("share", sharePath);
        _smbVolumeProvider = new SmbVolumeProvider(_server);
    }

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private static string Join(params string[] parts) => string.Join(Path.DirectorySeparatorChar, parts);

    private IReadOnlyList<IVolumeProvider> VolumeProviders => [Core.VolumeProviders.Local, _testVolumeProvider,
        _smbVolumeProvider];

    private NavigationContext CreateContext(string path = "") =>
        new(path, _ => { }, _fileVolumeProviders, []);

    private static void Ignore(string message)
    {
    }

    private static void Ignore()
    {
    }

    private static Task<VolumeCredentials?> NoCredentials(CredentialsRequest request) =>
        Task.FromResult<VolumeCredentials?>(null);

    private Task<bool> OpenAsync(NavigationContext context, Bookmark bookmark,
        Func<CredentialsRequest, Task<VolumeCredentials?>>? askCredentials = null) =>
        BookmarkOpener.OpenAsync(context, bookmark, _store, askCredentials ?? NoCredentials, Ignore, Ignore,
            VolumeProviders);

    [Fact]
    public async Task When_LocalDirectoryIsBookmarked_Then_ItIsOpenedFromAnotherVolume()
    {
        // arrange
        var dirPath = Path.Combine(_rootPath, "share", "dir1");
        using var context = CreateContext(dirPath);
        var bookmark = context.CreateBookmark(" dir1 ");
        using var otherContext = CreateContext();
        await otherContext.ChangeVolumeProviderAsync(_testVolumeProvider);

        // act
        var opened = await OpenAsync(otherContext, bookmark);

        // assert
        Assert.Equal("dir1", bookmark.Description);
        Assert.Null(bookmark.VolumeProviderName);
        Assert.Equal(dirPath, bookmark.Path);
        Assert.Equal("dir1", context.GetDefaultBookmarkDescription());
        Assert.True(opened);
        Assert.Same(Core.VolumeProviders.Local, otherContext.VolumeProvider);
        Assert.Equal(dirPath, otherContext.CurrentPath);
    }

    [Fact]
    public async Task When_PathInsideLocalImageIsBookmarked_Then_ImageIsOpened()
    {
        // arrange
        var imagePath = Join(_rootPath, "share", "dir1", "archive.zip", "inner");
        using var context = CreateContext();
        await context.ChangeLocationAsync(imagePath);
        var bookmark = context.CreateBookmark("inner");
        using var otherContext = CreateContext();

        // act
        await OpenAsync(otherContext, bookmark);
        var entries = await otherContext.ListEntriesAsync();

        // assert
        Assert.True(otherContext.IsInImage);
        Assert.Equal(imagePath, otherContext.CurrentPath);
        Assert.Contains(entries, entry => entry.Name == "file.txt");
    }

    [Fact]
    public async Task When_VolumeProviderPathIsBookmarked_Then_ItIsOpened()
    {
        // arrange
        using var context = CreateContext();
        await context.ChangeLocationAsync(_testVolumeProvider, Join("dir1", "dir2"));
        var bookmark = context.CreateBookmark("dir2");
        using var otherContext = CreateContext();

        // act
        await OpenAsync(otherContext, bookmark);

        // assert
        Assert.Equal("Test", bookmark.VolumeProviderName);
        Assert.Equal(Join("dir1", "dir2"), otherContext.CurrentPath);
        Assert.Same(_testVolumeProvider, otherContext.VolumeProvider);
    }

    [Fact]
    public async Task When_BookmarkedPathIsMissing_Then_LocationIsUnchanged()
    {
        // arrange
        using var context = CreateContext(_rootPath);
        var bookmark = new Bookmark("1", "missing", "Test", null, Join("dir1", "missing"), null, false);

        // act and assert
        await Assert.ThrowsAnyAsync<IOException>(() => OpenAsync(context, bookmark));
        Assert.Equal(_rootPath, context.CurrentPath);
        Assert.Same(Core.VolumeProviders.Local, context.VolumeProvider);
    }

    [Fact]
    public async Task When_VolumeProviderIsNotAvailable_Then_IOExceptionIsThrown()
    {
        // arrange
        using var context = CreateContext(_rootPath);
        var bookmark = new Bookmark("1", "disk", "Missing", null, "disk", null, false);

        // act and assert
        await Assert.ThrowsAsync<IOException>(() => OpenAsync(context, bookmark));
    }

    [Fact]
    public async Task When_SmbPathInsideImageIsBookmarked_Then_LocationHasNoImageOrCredentials()
    {
        // arrange
        _server.Credentials = _credentials;
        using var context = CreateContext();
        var connection = await VolumeConnector.ConnectAsync(_smbVolumeProvider,
            "smb://DOMAIN%5Cuser:secret@server/share", NoCredentials, Ignore, Ignore);
        await context.ChangeVolumeProviderAsync(connection!);
        var pathRoot = SmbPath.GetPathRoot("server");
        var imagePath = Join(pathRoot, "share", "dir1", "archive.zip", "inner");
        await context.ChangeLocationAsync(imagePath);

        // act
        var bookmark = context.CreateBookmark("inner");

        // assert
        Assert.Equal("SMB", bookmark.VolumeProviderName);
        Assert.Equal(Join(pathRoot, "share", "dir1"), bookmark.Location);
        Assert.Equal(imagePath, bookmark.Path);
        Assert.Null(bookmark.UserName);
        Assert.Null(context.ConnectedLocation!.Credentials);
    }

    [Fact]
    public async Task When_SmbBookmarkHasStoredPassword_Then_ItConnectsWithoutAsking()
    {
        // arrange
        _server.Credentials = _credentials;
        using var context = CreateContext();
        var connection = await VolumeConnector.ConnectAsync(_smbVolumeProvider, @"\\server\share",
            _ => Task.FromResult<VolumeCredentials?>(_credentials), Ignore, Ignore);
        await context.ChangeVolumeProviderAsync(connection!);
        var imagePath = Join(SmbPath.GetPathRoot("server"), "share", "dir1", "archive.zip", "inner");
        await context.ChangeLocationAsync(imagePath);
        var bookmark = context.CreateBookmark("inner");
        _store.Add(bookmark);
        _store.StorePassword(bookmark, context.ConnectedLocation!.Credentials!.Password);
        bookmark = Assert.Single(_store.Load());
        using var otherContext = CreateContext(_rootPath);
        var requests = new List<CredentialsRequest>();

        // act
        var opened = await OpenAsync(otherContext, bookmark, request =>
        {
            requests.Add(request);
            return Task.FromResult<VolumeCredentials?>(null);
        });

        // assert
        Assert.Equal(@"DOMAIN\user", bookmark.UserName);
        Assert.True(opened);
        Assert.Empty(requests);
        Assert.Equal(imagePath, otherContext.CurrentPath);
        Assert.Equal(_credentials, otherContext.ConnectedLocation?.Credentials);
        Assert.DoesNotContain("secret", File.ReadAllText(_store.FilePath));
    }

    [Fact]
    public async Task When_SmbBookmarkHasNoPassword_Then_ItAsksWithUserName()
    {
        // arrange
        _server.Credentials = _credentials;
        var bookmark = new Bookmark("1", "share", "SMB", @"\\server\share", Join(SmbPath.GetPathRoot("server"),
            "share", "dir1"), @"DOMAIN\user", false);
        using var context = CreateContext(_rootPath);
        var requests = new List<CredentialsRequest>();

        // act
        var cancelled = !await OpenAsync(context, bookmark, request =>
        {
            requests.Add(request);
            return Task.FromResult<VolumeCredentials?>(null);
        });
        var opened = await OpenAsync(context, bookmark, _ => Task.FromResult<VolumeCredentials?>(_credentials));

        // assert
        Assert.True(cancelled);
        Assert.Equal(@"DOMAIN\user", Assert.Single(requests).UserName);
        Assert.True(opened);
        Assert.Equal(bookmark.Path, context.CurrentPath);
    }

    [Fact]
    public async Task When_LeavingConnectedVolumeProvider_Then_ConnectedLocationIsCleared()
    {
        // arrange
        using var context = CreateContext();
        var connection = await VolumeConnector.ConnectAsync(_smbVolumeProvider, @"\\server\share", NoCredentials,
            Ignore, Ignore);
        await context.ChangeVolumeProviderAsync(connection!);
        await context.ChangeLocationAsync(Join("share", "dir1"));
        var connectedLocation = context.ConnectedLocation;

        // act
        await context.ChangeLocationAsync(_rootPath);

        // assert
        Assert.NotNull(connectedLocation);
        Assert.Null(context.ConnectedLocation);
        Assert.Null(context.CreateBookmark("root").VolumeProviderName);
    }
}
