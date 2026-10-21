namespace Hst.Commander.Abstractions;

/// <summary>
/// Interface for creating a new volume as a file, e.g. an empty Amiga disk file. Volume creators are listed in the
/// create dialog (F9), the user chooses one, enters the file name and the values of its <see cref="Fields" />, and
/// the file is created in the current location of the active pane.
/// </summary>
public interface IVolumeCreator
{
    /// <summary>
    /// Name shown in the create dialog, e.g. "Amiga disk file".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Short description shown next to the name, e.g. "Double density floppy disk (880 KB)".
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Extension of the file created including the dot, e.g. ".adf". It is added to the file name entered by the user
    /// when missing.
    /// </summary>
    string FileExtension { get; }

    /// <summary>
    /// Input fields the user enters when creating the volume, in the order shown. Hst Commander checks required fields
    /// are entered and numbers are valid before calling <see cref="Create" />.
    /// </summary>
    IReadOnlyList<VolumeCreatorField> Fields { get; }

    /// <summary>
    /// Writes the new volume to a stream. Throw <see cref="IOException" /> with a message for the user when a value is
    /// invalid; the partially created file is deleted.
    /// </summary>
    /// <param name="stream">Empty stream of the new file, seekable, readable and writable.</param>
    /// <param name="values">Values entered for the <see cref="Fields" />.</param>
    /// <param name="cancellationToken"></param>
    Task Create(Stream stream, VolumeCreatorValues values, CancellationToken cancellationToken = default);
}
