using System.Runtime.CompilerServices;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// Volume whose root lists a list of volumes as a directory named <see cref="ListName" />, e.g. the partition table "mbr",
/// listing the volumes of their own as directories, e.g. the partitions of a disk. A volume is mounted when listed or
/// opened and stays mounted until this volume is disposed; one that can't be mounted is only tried once when listed.
/// The list itself can't be changed, the mounted volumes are changed directly.
/// </summary>
public abstract class VolumeListVolume : IVolume, IDisposable
{
    private readonly Dictionary<int, IVolume> _mountedVolumes = new();
    private readonly HashSet<int> _unmountableVolumes = [];
    private bool _inList;
    private int? _index;

    /// <summary>Name of the volume list, e.g. the partition table "MBR".</summary>
    public abstract string Name { get; }

    /// <summary>Name of the directory in the root listing the volumes, <see cref="Name" /> in lower case, e.g. "mbr".</summary>
    protected string ListName => Name.ToLowerInvariant();

    /// <summary>Number of volumes listed.</summary>
    protected abstract int Count { get; }

    /// <summary>Entry of the directory in the root listing the volumes, named <see cref="ListName" />.</summary>
    protected abstract IEntry CreateListEntry();

    /// <summary>Name of the directory of a volume, e.g. "1" for the first partition.</summary>
    protected abstract string GetEntryName(int index);

    /// <summary>Mounts a volume, throwing <see cref="IOException" /> when it can't be mounted.</summary>
    protected abstract Task<IVolume> Mount(int index, CancellationToken cancellationToken);

    /// <summary>Entry of a volume in the list, with the mounted volume or null when it can't be mounted.</summary>
    protected abstract IEntry CreateEntry(int index, IVolume? volume);

    /// <summary>Message of the <see cref="NotSupportedException" /> thrown when changing the list.</summary>
    protected abstract string ReadOnlyMessage { get; }

    /// <summary>Volume of the current directory, null in the list.</summary>
    private IVolume? CurrentVolume => _index is { } index ? _mountedVolumes[index] : null;

    public IEnumerable<string> CurrentPathComponents => !_inList ? []
        : _index is { } index ? [ListName, GetEntryName(index), .._mountedVolumes[index].CurrentPathComponents]
        : [ListName];

    public async IAsyncEnumerable<IEntry> ListEntries(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (CurrentVolume is { } volume)
        {
            await foreach (var entry in volume.ListEntries(cancellationToken))
            {
                yield return entry;
            }

            yield break;
        }

        if (!_inList)
        {
            yield return CreateListEntry();
            yield break;
        }

        for (var index = 0; index < Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreateEntry(index, await TryMount(index, cancellationToken));
        }
    }

    /// <summary>Mounted volume, mounting it the first time; null when it can't be mounted.</summary>
    private async Task<IVolume?> TryMount(int index, CancellationToken cancellationToken)
    {
        if (_mountedVolumes.TryGetValue(index, out var volume))
        {
            return volume;
        }

        if (_unmountableVolumes.Contains(index))
        {
            return null;
        }

        try
        {
            return _mountedVolumes[index] = await Mount(index, cancellationToken);
        }
        catch (IOException)
        {
            _unmountableVolumes.Add(index);
            return null;
        }
    }

    public async Task ChangeDirectory(IEnumerable<string> pathComponents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathComponents);

        var previousInList = _inList;
        var previousIndex = _index;
        var previousPathComponents = CurrentVolume?.CurrentPathComponents.ToList();
        try
        {
            foreach (var pathComponent in pathComponents)
            {
                if (pathComponent.Equals(Paths.Root, StringComparison.OrdinalIgnoreCase))
                {
                    _inList = false;
                    _index = null;
                }
                else if (!_inList)
                {
                    if (!pathComponent.Equals(Paths.Parent, StringComparison.OrdinalIgnoreCase))
                    {
                        OpenList(pathComponent);
                    }
                }
                else if (CurrentVolume is not { } volume)
                {
                    if (pathComponent.Equals(Paths.Parent, StringComparison.OrdinalIgnoreCase))
                    {
                        _inList = false;
                    }
                    else
                    {
                        await OpenVolume(pathComponent, cancellationToken);
                    }
                }
                else if (pathComponent.Equals(Paths.Parent, StringComparison.OrdinalIgnoreCase) &&
                         !volume.CurrentPathComponents.Any())
                {
                    _index = null;
                }
                else
                {
                    await volume.ChangeDirectory([pathComponent], cancellationToken);
                }
            }
        }
        catch
        {
            _inList = previousInList;
            _index = previousIndex;
            if (CurrentVolume is { } volume && previousPathComponents != null)
            {
                await volume.ChangeDirectory([Paths.Root, ..previousPathComponents], cancellationToken);
            }

            throw;
        }
    }

    /// <summary>Changes from the root to the list of the volumes.</summary>
    private void OpenList(string name)
    {
        if (!name.Equals(ListName, StringComparison.OrdinalIgnoreCase))
        {
            throw new DirectoryNotFoundException($"'{name}' not found");
        }

        _inList = true;
    }

    /// <summary>Changes to the root of a volume, mounting it the first time.</summary>
    private async Task OpenVolume(string name, CancellationToken cancellationToken)
    {
        var index = Enumerable.Range(0, Count)
            .Where(index => GetEntryName(index).Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(index => (int?)index)
            .FirstOrDefault() ?? throw new DirectoryNotFoundException($"'{name}' not found");

        if (_mountedVolumes.TryGetValue(index, out var volume))
        {
            await volume.ChangeDirectory([Paths.Root], cancellationToken);
        }
        else
        {
            _mountedVolumes[index] = await Mount(index, cancellationToken);
            _unmountableVolumes.Remove(index);
        }

        _index = index;
    }

    public async Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default)
    {
        if (CurrentVolume is { } volume)
        {
            return await volume.OpenEntry(name, cancellationToken);
        }

        if (!_inList)
        {
            OpenList(name);
            return CreateListEntry();
        }

        await OpenVolume(name, cancellationToken);
        return CreateEntry(_index!.Value, CurrentVolume);
    }

    public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default) =>
        CurrentVolume?.OpenRead(name, cancellationToken) ??
        throw new FileNotFoundException($"File '{name}' not found");

    public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default) =>
        CurrentVolume?.OpenWrite(name, cancellationToken) ?? throw new NotSupportedException(ReadOnlyMessage);

    public Task CreateContainer(string name, CancellationToken cancellationToken = default) =>
        CurrentVolume?.CreateContainer(name, cancellationToken) ?? throw new NotSupportedException(ReadOnlyMessage);

    public Task Delete(string name, CancellationToken cancellationToken = default) =>
        CurrentVolume?.Delete(name, cancellationToken) ?? throw new NotSupportedException(ReadOnlyMessage);

    public Task Rename(string name, string newName, CancellationToken cancellationToken = default) =>
        CurrentVolume?.Rename(name, newName, cancellationToken) ?? throw new NotSupportedException(ReadOnlyMessage);

    public Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default) =>
        CurrentVolume?.SetModifiedDate(name, modifiedDate, cancellationToken) ??
        throw new NotSupportedException(ReadOnlyMessage);

    public Task<bool> SupportsAmigaMetadata(CancellationToken cancellationToken = default) =>
        CurrentVolume?.SupportsAmigaMetadata(cancellationToken) ?? Task.FromResult(false);

    public Task SetAmigaMetadata(string name, AmigaMetadata metadata, CancellationToken cancellationToken = default) =>
        CurrentVolume?.SetAmigaMetadata(name, metadata, cancellationToken) ??
        throw new NotSupportedException(ReadOnlyMessage);

    public Task<bool> CanSetAmigaMetadata(CancellationToken cancellationToken = default) =>
        CurrentVolume?.CanSetAmigaMetadata(cancellationToken) ?? Task.FromResult(false);

    public Task<EntryAttributes> GetChangeableAttributes(CancellationToken cancellationToken = default) =>
        CurrentVolume?.GetChangeableAttributes(cancellationToken) ?? Task.FromResult(EntryAttributes.None);

    public Task SetAttributes(string name, EntryAttributes attributes, CancellationToken cancellationToken = default) =>
        CurrentVolume?.SetAttributes(name, attributes, cancellationToken) ??
        throw new NotSupportedException(ReadOnlyMessage);

    /// <summary>Disposes resources after the mounted volumes, e.g. the streams of the partitions.</summary>
    protected virtual void DisposeResources()
    {
    }

    public void Dispose()
    {
        foreach (var volume in _mountedVolumes.Values)
        {
            (volume as IDisposable)?.Dispose();
        }

        _mountedVolumes.Clear();
        DisposeResources();
        GC.SuppressFinalize(this);
    }
}
