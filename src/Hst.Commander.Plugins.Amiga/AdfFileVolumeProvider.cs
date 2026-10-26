using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// File volume provider opening Amiga disk files (adf) with the Fast File System as writable volumes, detected by the
/// signature "DOS" followed by the file system type 0 to 7 at the start of the boot block and the size of a double
/// (880 KB) or high density (1760 KB) floppy disk.
/// </summary>
public class AdfFileVolumeProvider : IFileVolumeProvider
{
    private const long DoubleDensitySize = 901120;
    private const long HighDensitySize = 1802240;

    public string Name => "Adf";

    public string Description => "Amiga disk files";

    public bool CanWrite => true;

    public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // the size is checked when known, otherwise when mounted
        if (stream.CanSeek && stream.Length is not (DoubleDensitySize or HighDensitySize))
        {
            return false;
        }

        var dosType = new byte[4];
        var length = await stream.ReadAtLeastAsync(dosType, dosType.Length, false, cancellationToken);
        return length == dosType.Length && dosType[0] == 'D' && dosType[1] == 'O' && dosType[2] == 'S' &&
               dosType[3] <= 7;
    }

    public async Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return await AmigaFileSystems.MountAdf(stream);
    }
}
