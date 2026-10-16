using Hst.Commander.Core.Volumes;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNestedVolumeWithTestVolume
{
    private readonly NestedVolume nestedVolume;

    public GivenNestedVolumeWithTestVolume()
    {
        // arrange - create a local volume with entries using test volume
        var localVolume = new TestVolume();
        localVolume.AddEntry([], new TestEntry("local-dir1", EntryType.Container));
        localVolume.AddEntry(["local-dir1"], new TestEntry("local-cdrom.iso", EntryType.Item));

        // arrange - create a nested volume with the local volume
        nestedVolume = new NestedVolume(localVolume);
    }

    [Fact]
    public async Task When_ListEntries_Then_NestedVolumeListsEntriesFromLocalVolume()
    {
        // assert - current path components of the nested volume is root
        Assert.Equal([], nestedVolume.CurrentPathComponents);
        
        // act - list entries in entry "cdrom.iso" from the nested volume
        var entries = await nestedVolume.ListEntries().ToListAsync();
        
        // assert - entries has 1 entry with contents of iso9660 volume
        Assert.Single(entries);
        var cdDir1Entry = entries.FirstOrDefault(entry => entry.Name.Equals("local-dir1") && entry.Type == EntryType.Container);
        Assert.Equal("local-dir1", cdDir1Entry?.Name);
    }

    [Fact]
    public async Task When_Iso9660VolumeIsPushed_Then_NestedVolumeListsEntriesFromIso9660Volume()
    {
        // arrange - create a iso9660 volume with entries using test volume
        var iso9660VolumeProvider = new TestVolume();
        iso9660VolumeProvider.AddEntry([], new TestEntry("iso9660-dir1", EntryType.Container));
        iso9660VolumeProvider.AddEntry(["iso9660-dir1"], new TestEntry("iso9660-file1.txt", EntryType.Item));
        
        // act - open entry "local-dir1"
        await nestedVolume.OpenEntry("local-dir1");

        // assert - current path components of the nested volume is changed to "local-dir1"
        Assert.Equal(["local-dir1"], nestedVolume.CurrentPathComponents);
        
        // act - open entry "local-cdrom.iso"
        var cdRomIsoEntry = await nestedVolume.OpenEntry("local-cdrom.iso");
        
        // simulate a registry is used to determine the cdrom.iso entry stream can be read
        // by a iso9660 volume and pushes it to the nested volume
        var entryVolumeProvider = new EntryVolumeProvider(cdRomIsoEntry, iso9660VolumeProvider);
        nestedVolume.PushVolume(entryVolumeProvider);
        
        // assert - current path components of the nested volume is changed to "cdrom.iso"
        Assert.Equal(["local-dir1", "local-cdrom.iso"], nestedVolume.CurrentPathComponents);
        
        // act - list entries in entry "cdrom.iso" from the nested volume
        var entries = await nestedVolume.ListEntries().ToListAsync();
        
        // assert - entries has 1 entry with contents of iso9660 volume
        Assert.Single(entries);
        var cdDir1Entry = entries.FirstOrDefault(entry => entry.Name.Equals("iso9660-dir1") && entry.Type == EntryType.Container);
        Assert.Equal("iso9660-dir1", cdDir1Entry?.Name);
    }

    [Fact]
    public async Task When_ChangingToParentAtNestedVolumeRoot_Then_PreviousVolumeIsRestored()
    {
        var localVolume = new TestVolume();
        localVolume.AddEntry([], new TestEntry("local-dir", EntryType.Container));
        var volume = new NestedVolume(localVolume);
        volume.PushVolume(new TestVolume());

        await volume.ChangeDirectory([Paths.Parent]);

        var entries = await volume.ListEntries().ToListAsync();
        Assert.Single(entries);
        Assert.Equal("local-dir", entries[0].Name);
    }

    [Fact]
    public void When_EntryVolumeProviderIsAtRoot_Then_NestedVolumeReportsCurrentVolumeAtRoot()
    {
        var volume = new NestedVolume(new TestVolume());
        volume.PushVolume(new EntryVolumeProvider(
            new TestEntry("sample.iso", EntryType.Item),
            new TestVolume()));

        Assert.True(volume.CurrentVolumeIsAtRoot);
    }
}