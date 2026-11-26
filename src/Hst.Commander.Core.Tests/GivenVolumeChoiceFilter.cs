namespace Hst.Commander.Core.Tests;

public class GivenVolumeChoiceFilter
{
    private static readonly IReadOnlyList<Bookmark> Bookmarks =
    [
        new("1", "Games", null, null, @"c:\amiga\games", null, false),
        new("2", "Work disk", null, null, @"c:\images\work.hdf", null, false),
        new("3", "Server", "SMB", @"\\server\share", @"\\server\share\amiga", "user", true)
    ];

    private static IReadOnlyList<string> Ids(IReadOnlyList<VolumeChoice> choices) =>
        choices.Select(choice => choice.Bookmark!.Id).ToList();

    [Fact]
    public void When_QueryIsEmpty_Then_AllBookmarksAreListedInOrder()
    {
        // act
        var choices = VolumeChoice.Filter(Bookmarks, string.Empty);

        // assert
        Assert.Equal(["1", "2", "3"], Ids(choices));
    }

    [Fact]
    public void When_QueryMatchesDescription_Then_BookmarkIsListedIgnoringCase()
    {
        // act
        var choices = VolumeChoice.Filter(Bookmarks, "WORK");

        // assert
        Assert.Equal(["2"], Ids(choices));
    }

    [Fact]
    public void When_QueryMatchesPath_Then_BookmarksAreListed()
    {
        // act
        var choices = VolumeChoice.Filter(Bookmarks, "amiga");

        // assert
        Assert.Equal(["1", "3"], Ids(choices));
    }

    [Fact]
    public void When_QueryHasWords_Then_BookmarksMatchingEveryWordAreListed()
    {
        // act
        var choices = VolumeChoice.Filter(Bookmarks, "server amiga");

        // assert
        Assert.Equal(["3"], Ids(choices));
    }

    [Fact]
    public void When_QueryMatchesNothing_Then_NoBookmarksAreListed()
    {
        // act
        var choices = VolumeChoice.Filter(Bookmarks, "missing");

        // assert
        Assert.Empty(choices);
    }
}
