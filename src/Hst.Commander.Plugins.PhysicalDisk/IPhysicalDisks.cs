namespace Hst.Commander.Plugins.PhysicalDisk;

/// <summary>Lists and opens the physical disks of the computer, implemented for each operating system.</summary>
public interface IPhysicalDisks
{
    /// <summary>The physical disks with media, ordered by name.</summary>
    IReadOnlyList<PhysicalDisk> GetDisks();

    /// <summary>
    /// Opens a disk for reading as a seekable stream. Throws <see cref="UnauthorizedAccessException" /> when the user
    /// isn't allowed to, e.g. when not running as administrator on Windows.
    /// </summary>
    Stream OpenRead(PhysicalDisk disk);

    /// <summary>
    /// Opens a disk for reading and writing as a seekable stream. Throws <see cref="UnauthorizedAccessException" /> or
    /// <see cref="IOException" /> when it can't be written, e.g. when a volume on it is in use by the operating system.
    /// </summary>
    Stream OpenReadWrite(PhysicalDisk disk);
}
