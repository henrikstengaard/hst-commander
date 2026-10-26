namespace Hst.Commander.Abstractions;

/// <summary>
/// Interface for reading icons stored in icon files next to the entries they belong to, e.g. Amiga .info files. The
/// GUI shows the icon left of the entry name, both for the icon file itself and for the entry it belongs to, when the
/// icon file is listed in the same directory. Icons are read in any location, so also inside images.
/// </summary>
public interface IIconProvider
{
    /// <summary>
    /// Name of the icon provider, e.g. "Amiga icons".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Short description of the icon files, e.g. "Amiga .info icon files".
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Name of the icon file holding the icon of an entry, e.g. "file.info" for both "file" and "file.info". Only the
    /// name is checked, no files are read. Return null when the entry can't have an icon file of this provider.
    /// </summary>
    /// <param name="name">Name of a file or directory.</param>
    string? GetIconFileName(string name);

    /// <summary>
    /// Name of the icon file inside a volume entry (<see cref="IEntry.IsVolume" />, e.g. a partition) holding the icon
    /// of the volume, e.g. "Disk.info" in the root of an Amiga partition. The icon file is looked up when the icon is
    /// read. Defaults to null: volumes have no icon file of this provider.
    /// </summary>
    /// <param name="name">Name of the volume entry.</param>
    string? GetVolumeIconFileName(string name) => null;

    /// <summary>
    /// Reads the icon of an icon file returned by <see cref="GetIconFileName" />. Check the magic signature and return
    /// null when the file isn't a valid icon file of this provider.
    /// </summary>
    /// <param name="name">Name of the icon file.</param>
    /// <param name="stream">Contents of the icon file, seekable and positioned at the start.</param>
    /// <param name="size">Wanted width/height in pixels. The icon can be returned in its own size, it is scaled.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Icon image, null when the file has no icon.</returns>
    Task<IconImage?> ReadIcon(string name, Stream stream, int size, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads all icons of an icon file, shown when viewing the icon in the properties dialog, e.g. the normal and the
    /// selected image of an Amiga icon. The icon shown next to the entry (<see cref="ReadIcon" />) should be the
    /// first. Defaults to the icon read with <see cref="ReadIcon" /> in 256 pixels.
    /// </summary>
    /// <param name="name">Name of the icon file.</param>
    /// <param name="stream">Contents of the icon file, seekable and positioned at the start.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Icons of the file, empty when the file has none.</returns>
    async Task<IReadOnlyList<EntryIcon>> ReadIcons(string name, Stream stream,
        CancellationToken cancellationToken = default) =>
        await ReadIcon(name, stream, 256, cancellationToken) is { } icon ? [new EntryIcon("Icon", [icon])] : [];

    /// <summary>
    /// Reads properties of an icon file shown in the properties dialog of the entry it belongs to and of the icon file
    /// itself, e.g. the type and the position of an Amiga icon. Defaults to none.
    /// </summary>
    /// <param name="name">Name of the icon file.</param>
    /// <param name="stream">Contents of the icon file, seekable and positioned at the start.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Properties of the icon, empty when the file has none or isn't an icon.</returns>
    Task<IReadOnlyList<EntryProperty>> ReadIconProperties(string name, Stream stream,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<EntryProperty>>([]);
}
