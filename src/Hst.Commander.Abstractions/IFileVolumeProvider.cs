namespace Hst.Commander.Abstractions;

/// <summary>
/// Interface for opening files as volumes, e.g. ISO images or zip archives. When a file is opened (Enter or
/// double-click, or Ctrl+PageDown to open it as a volume), Hst Commander asks the file volume providers whether the
/// file is one of their files, typically by its magic signature, regardless of its extension, e.g. browsing a .nupkg
/// package as a zip archive, and browses the volume created instead of starting the file. Files are detected both in
/// local directories and inside other volumes, so e.g. a zip archive inside an ISO image can be opened.
/// </summary>
public interface IFileVolumeProvider
{
    /// <summary>
    /// Name of the file volume provider, e.g. "Zip".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Short description of the files opened as volumes, e.g. "Zip archives".
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Checks if a file can be opened as a volume, typically by reading its magic signature. Return false for files
    /// that can't be opened, so they are started as usual.
    /// </summary>
    /// <param name="name">Name of the file, e.g. to check its extension.</param>
    /// <param name="stream">Contents of the file, positioned at the start. The stream may not be seekable.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>True when the file can be opened as a volume with <see cref="CreateVolume" />.</returns>
    Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a volume at its root for browsing the contents of a file. Each call returns a volume of its own, as e.g.
    /// a copy opens the file again on a background thread. The stream stays open while the volume is used and is
    /// disposed by Hst Commander after the volume, which is disposed first when it implements
    /// <see cref="IDisposable" />.
    /// </summary>
    /// <param name="stream">Contents of the file, seekable and positioned at the start.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Volume at its root.</returns>
    Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default);
}
