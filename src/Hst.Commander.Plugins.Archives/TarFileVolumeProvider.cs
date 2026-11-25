using Hst.Commander.Abstractions;
using SharpCompress.Archives.Tar;
using SharpCompress.Readers;

namespace Hst.Commander.Plugins.Archives;

/// <summary>
/// File volume provider opening tar archives, detected by their first header block (see <see cref="TarHeader" />).
/// Compressed tar archives (e.g. .tar.gz and .tar.xz) are opened by <see cref="GzipFileVolumeProvider" /> and
/// <see cref="XzFileVolumeProvider" />.
/// </summary>
public class TarFileVolumeProvider : IFileVolumeProvider
{
    public string Name => "Tar";

    public string Description => "Tar archives";

    public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var block = new byte[TarHeader.Size];
        var length = await stream.ReadAtLeastAsync(block, block.Length, false, cancellationToken);
        return TarHeader.IsTarHeader(block.AsSpan(0, length));
    }

    public Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.Position = 0;
        return Task.FromResult<IVolume>(OpenTar(Name, stream, true));
    }

    /// <summary>
    /// Opens a tar archive in a seekable stream as a volume, disposing the stream with the volume unless
    /// <paramref name="leaveStreamOpen" />.
    /// </summary>
    internal static SharpCompressVolume OpenTar(string name, Stream stream, bool leaveStreamOpen)
    {
        var archive = TarArchive.OpenArchive(stream, new ReaderOptions { LeaveStreamOpen = leaveStreamOpen });
        try
        {
            return new SharpCompressVolume(name, archive);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }
}
