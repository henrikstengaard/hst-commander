using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Services given to the plugins (<see cref="IPlugin.Initialize" />): opening streams, e.g. partitions, with the
///     file volume providers of <see cref="VolumeProviders.Files" />, reading images with the image providers of
///     <see cref="VolumeProviders.Images" /> and the directories of the plugins' own files.
/// </summary>
/// <param name="getFileVolumeProviders">File volume providers asked when a stream is opened.</param>
/// <param name="getImageProviders">Image providers asked when images are read, none when null.</param>
/// <param name="pluginsDirectory">Directory holding the plugins' directories, none when null.</param>
public sealed class PluginHost(
    Func<IReadOnlyList<IFileVolumeProvider>> getFileVolumeProviders,
    Func<IReadOnlyList<IImageProvider>>? getImageProviders = null,
    string? pluginsDirectory = null) : IPluginHost
{
    /// <summary>Most bytes of a file read as images, like the viewer.</summary>
    public const long MaxImageLength = ViewerDocument.MaxImageLength;

    /// <summary>
    ///     Host asking <see cref="VolumeProviders.Files" /> and <see cref="VolumeProviders.Images" />, with the plugin
    ///     directories in "~/.hst-commander/plugins".
    /// </summary>
    public static PluginHost Instance { get; } = new(() => VolumeProviders.Files, () => VolumeProviders.Images,
        Path.Combine(SettingsPaths.Directory, PluginLoader.DirectoryName));

    public string? GetPluginDirectory(string pluginName)
    {
        ArgumentNullException.ThrowIfNull(pluginName);

        // lower case, so the directory is the same on case-sensitive file systems whatever the name's case
        return pluginsDirectory == null ? null : Path.Combine(pluginsDirectory, pluginName.ToLowerInvariant());
    }

    public async Task<IReadOnlyList<EntryImage>> ReadImages(string name, Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(stream);

        var providers = getImageProviders?.Invoke() ?? [];
        if (providers.Count == 0)
        {
            return [];
        }

        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        if (memoryStream.Length is 0 or > MaxImageLength)
        {
            return [];
        }

        var data = memoryStream.ToArray();
        foreach (var provider in providers)
        {
            try
            {
                using var providerStream = new MemoryStream(data, false);
                var images = await provider.ReadImages(name, providerStream, cancellationToken);
                if (images.Count > 0)
                {
                    return images;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // an invalid image or a failing plugin, the next provider is asked
            }
        }

        return [];
    }

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
