using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenTestVolume
{
    private readonly TestVolume testVolume;

    public GivenTestVolume()
    {
        testVolume = new TestVolume();
        testVolume.AddEntry([Paths.Root], new TestEntry("file1.txt", EntryType.Item));
        testVolume.AddEntry([Paths.Root], new TestEntry("file2.txt", EntryType.Item));
        testVolume.AddEntry([Paths.Root], new TestEntry("dir1", EntryType.Container));
        testVolume.AddEntry([Paths.Root, "dir1"], new TestEntry("file3.txt", EntryType.Item));
    }

    [Fact]
    public void When_GetCurrentPathComponentsFromRoot_Then_RootIsReturned()
    {
        // act
        var currentPathComponents = testVolume.CurrentPathComponents.ToArray();

        // assert
        Assert.Single(currentPathComponents);
        Assert.Equal([Paths.Root], currentPathComponents);
    }
    
    [Fact]
    public async Task When_GetCurrentPathComponentsFromDirectory_Then_DirectoryIsReturned()
    {
        // arrange
        await testVolume.ChangeDirectory(["dir1"]);
        
        // act
        var currentPathComponents = testVolume.CurrentPathComponents.ToArray();

        // assert
        Assert.Equal(2, currentPathComponents.Length);
        Assert.Equal([Paths.Root, "dir1"], currentPathComponents);
    }
    
    [Fact]
    public async Task When_ListingEntriesInRoot_Then_EntriesAreReturned()
    {
        // act
        var entries = await testVolume.ListEntries().ToListAsync();
        
        // assert
        Assert.Equal(3, entries.Count);
        var file1TxtEntry = entries.FirstOrDefault(entry => entry.Name.Equals("file1.txt") && entry.Type == EntryType.Item);
        Assert.Equal("file1.txt", file1TxtEntry?.Name);
        var file2TxtEntry = entries.FirstOrDefault(entry => entry.Name.Equals("file2.txt") && entry.Type == EntryType.Item);
        Assert.Equal("file2.txt", file2TxtEntry?.Name);
        var dir1Entry = entries.FirstOrDefault(entry => entry.Name.Equals("dir1") && entry.Type == EntryType.Container);
        Assert.Equal("dir1", dir1Entry?.Name);
    }

    [Fact]
    public async Task When_ListingEntriesInDirectory_Then_EntriesAreReturned()
    {
        // arrange
        await testVolume.ChangeDirectory(["dir1"]);

        // act
        var entries = await testVolume.ListEntries().ToListAsync();
        
        // assert
        Assert.Single(entries);
        var file1TxtEntry = entries.FirstOrDefault(entry => entry.Name.Equals("file3.txt") && entry.Type == EntryType.Item);
        Assert.Equal("file3.txt", file1TxtEntry?.Name);
    }
}