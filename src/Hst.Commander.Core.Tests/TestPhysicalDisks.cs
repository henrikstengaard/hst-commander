using Hst.Commander.Plugins.PhysicalDisk;

namespace Hst.Commander.Core.Tests;

/// <summary>
/// Physical disks for tests: disk image files opened like physical disks with <see cref="DiskStream" />. Disks can be
/// made read-only or unreadable, e.g. like disks without administrator rights.
/// </summary>
public class TestPhysicalDisks : IPhysicalDisks
{
    private readonly List<(PhysicalDisk Disk, string Path)> _disks = [];

    /// <summary>Names of disks that can only be read, e.g. with a mounted volume.</summary>
    public HashSet<string> ReadOnlyDisks { get; } = [];

    /// <summary>Names of disks that can't be opened, e.g. without administrator rights.</summary>
    public HashSet<string> DeniedDisks { get; } = [];

    public void AddDisk(string name, string path, string? model = null) =>
        _disks.Add((new PhysicalDisk(name, path, new FileInfo(path).Length, 512) { Model = model, BusType = "USB" },
            path));

    /// <summary>Names of disks the operating system runs from.</summary>
    public HashSet<string> SystemDisks { get; } = [];

    public IReadOnlyList<PhysicalDisk> GetDisks() => _disks
        .Select(disk => disk.Disk with { IsSystem = SystemDisks.Contains(disk.Disk.Name) }).ToList();

    public Stream OpenRead(PhysicalDisk disk) => Open(disk, false);

    public Stream OpenReadWrite(PhysicalDisk disk) => ReadOnlyDisks.Contains(disk.Name)
        ? throw new IOException($"A volume on physical disk '{disk.Name}' is in use")
        : Open(disk, true);

    private DiskStream Open(PhysicalDisk disk, bool canWrite)
    {
        if (DeniedDisks.Contains(disk.Name))
        {
            throw new UnauthorizedAccessException($"Access to physical disk '{disk.Name}' is denied");
        }

        var handle = File.OpenHandle(_disks.Single(entry => entry.Disk.Name == disk.Name).Path, FileMode.Open,
            canWrite ? FileAccess.ReadWrite : FileAccess.Read, FileShare.ReadWrite);
        return new DiskStream(handle, disk.Size, disk.SectorSize, canWrite);
    }
}
