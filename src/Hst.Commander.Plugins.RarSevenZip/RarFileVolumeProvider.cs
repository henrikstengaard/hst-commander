using Hst.Commander.Abstractions;
using SharpCompress.Archives.Rar;
using SharpCompress.Readers;

namespace Hst.Commander.Plugins.RarSevenZip;

/// <summary>
/// File volume provider opening rar archives, detected by their magic signature "Rar!" 1A 07 followed by 00 (rar 1.5
/// to 4) or 01 00 (rar 5). Only the first volume of a multi-volume archive is read.
/// </summary>
public class RarFileVolumeProvider : IFileVolumeProvider
{
    private static readonly byte[] Signature = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07];

    public string Name => "Rar";

    public string Description => "Rar archives";

    public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[Signature.Length + 2];
        var length = await stream.ReadAtLeastAsync(header, header.Length, false, cancellationToken);
        if (length < Signature.Length + 1 || !header.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            return false;
        }

        return header[Signature.Length] == 0x00 ||
               (length == header.Length && header[Signature.Length] == 0x01 && header[Signature.Length + 1] == 0x00);
    }

    public Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.Position = 0;
        var archive = RarArchive.OpenArchive(stream, new ReaderOptions { LeaveStreamOpen = true, DisableCheckIncomplete = true });
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
