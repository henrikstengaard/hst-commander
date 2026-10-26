namespace Hst.Commander.Abstractions;

/// <summary>
/// Interface for reading the images of a file viewed with F3, e.g. Amiga icons and IFF ILBM pictures. A file can have
/// several images, e.g. the normal and selected images of an Amiga icon or the sizes of a Windows icon file, shown one
/// at a time in the viewer.
/// </summary>
public interface IImageProvider
{
    /// <summary>
    /// Name of the image provider, e.g. "Amiga images".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of the image provider, e.g. "Amiga .info icons and IFF ILBM pictures".
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Reads the images of a file, checking its magic signature (the name may help, e.g. by its extension). Returns
    /// an empty list when the file isn't an image of this provider, so the next provider is asked.
    /// </summary>
    /// <param name="name">Name of the file.</param>
    /// <param name="stream">The file in memory, seekable and at the start.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<EntryImage>> ReadImages(string name, Stream stream,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// An image of a file shown in the viewer.
/// </summary>
/// <param name="Name">Name of the image, e.g. "Selected (GlowIcon)" or "Frame 2"; the viewer adds its size.</param>
/// <param name="Image">The image, PNG data or BGRA pixels.</param>
public sealed record EntryImage(string Name, IconImage Image);
