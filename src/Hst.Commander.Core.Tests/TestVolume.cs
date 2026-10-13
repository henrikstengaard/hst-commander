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

        // containers can be changed to, even when empty
        var containerPath = CreatePath(pathComponents.Append(entry.Name));
        if (entry.Type == EntryType.Container && !entries.ContainsKey(containerPath))
        {
            entries[containerPath] = [];
        }
    }

    /// <summary>
    /// Get an entry by path components, e.g. ["dir1", "file1.txt"]. Returns null when not found.
    /// </summary>
    public TestEntry? GetEntry(string[] pathComponents)
    {
        var path = CreatePath(pathComponents[..^1]);
        return entries.TryGetValue(path, out var pathEntries)
            ? pathEntries.FirstOrDefault(entry => entry.Name.Equals(pathComponents[^1]))
            : null;
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
        var changedPathComponents = currentPathComponents.ToList();
        foreach (var pathComponent in pathComponents)
        {
            if (pathComponent.Equals(Paths.Root))
            {
                changedPathComponents = [Paths.Root];
            }
            else if (pathComponent.Equals(Paths.Parent))
            {
                if (changedPathComponents.Count > 0 && !changedPathComponents[^1].Equals(Paths.Root))
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

    public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default)
    {
        var entry = GetEntry(currentPathComponents.Append(name).ToArray());
        if (entry == null || entry.Type != EntryType.Item)
        {
            throw new IOException("Entry not found");
        }

        return Task.FromResult<Stream>(new MemoryStream(entry.Data ?? [], false));
    }

    public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default)
    {
        var entry = GetEntry(currentPathComponents.Append(name).ToArray());
        if (entry == null)
        {
            entry = new TestEntry(name, EntryType.Item);
            AddEntry([..currentPathComponents], entry);
        }
        else if (entry.Type != EntryType.Item)
        {
            throw new IOException($"Entry '{name}' is a container");
        }

        return Task.FromResult<Stream>(new TestWriteStream(data =>
        {
            entry.Data = data;
            entry.Size = data.Length;
        }));
    }

    public Task CreateContainer(string name, CancellationToken cancellationToken = default)
    {
        var entry = GetEntry(currentPathComponents.Append(name).ToArray());
        if (entry == null)
        {
            AddEntry([..currentPathComponents], new TestEntry(name, EntryType.Container));
        }
        else if (entry.Type != EntryType.Container)
        {
            throw new IOException($"Entry '{name}' is not a container");
        }

        return Task.CompletedTask;
    }

    public Task Delete(string name, CancellationToken cancellationToken = default)
    {
        var entry = GetEntry(currentPathComponents.Append(name).ToArray()) ??
                    throw new IOException("Entry not found");
        var containerPath = CreatePath(currentPathComponents.Append(name));
        if (entry.Type == EntryType.Container)
        {
            if (entries.TryGetValue(containerPath, out var containerEntries) && containerEntries.Count > 0)
            {
                throw new IOException($"Container '{name}' is not empty");
            }

            entries.Remove(containerPath);
        }

        entries[CreatePath(currentPathComponents)].Remove(entry);
        return Task.CompletedTask;
    }

    public Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default)
    {
        var entry = GetEntry(currentPathComponents.Append(name).ToArray()) ??
                    throw new IOException("Entry not found");
        entry.ModifiedDate = modifiedDate;
        return Task.CompletedTask;
    }

    public string Name => "Test";

    /// <summary>
    /// Memory stream that stores the written data in the entry when disposed.
    /// </summary>
    private sealed class TestWriteStream(Action<byte[]> onDispose) : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                onDispose(ToArray());
            }

            base.Dispose(disposing);
        }
    }
}
