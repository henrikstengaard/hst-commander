using System.Runtime.CompilerServices;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core.Tests;

/// <summary>
/// Test volume used to represent a volume with containers and items for unit tests.
/// </summary>
public class TestVolume : IVolume
{
    private readonly Dictionary<string, List<TestEntry>> entries = new();
    private IList<string> currentPathComponents = new List<string> { Paths.Root };

    public void AddEntry(string[] pathComponents, TestEntry entry)
    {
        var path = CreatePath(pathComponents);
        
        if (!entries.ContainsKey(path))
        {
            entries[path] = [];
        }
        
        entries[path].Add(entry);
    }
    
    public IEnumerable<string> CurrentPathComponents => currentPathComponents;
    
    private static string CreatePath(IEnumerable<string> pathComponents)
    {
        var pathComponentsArray = pathComponents as string[] ?? pathComponents.ToArray();
        var components = pathComponentsArray.Length == 0 ||
                        !pathComponentsArray[0].Equals(Paths.Root, StringComparison.OrdinalIgnoreCase)
            ? new[] { Paths.Root }.Concat(pathComponentsArray)
            : pathComponentsArray;
        return string.Join("|", components);
    }

    public async IAsyncEnumerable<IEntry> ListEntries([EnumeratorCancellation] CancellationToken cancellationToken =
        default)
    {
        foreach (var entry in await GetEntriesFromCurrentPath())
        {
            yield return entry;
        }
    }

    private Task<List<TestEntry>> GetEntriesFromCurrentPath()
    {
        var currentPath = CreatePath(currentPathComponents);
        return Task.FromResult(entries.TryGetValue(currentPath, out var pathEntries) 
            ? pathEntries : []);
    }

    public Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default)
    {
        var pathComponentsArray = pathComponents as string[] ?? pathComponents.ToArray();

        var changedPathComponents = pathComponentsArray.Length == 0 || pathComponentsArray[0].Equals(Paths.Root)
            ? pathComponentsArray
            : currentPathComponents.Concat(pathComponentsArray).ToArray();

        var path = CreatePath(changedPathComponents);
        if (!entries.ContainsKey(path))
        {
            throw new IOException("Path not found");
        }

        currentPathComponents = changedPathComponents;
        
        return Task.CompletedTask;
    }

    public Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default)
    {
        var path = CreatePath(currentPathComponents);
        if (!entries.ContainsKey(path))
        {
            throw new IOException("Dir not found");
        }

        IEntry entry = entries[path].FirstOrDefault(entry => entry.Name.Equals(name));
        
        if (entry == null)
        {
            throw new IOException("Entry not found");
        }

        if (entry.Type == EntryType.Container)
        {
            currentPathComponents.Add(entry.Name);
        }
        
        return Task.FromResult(entry);
    }

    public string Name => "Test";
}