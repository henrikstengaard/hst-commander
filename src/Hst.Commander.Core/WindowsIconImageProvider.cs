using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Built-in image provider reading all images of a Windows icon file (.ico, magic <c>00 00 01 00</c>) with
///     <see cref="EmbeddedIconReader" />, largest first, then with the most colors.
/// </summary>
public sealed class WindowsIconImageProvider : IImageProvider
{
    private static readonly byte[] IconMagic = [0, 0, 1, 0];

    public string Name => "Windows icons";

    public string Description => "Windows .ico icon files";

    public Task<IReadOnlyList<EntryImage>> ReadImages(string name, Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var magic = new byte[IconMagic.Length];
        if (stream.ReadAtLeast(magic, magic.Length, false) < magic.Length || !magic.SequenceEqual(IconMagic))
        {
            return Task.FromResult<IReadOnlyList<EntryImage>>([]);
        }

        stream.Position = 0;
        var images = EmbeddedIconReader.ReadIcons("icon.ico", stream)
            .SelectMany(icon => icon.Images)
            .Select((image, index) => new EntryImage($"Icon image {index + 1}", image))
            .ToList();
        return Task.FromResult<IReadOnlyList<EntryImage>>(images);
    }
}
