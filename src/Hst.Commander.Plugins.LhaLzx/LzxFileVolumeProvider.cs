using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.LhaLzx;

/// <summary>
/// File volume provider opening lzx archives, detected by their magic signature "LZX".
/// </summary>
public class LzxFileVolumeProvider : IFileVolumeProvider
{
    private static readonly byte[] Signature = "LZX"u8.ToArray();

    public string Name => "Lzx";

    public string Description => "Lzx archives";

    public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var signature = new byte[Signature.Length];
        var length = await stream.ReadAtLeastAsync(signature, signature.Length, false, cancellationToken);
        return length == signature.Length && signature.AsSpan().SequenceEqual(Signature);
    }

    public async Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default) =>
        await LzxVolume.Create(stream);
}
