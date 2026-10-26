using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// Image provider of the viewer (F3) for Amiga images: all images of Amiga .info icons (disk objects, magic
/// <c>0xE310</c>, or PNG files named .info for AmigaOS 4 true color icons), e.g. the normal and selected images of the
/// true color, GlowIcon, NewIcon and planar images, and IFF ILBM and PBM pictures (<see cref="IlbmReader" />).
/// </summary>
public sealed class AmigaImageProvider : IImageProvider
{
    private static readonly AmigaIconProvider IconProvider = new();
    private static readonly byte[] DiskObjectMagic = [0xe3, 0x10];

    public string Name => "Amiga images";

    public string Description => "Amiga .info icons and IFF ILBM pictures";

    public async Task<IReadOnlyList<EntryImage>> ReadImages(string name, Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(stream);

        var data = new byte[stream.Length];
        await stream.ReadExactlyAsync(data, cancellationToken);
        if (IlbmReader.IsIff(data))
        {
            return IlbmReader.Read(data).Select(image => new EntryImage(image.Name, image.Image)).ToList();
        }

        // a PNG file is only a true color icon when it is named like one
        if (!data.AsSpan().StartsWith(DiskObjectMagic) &&
            !name.EndsWith(AmigaIconProvider.InfoExtension, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        stream.Position = 0;
        return (await IconProvider.ReadIcons(name, stream, cancellationToken))
            .SelectMany(icon => icon.Images.Select(image => new EntryImage(icon.Name, image)))
            .ToList();
    }
}
