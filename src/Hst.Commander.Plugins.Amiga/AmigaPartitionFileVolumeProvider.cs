using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// File volume provider opening Amiga partition images without a rigid disk block (e.g. UAE hardfiles) with the Fast
/// File System, PFS3 or Smart File System as writable volumes, detected by the id at the start of the first block (see
/// <see cref="AmigaFileSystems.GetPartitionImageDosType" />) and, when known, a size of at least 64 KB in whole blocks
/// of 512 bytes. Comes after <see cref="AdfFileVolumeProvider" />, which
/// opens images of the size of a floppy disk, and <see cref="RdbFileVolumeProvider" />.
/// </summary>
public class AmigaPartitionFileVolumeProvider : IFileVolumeProvider
{
    private const int BlockSize = 512;
    private const long MinSize = 64 * 1024;

    public string Name => "Amiga partition";

    public string Description => "Amiga partition images without a rigid disk block (e.g. UAE hardfiles)";

    public bool CanWrite => true;

    public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // the size is checked when known, otherwise when mounted, so other files starting with an id (e.g. a text
        // file starting with "DOS") aren't opened
        if (stream.CanSeek && (stream.Length < MinSize || stream.Length % BlockSize != 0))
        {
            return false;
        }

        var firstBlock = new byte[4];
        var length = await stream.ReadAtLeastAsync(firstBlock, firstBlock.Length, false, cancellationToken);
        return AmigaFileSystems.GetPartitionImageDosType(firstBlock.AsSpan(0, length)) != null;
    }

    public async Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return await AmigaFileSystems.MountPartitionImage(stream, Name);
    }
}
