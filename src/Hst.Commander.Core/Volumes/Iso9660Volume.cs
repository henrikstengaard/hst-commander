using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DiscUtils;
using DiscUtils.Iso9660;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Volumes;

public partial class Iso9660Volume(CDReader cdReader) : IVolume
{
    private IList<string> _currentPathComponents = [];

    public string Name => "Iso9660";

    public IEnumerable<string> CurrentPathComponents => _currentPathComponents;

    public async IAsyncEnumerable<IEntry> ListEntries([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var directory in await GetDirectories())
        {
            yield return directory;
        }

        foreach (var file in await GetFiles())
        {
            yield return file;
        }
    }

    private static string CreatePath(IEnumerable<string> pathComponents) => 
        string.Join(Path.DirectorySeparatorChar, pathComponents);

    private static readonly Regex Iso9660ExtensionRegex =
        MyRegex();

    private static string StripIso9660Extension(string path) => 
        Iso9660ExtensionRegex.Replace(path, string.Empty);

    private Task<List<IEntry>> GetDirectories()
    {
        var path = CreatePath(_currentPathComponents);
        var directoryInfo = cdReader.GetDirectoryInfo(path);
        return Task.FromResult(directoryInfo
            .GetDirectories("*", SearchOption.TopDirectoryOnly)
            .OrderBy(directory => directory.Name)
            .Select(CreateEntry).ToList());
    }

    private Task<List<IEntry>> GetFiles()
    {
        var path = CreatePath(_currentPathComponents);
        var directoryInfo = cdReader.GetDirectoryInfo(path);
        return Task.FromResult(directoryInfo
            .GetFiles("*", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file.Name)
            .Select(CreateEntry).ToList());
    }

    private static IEntry CreateEntry(DiscDirectoryInfo directoryInfo) => 
        new Entry(directoryInfo.Name, EntryType.Container, date: directoryInfo.LastWriteTimeUtc);

    private static IEntry CreateEntry(DiscFileInfo fileInfo) => 
        new Entry(StripIso9660Extension(fileInfo.Name), EntryType.Item, fileInfo.Length, fileInfo.LastWriteTimeUtc);

    public Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default)
    {
        var changedPathComponents = _currentPathComponents.ToList();
        foreach (var pathComponent in pathComponents)
        {
            if (pathComponent.Equals(Paths.Root, StringComparison.OrdinalIgnoreCase))
            {
                changedPathComponents.Clear();
            }
            else if (pathComponent.Equals(Paths.Parent, StringComparison.OrdinalIgnoreCase))
            {
                if (changedPathComponents.Count > 0)
                {
                    changedPathComponents.RemoveAt(changedPathComponents.Count - 1);
                }
            }
            else
            {
                changedPathComponents.Add(pathComponent);
            }
        }

        var path = CreatePath(changedPathComponents);
        var directoryInfo = cdReader.GetDirectoryInfo(path);
        if (!directoryInfo.Exists)
        {
            throw new IOException("Dir not found");
        }
        
        _currentPathComponents = changedPathComponents;
        
        return Task.CompletedTask;
    }

    public Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default)
    {
        var path = CreatePath(_currentPathComponents.Concat([name]));
        var fileSystemInfo = cdReader.GetFileSystemInfo(path);
        if (!fileSystemInfo.Exists)
        {
            throw new IOException("Entry not found");
        }

        var directoryInfo = cdReader.GetDirectoryInfo(path);
        if (directoryInfo.Exists)
        {
            var entry = CreateEntry(directoryInfo);
            
            _currentPathComponents.Add(entry.Name);
            
            return Task.FromResult(entry);
        }

        var fileInfo = cdReader.GetFileInfo(path);
        if (!fileInfo.Exists)
        {
            throw new IOException("Entry not found");
        }

        var fileEntry = CreateEntry(fileInfo);

        return Task.FromResult(fileEntry);
    }

    [GeneratedRegex(";\\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex MyRegex();
}