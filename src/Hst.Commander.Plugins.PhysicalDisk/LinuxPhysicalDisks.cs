using System.Globalization;
using System.Runtime.Versioning;

namespace Hst.Commander.Plugins.PhysicalDisk;

/// <summary>
/// Physical disks of Linux, the SCSI/SATA/USB ("sd"), NVMe ("nvme"), MMC/SD card ("mmcblk") and virtual ("vd", "xvd",
/// "hd") block devices in /sys/block with media, opened as /dev/name. Sizes are in 512-byte units in sysfs.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxPhysicalDisks : UnixPhysicalDisks
{
    private const string BlockDirectory = "/sys/block";
    private static readonly string[] DiskPrefixes = ["sd", "nvme", "mmcblk", "vd", "xvd", "hd"];

    protected override string AccessHint => "run Hst Commander as root or add the user to the disk group";

    public override IReadOnlyList<PhysicalDisk> GetDisks()
    {
        if (!Directory.Exists(BlockDirectory))
        {
            return [];
        }

        var systemDiskNames = GetSystemDiskNames();
        return Directory.GetFileSystemEntries(BlockDirectory)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(IsDiskName)
            .Order(StringComparer.Ordinal)
            .Select(GetDisk)
            .OfType<PhysicalDisk>()
            .Select(disk => disk with { IsSystem = systemDiskNames.Contains(disk.Name) })
            .ToList();
    }

    /// <summary>
    /// Names of the disks with the root file system or the boot partitions, e.g. "nvme0n1" for "/dev/nvme0n1p2"
    /// mounted at "/", through device mapper and RAID devices to the disks they're made of.
    /// </summary>
    private static HashSet<string> GetSystemDiskNames()
    {
        string[] systemMountPoints = ["/", "/boot", "/boot/efi"];
        return (ReadText("/proc/mounts") ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(' '))
            .Where(fields => fields.Length > 1 && systemMountPoints.Contains(fields[1]) &&
                             fields[0].StartsWith("/dev/", StringComparison.Ordinal))
            .SelectMany(fields => GetDiskNames(Path.GetFileName(ResolveLink(fields[0])), 0))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Disks a block device is on: a disk itself, the disk of a partition (its parent in sysfs), or the disks of the
    /// devices a device mapper or RAID device ("dm-0", "md0") is made of (its "slaves").
    /// </summary>
    private static IEnumerable<string> GetDiskNames(string deviceName, int depth)
    {
        var slavesDirectory = Path.Combine(BlockDirectory, deviceName, "slaves");
        if (depth < 8 && Directory.Exists(slavesDirectory) &&
            Directory.GetFileSystemEntries(slavesDirectory) is { Length: > 0 } slaves)
        {
            return slaves.Select(Path.GetFileName).OfType<string>()
                .SelectMany(slave => GetDiskNames(slave, depth + 1)).ToList();
        }

        if (Directory.Exists(Path.Combine(BlockDirectory, deviceName)))
        {
            return [deviceName];
        }

        // a partition, e.g. /sys/class/block/sda1 links to .../block/sda/sda1
        try
        {
            var partition = new DirectoryInfo(Path.Combine("/sys/class/block", deviceName)).ResolveLinkTarget(true);
            return Path.GetFileName(Path.GetDirectoryName(partition?.FullName)) is { Length: > 0 } diskName
                ? [diskName]
                : [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    /// <summary>True for disks, not e.g. loop devices or the boot partitions of eMMC ("mmcblk0boot0").</summary>
    private static bool IsDiskName(string name) =>
        DiskPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)) &&
        !name.Contains("boot", StringComparison.Ordinal) && !name.Contains("rpmb", StringComparison.Ordinal);

    private static PhysicalDisk? GetDisk(string name)
    {
        var directory = Path.Combine(BlockDirectory, name);
        if (!long.TryParse(ReadText(Path.Combine(directory, "size")), NumberStyles.None, CultureInfo.InvariantCulture,
                out var sectors) || sectors == 0)
        {
            return null;
        }

        long.TryParse(ReadText(Path.Combine(directory, "queue", "logical_block_size")), NumberStyles.None,
            CultureInfo.InvariantCulture, out var sectorSize);
        var vendor = ReadText(Path.Combine(directory, "device", "vendor"));
        var model = string.Join(' ', new[] { vendor, ReadText(Path.Combine(directory, "device", "model")) }
            .Where(part => !string.IsNullOrEmpty(part)));
        return new PhysicalDisk(name, $"/dev/{name}", sectors * 512, PhysicalDisks.ToSectorSize(sectorSize))
        {
            Model = model.Length > 0 ? model : null,
            BusType = GetBusType(name, directory),
            IsRemovable = ReadText(Path.Combine(directory, "removable")) == "1"
        };
    }

    /// <summary>Bus of a disk by its device path in sysfs (e.g. ".../usb2/...") or name; null when unknown.</summary>
    private static string? GetBusType(string name, string directory)
    {
        string? devicePath;
        try
        {
            devicePath = new DirectoryInfo(directory).ResolveLinkTarget(true)?.FullName;
        }
        catch (IOException)
        {
            devicePath = null;
        }

        return devicePath?.Contains("/usb", StringComparison.Ordinal) == true ? "USB"
            : name.StartsWith("nvme", StringComparison.Ordinal) ? "NVMe"
            : name.StartsWith("mmcblk", StringComparison.Ordinal) ? "SD/MMC"
            : devicePath?.Contains("/ata", StringComparison.Ordinal) == true ? "ATA"
            : devicePath?.Contains("/virtio", StringComparison.Ordinal) == true ? "Virtual"
            : null;
    }

    /// <summary>Devices in /proc/mounts, resolving links, e.g. "/dev/disk/by-uuid/..." to "/dev/sdb1".</summary>
    protected override IEnumerable<string> GetMountedDevices() =>
        (ReadText("/proc/mounts") ?? string.Empty)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Split(' ')[0])
        .Where(device => device.StartsWith("/dev/", StringComparison.Ordinal))
        .Select(ResolveLink);

    private static string ResolveLink(string path)
    {
        try
        {
            return new FileInfo(path).ResolveLinkTarget(true)?.FullName ?? path;
        }
        catch (IOException)
        {
            return path;
        }
    }

    /// <summary>
    /// True for the disk and its partitions, the directories in its sysfs directory starting with its name, e.g.
    /// "sdb1" or "nvme0n1p1".
    /// </summary>
    protected override bool IsOnDisk(string mountedDevice, PhysicalDisk disk)
    {
        var deviceName = Path.GetFileName(mountedDevice);
        return deviceName == disk.Name ||
               (deviceName.StartsWith(disk.Name, StringComparison.Ordinal) &&
                Directory.Exists(Path.Combine(BlockDirectory, disk.Name, deviceName)));
    }
}
