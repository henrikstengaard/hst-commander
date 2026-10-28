namespace Hst.Commander.Plugins.PhysicalDisk;

/// <summary>A physical disk of the computer, e.g. an internal SSD or a USB stick.</summary>
/// <param name="Name">Name of the disk in paths, e.g. "PhysicalDrive2" on Windows, "sdb" on Linux or "disk2" on macOS.</param>
/// <param name="Path">Path of the device opened, e.g. "\.\PhysicalDrive2", "/dev/sdb" or "/dev/rdisk2".</param>
/// <param name="Size">Size of the disk in bytes.</param>
/// <param name="SectorSize">Size of the sectors in bytes, e.g. 512 or 4096; reads and writes are aligned to it.</param>
public sealed record PhysicalDisk(string Name, string Path, long Size, int SectorSize)
{
    /// <summary>Model of the disk including its vendor, e.g. "SanDisk Ultra"; null when unknown.</summary>
    public string? Model { get; init; }

    /// <summary>Bus the disk is attached with, e.g. "USB", "SATA" or "NVMe"; null when unknown.</summary>
    public string? BusType { get; init; }

    /// <summary>True when the disk or its media is removable, e.g. a USB stick or an SD card.</summary>
    public bool IsRemovable { get; init; }

    /// <summary>
    /// True for a disk the operating system runs from, e.g. with the Windows volume or the root file system, which
    /// isn't opened so it can't be damaged.
    /// </summary>
    public bool IsSystem { get; init; }
}
