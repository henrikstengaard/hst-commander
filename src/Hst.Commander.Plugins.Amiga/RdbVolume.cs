using System.Globalization;
using System.Runtime.CompilerServices;
using Hst.Amiga.RigidDiskBlocks;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// Volume browsing the partitions of a hard disk image with a rigid disk block (RDB). The root lists the partitions as
/// directories named 1 to the number of partitions, in the order of the rigid disk block. A partition is mounted when
/// opened (Fast File System or PFS3) and stays mounted until the volume is disposed. The partitions can't be changed,
/// their file systems are writable when the stream is.
/// </summary>
public class RdbVolume : IVolume, IDisposable
{
    private readonly Stream _stream;
    private readonly IReadOnlyList<PartitionBlock> _partitionBlocks;
    private readonly IDisposable? _resource;
    private readonly Dictionary<int, AmigaFileSystemVolume> _mountedVolumes = new();
    private int? _partitionIndex;

    /// <param name="stream">Stream of the disk, written to when writable.</param>
    /// <param name="partitionBlocks">Partitions of the rigid disk block, at least one.</param>
    /// <param name="resource">Resource disposed with the volume after the partitions, e.g. a virtual disk.</param>
    public RdbVolume(Stream stream, IReadOnlyList<PartitionBlock> partitionBlocks, IDisposable? resource = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(partitionBlocks);

        _stream = stream;
        _partitionBlocks = partitionBlocks;
        _resource = resource;
    }

    public string Name => "Rdb";

    /// <summary>Partition volume of the current directory, null in the partition list.</summary>
    private AmigaFileSystemVolume? CurrentVolume =>
        _partitionIndex is { } partitionIndex ? _mountedVolumes[partitionIndex] : null;

    public IEnumerable<string> CurrentPathComponents => _partitionIndex is { } partitionIndex
        ? [GetPartitionName(partitionIndex), .._mountedVolumes[partitionIndex].CurrentPathComponents]
        : [];

    private static string GetPartitionName(int partitionIndex) =>
        (partitionIndex + 1).ToString(CultureInfo.InvariantCulture);

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

        for (var partitionIndex = 0; partitionIndex < _partitionBlocks.Count; partitionIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreatePartitionEntry(partitionIndex);
        }
    }

    private IEntry CreatePartitionEntry(int partitionIndex) =>
        new PartitionEntry(GetPartitionName(partitionIndex), EntryType.Container,
            _partitionBlocks[partitionIndex].PartitionSize, null);

    public async Task ChangeDirectory(IEnumerable<string> pathComponents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathComponents);

        var previousPartitionIndex = _partitionIndex;
        var previousPathComponents = CurrentVolume?.CurrentPathComponents.ToList();
        try
        {
            foreach (var pathComponent in pathComponents)
            {
                if (pathComponent.Equals(Paths.Root, StringComparison.OrdinalIgnoreCase))
                {
                    _partitionIndex = null;
                }
                else if (CurrentVolume is not { } volume)
                {
                    if (!pathComponent.Equals(Paths.Parent, StringComparison.OrdinalIgnoreCase))
                    {
                        await OpenPartition(pathComponent, cancellationToken);
                    }
                }
                else if (pathComponent.Equals(Paths.Parent, StringComparison.OrdinalIgnoreCase) && volume.IsAtRoot)
                {
                    _partitionIndex = null;
                }
                else
                {
                    await volume.ChangeDirectory([pathComponent], cancellationToken);
                }
            }
        }
        catch
        {
            _partitionIndex = previousPartitionIndex;
            if (CurrentVolume is { } volume && previousPathComponents != null)
            {
                await volume.ChangeDirectory([Paths.Root, ..previousPathComponents], cancellationToken);
            }

            throw;
        }
    }

    /// <summary>Changes to the root of a partition, mounting its file system the first time.</summary>
    private async Task OpenPartition(string name, CancellationToken cancellationToken)
    {
        if (!int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1 ||
            number > _partitionBlocks.Count)
        {
            throw new DirectoryNotFoundException($"Partition '{name}' not found");
        }

        var partitionIndex = number - 1;
        if (_mountedVolumes.TryGetValue(partitionIndex, out var volume))
        {
            await volume.ChangeDirectory([Paths.Root], cancellationToken);
        }
        else
        {
            _mountedVolumes[partitionIndex] = await MountPartition(partitionIndex);
        }

        _partitionIndex = partitionIndex;
    }

    private async Task<AmigaFileSystemVolume> MountPartition(int partitionIndex)
    {
        var partitionBlock = _partitionBlocks[partitionIndex];
        try
        {
            return await AmigaFileSystems.MountPartition(_stream, partitionBlock, GetPartitionName(partitionIndex));
        }
        catch (Exception exception) when (exception is not IOException)
        {
            throw new IOException($"Partition '{GetPartitionName(partitionIndex)}' ({partitionBlock.DriveName}) " +
                                  $"can't be mounted: {exception.Message}", exception);
        }
    }

    public async Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default)
    {
        if (CurrentVolume is { } volume)
        {
            return await volume.OpenEntry(name, cancellationToken);
        }

        await OpenPartition(name, cancellationToken);
        return CreatePartitionEntry(_partitionIndex!.Value);
    }

    public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default) =>
        CurrentVolume?.OpenRead(name, cancellationToken) ??
        throw new FileNotFoundException($"File '{name}' not found");

    public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default) =>
        CurrentVolume?.OpenWrite(name, cancellationToken) ?? throw PartitionsReadOnly();

    public Task CreateContainer(string name, CancellationToken cancellationToken = default) =>
        CurrentVolume?.CreateContainer(name, cancellationToken) ?? throw PartitionsReadOnly();

    public Task Delete(string name, CancellationToken cancellationToken = default) =>
        CurrentVolume?.Delete(name, cancellationToken) ?? throw PartitionsReadOnly();

    public Task Rename(string name, string newName, CancellationToken cancellationToken = default) =>
        CurrentVolume?.Rename(name, newName, cancellationToken) ?? throw PartitionsReadOnly();

    public Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default) =>
        CurrentVolume?.SetModifiedDate(name, modifiedDate, cancellationToken) ?? throw PartitionsReadOnly();

    private static NotSupportedException PartitionsReadOnly() =>
        new("The partition list is read-only, open a partition to change its files");

    public void Dispose()
    {
        foreach (var volume in _mountedVolumes.Values)
        {
            volume.Dispose();
        }

        _mountedVolumes.Clear();
        _stream.Flush();
        _resource?.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record PartitionEntry(string Name, EntryType Type, long? Size, DateTime? ModifiedDate) : IEntry;
}
