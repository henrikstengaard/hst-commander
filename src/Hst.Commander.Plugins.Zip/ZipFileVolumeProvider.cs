using System.IO.Compression;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Zip;

/// <summary>
/// File volume provider opening zip archives, detected by their magic signature: "PK" followed by a local file header,
/// an end of central directory record (empty archive) or a spanned archive marker.
/// </summary>
public class ZipFileVolumeProvider : IFileVolumeProvider
{
    private static readonly byte[][] Signatures =
    [
        [0x50, 0x4B, 0x03, 0x04],
        [0x50, 0x4B, 0x05, 0x06],
        [0x50, 0x4B, 0x07, 0x08]
    ];

    public string Name => "Zip";

    public string Description => "Zip archives";

    // files in zip format with other extensions, e.g. .docx or .jar, are opened with Ctrl+PageDown
    public IReadOnlyCollection<string> Extensions { get; } = [".zip"];

    public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var signature = new byte[4];
        var length = await stream.ReadAtLeastAsync(signature, signature.Length, false, cancellationToken);
        return length == signature.Length &&
               Signatures.Any(zipSignature => signature.AsSpan().SequenceEqual(zipSignature));
    }

    public Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return Task.FromResult<IVolume>(new ZipVolume(new ZipArchive(stream, ZipArchiveMode.Read, true)));
    }
}
