using System.Buffers.Binary;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Reads the icon contained in a file without any OS support, so it works for files inside images too: the first
///     icon group in the resources of a Windows executable (like Explorer shows) or an image of an .ico file. The image
///     closest to the requested size is chosen (the smallest at least that size, with the most colors).
/// </summary>
public static class EmbeddedIconReader
{
    private const int MaxResourceSize = 4 * 1024 * 1024;
    private const uint ResourceTypeIcon = 3;
    private const uint ResourceTypeGroupIcon = 14;

    /// <summary>Extensions of files containing their own icon.</summary>
    public static IReadOnlyCollection<string> Extensions { get; } = [".exe", ".scr", ".ico"];

    public static bool HasEmbeddedIcon(string name) =>
        Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Reads the icon of a file, null when it has none or the file is invalid.
    /// </summary>
    /// <param name="name">File name, the extension tells the format.</param>
    /// <param name="stream">File data, copied to memory when not seekable.</param>
    /// <param name="size">Wanted width/height in pixels.</param>
    public static IconImage? ReadIcon(string name, Stream stream, int size)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            stream = EnsureSeekable(stream);
            return Path.GetExtension(name).Equals(".ico", StringComparison.OrdinalIgnoreCase)
                ? ReadIcoFile(stream, size)
                : ReadExecutableIcon(stream, size);
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException
                                              or ArgumentException or InvalidDataException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>Most icons read from an executable by <see cref="ReadIcons" />, e.g. system libraries have hundreds.</summary>
    public const int MaxIcons = 1000;

    /// <summary>
    ///     Reads all icons of a file, empty when it has none or the file is invalid: an .ico file is one icon with all
    ///     its images, an executable has an icon for each icon group in its resources (named by the resource name or
    ///     number, e.g. "#1"), at most <see cref="MaxIcons" />. The images are ordered largest first, then with the most
    ///     colors.
    /// </summary>
    /// <param name="name">File name, the extension tells the format.</param>
    /// <param name="stream">File data, copied to memory when not seekable.</param>
    public static IReadOnlyList<EntryIcon> ReadIcons(string name, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            stream = EnsureSeekable(stream);
            if (Path.GetExtension(name).Equals(".ico", StringComparison.OrdinalIgnoreCase))
            {
                var images = ReadIcoEntries(stream)
                    .Select(entry => DecodeImage(ReadAt(stream, entry.OffsetOrId, CheckSize(entry.Size))))
                    .OfType<IconImage>()
                    .ToList();
                return images.Count == 0 ? [] : [new EntryIcon("Icon", SortImages(images))];
            }

            if (OpenResources(stream) is not { } resources)
            {
                return [];
            }

            var icons = new List<EntryIcon>();
            foreach (var group in resources.ReadAll(ResourceTypeGroupIcon).Take(MaxIcons))
            {
                var images = ReadGroupEntries(group.Data)
                    .Select(entry => ReadImage(() => resources.ReadFirst(ResourceTypeIcon, entry.OffsetOrId)))
                    .OfType<IconImage>()
                    .ToList();
                if (images.Count > 0)
                {
                    icons.Add(new EntryIcon(group.Name, SortImages(images)));
                }
            }

            return icons;
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException
                                              or ArgumentException or InvalidDataException or OverflowException)
        {
            return [];
        }
    }

    /// <summary>Decodes an image, null when it is invalid, so the other images of an icon are still read.</summary>
    private static IconImage? ReadImage(Func<byte[]?> readData)
    {
        try
        {
            return readData() is { } data ? DecodeImage(data) : null;
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException
                                              or ArgumentException or InvalidDataException or OverflowException)
        {
            return null;
        }
    }

    private static IReadOnlyList<IconImage> SortImages(IEnumerable<IconImage> images) => images
        .OrderByDescending(image => image.Width * image.Height)
        .ThenByDescending(image => image.BitsPerPixel ?? 0)
        .ToList();

    private static Stream EnsureSeekable(Stream stream)
    {
        if (stream.CanSeek)
        {
            return stream;
        }

        var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        memoryStream.Position = 0;
        return memoryStream;
    }

    private sealed record IconDirectoryEntry(int Width, int Height, int BitCount, uint Size, uint OffsetOrId);

    private static IconImage? ReadIcoFile(Stream stream, int size)
    {
        var entries = ReadIcoEntries(stream);
        if (entries.Count == 0)
        {
            return null;
        }

        var entry = ChooseEntry(entries, size);
        return DecodeImage(ReadAt(stream, entry.OffsetOrId, CheckSize(entry.Size)));
    }

    private static IReadOnlyList<IconDirectoryEntry> ReadIcoEntries(Stream stream)
    {
        var header = ReadAt(stream, 0, 6);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4));
        if (BinaryPrimitives.ReadUInt16LittleEndian(header) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2)) != 1 || count == 0)
        {
            return [];
        }

        var entriesData = ReadAt(stream, 6, count * 16);
        return Enumerable.Range(0, count)
            .Select(index => ReadIconDirectoryEntry(entriesData.AsSpan(index * 16),
                BinaryPrimitives.ReadUInt32LittleEndian(entriesData.AsSpan(index * 16 + 12))))
            .ToList();
    }

    /// <summary>Entries of an icon group resource, the offsets are the ids of the icon resources.</summary>
    private static IReadOnlyList<IconDirectoryEntry> ReadGroupEntries(byte[]? groupData)
    {
        if (groupData == null || groupData.Length < 6)
        {
            return [];
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(groupData.AsSpan(4));
        return Enumerable.Range(0, count)
            .Where(index => 6 + (index + 1) * 14 <= groupData.Length)
            .Select(index => ReadIconDirectoryEntry(groupData.AsSpan(6 + index * 14),
                BinaryPrimitives.ReadUInt16LittleEndian(groupData.AsSpan(6 + index * 14 + 12))))
            .ToList();
    }

    private static IconDirectoryEntry ReadIconDirectoryEntry(ReadOnlySpan<byte> data, uint offsetOrId) => new(
        data[0] == 0 ? 256 : data[0],
        data[1] == 0 ? 256 : data[1],
        BinaryPrimitives.ReadUInt16LittleEndian(data[6..]),
        BinaryPrimitives.ReadUInt32LittleEndian(data[8..]),
        offsetOrId);

    /// <summary>The smallest image at least the wanted size, otherwise the largest; then the one with most colors.</summary>
    private static IconDirectoryEntry ChooseEntry(IReadOnlyList<IconDirectoryEntry> entries, int size)
    {
        var largeEnough = entries.Where(entry => entry.Width >= size).ToList();
        var candidates = largeEnough.Count > 0
            ? largeEnough.Where(entry => entry.Width == largeEnough.Min(e => e.Width))
            : entries.Where(entry => entry.Width == entries.Max(e => e.Width));
        // PNG images often have bit count 0
        return candidates.MaxBy(entry => entry.BitCount == 0 ? 32 : entry.BitCount)!;
    }

    private static IconImage? ReadExecutableIcon(Stream stream, int size)
    {
        if (OpenResources(stream) is not { } resources)
        {
            return null;
        }

        var entries = ReadGroupEntries(resources.ReadFirst(ResourceTypeGroupIcon, null));
        if (entries.Count == 0)
        {
            return null;
        }

        var iconData = resources.ReadFirst(ResourceTypeIcon, ChooseEntry(entries, size).OffsetOrId);
        return iconData == null ? null : DecodeImage(iconData);
    }

    /// <summary>Resources of a Windows executable, null when it isn't one or has no resources.</summary>
    private static ResourceReader? OpenResources(Stream stream)
    {
        var dosHeader = ReadAt(stream, 0, 64);
        if (dosHeader[0] != 'M' || dosHeader[1] != 'Z')
        {
            return null;
        }

        var peOffset = BinaryPrimitives.ReadUInt32LittleEndian(dosHeader.AsSpan(0x3c));
        var fileHeader = ReadAt(stream, peOffset, 24);
        if (BinaryPrimitives.ReadUInt32LittleEndian(fileHeader) != 0x4550) // "PE\0\0"
        {
            return null;
        }

        var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(fileHeader.AsSpan(6));
        var optionalHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(fileHeader.AsSpan(20));
        var optionalHeader = ReadAt(stream, peOffset + 24, optionalHeaderSize);
        var dataDirectoriesOffset = BinaryPrimitives.ReadUInt16LittleEndian(optionalHeader) switch
        {
            0x10b => 96, // PE32
            0x20b => 112, // PE32+
            _ => -1
        };
        // the resource table is the third data directory
        if (dataDirectoriesOffset < 0 || optionalHeaderSize < dataDirectoriesOffset + 3 * 8)
        {
            return null;
        }

        var resourceRva = BinaryPrimitives.ReadUInt32LittleEndian(optionalHeader.AsSpan(dataDirectoriesOffset + 16));
        if (resourceRva == 0)
        {
            return null;
        }

        var sectionTable = ReadAt(stream, peOffset + 24 + optionalHeaderSize, sectionCount * 40);
        var sections = Enumerable.Range(0, sectionCount)
            .Select(index => new Section(
                BinaryPrimitives.ReadUInt32LittleEndian(sectionTable.AsSpan(index * 40 + 12)),
                Math.Max(BinaryPrimitives.ReadUInt32LittleEndian(sectionTable.AsSpan(index * 40 + 8)),
                    BinaryPrimitives.ReadUInt32LittleEndian(sectionTable.AsSpan(index * 40 + 16))),
                BinaryPrimitives.ReadUInt32LittleEndian(sectionTable.AsSpan(index * 40 + 20))))
            .ToList();
        return new ResourceReader(stream, sections, RvaToOffset(sections, resourceRva));
    }

    private sealed record Section(uint VirtualAddress, uint VirtualSize, uint RawDataOffset);

    private static uint RvaToOffset(IEnumerable<Section> sections, uint rva)
    {
        var section = sections.FirstOrDefault(section =>
            rva >= section.VirtualAddress && rva < section.VirtualAddress + section.VirtualSize);
        return section == null
            ? throw new InvalidDataException("Resource outside sections.")
            : rva - section.VirtualAddress + section.RawDataOffset;
    }

    /// <summary>Reads data of a resource directory tree: type, name/id, language.</summary>
    private sealed class ResourceReader(Stream stream, IReadOnlyList<Section> sections, uint rootOffset)
    {
        /// <summary>Data of the first resource of a type (with the id when given) in the first language.</summary>
        public byte[]? ReadFirst(uint type, uint? id)
        {
            var typeOffset = FindEntry(0, type);
            var nameOffset = typeOffset is { } typeDirectory ? FindEntry(typeDirectory, id) : null;
            var languageOffset = nameOffset is { } nameDirectory ? FindEntry(nameDirectory, null) : null;
            return languageOffset is { } dataEntryOffset ? ReadData(dataEntryOffset) : null;
        }

        /// <summary>
        ///     Data of all resources of a type in their first language, named by the resource name or "#" and the id.
        /// </summary>
        public IEnumerable<(string Name, byte[]? Data)> ReadAll(uint type)
        {
            if (FindEntry(0, type) is not { } typeDirectory)
            {
                yield break;
            }

            foreach (var (nameOrId, nameDirectory) in ReadEntries(typeDirectory))
            {
                var name = (nameOrId & 0x80000000) != 0 ? ReadName(nameOrId & 0x7fffffff) : $"#{nameOrId}";
                var languageOffset = FindEntry(nameDirectory, null);
                yield return (name, languageOffset is { } dataEntryOffset ? ReadData(dataEntryOffset) : null);
            }
        }

        /// <summary>Resource name: its length in characters followed by the UTF-16 characters.</summary>
        private string ReadName(uint offset)
        {
            var length = BinaryPrimitives.ReadUInt16LittleEndian(ReadAt(stream, rootOffset + offset, 2));
            return System.Text.Encoding.Unicode.GetString(ReadAt(stream, rootOffset + offset + 2, length * 2));
        }

        private byte[] ReadData(uint dataEntryOffset)
        {
            var dataEntry = ReadAt(stream, rootOffset + dataEntryOffset, 16);
            var dataRva = BinaryPrimitives.ReadUInt32LittleEndian(dataEntry);
            var dataSize = BinaryPrimitives.ReadUInt32LittleEndian(dataEntry.AsSpan(4));
            return ReadAt(stream, RvaToOffset(sections, dataRva), CheckSize(dataSize));
        }

        /// <summary>
        ///     Offset (relative to the resource root) the entry with the id points to, or of the first entry when
        ///     no id is given. Subdirectory offsets have the high bit cleared.
        /// </summary>
        private uint? FindEntry(uint directoryOffset, uint? id) => ReadEntries(directoryOffset)
            .Where(entry => id == null || entry.NameOrId == id)
            .Select(entry => (uint?)entry.Offset)
            .FirstOrDefault();

        /// <summary>
        ///     Entries of a resource directory: the name (offset with the high bit set) or id, and the offset it points
        ///     to (relative to the resource root, subdirectory offsets with the high bit cleared).
        /// </summary>
        private List<(uint NameOrId, uint Offset)> ReadEntries(uint directoryOffset)
        {
            var header = ReadAt(stream, rootOffset + directoryOffset, 16);
            var count = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(12)) +
                        BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(14));
            var entries = ReadAt(stream, rootOffset + directoryOffset + 16, count * 8);
            return Enumerable.Range(0, count)
                .Select(index => (BinaryPrimitives.ReadUInt32LittleEndian(entries.AsSpan(index * 8)),
                    BinaryPrimitives.ReadUInt32LittleEndian(entries.AsSpan(index * 8 + 4)) & 0x7fffffff))
                .ToList();
        }
    }

    /// <summary>Decodes an icon image, a PNG or a DIB with the color image followed by the 1-bit AND mask.</summary>
    private static IconImage? DecodeImage(byte[] data)
    {
        if (data.Length >= 24 && data.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
        {
            var pngWidth = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16));
            var pngHeight = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20));
            // bit depth per sample and color type: 6 rgba, 2 rgb, 4 gray with alpha, otherwise one sample
            int? pngBitsPerPixel = data.Length >= 26
                ? data[24] * data[25] switch { 6 => 4, 2 => 3, 4 => 2, _ => 1 }
                : null;
            return new IconImage(pngWidth, pngHeight, data, null, pngBitsPerPixel);
        }

        if (data.Length < 40)
        {
            return null;
        }

        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(data);
        var width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        var height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8)) / 2; // color image and mask
        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14));
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16));
        var colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(32));
        if (headerSize < 40 || width is <= 0 or > 1024 || height is <= 0 or > 1024 || compression != 0 ||
            bitCount is not (1 or 4 or 8 or 24 or 32))
        {
            return null;
        }

        var paletteSize = bitCount <= 8 ? colorsUsed > 0 ? colorsUsed : 1 << bitCount : 0;
        var colorOffset = headerSize + paletteSize * 4;
        var colorStride = (width * bitCount + 31) / 32 * 4;
        var maskOffset = colorOffset + colorStride * height;
        var maskStride = (width + 31) / 32 * 4;
        if (maskOffset > data.Length)
        {
            return null;
        }

        var hasMask = maskOffset + maskStride * height <= data.Length;
        var pixels = new byte[width * height * 4];
        var hasAlpha = false;
        for (var y = 0; y < height; y++)
        {
            // rows are stored bottom-up
            var row = colorOffset + (height - 1 - y) * colorStride;
            for (var x = 0; x < width; x++)
            {
                var pixel = (y * width + x) * 4;
                switch (bitCount)
                {
                    case 32:
                        data.AsSpan(row + x * 4, 4).CopyTo(pixels.AsSpan(pixel));
                        hasAlpha |= pixels[pixel + 3] != 0;
                        break;
                    case 24:
                        data.AsSpan(row + x * 3, 3).CopyTo(pixels.AsSpan(pixel));
                        break;
                    default:
                        var bitOffset = x * bitCount;
                        var index = (data[row + bitOffset / 8] >> (8 - bitCount - bitOffset % 8)) & ((1 << bitCount) - 1);
                        if (index < paletteSize)
                        {
                            data.AsSpan(headerSize + index * 4, 3).CopyTo(pixels.AsSpan(pixel));
                        }

                        break;
                }
            }
        }

        // icons without alpha channel use the AND mask for transparency
        if (!hasAlpha)
        {
            for (var y = 0; y < height; y++)
            {
                var row = maskOffset + (height - 1 - y) * maskStride;
                for (var x = 0; x < width; x++)
                {
                    var transparent = hasMask && (data[row + x / 8] & (0x80 >> (x % 8))) != 0;
                    pixels[(y * width + x) * 4 + 3] = transparent ? (byte)0 : (byte)255;
                }
            }
        }

        return new IconImage(width, height, null, pixels, bitCount);
    }

    private static int CheckSize(uint size) => size is > 0 and <= MaxResourceSize
        ? (int)size
        : throw new InvalidDataException("Invalid icon size.");

    private static byte[] ReadAt(Stream stream, long offset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > stream.Length)
        {
            throw new InvalidDataException("Data outside file.");
        }

        var buffer = new byte[count];
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }
}
