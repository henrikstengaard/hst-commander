using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.LhaLzx;

/// <summary>
/// File volume provider opening lha archives, detected by the compression method of the first header at offset 2:
/// "-lh?-", "-lz?-" or "-pm?-".
/// </summary>
public class LhaFileVolumeProvider : IFileVolumeProvider
{
    private static readonly string[] MethodPrefixes = ["-lh", "-lz", "-pm"];

    public string Name => "Lha";

    public string Description => "Lha archives";

    public IReadOnlyCollection<string> Extensions { get; } = [".lha", ".lzh"];

    public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[7];
        var length = await stream.ReadAtLeastAsync(header, header.Length, false, cancellationToken);
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
