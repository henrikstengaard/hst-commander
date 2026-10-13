namespace Hst.Commander.Extensions.Volume.Abstractions;

/// <summary>
/// Interface for providing a volume to Hst Commander.
/// </summary>
public interface IVolume
{
    /// <summary>
    /// Name of volume.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Path components of current directory.
    /// </summary>
    public IEnumerable<string> CurrentPathComponents { get; }

    /// <summary>
    /// List entries in current directory.
    /// </summary>
    /// <returns></returns>
    IAsyncEnumerable<IEntry> ListEntries(CancellationToken cancellationToken = default);

    /// <summary>
    /// Change directory with absolute or relative path components.
    /// </summary>
    /// <param name="pathComponents">Path components to change to.</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default);
    
    Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Open an item in current directory for reading.
    /// </summary>
    /// <param name="name">Name of item to read.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Stream with the contents of the item.</returns>
    Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create an item in current directory for writing. An existing item is overwritten.
    /// Throws <see cref="NotSupportedException"/> when the volume is read-only.
    /// </summary>
    /// <param name="name">Name of item to write.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Stream to write the contents of the item to.</returns>
    Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a container in current directory, if it doesn't exist.
    /// Throws <see cref="NotSupportedException"/> when the volume is read-only.
    /// </summary>
    /// <param name="name">Name of container to create.</param>
    /// <param name="cancellationToken"></param>
    Task CreateContainer(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an item or an empty container in current directory.
    /// Throws <see cref="NotSupportedException"/> when the volume is read-only.
    /// </summary>
    /// <param name="name">Name of entry to delete.</param>
    /// <param name="cancellationToken"></param>
    Task Delete(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set the modified date of an item or container in current directory, e.g. to preserve it when copying.
    /// Throws <see cref="NotSupportedException"/> when the volume is read-only.
    /// </summary>
    /// <param name="name">Name of entry to set the modified date of.</param>
    /// <param name="modifiedDate">Modified date. Dates of kind unspecified are treated as UTC.</param>
    /// <param name="cancellationToken"></param>
    Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default);
}