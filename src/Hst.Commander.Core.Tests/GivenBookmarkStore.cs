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
    public void When_BookmarkIsEdited_Then_ItsDescriptionAndPathChange()
    {
        // arrange
        _store.Add(CreateBookmark("1", "temp"));

        // act
        _store.Edit("1", new BookmarkChanges("  Temporary  ", null, @" c:	emp\dir ", null, null, false));

        // assert
        Assert.Equal(new Bookmark("1", "Temporary", null, null, @"c:	emp\dir", null, false),
            Assert.Single(_store.Load()));
    }

    [Fact]
    public void When_BookmarkWithLocationIsEdited_Then_LocationUserNameAndPasswordChange()
    {
        // arrange
        var bookmark = CreateBookmark("1", "share", "user");
        _store.Add(bookmark);
        _store.StorePassword(bookmark, "secret");

        // act
        _store.Edit("1", new BookmarkChanges("share2", @"\server2\share", @"\server2\share\dir", "user2",
            "secret2", true));

        // assert
        var loaded = Assert.Single(_store.Load());
        Assert.Equal(new Bookmark("1", "share2", "SMB", @"\server2\share", @"\server2\share\dir", "user2", true),
            loaded);
        Assert.Equal(new VolumeCredentials("user2", "secret2"), _store.GetCredentials(loaded));
    }

    [Fact]
    public void When_BookmarkIsEditedWithoutNewPassword_Then_StoredPasswordIsKept()
    {
        // arrange
        var bookmark = CreateBookmark("1", "share", "user");
        _store.Add(bookmark);
        _store.StorePassword(bookmark, "secret");

        // act
        _store.Edit("1", new BookmarkChanges("share", bookmark.Location, bookmark.Path, "user", string.Empty, true));

        // assert
        Assert.True(Assert.Single(_store.Load()).HasPassword);
        Assert.Equal("secret", _credentialStore.Passwords["1"]);
    }

    [Theory]
    [InlineData("user", false)]
    [InlineData("", true)]
    public void When_PasswordIsNoLongerStored_Then_ItIsDeleted(string userName, bool storePassword)
    {
        // arrange
        var bookmark = CreateBookmark("1", "share", "user");
        _store.Add(bookmark);
        _store.StorePassword(bookmark, "secret");

        // act
        _store.Edit("1", new BookmarkChanges("share", bookmark.Location, bookmark.Path, userName, "new",
            storePassword));

        // assert
        var loaded = Assert.Single(_store.Load());
        Assert.False(loaded.HasPassword);
        Assert.Equal(userName.Length > 0 ? userName : null, loaded.UserName);
        Assert.Empty(_credentialStore.Passwords);
    }

    [Fact]
    public void When_DeletedBookmarkIsEdited_Then_IOExceptionIsThrown() =>
        Assert.Throws<IOException>(() =>
            _store.Edit("1", new BookmarkChanges("temp", null, @"c:	emp", null, null, false)));

    [Theory]
    [InlineData("", @"\server\share", @"\server\share\dir", "user", "", false, "Enter a description.")]
    [InlineData("share", " ", @"\server\share\dir", "user", "", false, "Enter a location.")]
    [InlineData("share", @"\server\share", "", "user", "", false, "Enter a path.")]
    [InlineData("share", @"\server\share", @"\server\share\dir", "user", "", true,
        "Enter the password to store.")]
    [InlineData("share", @"\server\share", @"\server\share\dir", "user", "secret", true, null)]
    [InlineData("share", @"\server\share", @"\server\share\dir", "", "", true, null)]
    public void When_ChangesAreChecked_Then_ErrorIsReturnedForInvalidInput(string description, string location,
        string path, string userName, string password, bool storePassword, string? expectedError)
    {
        // arrange
        var bookmark = CreateBookmark("1", "share", "user");
        var changes = new BookmarkChanges(description, location, path, userName, password, storePassword);

        // act and assert
        Assert.Equal(expectedError, changes.GetError(bookmark));
    }

    [Fact]
    public void When_PathInOtherLocationIsRequested_Then_LocationIsReplaced()
    {
        // arrange
        var bookmark = CreateBookmark("1", "share", "user");

        // act and assert
        Assert.Equal(@"\server2\data\dir", bookmark.GetPathInLocation(@"\server2\data"));
        Assert.Null(CreateBookmark("2", "temp").GetPathInLocation(@"c:\other"));
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
    public void When_VolumeChoicesAreListed_Then_BookmarksItemIsFirst()
    {
        // arrange
        var bookmark = CreateBookmark("1", "temp");

        // act
        var choices = VolumeChoice.List([Core.VolumeProviders.Local]);
        var bookmarkChoice = Assert.Single(VolumeChoice.List([bookmark]));

        // assert
        Assert.Equal(2, choices.Count);
        Assert.True(choices[0].IsBookmarks);
        Assert.Equal("Bookmarks", choices[0].Name);
        Assert.Same(Core.VolumeProviders.Local, choices[1].VolumeProvider);
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
