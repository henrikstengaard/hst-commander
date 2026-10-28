using System.Globalization;
using System.Runtime.CompilerServices;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.PhysicalDisk;

/// <summary>
/// Volume listing the physical disks as items named by the disk, e.g. "PhysicalDrive2", shown with their model, e.g.
/// "PhysicalDrive2 - SanDisk Ultra". A disk is opened as a disk image by the file volume providers of Hst Commander,
/// so paths start with the disk, e.g. "PhysicalDrive2\mbr\1". Disks have an id
/// (<see cref="IVolume.GetWritableImageId" />), so a disk is opened once for reading and writing and shared by both
/// panes. The list itself is read-only.
/// </summary>
public sealed class PhysicalDiskVolume(IPhysicalDisks disks) : IVolume
{
    /// <summary>Name of the volume, shown as the path of the disk list.</summary>
    public const string VolumeName = "Physical disks";

    private IReadOnlyList<PhysicalDisk>? _disks;

    public string Name => VolumeName;

    public IEnumerable<string> CurrentPathComponents => [];

    public async IAsyncEnumerable<IEntry> ListEntries(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // listed again each time, so disks attached or removed are shown when refreshing
        _disks = await Task.Run(disks.GetDisks, cancellationToken);
        foreach (var disk in _disks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return CreateEntry(disk);
        }
    }

    public Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathComponents);

        foreach (var pathComponent in pathComponents)
        {
            if (!pathComponent.Equals(Paths.Root, StringComparison.OrdinalIgnoreCase) &&
                !pathComponent.Equals(Paths.Parent, StringComparison.OrdinalIgnoreCase))
            {
                throw new DirectoryNotFoundException($"Directory '{pathComponent}' not found");
            }
        }

        return Task.CompletedTask;
    }

    public Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateEntry(GetDisk(name)));

    public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default)
    {
        var disk = GetAccessibleDisk(name);
        return Task.Run(() => disks.OpenRead(disk), cancellationToken);
    }

    /// <summary>Path of the device of a disk, e.g. "\.\PhysicalDrive2"; null when there's no such disk.</summary>
    public string? GetWritableImageId(string name) => FindDisk(name)?.Path;

    public Task<Stream> OpenReadWrite(string name, CancellationToken cancellationToken = default)
    {
        var disk = GetAccessibleDisk(name);
        return Task.Run(() => disks.OpenReadWrite(disk), cancellationToken);
    }

    public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default) => throw ReadOnly();

    public Task CreateContainer(string name, CancellationToken cancellationToken = default) => throw ReadOnly();

    public Task Delete(string name, CancellationToken cancellationToken = default) => throw ReadOnly();

    public Task Rename(string name, string newName, CancellationToken cancellationToken = default) => throw ReadOnly();

    public Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default) =>
        throw ReadOnly();

    private static NotSupportedException ReadOnly() =>
        new("The physical disk list is read-only, open a disk to change the files of its partitions");

    private PhysicalDisk? FindDisk(string name)
    {
        _disks ??= disks.GetDisks();
        return _disks.FirstOrDefault(disk => disk.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private PhysicalDisk GetDisk(string name) =>
        FindDisk(name) ?? throw new FileNotFoundException($"Physical disk '{name}' not found");

    /// <summary>A disk that may be opened; system disks aren't, so the operating system can't be damaged.</summary>
    private PhysicalDisk GetAccessibleDisk(string name)
    {
        var disk = GetDisk(name);
        return disk.IsSystem
            ? throw new UnauthorizedAccessException($"Physical disk '{disk.Name}' is a system disk and not accessible")
            : disk;
    }

    private static IEntry CreateEntry(PhysicalDisk disk)
    {
        var diskSection = new List<EntryProperty> { new("Disk", disk.Name) };
        if (!string.IsNullOrEmpty(disk.Model))
        {
            diskSection.Add(new EntryProperty("Model", disk.Model));
        }

        if (!string.IsNullOrEmpty(disk.BusType))
        {
            diskSection.Add(new EntryProperty("Bus", disk.BusType));
        }

        diskSection.Add(new EntryProperty("Removable", disk.IsRemovable ? "Yes" : "No"));
        diskSection.Add(new EntryProperty("System disk", disk.IsSystem ? "Yes" : "No"));
        diskSection.Add(new EntryProperty("Device", disk.Path));

        List<EntryProperty> sizeSection =
        [
            new("Sector size", string.Create(CultureInfo.InvariantCulture, $"{disk.SectorSize} bytes")),
            EntryProperty.FromBytes("Size", disk.Size)
        ];

        var displayName = string.IsNullOrEmpty(disk.Model) ? disk.Name : $"{disk.Name} - {disk.Model}";
        return new PhysicalDiskEntry(disk.Name, displayName, disk.Size,
            new EntryDetails("Physical disk", [diskSection, sizeSection]));
    }

    private sealed record PhysicalDiskEntry(string Name, string DisplayName, long? Size, EntryDetails Details) : IEntry
    {
        public EntryType Type => EntryType.Item;

        public DateTime? ModifiedDate => null;
    }
}
