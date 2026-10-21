using DiscUtils.Iso9660;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Volumes;

/// <summary>
/// File volume provider opening ISO 9660 images as <see cref="Iso9660Volume" />, detected by the signature "CD001" of
/// the first volume descriptor at offset 0x8001.
/// </summary>
public class Iso9660FileVolumeProvider : IFileVolumeProvider
{
    private const int SignatureOffset = 0x8001;
    private static readonly byte[] Signature = "CD001"u8.ToArray();

    public string Name => "ISO 9660";

    public string Description => "ISO 9660 / CD-ROM images";

    public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[SignatureOffset + Signature.Length];
        var length = await stream.ReadAtLeastAsync(header, header.Length, false, cancellationToken);
        return length == header.Length && header.AsSpan(SignatureOffset).SequenceEqual(Signature);
    }

    public Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return Task.FromResult<IVolume>(new Iso9660Volume(new CDReader(stream, true)));
    }
}
