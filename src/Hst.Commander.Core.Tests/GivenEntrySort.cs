using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenEntrySort
{
    private static readonly IReadOnlyList<IEntry> ListedEntries =
    [
        new TestEntry(Paths.Parent, EntryType.Container),
        new TestEntry("b-dir", EntryType.Container, null, new DateTime(2024, 1, 1)),
        new TestEntry("a-dir", EntryType.Container, null, new DateTime(2025, 1, 1)),
        new TestEntry("a.txt", EntryType.Item, 300, new DateTime(2023, 1, 1)),
        new TestEntry("b.txt", EntryType.Item, 100, new DateTime(2026, 1, 1)),
        new TestEntry("c.txt", EntryType.Item, 300, new DateTime(2022, 1, 1))
    ];

    private static List<string> SortNames(EntrySort sort) => sort.Sort(ListedEntries).Select(e => e.Name).ToList();

    [Fact]
    public void When_SortByNameAscending_Then_ListedOrderIsKept()
    {
        Assert.Same(ListedEntries, EntrySort.Default.Sort(ListedEntries));
    }

    [Fact]
    public void When_SortByNameDescending_Then_DirectoriesAndFilesAreReversedAfterParent()
    {
        Assert.Equal([Paths.Parent, "a-dir", "b-dir", "c.txt", "b.txt", "a.txt"],
            SortNames(new EntrySort(EntryColumn.Name, true)));
    }

    [Fact]
    public void When_SortBySize_Then_EqualSizesAreOrderedByName()
    {
        Assert.Equal([Paths.Parent, "b-dir", "a-dir", "b.txt", "a.txt", "c.txt"],
            SortNames(new EntrySort(EntryColumn.Size)));
        Assert.Equal([Paths.Parent, "a-dir", "b-dir", "c.txt", "a.txt", "b.txt"],
            SortNames(new EntrySort(EntryColumn.Size, true)));
    }

    [Fact]
    public void When_SortByModified_Then_DirectoriesStayBeforeFiles()
    {
        Assert.Equal([Paths.Parent, "b-dir", "a-dir", "c.txt", "a.txt", "b.txt"],
            SortNames(new EntrySort(EntryColumn.Modified)));
        Assert.Equal([Paths.Parent, "a-dir", "b-dir", "b.txt", "a.txt", "c.txt"],
            SortNames(new EntrySort(EntryColumn.Modified, true)));
    }

    [Fact]
    public void When_ToggleColumn_Then_SameColumnIsReversedAndOtherColumnIsAscending()
    {
        var descending = EntrySort.Default.Toggle(EntryColumn.Name);
        var size = descending.Toggle(EntryColumn.Size);

        Assert.Equal(new EntrySort(EntryColumn.Name, true), descending);
        Assert.Equal(new EntrySort(EntryColumn.Size), size);
        Assert.Equal("Name ▼", descending.GetHeader(EntryColumn.Name));
        Assert.Equal("Size ▲", size.GetHeader(EntryColumn.Size));
        Assert.Equal("Name", size.GetHeader(EntryColumn.Name));
    }

    [Fact]
    public void When_SerializedToJsonAndBack_Then_SortIsKept()
    {
        // as saved in the settings of the front-ends
        var options = new System.Text.Json.JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        var sort = new EntrySort(EntryColumn.Modified, true);

        var json = System.Text.Json.JsonSerializer.Serialize(sort, options);

        Assert.Equal("""{"Column":"Modified","Descending":true}""", json);
        Assert.Equal(sort, System.Text.Json.JsonSerializer.Deserialize<EntrySort>(json, options));
    }

    [Fact]
    public async Task When_ToggleSortOfNavigationContext_Then_ListedEntriesAreSortedAndStaySortedWhenListedAgain()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hst-commander-sort-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(path, "a.txt"), new byte[10]);
            await File.WriteAllBytesAsync(Path.Combine(path, "b.txt"), new byte[30]);
            await File.WriteAllBytesAsync(Path.Combine(path, "c.txt"), new byte[20]);
            using var context = new NavigationContext(path);
            var listed = (await context.ListEntriesAsync()).Select(e => e.Name).Where(n => n != Paths.Parent);
            Assert.Equal(["a.txt", "b.txt", "c.txt"], listed);

            context.ToggleSort(EntryColumn.Size);
            var sorted = context.ToggleSort(EntryColumn.Size).Select(e => e.Name).Where(n => n != Paths.Parent);
            var listedAgain = (await context.ListEntriesAsync()).Select(e => e.Name).Where(n => n != Paths.Parent);

            Assert.Equal(["b.txt", "c.txt", "a.txt"], sorted);
            Assert.Equal(["b.txt", "c.txt", "a.txt"], listedAgain);
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }
}
