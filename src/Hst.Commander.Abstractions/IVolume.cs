namespace Hst.Commander.Abstractions;

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
    /// Rename an item or container in current directory.
    /// Throws <see cref="IOException"/> when an entry with the new name exists, except when only the case of the name
    /// changes, and <see cref="NotSupportedException"/> when the volume is read-only.
    /// </summary>
    /// <param name="name">Name of entry to rename.</param>
    /// <param name="newName">New name of entry.</param>
    /// <param name="cancellationToken"></param>
    Task Rename(string name, string newName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set the modified date of an item or container in current directory, e.g. to preserve it when copying.
    /// Throws <see cref="NotSupportedException"/> when the volume is read-only.
    /// </summary>
    /// <param name="name">Name of entry to set the modified date of.</param>
    /// <param name="modifiedDate">Modified date. Dates of kind unspecified are treated as UTC.</param>
    /// <param name="cancellationToken"></param>
    Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the current directory stores Amiga names, protection bits and comments as they are, e.g. an Amiga
    /// file system. Copying between such volumes keeps them; copying to or from other volumes uses UAE metadata files.
    /// Defaults to false.
    /// </summary>
    Task<bool> SupportsAmigaMetadata(CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>
    /// Set the Amiga protection bits and comment of an item or container in current directory.
    /// Throws <see cref="NotSupportedException"/> when the volume doesn't store them (the default) or is read-only.
    /// </summary>
    /// <param name="name">Name of entry to set the protection bits and comment of.</param>
    /// <param name="metadata">Protection bits and comment.</param>
    /// <param name="cancellationToken"></param>
    Task SetAmigaMetadata(string name, AmigaMetadata metadata, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Amiga protection bits and comments are not supported");

    /// <summary>
    /// True when <see cref="SetAmigaMetadata" /> can change the protection bits and comments of entries in the current
    /// directory, e.g. a writable Amiga file system. The properties dialog then lets the protection bits be changed.
    /// Defaults to false.
    /// </summary>
    Task<bool> CanSetAmigaMetadata(CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <summary>
    /// Attributes (<see cref="IEntry.Attributes" />) of entries in the current directory that
    /// <see cref="SetAttributes" /> can change, shown as check boxes the properties dialog can change. Defaults to
    /// none.
    /// </summary>
    Task<EntryAttributes> GetChangeableAttributes(CancellationToken cancellationToken = default) =>
        Task.FromResult(EntryAttributes.None);

    /// <summary>
    /// Set the attributes of an item or container in current directory. Only the attributes of
    /// <see cref="GetChangeableAttributes" /> are changed, other attributes are kept.
    /// Throws <see cref="NotSupportedException"/> when the volume can't change attributes (the default) or is read-only.
    /// </summary>
    /// <param name="name">Name of entry to set the attributes of.</param>
    /// <param name="attributes">Attributes of the entry.</param>
    /// <param name="cancellationToken"></param>
    Task SetAttributes(string name, EntryAttributes attributes, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Changing attributes is not supported");
}
