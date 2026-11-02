using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenBookmarkStore : IDisposable
{
    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    private readonly TestCredentialStore _credentialStore = new();
    private readonly BookmarkStore _store;

    public GivenBookmarkStore()
    {
        _store = new BookmarkStore(Path.Combine(_rootPath, "settings", "bookmarks.json"), _credentialStore);
    }

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private static Bookmark CreateBookmark(string id, string description, string? userName = null) =>
        new(id, description, userName == null ? null : "SMB", userName == null ? null : @"\\server\share",
            userName == null ? @"c:\temp" : @"\\server\share\dir", userName, false);

    [Fact]
    public void When_FileDoesNotExist_Then_NoBookmarksAreLoaded() => Assert.Empty(_store.Load());

    [Fact]
    public void When_BookmarksAreAdded_Then_TheyAreLoadedSortedByDescription()
    {
        // act
        _store.Add(CreateBookmark("1", "temp"));
        _store.Add(CreateBookmark("2", "Amiga"));

        // assert
        Assert.Equal(["Amiga", "temp"], _store.Load().Select(bookmark => bookmark.Description));
        Assert.Equal(CreateBookmark("1", "temp"), _store.Load()[1]);
    }

    [Fact]
    public void When_BookmarkIsRenamed_Then_ItsDescriptionChanges()
    {
        // arrange
        _store.Add(CreateBookmark("1", "temp"));

        // act
        _store.Rename("1", "  Temporary  ");

        // assert
        Assert.Equal("Temporary", Assert.Single(_store.Load()).Description);
    }

    [Fact]
    public void When_PasswordIsStored_Then_ItIsInTheCredentialStoreAndNotInTheFile()
    {
        // arrange
        var bookmark = CreateBookmark("1", "share", "user");
        _store.Add(bookmark);

        // act
        _store.StorePassword(bookmark, "secret");

        // assert
        var loaded = Assert.Single(_store.Load());
        Assert.True(loaded.HasPassword);
        Assert.Equal("secret", _credentialStore.Passwords["1"]);
        Assert.Equal(new VolumeCredentials("user", "secret"), _store.GetCredentials(loaded));
        Assert.DoesNotContain("secret", File.ReadAllText(_store.FilePath));
    }

    [Fact]
    public void When_BookmarkWithPasswordIsDeleted_Then_PasswordIsDeleted()
    {
        // arrange
        var bookmark = CreateBookmark("1", "share", "user");
        _store.Add(bookmark);
        _store.StorePassword(bookmark, "secret");

        // act
        _store.Delete("1");

        // assert
        Assert.Empty(_store.Load());
        Assert.Empty(_credentialStore.Passwords);
    }

    [Fact]
    public void When_CredentialStoreFails_Then_BookmarkIsKeptWithoutPassword()
    {
        // arrange
        var bookmark = CreateBookmark("1", "share", "user");
        _store.Add(bookmark);
        _credentialStore.FailSave = true;

        // act and assert
        Assert.Throws<IOException>(() => _store.StorePassword(bookmark, "secret"));
        var loaded = Assert.Single(_store.Load());
        Assert.False(loaded.HasPassword);
        Assert.Null(_store.GetCredentials(loaded));
    }

    [Fact]
    public void When_CredentialStoreIsUnavailable_Then_PasswordIsNotStored()
    {
        // arrange
        var store = new BookmarkStore(_store.FilePath, new TestCredentialStore(false));
        var bookmark = CreateBookmark("1", "share", "user");
        store.Add(bookmark);

        // act and assert
        Assert.Throws<IOException>(() => store.StorePassword(bookmark, "secret"));
        Assert.False(Assert.Single(store.Load()).HasPassword);
    }

    [Fact]
    public void When_VolumeChoicesAreListed_Then_BookmarksItemFollowsTheVolumeProviders()
    {
        // arrange
        var bookmark = CreateBookmark("1", "temp");

        // act
        var choices = VolumeChoice.List([Core.VolumeProviders.Local]);
        var bookmarkChoice = Assert.Single(VolumeChoice.List([bookmark]));

        // assert
        Assert.Equal(2, choices.Count);
        Assert.Same(Core.VolumeProviders.Local, choices[0].VolumeProvider);
        Assert.True(choices[1].IsBookmarks);
        Assert.Equal("Bookmarks", choices[1].Name);
        Assert.False(bookmarkChoice.IsBookmarks);
        Assert.Equal("temp", bookmarkChoice.Name);
        Assert.Equal(@"c:\temp", bookmarkChoice.Description);
    }

    [Fact]
    public void When_FileIsInvalid_Then_NoBookmarksAreLoaded()
    {
        // arrange
        Directory.CreateDirectory(Path.GetDirectoryName(_store.FilePath)!);
        File.WriteAllText(_store.FilePath, "{ not json");

        // act and assert
        Assert.Empty(_store.Load());
    }
}
