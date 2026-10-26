using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Services given to the plugins (<see cref="IPlugin.Initialize" />): opening streams, e.g. partitions, with the
///     file volume providers of <see cref="VolumeProviders.Files" />.
/// </summary>
public sealed class PluginHost(Func<IReadOnlyList<IFileVolumeProvider>> getFileVolumeProviders) : IPluginHost
{
    /// <summary>Host asking <see cref="VolumeProviders.Files" /> when a stream is opened.</summary>
    public static PluginHost Instance { get; } = new(() => VolumeProviders.Files);

    public async Task<IVolume?> OpenVolume(string name, Stream stream, IFileVolumeProvider? exclude = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            throw new ArgumentException("The stream must be seekable", nameof(stream));
        }

        foreach (var fileVolumeProvider in getFileVolumeProviders())
        {
            if (ReferenceEquals(fileVolumeProvider, exclude))
            {
                continue;
            }

            stream.Position = 0;
            if (!await IsMatch(fileVolumeProvider, name, stream, cancellationToken))
            {
                continue;
            }

            stream.Position = 0;
            return await fileVolumeProvider.CreateVolume(stream, cancellationToken);
        }

        return null;
    }

    /// <summary>Asks a file volume provider, a stream that can't be read isn't detected.</summary>
    private static async Task<bool> IsMatch(IFileVolumeProvider fileVolumeProvider, string name, Stream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            return await fileVolumeProvider.IsMatch(name, stream, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            return false;
        }
    }
}
