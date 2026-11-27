using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.LhaLzx;

/// <summary>
/// File volume provider opening lha archives, detected by the compression method of the first header at offset 2:
/// "-lh?-", "-lz?-" or "-pm?-". An empty archive (no bytes or only the end of archive marker, e.g. created with
/// <see cref="LhaVolumeCreator" />) is detected by its extension, ".lha" or ".lzh". Archives are writable images.
/// </summary>
public class LhaFileVolumeProvider : IFileVolumeProvider
{
    private static readonly string[] MethodPrefixes = ["-lh", "-lz", "-pm"];
    private static readonly string[] Extensions = [".lha", ".lzh"];

    public string Name => "Lha";

    public string Description => "Lha archives";

    public bool CanWrite => true;

    public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[7];
        var length = await stream.ReadAtLeastAsync(header, header.Length, false, cancellationToken);
        if (length == 0 || (length == 1 && header[0] == 0))
        {
            return Extensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        }

        if (length != header.Length || header[6] != '-')
        {
            return false;
        }

        var methodPrefix = System.Text.Encoding.ASCII.GetString(header, 2, 3);
        return MethodPrefixes.Contains(methodPrefix);
    }

    public async Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default) =>
        await LhaVolume.Create(stream);
}
