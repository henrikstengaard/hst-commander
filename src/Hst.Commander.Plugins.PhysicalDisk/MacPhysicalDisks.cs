using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Xml.Linq;

namespace Hst.Commander.Plugins.PhysicalDisk;

/// <summary>
/// Physical disks of macOS, the whole disks listed by "diskutil list -plist physical" with their details from
/// "diskutil info -plist", opened as raw disks (/dev/rdiskN) for speed. Disks with mounted volumes are only opened for
/// reading; unmount them with "diskutil unmountDisk" to write to them.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacPhysicalDisks : UnixPhysicalDisks
{
    private static readonly TimeSpan DiskutilTimeout = TimeSpan.FromSeconds(10);

    protected override string AccessHint => "run Hst Commander as root (sudo)";

    public override IReadOnlyList<PhysicalDisk> GetDisks()
    {
        var list = RunDiskutil("list", "-plist", "physical");
        if (list?.GetValueOrDefault("WholeDisks") is not List<object?> wholeDisks)
        {
            return [];
        }

        // queried at the same time, as a sleeping disk answers only after waking up
        var systemDiskNames = GetSystemDiskNames();
        return wholeDisks.OfType<string>()
            .AsParallel()
            .AsOrdered()
            .Select(GetDisk)
            .OfType<PhysicalDisk>()
            .Select(disk => disk with { IsSystem = systemDiskNames.Contains(disk.Name) })
            .ToList();
    }

    /// <summary>
    /// Names of the whole disks with the root volume: its whole disk and, for APFS, the whole disks of the physical
    /// stores of its container, e.g. "disk0" for the store "disk0s2" of the synthesized "disk3".
    /// </summary>
    private static HashSet<string> GetSystemDiskNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var info = RunDiskutil("info", "-plist", "/");
        if (info == null)
        {
            return names;
        }

        if (info.GetValueOrDefault("ParentWholeDisk") is string parentWholeDisk)
        {
            names.Add(parentWholeDisk);
        }

        if (info.GetValueOrDefault("APFSPhysicalStores") is List<object?> stores)
        {
            foreach (var store in stores.OfType<Dictionary<string, object?>>())
            {
                if (store.GetValueOrDefault("APFSPhysicalStore") is string partition)
                {
                    names.Add(ToWholeDiskName(partition));
                }
            }
        }

        return names;
    }

    /// <summary>Name of the whole disk of a partition, e.g. "disk0" for "disk0s2".</summary>
    private static string ToWholeDiskName(string partition)
    {
        const string prefix = "disk";
        var slice = partition.StartsWith(prefix, StringComparison.Ordinal) ? partition.IndexOf('s', prefix.Length) : -1;
        return slice > 0 ? partition[..slice] : partition;
    }

    private static PhysicalDisk? GetDisk(string name)
    {
        var info = RunDiskutil("info", "-plist", name);
        var size = info == null ? null : GetLong(info, "TotalSize") ?? GetLong(info, "Size");
        if (info == null || size is not > 0)
        {
            return null;
        }

        var busType = info.GetValueOrDefault("BusProtocol") as string;
        return new PhysicalDisk(name, $"/dev/r{name}", size.Value,
            PhysicalDisks.ToSectorSize(GetLong(info, "DeviceBlockSize") ?? 512))
        {
            Model = (info.GetValueOrDefault("MediaName") as string)?.Trim() is { Length: > 0 } model ? model : null,
            BusType = string.IsNullOrWhiteSpace(busType) ? null : busType,
            IsRemovable = info.GetValueOrDefault("RemovableMediaOrExternalDevice") is true ||
                          info.GetValueOrDefault("Removable") is true
        };
    }

    private static long? GetLong(IReadOnlyDictionary<string, object?> dictionary, string key) =>
        dictionary.GetValueOrDefault(key) as long?;

    /// <summary>Devices mounted, the first column of "mount", e.g. "/dev/disk4s1".</summary>
    protected override IEnumerable<string> GetMountedDevices() =>
        (Run("mount") ?? string.Empty)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Select(line => line.Split(' ')[0])
        .Where(device => device.StartsWith("/dev/", StringComparison.Ordinal));

    /// <summary>True for the disk and its partitions, e.g. "/dev/disk4" and "/dev/disk4s1" for "disk4".</summary>
    protected override bool IsOnDisk(string mountedDevice, PhysicalDisk disk)
    {
        var deviceName = Path.GetFileName(mountedDevice);
        return deviceName == disk.Name || deviceName.StartsWith(disk.Name + "s", StringComparison.Ordinal);
    }

    /// <summary>Runs diskutil with a plist output, returning its top dictionary; null when it fails.</summary>
    private static Dictionary<string, object?>? RunDiskutil(params string[] arguments)
    {
        var output = Run("diskutil", arguments);
        if (string.IsNullOrEmpty(output))
        {
            return null;
        }

        try
        {
            return XDocument.Parse(output).Root?.Element("dict") is { } dict ? ReadDictionary(dict) : null;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static Dictionary<string, object?> ReadDictionary(XElement dict)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        string? key = null;
        foreach (var element in dict.Elements())
        {
            if (element.Name == "key")
            {
                key = element.Value;
            }
            else if (key != null)
            {
                dictionary[key] = ReadValue(element);
                key = null;
            }
        }

        return dictionary;
    }

    private static object? ReadValue(XElement element) => element.Name.LocalName switch
    {
        "string" => element.Value,
        "integer" => long.TryParse(element.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null,
        "true" => true,
        "false" => false,
        "array" => element.Elements().Select(ReadValue).ToList(),
        "dict" => ReadDictionary(element),
        _ => null
    };

    /// <summary>Runs a program, returning its output; null when it can't be started, fails or times out.</summary>
    private static string? Run(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(DiskutilTimeout))
            {
                process.Kill();
                return null;
            }

            return process.ExitCode == 0 ? output.Result : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
