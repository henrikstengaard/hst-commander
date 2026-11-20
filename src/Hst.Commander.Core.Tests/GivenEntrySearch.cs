using System.Text;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenEntrySearch : IDisposable
{
    private readonly string rootPath;

    public GivenEntrySearch()
    {
        // root: a.txt, b.TXT, latin.txt, data.bin, archive.zip, sub1/c.txt, sub1/sub2/d.txt, sub1/sub2/sub3/e.txt
        rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(rootPath, "sub1", "sub2", "sub3"));
        File.WriteAllText(Path.Combine(rootPath, "a.txt"), "Hello World");
        File.WriteAllText(Path.Combine(rootPath, "b.TXT"), "something else");
        File.WriteAllBytes(Path.Combine(rootPath, "latin.txt"), Encoding.Latin1.GetBytes("un café noir"));
        File.WriteAllBytes(Path.Combine(rootPath, "data.bin"), [0x00, 0xDE, 0xAD, 0xBE, 0xEF, 0x01]);
        File.WriteAllBytes(Path.Combine(rootPath, "archive.zip"), ZipTestData.CreateZipWithDirectoriesAndFiles());
        File.WriteAllText(Path.Combine(rootPath, "sub1", "c.txt"), "a needle in a haystack");
        File.WriteAllText(Path.Combine(rootPath, "sub1", "sub2", "d.txt"), "deep");
        File.WriteAllText(Path.Combine(rootPath, "sub1", "sub2", "sub3", "e.txt"), "deeper");
    }

    public void Dispose() => Directory.Delete(rootPath, true);

    private sealed class ListProgress<T> : IProgress<T>
    {
        public List<T> Items { get; } = [];

        public void Report(T value)
        {
            lock (Items)
            {
                Items.Add(value);
            }
        }
    }

    private async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchOptions options,
        NavigationContext? navigationContext = null)
    {
        using var ownContext = navigationContext == null ? new NavigationContext(rootPath) : null;
        var results = new ListProgress<SearchResult>();
        await (navigationContext ?? ownContext!).SearchAsync(options, results);
        return results.Items;
    }

    private SearchOptions Options(string namePattern) => SearchOptions.Default with
    {
        StartPath = rootPath,
        NamePattern = namePattern
    };

    private static IEnumerable<string> Names(IEnumerable<SearchResult> results) =>
        results.Select(result => result.Name).Order(StringComparer.Ordinal);

    [Fact]
    public async Task When_SearchWithWildcard_Then_MatchingEntriesInAllSubdirectoriesAreFound()
    {
        // act
        var results = await SearchAsync(Options("*.txt"));

        // assert
        Assert.Equal(["a.txt", "b.TXT", "c.txt", "d.txt", "e.txt", "latin.txt"], Names(results));
        var c = results.Single(result => result.Name == "c.txt");
        Assert.Equal(Path.Combine(rootPath, "sub1"), c.DirectoryPath);
        Assert.Equal(Path.Combine(rootPath, "sub1", "c.txt"), c.FullPath);
    }

    [Fact]
    public async Task When_SearchCaseSensitive_Then_OnlyNamesWithSameCaseAreFound()
    {
        // act
        var results = await SearchAsync(Options("*.TXT") with { CaseSensitive = true });

        // assert
        Assert.Equal(["b.TXT"], Names(results));
    }

    [Fact]
    public async Task When_SearchWithRegex_Then_MatchingNamesAreFound()
    {
        // act
        var autoDetected = await SearchAsync(Options(@"^[a-c]\.txt$"));
        var forced = await SearchAsync(Options("sub.") with { NameIsRegex = true });

        // assert
        Assert.Equal(["a.txt", "b.TXT", "c.txt"], Names(autoDetected));
        Assert.Equal(["sub1", "sub2", "sub3"], Names(forced));
    }

    [Theory]
    [InlineData(SearchDepth.Current, 0, new[] { "a.txt", "b.TXT", "latin.txt" })]
    [InlineData(SearchDepth.Levels, 1, new[] { "a.txt", "b.TXT", "c.txt", "latin.txt" })]
    [InlineData(SearchDepth.Levels, 2, new[] { "a.txt", "b.TXT", "c.txt", "d.txt", "latin.txt" })]
    [InlineData(SearchDepth.All, 0, new[] { "a.txt", "b.TXT", "c.txt", "d.txt", "e.txt", "latin.txt" })]
    public async Task When_SearchWithDepth_Then_SubdirectoriesAreSearchedAsDeep(SearchDepth depth, int levels,
        string[] expected)
    {
        // act
        var results = await SearchAsync(Options("*.txt") with { Depth = depth, Levels = levels });

        // assert
        Assert.Equal(expected, Names(results));
    }

    [Fact]
    public async Task When_SearchForText_Then_FilesContainingItCaseInsensitiveAreFound()
    {
        // act
        var results = await SearchAsync(Options("*") with
        {
            ContentType = SearchContentType.Text, Content = "NEEDLE"
        });

        // assert
        Assert.Equal(["c.txt"], Names(results));
    }

    [Fact]
    public async Task When_SearchForNonAsciiText_Then_Latin1FilesAreFound()
    {
        // act
        var results = await SearchAsync(Options("*") with { ContentType = SearchContentType.Text, Content = "café" });

        // assert
        Assert.Equal(["latin.txt"], Names(results));
    }

    [Fact]
    public async Task When_SearchForRegex_Then_FilesMatchingItAreFound()
    {
        // act
        var results = await SearchAsync(Options("*.txt") with
        {
            ContentType = SearchContentType.Regex, Content = @"^deep(er)?$"
        });

        // assert
        Assert.Equal(["d.txt", "e.txt"], Names(results));
    }

    [Fact]
    public async Task When_SearchForHex_Then_FilesContainingTheBytesAreFound()
    {
        // act
        var results = await SearchAsync(Options("*") with { ContentType = SearchContentType.Hex, Content = "de ad be ef" });

        // assert
        Assert.Equal(["data.bin"], Names(results));
    }

    [Fact]
    public async Task When_SearchInImages_Then_EntriesInArchivesAreFound()
    {
        // act
        var withoutImages = await SearchAsync(Options("file*.txt"));
        var withImages = await SearchAsync(Options("file*.txt") with { SearchInImages = true });

        // assert
        Assert.Empty(withoutImages);
        Assert.Equal(["file1.txt", "file2.txt"], Names(withImages));
        var file2 = withImages.Single(result => result.Name == "file2.txt");
        Assert.Equal(Path.Combine(rootPath, "archive.zip", "dir1"), file2.DirectoryPath);
    }

    [Fact]
    public async Task When_GoToSearchResult_Then_LocationIsChangedAndEntryIsSelected()
    {
        // arrange
        using var navigationContext = new NavigationContext(rootPath);
        var result = (await SearchAsync(Options("file2.txt") with { SearchInImages = true }, navigationContext))
            .Single();

        // act
        await navigationContext.GoToSearchResultAsync(result);
        var entries = await navigationContext.ListEntriesAsync();
        var selectedIndex = navigationContext.GetSelectedIndex(entries, rootPath, 0);

        // assert
        Assert.Equal(Path.Combine(rootPath, "archive.zip", "dir1"), navigationContext.CurrentPath);
        Assert.Equal("file2.txt", entries[selectedIndex].Name);
    }

    [Fact]
    public async Task When_GoToSearchResultInCurrentLocation_Then_EntryIsSelected()
    {
        // arrange
        using var navigationContext = new NavigationContext(rootPath);
        var result = (await SearchAsync(Options("latin.txt"), navigationContext)).Single();

        // act
        await navigationContext.GoToSearchResultAsync(result);
        var entries = await navigationContext.ListEntriesAsync();
        var selectedIndex = navigationContext.GetSelectedIndex(entries, rootPath, 0);

        // assert
        Assert.Equal("latin.txt", entries[selectedIndex].Name);
    }

    [Fact]
    public async Task When_SearchTestVolumeProvider_Then_EntriesAreFoundAndCanBeGoneTo()
    {
        // arrange
        var volumeProvider = new TestVolumeProvider(volume =>
        {
            volume.AddEntry([], new TestEntry("dir1", EntryType.Container));
            volume.AddEntry(["dir1"], new TestEntry("file2.txt", EntryType.Item, 5, data: "file2"u8.ToArray()));
        });
        using var navigationContext = new NavigationContext(rootPath);
        await navigationContext.ChangeVolumeProviderAsync(volumeProvider);
        var options = SearchOptions.Default with { StartPath = navigationContext.CurrentPath, NamePattern = "*.txt" };

        // act
        var result = (await SearchAsync(options, navigationContext)).Single();
        await navigationContext.ChangeLocationAsync(rootPath);
        await navigationContext.GoToSearchResultAsync(result);

        // assert
        Assert.Same(volumeProvider, navigationContext.VolumeProvider);
        Assert.Equal("dir1", navigationContext.CurrentPath);
        Assert.Equal("file2.txt", navigationContext.PreviousEntryName);
    }

    [Fact]
    public async Task When_SearchIsCancelled_Then_OperationCanceledExceptionIsThrown()
    {
        // arrange
        using var navigationContext = new NavigationContext(rootPath);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // act and assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            navigationContext.SearchAsync(Options("*"), null, null, cancellation.Token));
    }

    [Fact]
    public async Task When_SearchWithInvalidRegex_Then_IOExceptionIsThrown()
    {
        // act and assert
        await Assert.ThrowsAsync<IOException>(() => SearchAsync(Options("*") with
        {
            ContentType = SearchContentType.Regex, Content = "("
        }));
    }

    [Fact]
    public async Task When_SearchCompletes_Then_ProgressIsReported()
    {
        // arrange
        using var navigationContext = new NavigationContext(rootPath);
        var progress = new ListProgress<SearchProgress>();

        // act
        await navigationContext.SearchAsync(Options("*.txt") with { Depth = SearchDepth.Current }, null, progress);

        // assert
        var last = progress.Items.Last();
        Assert.True(last.IsComplete);
        Assert.Equal(3, last.Found);
        Assert.Equal(1, last.Directories);
        Assert.Equal(5, last.Files);
    }

    [Theory]
    [InlineData("DE AD BE EF", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })]
    [InlineData("deadbeef", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF })]
    [InlineData("0xDE, 0xAD", new byte[] { 0xDE, 0xAD })]
    [InlineData("$4d-$5a", new byte[] { 0x4D, 0x5A })]
    public void When_ParseHex_Then_BytesAreReturned(string text, byte[] expected) =>
        Assert.Equal(expected, ContentMatcher.ParseHex(text));

    [Theory]
    [InlineData("ABC")]
    [InlineData("GG")]
    [InlineData("  ")]
    public void When_ParseInvalidHex_Then_IOExceptionIsThrown(string text) =>
        Assert.Throws<IOException>(() => ContentMatcher.ParseHex(text));

    [Theory]
    [InlineData(SearchContentType.Text, "needle")]
    [InlineData(SearchContentType.Hex, "6E 65 65 64 6C 65")]
    [InlineData(SearchContentType.Regex, "ne+dle")]
    public async Task When_MatchAcrossChunks_Then_MatchIsFound(SearchContentType contentType, string content)
    {
        // arrange: "needle" starts 3 bytes before the end of the first chunk
        var data = new byte[ContentMatcher.ChunkSize * 2];
        "needle"u8.CopyTo(data.AsSpan(ContentMatcher.ChunkSize - 3));
        var matcher = ContentMatcher.Create(contentType, content, false)!;

        // act
        var isMatch = await matcher.IsMatchAsync(new NonSeekableStream(new MemoryStream(data)));

        // assert
        Assert.True(isMatch);
    }

    [Fact]
    public void When_ContentIsEmpty_Then_NoMatcherIsCreated() =>
        Assert.Null(ContentMatcher.Create(SearchContentType.Text, string.Empty, false));

    private static readonly TestVolumeProvider SortVolumeProvider = new(_ => { });

    private static SearchResult CreateResult(string name, EntryType type, long? size, DateTime? modified) =>
        new("dir", new TestEntry(name, type, size, modified), SortVolumeProvider, null);

    private static readonly SearchResult[] SortResults =
    [
        CreateResult("b.txt", EntryType.Item, 300, new DateTime(2024, 1, 1)),
        CreateResult("a.txt", EntryType.Item, 100, new DateTime(2026, 1, 1)),
        CreateResult("sub", EntryType.Container, null, new DateTime(2025, 1, 1)),
        CreateResult("C.txt", EntryType.Item, 200, new DateTime(2023, 1, 1))
    ];

    [Theory]
    [InlineData(SearchResultColumn.Path, false, new[] { "a.txt", "b.txt", "C.txt", "sub" })]
    [InlineData(SearchResultColumn.Path, true, new[] { "sub", "C.txt", "b.txt", "a.txt" })]
    [InlineData(SearchResultColumn.Size, false, new[] { "sub", "a.txt", "C.txt", "b.txt" })]
    [InlineData(SearchResultColumn.Size, true, new[] { "b.txt", "C.txt", "a.txt", "sub" })]
    [InlineData(SearchResultColumn.Modified, false, new[] { "C.txt", "b.txt", "sub", "a.txt" })]
    [InlineData(SearchResultColumn.Modified, true, new[] { "a.txt", "sub", "b.txt", "C.txt" })]
    public void When_SortResults_Then_ResultsAreOrderedByColumn(SearchResultColumn column, bool descending,
        string[] expected)
    {
        // act
        var sorted = new SearchResultSort(column, descending).Sort(SortResults);

        // assert
        Assert.Equal(expected, sorted.Select(result => result.Name));
    }

    [Fact]
    public void When_ResultsAreInsertedInOrder_Then_TheyAreSorted()
    {
        // arrange
        var sort = new SearchResultSort(SearchResultColumn.Size, true);
        var results = new List<SearchResult>();

        // act
        foreach (var result in SortResults)
        {
            results.Insert(sort.IndexToInsert(results, result), result);
        }

        // assert
        Assert.Equal(sort.Sort(SortResults), results);
    }

    [Fact]
    public void When_ToggleColumn_Then_SameColumnIsReversedAndOtherColumnIsAscending()
    {
        // act
        var descending = SearchResultSort.Default.Toggle(SearchResultColumn.Path);
        var size = descending.Toggle(SearchResultColumn.Size);

        // assert
        Assert.Equal(new SearchResultSort(SearchResultColumn.Path, true), descending);
        Assert.Equal(new SearchResultSort(SearchResultColumn.Size), size);
        Assert.Equal("Path ▼", descending.GetHeader(SearchResultColumn.Path));
        Assert.Equal("Size ▲", size.GetHeader(SearchResultColumn.Size));
        Assert.Equal("Path", size.GetHeader(SearchResultColumn.Path));
    }
}
