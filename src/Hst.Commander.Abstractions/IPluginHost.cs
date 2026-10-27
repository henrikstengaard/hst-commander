namespace Hst.Commander.Abstractions;

/// <summary>
/// Services Hst Commander gives plugins with <see cref="IPlugin.Initialize" />, so plugins can work together without
/// knowing each other, e.g. a disk plugin opening a partition with the file volume provider of another plugin.
/// </summary>
public interface IPluginHost
{
    /// <summary>
    /// Opens a stream as a volume with the first file volume provider detecting it, built-in or of any plugin, e.g. a
    /// partition of a disk holding an Amiga rigid disk block. The stream is passed on as it is, so the volume is
    /// writable when the stream is and the file volume provider can write (<see cref="IFileVolumeProvider.CanWrite" />).
    /// </summary>
    /// <param name="name">Name of the stream given to <see cref="IFileVolumeProvider.IsMatch" />, e.g. "1".</param>
    /// <param name="stream">
    /// Seekable stream to open. The caller keeps owning it: it stays open while the volume is used and is disposed by
    /// the caller after the volume, which is disposed first when it implements <see cref="IDisposable" />.
    /// </param>
    /// <param name="exclude">File volume provider not asked, e.g. the caller's own, to find other volumes on the same
    /// stream.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Volume at its root, null when no file volume provider detects the stream.</returns>
    Task<IVolume?> OpenVolume(string name, Stream stream, IFileVolumeProvider? exclude = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Directory of a plugin's own files in the user's settings, named by the plugin's name in lower case, e.g.
    /// "~/.hst-commander/plugins/amiga" for the plugin named "Amiga". It may not exist. Null when the host has none.
    /// </summary>
    /// <param name="pluginName">Name of the plugin, <see cref="IPlugin.Name" />.</param>
    string? GetPluginDirectory(string pluginName) => null;

    /// <summary>
    /// Reads the images of a file with the first image provider reading any, built-in (e.g. PNG, JPEG, GIF, BMP, WebP
    /// and Windows icons) or of any plugin, e.g. to use an image file as an icon. Errors of image providers are ignored.
    /// </summary>
    /// <param name="name">Name of the file, e.g. "def_drawer.png".</param>
    /// <param name="stream">Stream of the file at its start, read to the end and kept open.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The images, empty when no image provider reads the file.</returns>
    Task<IReadOnlyList<EntryImage>> ReadImages(string name, Stream stream,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EntryImage>>([]);
}
