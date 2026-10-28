namespace Hst.Commander.Plugins.PhysicalDisk;

/// <summary>The physical disks of the operating system Hst Commander runs on.</summary>
public static class PhysicalDisks
{
    /// <summary>Physical disks of Windows, Linux or macOS; null on other operating systems.</summary>
    public static IPhysicalDisks? ForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? new WindowsPhysicalDisks()
        : OperatingSystem.IsLinux() ? new LinuxPhysicalDisks()
        : OperatingSystem.IsMacOS() ? new MacPhysicalDisks()
        : null;

    /// <summary>Sector size reported by the operating system, or 512 when it isn't a power of two up to 4096.</summary>
    internal static int ToSectorSize(long sectorSize) =>
        sectorSize is >= 512 and <= 4096 && (sectorSize & (sectorSize - 1)) == 0 ? (int)sectorSize : 512;
}
