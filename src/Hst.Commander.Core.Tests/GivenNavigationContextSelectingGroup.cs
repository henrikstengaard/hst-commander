using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextSelectingGroup
{
    private readonly TestVolumeProvider volumeProvider = new(volume =>
    {
        volume.AddEntry([], new TestEntry("dir.txt", EntryType.Container));
        volume.AddEntry([], new TestEntry("dir1", EntryType.Container));
        volume.AddEntry([], new TestEntry("a.txt", EntryType.Item, 1));
        volume.AddEntry([], new TestEntry("B.TXT", EntryType.Item, 2));
        volume.AddEntry([], new TestEntry("report1.md", EntryType.Item, 3));
        volume.AddEntry([], new TestEntry("report22.md", EntryType.Item, 4));
    });

    private async Task<(NavigationContext NavigationContext, IReadOnlyList<IEntry> Entries)> CreateAsync()
    {
        var navigationContext = new NavigationContext(string.Empty);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);
        return (navigationContext, await navigationContext.ListEntriesAsync());
    }

    private static string[] MarkedNames(NavigationContext navigationContext) =>
        navigationContext.MarkedEntries.Select(entry => entry.Name).Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task When_MarkGroupWithWildcard_Then_MatchingEntriesAreMarkedIgnoringCase()
    {
        // arrange
        var (navigationContext, entries) = await CreateAsync();
        using var _ = navigationContext;

        // act
        var count = navigationContext.MarkGroup(entries, new SelectGroupOptions("*.txt"));

        // assert
        Assert.Equal(3, count);
        Assert.Equal(["B.TXT", "a.txt", "dir.txt"], MarkedNames(navigationContext));
    }

    [Fact]
    public async Task When_MarkGroupCaseSensitive_Then_OnlyEntriesMatchingCaseAreMarked()
    {
        // arrange
        var (navigationContext, entries) = await CreateAsync();
        using var _ = navigationContext;

        // act
        navigationContext.MarkGroup(entries, new SelectGroupOptions("*.txt", CaseSensitive: true));

        // assert
        Assert.Equal(["a.txt", "dir.txt"], MarkedNames(navigationContext));
    }

    [Fact]
    public async Task When_MarkGroupFilesOnly_Then_DirectoriesAreNotMarked()
    {
        // arrange
        var (navigationContext, entries) = await CreateAsync();
        using var _ = navigationContext;

        // act
        navigationContext.MarkGroup(entries, new SelectGroupOptions("*", FilesOnly: true));

        // assert
        Assert.Equal(["B.TXT", "a.txt", "report1.md", "report22.md"], MarkedNames(navigationContext));
    }

    [Fact]
    public async Task When_MarkGroupWithAll_Then_ParentIsNotMarked()
    {
        // arrange
        var (navigationContext, entries) = await CreateAsync();
        using var _ = navigationContext;
        await navigationContext.ChangeLocationAsync("dir1");
        entries = await navigationContext.ListEntriesAsync();

        // act
        var count = navigationContext.MarkGroup(entries, SelectGroupOptions.Default);

        // assert
        Assert.Equal(Paths.Parent, Assert.Single(entries).Name);
        Assert.Equal(0, count);
        Assert.Empty(navigationContext.MarkedEntries);
    }

    [Fact]
    public async Task When_MarkGroupWithRegex_Then_MatchingEntriesAreMarked()
    {
        // arrange
        var (navigationContext, entries) = await CreateAsync();
        using var _ = navigationContext;

        // act
        navigationContext.MarkGroup(entries, new SelectGroupOptions(@"^report\d{2}\.md$"));

        // assert
        Assert.Equal(["report22.md"], MarkedNames(navigationContext));
    }

    [Fact]
    public async Task When_MarkGroup_Then_ExistingMarksAreKept()
    {
        // arrange
        var (navigationContext, entries) = await CreateAsync();
        using var _ = navigationContext;
        navigationContext.ToggleMark(entries.Single(entry => entry.Name == "dir1"));

        // act
        navigationContext.MarkGroup(entries, new SelectGroupOptions("*.md"));

        // assert
        Assert.Equal(["dir1", "report1.md", "report22.md"], MarkedNames(navigationContext));
    }

    [Fact]
    public async Task When_UnmarkGroup_Then_MatchingEntriesAreUnmarkedAndOtherMarksAreKept()
    {
        // arrange
        var (navigationContext, entries) = await CreateAsync();
        using var _ = navigationContext;
        navigationContext.MarkGroup(entries, SelectGroupOptions.Default);

        // act
        var count = navigationContext.UnmarkGroup(entries, new SelectGroupOptions("*.txt", FilesOnly: true));

        // assert
        Assert.Equal(2, count);
        Assert.Equal(["dir.txt", "dir1", "report1.md", "report22.md"], MarkedNames(navigationContext));
    }

    [Fact]
    public async Task When_UnmarkGroupWithRegexCaseSensitive_Then_OnlyEntriesMatchingCaseAreUnmarked()
    {
        // arrange
        var (navigationContext, entries) = await CreateAsync();
        using var _ = navigationContext;
        navigationContext.MarkGroup(entries, new SelectGroupOptions("*.txt"));

        // act
        navigationContext.UnmarkGroup(entries, new SelectGroupOptions(@"\.txt$", CaseSensitive: true));

        // assert
        Assert.Equal(["B.TXT"], MarkedNames(navigationContext));
    }

    [Fact]
    public async Task When_InvertMarks_Then_MarkedEntriesAreUnmarkedAndOthersMarked()
    {
        // arrange
        var (navigationContext, entries) = await CreateAsync();
        using var _ = navigationContext;
        navigationContext.MarkGroup(entries, new SelectGroupOptions("*.txt"));

        // act
        navigationContext.InvertMarks(entries);

        // assert
        Assert.Equal(["dir1", "report1.md", "report22.md"], MarkedNames(navigationContext));
    }

    [Fact]
    public async Task When_InvertMarksWithParent_Then_ParentIsNotMarked()
    {
        // arrange
        var (navigationContext, _) = await CreateAsync();
        using var __ = navigationContext;
        await navigationContext.ChangeLocationAsync("dir1");
        var entries = await navigationContext.ListEntriesAsync();

        // act
        navigationContext.InvertMarks(entries);

        // assert
        Assert.Empty(navigationContext.MarkedEntries);
    }

    [Fact]
    public async Task When_MarkGroupWithInvalidRegex_Then_IOExceptionIsThrown()
    {
        // arrange
        var (navigationContext, entries) = await CreateAsync();
        using var _ = navigationContext;

        // act & assert
        Assert.Throws<IOException>(() => navigationContext.MarkGroup(entries, new SelectGroupOptions("(abc")));
    }

    [Theory]
    [InlineData("*.txt", false)]
    [InlineData("a?.txt;*.md", false)]
    [InlineData(@"\.txt$", true)]
    [InlineData("a+", true)]
    [InlineData("(a|b)", true)]
    public void When_IsRegex_Then_RegexCharactersAreDetected(string filter, bool expected) =>
        Assert.Equal(expected, NameFilter.IsRegex(filter));
}
