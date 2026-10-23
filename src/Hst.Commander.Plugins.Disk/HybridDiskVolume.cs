using System.Runtime.CompilerServices;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// Volume browsing a hybrid disk with more than one partition table, e.g. a master boot record in the first block and
/// an Amiga rigid disk block in the next blocks. The root lists the entries in the roots of the partition table
/// volumes, their partition tables, e.g. "mbr" and "rdb", each listing its own partitions.
/// </summary>
public sealed class HybridDiskVolume : IVolume, IDisposable
{
    private readonly IReadOnlyList<IVolume> _partitionTables;
    private readonly IDisposable? _resource;
    private IVolume? _currentVolume;

    /// <param name="partitionTables">Volumes of the partition tables, disposed with this volume.</param>
    /// <param name="resource">Resource disposed with the volume after the partition tables, e.g. a virtual disk.</param>
    public HybridDiskVolume(IReadOnlyList<IVolume> partitionTables, IDisposable? resource = null)
    {
        ArgumentNullException.ThrowIfNull(partitionTables);

        _partitionTables = partitionTables;
        _resource = resource;
    }

    public string Name => "Disk";

    public IEnumerable<string> CurrentPathComponents => _currentVolume?.CurrentPathComponents ?? [];

    public async IAsyncEnumerable<IEntry> ListEntries(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_currentVolume is { } volume)
        {
            await foreach (var entry in volume.ListEntries(cancellationToken))
            {
                yield return entry;
            }

            yield break;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var partitionTable in _partitionTables)
        {
            await partitionTable.ChangeDirectory([Paths.Root], cancellationToken);
            await foreach (var entry in partitionTable.ListEntries(cancellationToken))
            {
                if (names.Add(entry.Name))
                {
                    yield return entry;
                }
            }
        }
    }

    /// <summary>Partition table volume with an entry named <paramref name="name" /> in its root, changed to its root.</summary>
    private async Task<IVolume> FindPartitionTable(string name, CancellationToken cancellationToken)
    {
        foreach (var partitionTable in _partitionTables)
        {
            await partitionTable.ChangeDirectory([Paths.Root], cancellationToken);
            await foreach (var entry in partitionTable.ListEntries(cancellationToken))
            {
                if (entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return partitionTable;
                }
            }
        }

        throw new DirectoryNotFoundException($"'{name}' not found");
    }

    public async Task ChangeDirectory(IEnumerable<string> pathComponents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathComponents);

        var previousVolume = _currentVolume;
        var previousPathComponents = _currentVolume?.CurrentPathComponents.ToList();
        try
        {
            foreach (var pathComponent in pathComponents)
            {
                if (pathComponent.Equals(Paths.Root, StringComparison.OrdinalIgnoreCase))
                {
                    _currentVolume = null;
                }
                else if (_currentVolume is not { } volume)
                {
                    if (!pathComponent.Equals(Paths.Parent, StringComparison.OrdinalIgnoreCase))
                    {
                        var partitionTable = await FindPartitionTable(pathComponent, cancellationToken);
                        await partitionTable.ChangeDirectory([pathComponent], cancellationToken);
                        _currentVolume = partitionTable;
                    }
                }
                else
                {
                    await volume.ChangeDirectory([pathComponent], cancellationToken);
                    if (!volume.CurrentPathComponents.Any())
                    {
                        _currentVolume = null;
                    }
                }
            }
        }
        catch
        {
            _currentVolume = previousVolume;
            if (_currentVolume is { } volume && previousPathComponents != null)
            {
                await volume.ChangeDirectory([Paths.Root, ..previousPathComponents], cancellationToken);
            }

            throw;
        }
    }

    public async Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default)
    {
        if (_currentVolume is { } volume)
        {
            return await volume.OpenEntry(name, cancellationToken);
        }

        var partitionTable = await FindPartitionTable(name, cancellationToken);
        var entry = await partitionTable.OpenEntry(name, cancellationToken);
        _currentVolume = partitionTable;
        return entry;
    }

    public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default) =>
        _currentVolume?.OpenRead(name, cancellationToken) ??
        throw new FileNotFoundException($"File '{name}' not found");

    public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default) =>
        _currentVolume?.OpenWrite(name, cancellationToken) ?? throw ReadOnly();

    public Task CreateContainer(string name, CancellationToken cancellationToken = default) =>
        _currentVolume?.CreateContainer(name, cancellationToken) ?? throw ReadOnly();

    public Task Delete(string name, CancellationToken cancellationToken = default) =>
        _currentVolume?.Delete(name, cancellationToken) ?? throw ReadOnly();

    public Task Rename(string name, string newName, CancellationToken cancellationToken = default) =>
        _currentVolume?.Rename(name, newName, cancellationToken) ?? throw ReadOnly();

    public Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default) =>
        _currentVolume?.SetModifiedDate(name, modifiedDate, cancellationToken) ?? throw ReadOnly();

    public Task<bool> SupportsAmigaMetadata(CancellationToken cancellationToken = default) =>
        _currentVolume?.SupportsAmigaMetadata(cancellationToken) ?? Task.FromResult(false);

    public Task SetAmigaMetadata(string name, AmigaMetadata metadata, CancellationToken cancellationToken = default) =>
        _currentVolume?.SetAmigaMetadata(name, metadata, cancellationToken) ?? throw ReadOnly();

    private static NotSupportedException ReadOnly() =>
        new("The partition table list is read-only, open a partition to change its files");

    public void Dispose()
    {
        foreach (var partitionTable in _partitionTables)
        {
            (partitionTable as IDisposable)?.Dispose();
        }

        _resource?.Dispose();
        GC.SuppressFinalize(this);
    }
}
