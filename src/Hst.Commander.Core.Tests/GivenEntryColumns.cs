using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Core.Tests;

public class GivenEntryColumns
{
    [Fact]
    public void When_NoColumnsAreChosen_Then_NameSizeAndModifiedAreShown()
    {
        Assert.Equal([EntryColumns.Name, EntryColumns.Size, EntryColumns.Modified], EntryColumns.Resolve(null));
    }

    [Fact]
    public void When_ColumnsAreResolved_Then_UnknownAndDuplicateIdsAreLeftOutAndNameIsAdded()
    {
        var columns = EntryColumns.Resolve(["Modified", "Unknown.Column", "Attributes", "Modified"]);

        Assert.Equal([EntryColumns.Name, EntryColumns.Modified, EntryColumns.Attributes], columns);
    }

    [Fact]
    public void When_ColumnsAreResolved_Then_TheirOrderIsKept()
    {
        var columns = EntryColumns.Resolve(["Size", "Name", "Amiga.ProtectionBits"]);

        Assert.Equal(["Size", "Name", "Amiga.ProtectionBits"], EntryColumns.GetIds(columns));
    }

    [Theory]
    [InlineData(EntryAttributes.None, "----")]
    [InlineData(EntryAttributes.ReadOnly | EntryAttributes.Archive, "r--a")]
    [InlineData(EntryAttributes.ReadOnly | EntryAttributes.Hidden | EntryAttributes.System | EntryAttributes.Archive,
        "rhsa")]
    public void When_AttributesAreFormatted_Then_TheyAreShownAsLetters(EntryAttributes attributes, string expected)
    {
        Assert.Equal(expected, EntryColumns.FormatAttributes(attributes));
    }

    [Fact]
    public void When_EntryHasPermissions_Then_AttributesColumnShowsThem()
    {
        var entry = new Entry("file", EntryType.Item, 1)
        {
            UnixMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead
        };

        Assert.Equal("rw-r--r--", EntryColumns.Attributes.GetText(entry, new EntryColumnContext()));
        Assert.Equal(string.Empty, EntryColumns.Attributes.GetText(new TestEntry("x", EntryType.Item),
            new EntryColumnContext()));
    }

    [Fact]
    public void When_SortedByAttributes_Then_EntriesAreOrderedByTheirText()
    {
        IReadOnlyList<IEntry> entries =
        [
            new Entry("a", EntryType.Item, 1) { Attributes = EntryAttributes.ReadOnly },
            new Entry("b", EntryType.Item, 1) { Attributes = EntryAttributes.Archive },
            new Entry("c", EntryType.Item, 1) { Attributes = EntryAttributes.None }
        ];

        var sorted = new EntrySort(EntryColumns.AttributesId).Sort(entries).Select(entry => entry.Name);

        // "----", "---a", "r---"
        Assert.Equal(["c", "b", "a"], sorted);
    }

    [Fact]
    public void When_SortedByUnknownColumn_Then_ListedOrderIsKept()
    {
        IReadOnlyList<IEntry> entries = [new TestEntry("b", EntryType.Item), new TestEntry("a", EntryType.Item)];

        Assert.Same(entries, new EntrySort("Unknown.Column").Sort(entries));
    }

    [Fact]
    public async Task When_LocalEntriesAreListed_Then_TheyHaveAttributesOfTheHostOs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hst-commander-columns-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            var filePath = Path.Combine(path, "file.txt");
            await File.WriteAllTextAsync(filePath, "text");
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(filePath, FileAttributes.ReadOnly | FileAttributes.Archive);
            }

            using var context = new NavigationContext(path)
            {
                Columns = EntryColumns.Resolve([EntryColumns.NameId, EntryColumns.AttributesId])
            };
            var entry = (await context.ListEntriesAsync()).Single(listed => listed.Name == "file.txt");

            var text = context.GetColumnText(EntryColumns.Attributes, entry);

            Assert.Equal(OperatingSystem.IsWindows() ? "r--a" : FileSystemDetails.FormatUnixPermissions(
                File.GetUnixFileMode(filePath)), text);
        }
        finally
        {
            foreach (var file in Directory.GetFiles(path))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(path, true);
        }
    }
}
