using Hst.Commander.Abstractions;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Readers;

namespace Hst.Commander.Plugins.RarSevenZip;

/// <summary>
/// File volume provider opening 7z archives, detected by their magic signature "7z" BC AF 27 1C.
/// </summary>
public class SevenZipFileVolumeProvider : IFileVolumeProvider
{
    private static readonly byte[] Signature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];

    public string Name => "7z";

    public string Description => "7z archives";

    public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var signature = new byte[Signature.Length];
        var length = await stream.ReadAtLeastAsync(signature, signature.Length, false, cancellationToken);
        return length == signature.Length && signature.AsSpan().SequenceEqual(Signature);
    }

    public Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.Position = 0;
        var archive = SevenZipArchive.OpenArchive(stream, new ReaderOptions { LeaveStreamOpen = true });
        try
        {
            return Task.FromResult<IVolume>(new SharpCompressVolume(Name, archive));
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }
}
