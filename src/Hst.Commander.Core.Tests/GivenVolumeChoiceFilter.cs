using Hst.Commander.Abstractions;

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
    public void When_QueryMatchesVolumeProvider_Then_ItIsListedByNameOrDescription()
    {
        // arrange
        var choices = VolumeChoice.List([
            new NamedVolumeProvider("Local", "Local directories"),
            new NamedVolumeProvider("FTP", "FTP and SFTP servers")
        ]);

        // act
        var byName = VolumeChoice.Filter(choices, "ftp");
        var byDescription = VolumeChoice.Filter(choices, "BOOKMARKED");

        // assert
        Assert.Equal(["FTP"], byName.Select(choice => choice.Name));
        Assert.Equal([VolumeChoice.Bookmarks], byDescription);
    }

    [Fact]
    public void When_QueryMatchesNothing_Then_NoBookmarksAreListed()
    {
        // act
        var choices = VolumeChoice.Filter(Bookmarks, "missing");

        // assert
        Assert.Empty(choices);
    }

    [Fact]
    public void When_QueryMatchesVolumeCreator_Then_ItIsListedByNameOrDescription()
    {
        // arrange
        IReadOnlyList<IVolumeCreator> creators =
        [
            new NamedVolumeCreator("Amiga disk", "Double density adf"),
            new NamedVolumeCreator("exFAT image", "Disk image with an exFAT file system")
        ];

        // act
        var byName = VolumeCreation.Filter(creators, "amiga");
        var byDescription = VolumeCreation.Filter(creators, "disk exfat");
        var none = VolumeCreation.Filter(creators, "missing");

        // assert
        Assert.Equal(["Amiga disk"], byName.Select(creator => creator.Name));
        Assert.Equal(["exFAT image"], byDescription.Select(creator => creator.Name));
        Assert.Empty(none);
    }

    private sealed class NamedVolumeProvider(string name, string description) : IVolumeProvider
    {
        public string Name => name;

        public string Description => description;

        public Task<IVolume> CreateVolume(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NamedVolumeCreator(string name, string description) : IVolumeCreator
    {
        public string Name => name;

        public string Description => description;

        public string FileExtension => ".test";

        public IReadOnlyList<VolumeCreatorField> Fields => [];

        public Task Create(Stream stream, VolumeCreatorValues values, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}