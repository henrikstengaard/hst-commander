using Microsoft.Win32.SafeHandles;

namespace Hst.Commander.Plugins.PhysicalDisk;

/// <summary>
/// Physical disks of Linux and macOS, device files opened as root (or, on Linux, as a member of the disk group). A
/// disk with a mounted partition is only opened for reading, as the operating system writes to the partition too.
/// </summary>
internal abstract class UnixPhysicalDisks : IPhysicalDisks
{
    public abstract IReadOnlyList<PhysicalDisk> GetDisks();

    /// <summary>Paths of the devices with a mounted file system, e.g. "/dev/sdb1".</summary>
    protected abstract IEnumerable<string> GetMountedDevices();

    /// <summary>True when a mounted device is the disk or one of its partitions.</summary>
    protected abstract bool IsOnDisk(string mountedDevice, PhysicalDisk disk);

    /// <summary>What to do to get access to the disks, e.g. "run Hst Commander as root".</summary>
    protected abstract string AccessHint { get; }

    public Stream OpenRead(PhysicalDisk disk) =>
        new DiskStream(Open(disk, FileAccess.Read), disk.Size, disk.SectorSize, false);

    public Stream OpenReadWrite(PhysicalDisk disk)
    {
        if (GetMountedDevices().FirstOrDefault(device => IsOnDisk(device, disk)) is { } mountedDevice)
        {
            throw new IOException($"'{mountedDevice}' on physical disk '{disk.Name}' is mounted");
        }

        return new DiskStream(Open(disk, FileAccess.ReadWrite), disk.Size, disk.SectorSize, true);
    }

    private SafeFileHandle Open(PhysicalDisk disk, FileAccess access)
    {
        try
        {
            return File.OpenHandle(disk.Path, FileMode.Open, access, FileShare.ReadWrite);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new UnauthorizedAccessException(
                $"Access to physical disk '{disk.Name}' is denied, {AccessHint} to open it", exception);
        }
    }

    /// <summary>Text of a file, trimmed; null when it can't be read.</summary>
    protected static string? ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
