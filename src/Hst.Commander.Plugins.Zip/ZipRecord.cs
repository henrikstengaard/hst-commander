namespace Hst.Commander.Plugins.Zip;

/// <summary>
/// Central directory record of a zip archive entry, as read and written by <see cref="ZipFormat" />. Sizes and the
/// offset are the zip64 values when the archive stores them in a zip64 extra field, which isn't part of
/// <see cref="Extra" /> (it's written again from the values when needed).
/// </summary>
internal sealed record ZipRecord
{
    public ushort VersionMadeBy { get; init; }
    public ushort VersionNeeded { get; init; }
    public ushort Flags { get; init; }
    public ushort Method { get; init; }

    /// <summary>MS-DOS date in the high 16 bits and time in the low 16 bits, local time.</summary>
    public uint DosDateTime { get; init; }

    public uint Crc { get; init; }
    public long CompressedSize { get; init; }
    public long UncompressedSize { get; init; }
    public byte[] NameBytes { get; init; } = [];

    /// <summary>Extra fields of the central directory record without the zip64 extra field.</summary>
    public byte[] Extra { get; init; } = [];

    public byte[] Comment { get; init; } = [];
    public ushort InternalAttributes { get; init; }
    public uint ExternalAttributes { get; init; }
    public long LocalHeaderOffset { get; init; }

    /// <summary>Name of the entry, as decoded by <see cref="System.IO.Compression.ZipArchive" />.</summary>
    public string FullName { get; init; } = string.Empty;
}
