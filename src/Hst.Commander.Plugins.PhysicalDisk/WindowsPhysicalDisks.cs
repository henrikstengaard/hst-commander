using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static Hst.Commander.Plugins.PhysicalDisk.NativeMethods;

namespace Hst.Commander.Plugins.PhysicalDisk;

/// <summary>
/// Physical disks of Windows, "\\.\PhysicalDrive0" to "\\.\PhysicalDrive63", listed without administrator rights but
/// only opened as administrator. Windows doesn't allow writing to the sectors of its mounted volumes, so opening a
/// disk for writing locks and dismounts the volumes on it until the disk is closed (Windows mounts them again), and
/// fails when one is in use, e.g. the system drive.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsPhysicalDisks : IPhysicalDisks
{
    private const string DeviceNamespace = @"\\.\";
    private const string DevicePrefix = DeviceNamespace + "PhysicalDrive";
    private const int MaxDiskCount = 64;
    private const int LockAttempts = 10;
    private const int QueryParallelism = 16;

    /// <summary>Names of the STORAGE_BUS_TYPE values.</summary>
    private static readonly string?[] BusTypes =
    [
        null, "SCSI", "ATAPI", "ATA", "IEEE 1394", "SSA", "Fibre Channel", "USB", "RAID", "iSCSI", "SAS", "SATA", "SD",
        "MMC", "Virtual", "Virtual", "Storage Spaces", "NVMe", "SCM", "UFS"
    ];

    public IReadOnlyList<PhysicalDisk> GetDisks()
    {
        // queried at the same time, as a sleeping disk (e.g. a USB drive) answers only after waking up, which can
        // take seconds
        var systemDiskNumbers = GetSystemDiskNumbers();
        return Enumerable.Range(0, MaxDiskCount)
            .AsParallel()
            .AsOrdered()
            .WithDegreeOfParallelism(QueryParallelism)
            .Select(number => GetDisk(number) is { } disk
                ? disk with { IsSystem = systemDiskNumbers.Contains(number) }
                : null)
            .OfType<PhysicalDisk>()
            .ToList();
    }

    /// <summary>Numbers of the disks with the Windows volume, e.g. 0 for "C:" on "\\.\PhysicalDrive0".</summary>
    private static HashSet<int> GetSystemDiskNumbers()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        return string.IsNullOrEmpty(root)
            ? []
            : GetDiskNumbers(DeviceNamespace + root.TrimEnd(Path.DirectorySeparatorChar)).ToHashSet();
    }

    /// <summary>A disk queried without access rights; null when there's no such disk or it has no media.</summary>
    private static PhysicalDisk? GetDisk(int number)
    {
        // named without the device prefix, which Path.GetFileName treats as a root
        var path = DevicePrefix + number.ToString(CultureInfo.InvariantCulture);
        var name = path[DeviceNamespace.Length..];
        using var handle = CreateFile(path, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        // DISK_GEOMETRY_EX: bytes per sector at 20, disk size at 24
        var geometry = new byte[256];
        if (DeviceIoControl(handle, IoctlDiskGetDriveGeometryEx, null, geometry) is not >= 32)
        {
            return null;
        }

        var sectorSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(geometry.AsSpan(20));
        var size = BinaryPrimitives.ReadInt64LittleEndian(geometry.AsSpan(24));
        if (size <= 0)
        {
            return null;
        }

        var disk = new PhysicalDisk(name, path, size, sectorSize is > 0 ? sectorSize : 512);

        // STORAGE_DEVICE_DESCRIPTOR of the standard query of the device property (STORAGE_PROPERTY_QUERY of zeros)
        var descriptor = new byte[1024];
        if (DeviceIoControl(handle, IoctlStorageQueryProperty, new byte[12], descriptor) is not { } length ||
            length < 32)
        {
            return disk;
        }

        var span = descriptor.AsSpan(0, length);
        var vendor = ReadString(span, BinaryPrimitives.ReadInt32LittleEndian(span[12..]));
        var product = ReadString(span, BinaryPrimitives.ReadInt32LittleEndian(span[16..]));
        var busType = BinaryPrimitives.ReadInt32LittleEndian(span[28..]);
        var model = string.Join(' ', new[] { vendor, product }.Where(part => !string.IsNullOrEmpty(part)));
        return disk with
        {
            Model = model.Length > 0 ? model : null,
            BusType = busType >= 0 && busType < BusTypes.Length ? BusTypes[busType] : null,
            IsRemovable = span[10] != 0
        };
    }

    /// <summary>Zero terminated ASCII string at an offset of the descriptor, trimmed; null when the offset is 0.</summary>
    private static string? ReadString(ReadOnlySpan<byte> descriptor, int offset)
    {
        if (offset <= 0 || offset >= descriptor.Length)
        {
            return null;
        }

        var bytes = descriptor[offset..];
        var end = bytes.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end >= 0 ? bytes[..end] : bytes).Trim();
    }

    public Stream OpenRead(PhysicalDisk disk) =>
        new DiskStream(Open(disk, GenericRead), disk.Size, disk.SectorSize, false);

    public Stream OpenReadWrite(PhysicalDisk disk)
    {
        var handle = Open(disk, GenericRead | GenericWrite);
        try
        {
            var lockedVolumes = LockVolumes(disk);
            return new DiskStream(handle, disk.Size, disk.SectorSize, true, lockedVolumes);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static SafeFileHandle Open(PhysicalDisk disk, uint access)
    {
        var handle = CreateFile(disk.Path, access, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (!handle.IsInvalid)
        {
            return handle;
        }

        var error = Marshal.GetLastWin32Error();
        handle.Dispose();
        if (error == ErrorAccessDenied)
        {
            throw new UnauthorizedAccessException(
                $"Access to physical disk '{disk.Name}' is denied, run Hst Commander as administrator to open it");
        }

        throw new IOException($"Physical disk '{disk.Name}' can't be opened: {new Win32Exception(error).Message}");
    }

    /// <summary>
    /// Locks and dismounts the volumes on a disk, so its sectors can be written. Throws <see cref="IOException" />
    /// when a volume can't be locked, e.g. because files on it are open.
    /// </summary>
    private static LockedVolumes LockVolumes(PhysicalDisk disk)
    {
        var diskNumber = int.Parse(disk.Path[DevicePrefix.Length..], CultureInfo.InvariantCulture);
        var lockedVolumes = new LockedVolumes();
        try
        {
            foreach (var volumePath in GetVolumePaths().Where(volumePath => IsOnDisk(volumePath, diskNumber)))
            {
                var handle = CreateFile(volumePath, GenericRead | GenericWrite, FileShareReadWrite, IntPtr.Zero,
                    OpenExisting, 0, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    throw new IOException($"A volume on physical disk '{disk.Name}' can't be opened");
                }

                lockedVolumes.Add(handle);
                if (!Lock(handle))
                {
                    throw new IOException($"A volume on physical disk '{disk.Name}' is in use");
                }

                DeviceIoControl(handle, FsctlDismountVolume, null, null);
            }

            return lockedVolumes;
        }
        catch
        {
            lockedVolumes.Dispose();
            throw;
        }
    }

    /// <summary>Locks a volume, trying again for a second, e.g. while Explorer reads it.</summary>
    private static bool Lock(SafeFileHandle volume)
    {
        for (var attempt = 1; attempt <= LockAttempts; attempt++)
        {
            if (DeviceIoControl(volume, FsctlLockVolume, null, null) != null)
            {
                return true;
            }

            if (attempt < LockAttempts)
            {
                Thread.Sleep(100);
            }
        }

        return false;
    }

    /// <summary>Paths of the volumes of the computer, e.g. "\\?\Volume{...}" without the trailing backslash.</summary>
    private static List<string> GetVolumePaths()
    {
        var paths = new List<string>();
        var name = new char[1024];
        var find = FindFirstVolume(name, name.Length);
        if (find == new IntPtr(-1))
        {
            return paths;
        }

        try
        {
            do
            {
                paths.Add(new string(name, 0, Array.IndexOf(name, '\0')).TrimEnd('\\'));
            } while (FindNextVolume(find, name, name.Length));
        }
        finally
        {
            FindVolumeClose(find);
        }

        return paths;
    }

    /// <summary>True when a volume has an extent on a disk.</summary>
    private static bool IsOnDisk(string volumePath, int diskNumber) => GetDiskNumbers(volumePath).Contains(diskNumber);

    /// <summary>Numbers of the disks a volume has extents on, e.g. "\\.\C:"; empty when they can't be read.</summary>
    private static List<int> GetDiskNumbers(string volumePath)
    {
        var numbers = new List<int>();
        using var handle = CreateFile(volumePath, 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return numbers;
        }

        // VOLUME_DISK_EXTENTS: number of extents, then DISK_EXTENTs of 24 bytes starting with the disk number
        var extents = new byte[1024];
        if (DeviceIoControl(handle, IoctlVolumeGetVolumeDiskExtents, null, extents) is not >= 8)
        {
            return numbers;
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(extents);
        for (var index = 0; index < count && 8 + index * 24 + 4 <= extents.Length; index++)
        {
            numbers.Add(BinaryPrimitives.ReadInt32LittleEndian(extents.AsSpan(8 + index * 24)));
        }

        return numbers;
    }

    /// <summary>Handles of locked volumes, unlocked when closed.</summary>
    private sealed class LockedVolumes : IDisposable
    {
        private readonly List<SafeFileHandle> _handles = [];

        public void Add(SafeFileHandle handle) => _handles.Add(handle);

        public void Dispose()
        {
            foreach (var handle in _handles)
            {
                handle.Dispose();
            }

            _handles.Clear();
        }
    }
}
