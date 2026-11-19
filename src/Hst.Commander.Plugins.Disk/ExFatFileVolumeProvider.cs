using DiscUtils.ExFat;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Disk;

/// <summary>
/// File volume provider opening exFAT file systems without a partition table (e.g. created with
/// <see cref="ExFatVolumeCreator" /> or an image of a partition) as writable volumes. Detected by "EXFAT   " in the
/// boot sector, which ends with 0x55 0xAA; <see cref="DiskFileVolumeProvider" /> doesn't match these boot sectors.
/// </summary>
public class ExFatFileVolumeProvider : IFileVolumeProvider
{
    private const int BootSectorSize = 512;
    private const int FileSystemNameOffset = 3;
    private static readonly byte[] FileSystemName = "EXFAT   "u8.ToArray();

    /// <summary>Path separators of exFAT paths, '\' first as <see cref="DiscFileSystemVolume" /> joins paths with it.</summary>
    private static readonly char[] PathSeparators = ['\\', '/'];

    public string Name => "exFAT";

    public string Description => "exFAT file systems without a partition table";

    public bool CanWrite => true;

    public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var bootSector = new byte[BootSectorSize];
        var length = await stream.ReadAtLeastAsync(bootSector, bootSector.Length, false, cancellationToken);
        return length == bootSector.Length && IsBootSector(bootSector);
    }

    /// <summary>True when a sector is the boot sector of an exFAT file system.</summary>
    internal static bool IsBootSector(ReadOnlySpan<byte> sector) =>
        sector.Length >= BootSectorSize &&
        sector.Slice(FileSystemNameOffset, FileSystemName.Length).SequenceEqual(FileSystemName) &&
        sector[510] == 0x55 && sector[511] == 0xAA;

    /// <summary>
    /// Mounts the exFAT file system, throwing <see cref="IOException" /> when it can't be read. The stream must be
    /// seekable.
    /// </summary>
    public Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            return Task.FromResult<IVolume>(Mount(Name, stream));
        }
        catch (Exception exception) when (exception is not IOException)
        {
            throw new IOException($"The exFAT file system can't be read: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Mounts the exFAT file system in a seekable stream, e.g. a partition, with its volume label, which DiscUtils
    /// doesn't read (see <see cref="ExFatVolumeLabel" />).
    /// </summary>
    internal static DiscFileSystemVolume Mount(string name, Stream stream)
    {
        var volumeLabel = ExFatVolumeLabel.Read(stream);
        stream.Position = 0;
        // the separators default to the OS's (only '/' on macOS and Linux), DiscFileSystemVolume joins paths with '\'
        return new DiscFileSystemVolume(name, new ExFatFileSystem(stream, PathSeparators), volumeLabel: volumeLabel);
    }
}
