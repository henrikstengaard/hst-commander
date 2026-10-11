using Hst.Commander.Core.Volumes;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenLocalVolume
{
    [Fact]
    public async Task When_ListEntriesInRoot_Then_LocalVolumeListsEntries()
    {
        // arrange - create local volume with entries
        var localVolume = new LocalVolume(string.Empty);

        // act - list entries in root
        var entries = await localVolume.ListEntries().ToListAsync();
        
        // assert - root contains entries
        Assert.NotEmpty(entries);
    }

    [Fact]
    public async Task When_ListEntriesInDirectory_Then_LocalVolumeListsEntries()
    {
        var localPath = $"local-{Guid.NewGuid()}";

        try
        {
            await CreateLocalDirectoriesAndFiles(localPath);

            // arrange - create local volume with entries
            var localVolume = new LocalVolume(localPath);
            
            // act - list entries in entries
            var entries = await localVolume.ListEntries().ToListAsync();
            
            // assert - directory lists 3 entries from local volume
            Assert.Equal(3, entries.Count);
            var dir1Entry = entries.FirstOrDefault(entry => entry.Name.Equals("dir1") && entry.Type == EntryType.Container);
            Assert.Equal("dir1", dir1Entry?.Name);
            var file1TxtEntry = entries.FirstOrDefault(entry => entry.Name.Equals("file1.txt") && entry.Type == EntryType.Item);
            Assert.Equal("file1.txt", file1TxtEntry?.Name);
            var file2TxtEntry = entries.FirstOrDefault(entry => entry.Name.Equals("file2.txt") && entry.Type == EntryType.Item);
            Assert.Equal("file2.txt", file2TxtEntry?.Name);
        }
        finally
        {
            if (Directory.Exists(localPath))
            {
                Directory.Delete(localPath, true);
            }
        }
    }
    
    [Fact]
    public async Task When_ChangeDirectory_Then_DirectoryIsChanged()
    {
        var localPath = $"local-{Guid.NewGuid()}";
        var localPathComponents = Path.GetFullPath(localPath).Split(Path.DirectorySeparatorChar);

        try
        {
            await CreateLocalDirectoriesAndFiles(localPath);

            // arrange - create local volume with entries
            var localVolume = new LocalVolume(localPath);

            // assert - current path components of the local volume is equal to local path components
            Assert.Equal(localPathComponents, localVolume.CurrentPathComponents);

            // act - change directory to "dir1"
            await localVolume.ChangeDirectory(["dir1"]);
        
            // assert - current path components of the local volume is changed to "dir1"
            Assert.Equal(localPathComponents.Concat(["dir1"]), localVolume.CurrentPathComponents);
        }
        finally
        {
            if (Directory.Exists(localPath))
            {
                Directory.Delete(localPath, true);
            }
        }
    }

    [Fact]
    public async Task When_OpenDirectoryEntry_Then_DirectoryIsChanged()
    {
        // arrange - create local volume with entries
        var localVolume = new LocalVolume(string.Empty);
        
        // assert - current path components of the local volume is root
        Assert.Equal([], localVolume.CurrentPathComponents);

        // arrange - get first entry in root
        var entries = await localVolume.ListEntries().ToListAsync();
        var firstEntry = entries.FirstOrDefault();
        Assert.NotNull(firstEntry);

        // act - open first entry
        await localVolume.OpenEntry(firstEntry.Name);

        // assert - current path components of the local volume is first entry
        Assert.Equal([firstEntry.Name], localVolume.CurrentPathComponents);
    }

    [Fact]
    public async Task When_OpenFileEntry_Then_DirectoryIsNotChanged()
    {
        var localPath = $"local-{Guid.NewGuid()}";
        var localPathComponents = Path.GetFullPath(localPath).Split(Path.DirectorySeparatorChar);

        try
        {
            await CreateLocalDirectoriesAndFiles(localPath);

            // arrange - create local volume with entries
            var localVolume = new LocalVolume(localPath);

            // assert - current path components of the local volume is equal to local path components
            Assert.Equal(localPathComponents, localVolume.CurrentPathComponents);

            // act - open entry "file1.txt"
            var entry = await localVolume.OpenEntry("file1.txt");
        
            // assert - entry is a file
            Assert.NotNull(entry);
            Assert.Equal("file1.txt", entry.Name);
            Assert.Equal(EntryType.Item, entry.Type);
        
            // assert - current path components of the local volume is equal to local path components
            Assert.Equal(localPathComponents, localVolume.CurrentPathComponents);
        }
        finally
        {
            if (Directory.Exists(localPath))
            {
                Directory.Delete(localPath, true);
            }
        }
    }

    private static async Task CreateLocalDirectoriesAndFiles(string path)
    {
        Directory.CreateDirectory(path);
        await File.WriteAllTextAsync(Path.Combine(path, "file1.txt"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(path, "file2.txt"), string.Empty);
        var dir1Path = Path.Combine(path, "dir1");
        Directory.CreateDirectory(dir1Path);
        await File.WriteAllTextAsync(Path.Combine(dir1Path, "file3.txt"), string.Empty);
    }
}