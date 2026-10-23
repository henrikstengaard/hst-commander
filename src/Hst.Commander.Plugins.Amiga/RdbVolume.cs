using System.Globalization;
using System.Runtime.CompilerServices;
using Hst.Amiga.RigidDiskBlocks;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// Volume browsing the partitions of a hard disk image with a rigid disk block (RDB). The root lists the partition
/// table as the directory "rdb" (shown as "RDB"), listing the partitions as directories named by their partition
/// number (1, 2, ...), in the order of the rigid disk block, so paths show the partition table, e.g. "rdb\1". The partitions are shown with
/// their number, drive name and volume name (e.g. "1 - DH0 - Workbench", see <see cref="IEntry.DisplayName" />) and
/// their partition details in the properties dialog (<see cref="IEntry.Details" />). A partition is mounted (Fast File System or PFS3) when listed or
/// opened and stays mounted until the volume is disposed. The partitions can't be changed,
/// their file systems are writable when the stream is.
/// </summary>
public class RdbVolume : IVolume, IDisposable
{
    private readonly Stream _stream;
    private readonly IReadOnlyList<PartitionBlock> _partitionBlocks;
    private readonly IDisposable? _resource;
    private const string PartitionTypeName = "Amiga partition";
    private const string PartitionTableName = "rdb";
    private const string PartitionTableDisplayName = "RDB";

    private readonly Dictionary<int, AmigaFileSystemVolume> _mountedVolumes = new();
    private readonly HashSet<int> _unmountablePartitions = [];
    private bool _inPartitionTable;
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

    public IEnumerable<string> CurrentPathComponents => !_inPartitionTable ? []
        : _partitionIndex is { } partitionIndex
            ? [PartitionTableName, GetPartitionName(partitionIndex), .._mountedVolumes[partitionIndex].CurrentPathComponents]
            : [PartitionTableName];

    /// <summary>Name of a partition: the partition number, e.g. "1".</summary>
    private static string GetPartitionName(int partitionIndex) =>
        (partitionIndex + 1).ToString(CultureInfo.InvariantCulture);

    private PartitionTableEntry CreatePartitionTableEntry() => new(PartitionTableName, PartitionTableDisplayName,
        new EntryDetails(
        "Partition table",
        [
            [
                new EntryProperty("Partition table", "Rigid disk block (RDB)"),
                new EntryProperty("Partitions", _partitionBlocks.Count.ToString(CultureInfo.InvariantCulture))
            ]
        ]));

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

        if (!_inPartitionTable)
        {
            yield return CreatePartitionTableEntry();
            yield break;
        }

        for (var partitionIndex = 0; partitionIndex < _partitionBlocks.Count; partitionIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return await CreatePartitionEntry(partitionIndex);
        }
    }

    /// <summary>
    /// Creates the entry of a partition, mounting its file system the first time for the volume name and space;
    /// a partition that can't be mounted (e.g. SFS) is shown without them.
    /// </summary>
    private async Task<IEntry> CreatePartitionEntry(int partitionIndex)
    {
        var name = GetPartitionName(partitionIndex);
        var partitionBlock = _partitionBlocks[partitionIndex];
        var volume = await TryMountPartition(partitionIndex);
        var driveName = partitionBlock.DriveName?.Trim();
        var volumeName = volume?.VolumeName.Trim();

        var displayName = string.Join(" - ",
            new[] { name, driveName, volumeName }.Where(part => !string.IsNullOrEmpty(part)));
        return new PartitionEntry(name, displayName, partitionBlock.PartitionSize,
            CreateDetails(partitionIndex, driveName, volumeName, volume));
    }

    private EntryDetails CreateDetails(int partitionIndex, string? driveName, string? volumeName,
        AmigaFileSystemVolume? volume)
    {
        var partitionBlock = _partitionBlocks[partitionIndex];
        var partitionSection = new List<EntryProperty> { new("Partition", GetPartitionName(partitionIndex)) };
        if (!string.IsNullOrEmpty(driveName))
        {
            partitionSection.Add(new EntryProperty("Drive name", driveName));
        }

        if (!string.IsNullOrEmpty(volumeName))
        {
            partitionSection.Add(new EntryProperty("Volume name", volumeName));
        }

        partitionSection.Add(new EntryProperty("File system", AmigaFileSystems.FormatFileSystem(partitionBlock.DosType)));

        var bootSection = new List<EntryProperty>
        {
            new("Bootable", partitionBlock.Bootable ? "Yes" : "No"),
            new("Boot priority", partitionBlock.BootPriority.ToString(CultureInfo.InvariantCulture)),
            new("Cylinders", string.Create(CultureInfo.InvariantCulture,
                $"{partitionBlock.LowCyl} - {partitionBlock.HighCyl}"))
        };

        if (volume == null)
        {
            return new EntryDetails(PartitionTypeName, [partitionSection, bootSection]);
        }

        var capacity = volume.Size;
        var free = Math.Clamp(volume.Free, 0, capacity);
        List<EntryProperty> spaceSection =
        [
            EntryProperty.FromBytes("Used space", capacity - free),
            EntryProperty.FromBytes("Free space", free),
            EntryProperty.FromBytes("Capacity", capacity)
        ];
        return new EntryDetails(PartitionTypeName, [partitionSection, bootSection, spaceSection],
            capacity > 0 ? (double)(capacity - free) / capacity : null);
    }

    /// <summary>Mounted file system of a partition, mounting it the first time; null when it can't be mounted.</summary>
    private async Task<AmigaFileSystemVolume?> TryMountPartition(int partitionIndex)
    {
        if (_mountedVolumes.TryGetValue(partitionIndex, out var volume))
        {
            return volume;
        }

        if (_unmountablePartitions.Contains(partitionIndex))
        {
            return null;
        }

        try
        {
            return _mountedVolumes[partitionIndex] = await MountPartition(partitionIndex);
        }
        catch (IOException)
        {
            _unmountablePartitions.Add(partitionIndex);
            return null;
        }
    }

    public async Task ChangeDirectory(IEnumerable<string> pathComponents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathComponents);

        var previousInPartitionTable = _inPartitionTable;
        var previousPartitionIndex = _partitionIndex;
        var previousPathComponents = CurrentVolume?.CurrentPathComponents.ToList();
        try
        {
            foreach (var pathComponent in pathComponents)
            {
                if (pathComponent.Equals(Paths.Root, StringComparison.OrdinalIgnoreCase))
                {
                    _inPartitionTable = false;
                    _partitionIndex = null;
                }
                else if (!_inPartitionTable)
                {
                    if (!pathComponent.Equals(Paths.Parent, StringComparison.OrdinalIgnoreCase))
                    {
                        OpenPartitionTable(pathComponent);
                    }
                }
                else if (CurrentVolume is not { } volume)
                {
                    if (pathComponent.Equals(Paths.Parent, StringComparison.OrdinalIgnoreCase))
                    {
                        _inPartitionTable = false;
                    }
                    else
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
            _inPartitionTable = previousInPartitionTable;
            _partitionIndex = previousPartitionIndex;
            if (CurrentVolume is { } volume && previousPathComponents != null)
            {
                await volume.ChangeDirectory([Paths.Root, ..previousPathComponents], cancellationToken);
            }

            throw;
        }
    }

    /// <summary>Changes from the root to the partition list.</summary>
    private void OpenPartitionTable(string name)
    {
        if (!name.Equals(PartitionTableName, StringComparison.OrdinalIgnoreCase))
        {
            throw new DirectoryNotFoundException($"'{name}' not found");
        }

        _inPartitionTable = true;
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

        if (!_inPartitionTable)
        {
            OpenPartitionTable(name);
            return CreatePartitionTableEntry();
        }

        await OpenPartition(name, cancellationToken);
        return await CreatePartitionEntry(_partitionIndex!.Value);
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

    public Task<bool> SupportsAmigaMetadata(CancellationToken cancellationToken = default) =>
        Task.FromResult(CurrentVolume != null);

    public Task SetAmigaMetadata(string name, AmigaMetadata metadata, CancellationToken cancellationToken = default) =>
        CurrentVolume?.SetAmigaMetadata(name, metadata, cancellationToken) ?? throw PartitionsReadOnly();

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

    /// <summary>
    /// Partition in the partition list, shown with its number, drive name and volume name, e.g. "1 - DH0 - Workbench"
    /// (no drive name when it has none, no volume name when it can't be mounted). It is a volume of its own, so its icon is the
    /// Disk.info file in its root.
    /// </summary>
    private sealed record PartitionEntry(string Name, string DisplayName, long? Size, EntryDetails Details) : IEntry
    {
        public EntryType Type => EntryType.Container;

        public DateTime? ModifiedDate => null;

        public bool IsVolume => true;
    }

    /// <summary>Partition table in the root, the directory "rdb" shown as "RDB" listing the partitions.</summary>
    private sealed record PartitionTableEntry(string Name, string DisplayName, EntryDetails Details) : IEntry
    {
        public EntryType Type => EntryType.Container;

        public long? Size => null;

        public DateTime? ModifiedDate => null;
    }
}
