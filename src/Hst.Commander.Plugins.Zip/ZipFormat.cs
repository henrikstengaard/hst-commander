using System.Buffers.Binary;

namespace Hst.Commander.Plugins.Zip;

/// <summary>
/// Reads and writes the structures of a zip archive that <see cref="ZipVolume" /> changes: the central directory
/// (with zip64 records), the end of central directory record and local file headers. The data of entries is read
/// with <see cref="System.IO.Compression.ZipArchive" />.
/// </summary>
internal static class ZipFormat
{
    public const uint LocalHeaderSignature = 0x04034b50;
    public const uint CentralDirectorySignature = 0x02014b50;
    public const uint EndOfCentralDirectorySignature = 0x06054b50;
    public const uint Zip64EndOfCentralDirectorySignature = 0x06064b50;
    public const uint Zip64LocatorSignature = 0x07064b50;

    public const int LocalHeaderSize = 30;
    public const int CentralDirectoryRecordSize = 46;
    public const int EndOfCentralDirectorySize = 22;
    public const int Zip64EndOfCentralDirectorySize = 56;
    public const int Zip64LocatorSize = 20;

    public const ushort Zip64ExtraId = 0x0001;
    public const ushort NtfsExtraId = 0x000a;
    public const ushort ExtendedTimestampExtraId = 0x5455;

    /// <summary>General purpose flag: the name and comment are UTF-8.</summary>
    public const ushort Utf8Flag = 0x0800;

    public const ushort MethodStored = 0;
    public const ushort MethodDeflated = 8;

    public const ushort VersionDefault = 20;
    public const ushort VersionStored = 10;
    public const ushort VersionZip64 = 45;

    /// <summary>Directory attribute of MS-DOS external attributes.</summary>
    public const uint DirectoryAttribute = 0x10;

    private const uint Max32 = 0xffffffff;
    private const ushort Max16 = 0xffff;

    /// <summary>Central directory of an archive.</summary>
    /// <param name="Records">Records in the order of the central directory.</param>
    /// <param name="Offset">Offset of the central directory, the end of the entries' data.</param>
    /// <param name="Comment">Comment of the archive.</param>
    public sealed record CentralDirectory(List<ZipRecord> Records, long Offset, byte[] Comment);

    /// <summary>
    /// Reads the central directory of an archive. Throws <see cref="InvalidDataException" /> with the reason when
    /// it can't be changed, e.g. an archive spanning several disks.
    /// </summary>
    public static CentralDirectory ReadCentralDirectory(Stream stream)
    {
        var length = stream.Length;
        if (length == 0)
        {
            return new CentralDirectory([], 0, []);
        }

        var (eocdOffset, eocd) = FindEndOfCentralDirectory(stream, length);
        var disk = BinaryPrimitives.ReadUInt16LittleEndian(eocd.AsSpan(4));
        var centralDirectoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(eocd.AsSpan(6));
        long diskEntries = BinaryPrimitives.ReadUInt16LittleEndian(eocd.AsSpan(8));
        long entries = BinaryPrimitives.ReadUInt16LittleEndian(eocd.AsSpan(10));
        long size = BinaryPrimitives.ReadUInt32LittleEndian(eocd.AsSpan(12));
        long offset = BinaryPrimitives.ReadUInt32LittleEndian(eocd.AsSpan(16));
        var comment = eocd[EndOfCentralDirectorySize..];
        var endOfCentralDirectory = eocdOffset;

        if (entries == Max16 || diskEntries == Max16 || size == Max32 || offset == Max32 ||
            disk == Max16 || centralDirectoryDisk == Max16)
        {
            if (eocdOffset < Zip64LocatorSize)
            {
                throw new InvalidDataException("The zip64 end of central directory locator is missing");
            }

            var locator = ReadAt(stream, eocdOffset - Zip64LocatorSize, Zip64LocatorSize);
            if (BinaryPrimitives.ReadUInt32LittleEndian(locator) != Zip64LocatorSignature)
            {
                throw new InvalidDataException("The zip64 end of central directory locator is missing");
            }

            var zip64Offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(locator.AsSpan(8));
            if (BinaryPrimitives.ReadUInt32LittleEndian(locator.AsSpan(16)) > 1 || zip64Offset < 0 ||
                zip64Offset > eocdOffset - Zip64LocatorSize - Zip64EndOfCentralDirectorySize)
            {
                throw new InvalidDataException("Zip archives spanning several disks can't be changed");
            }

            var zip64 = ReadAt(stream, zip64Offset, Zip64EndOfCentralDirectorySize);
            if (BinaryPrimitives.ReadUInt32LittleEndian(zip64) != Zip64EndOfCentralDirectorySignature)
            {
                throw new InvalidDataException("The zip64 end of central directory record is missing");
            }

            disk = (ushort)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(zip64.AsSpan(16)), Max16);
            centralDirectoryDisk = (ushort)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(zip64.AsSpan(20)), Max16);
            diskEntries = (long)BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(24));
            entries = (long)BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(32));
            size = (long)BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(40));
            offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(48));
            endOfCentralDirectory = zip64Offset;
        }

        if (disk != 0 || centralDirectoryDisk != 0 || diskEntries != entries)
        {
            throw new InvalidDataException("Zip archives spanning several disks can't be changed");
        }

        if (offset < 0 || size < 0 || entries < 0 || offset + size > endOfCentralDirectory ||
            size > int.MaxValue)
        {
            throw new InvalidDataException("The central directory of the zip archive is invalid");
        }

        var data = ReadAt(stream, offset, (int)size);
        var records = new List<ZipRecord>();
        var position = 0;
        for (long index = 0; index < entries; index++)
        {
            records.Add(ReadRecord(data, ref position, offset));
        }

        var offsets = new HashSet<long>();
        if (records.Any(record => !offsets.Add(record.LocalHeaderOffset)))
        {
            throw new InvalidDataException("Entries of the zip archive share their data");
        }

        return new CentralDirectory(records, offset, comment);
    }

    private static (long Offset, byte[] Record) FindEndOfCentralDirectory(Stream stream, long length)
    {
        var searchLength = (int)Math.Min(length, EndOfCentralDirectorySize + Max16);
        var searchOffset = length - searchLength;
        var data = ReadAt(stream, searchOffset, searchLength);
        int? found = null;
        for (var index = searchLength - EndOfCentralDirectorySize; index >= 0; index--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(index)) != EndOfCentralDirectorySignature)
            {
                continue;
            }

            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(index + 20));
            if (index + EndOfCentralDirectorySize + commentLength == searchLength)
            {
                found = index;
                break;
            }

            found ??= index;
        }

        if (found is not { } eocdIndex)
        {
            throw new InvalidDataException("The end of central directory record of the zip archive is missing");
        }

        var recordLength = Math.Min(searchLength - eocdIndex,
            EndOfCentralDirectorySize + BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(eocdIndex + 20)));
        return (searchOffset + eocdIndex, data[eocdIndex..(eocdIndex + recordLength)]);
    }

    private static ZipRecord ReadRecord(byte[] data, ref int position, long centralDirectoryOffset)
    {
        if (position + CentralDirectoryRecordSize > data.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position)) != CentralDirectorySignature)
        {
            throw new InvalidDataException("The central directory of the zip archive is invalid");
        }

        var span = data.AsSpan(position);
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(span[28..]);
        var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(span[30..]);
        var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(span[32..]);
        var recordLength = CentralDirectoryRecordSize + nameLength + extraLength + commentLength;
        if (position + recordLength > data.Length)
        {
            throw new InvalidDataException("The central directory of the zip archive is invalid");
        }

        long compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(span[20..]);
        long uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(span[24..]);
        var diskStart = BinaryPrimitives.ReadUInt16LittleEndian(span[34..]);
        long localHeaderOffset = BinaryPrimitives.ReadUInt32LittleEndian(span[42..]);
        var extra = span.Slice(CentralDirectoryRecordSize + nameLength, extraLength).ToArray();

        // the zip64 extra field has the values of the fields that don't fit, in this order
        if (FindExtraField(extra, Zip64ExtraId) is var (fieldOffset, fieldLength))
        {
            var field = extra.AsSpan(fieldOffset, fieldLength);
            var fieldPosition = 0;
            if (uncompressedSize == Max32 && TryReadUInt64(field, ref fieldPosition, out var value))
            {
                uncompressedSize = value;
            }

            if (compressedSize == Max32 && TryReadUInt64(field, ref fieldPosition, out value))
            {
                compressedSize = value;
            }

            if (localHeaderOffset == Max32 && TryReadUInt64(field, ref fieldPosition, out value))
            {
                localHeaderOffset = value;
            }

            if (diskStart == Max16 && fieldPosition + 4 <= field.Length)
            {
                diskStart = (ushort)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(field[fieldPosition..]), Max16);
            }

            extra = RemoveExtraFields(extra, Zip64ExtraId);
        }

        if (diskStart != 0)
        {
            throw new InvalidDataException("Zip archives spanning several disks can't be changed");
        }

        if (compressedSize < 0 || uncompressedSize < 0 || localHeaderOffset < 0 ||
            localHeaderOffset >= centralDirectoryOffset)
        {
            throw new InvalidDataException("The central directory of the zip archive is invalid");
        }

        var record = new ZipRecord
        {
            VersionMadeBy = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]),
            VersionNeeded = BinaryPrimitives.ReadUInt16LittleEndian(span[6..]),
            Flags = BinaryPrimitives.ReadUInt16LittleEndian(span[8..]),
            Method = BinaryPrimitives.ReadUInt16LittleEndian(span[10..]),
            DosDateTime = (uint)BinaryPrimitives.ReadUInt16LittleEndian(span[14..]) << 16 |
                          BinaryPrimitives.ReadUInt16LittleEndian(span[12..]),
            Crc = BinaryPrimitives.ReadUInt32LittleEndian(span[16..]),
            CompressedSize = compressedSize,
            UncompressedSize = uncompressedSize,
            NameBytes = span.Slice(CentralDirectoryRecordSize, nameLength).ToArray(),
            Extra = extra,
            Comment = span.Slice(CentralDirectoryRecordSize + nameLength + extraLength, commentLength).ToArray(),
            InternalAttributes = BinaryPrimitives.ReadUInt16LittleEndian(span[36..]),
            ExternalAttributes = BinaryPrimitives.ReadUInt32LittleEndian(span[38..]),
            LocalHeaderOffset = localHeaderOffset
        };
        position += recordLength;
        return record;
    }

    private static bool TryReadUInt64(ReadOnlySpan<byte> field, ref int position, out long value)
    {
        if (position + 8 > field.Length)
        {
            value = 0;
            return false;
        }

        value = (long)BinaryPrimitives.ReadUInt64LittleEndian(field[position..]);
        position += 8;
        return true;
    }

    /// <summary>Finds an extra field by id, null when not found or the extra fields are invalid.</summary>
    /// <returns>Offset and length of the field's data.</returns>
    private static (int Offset, int Length)? FindExtraField(byte[] extra, ushort id)
    {
        var position = 0;
        while (position + 4 <= extra.Length)
        {
            var fieldId = BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(position));
            var fieldLength = BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(position + 2));
            if (position + 4 + fieldLength > extra.Length)
            {
                return null;
            }

            if (fieldId == id)
            {
                return (position + 4, fieldLength);
            }

            position += 4 + fieldLength;
        }

        return null;
    }

    /// <summary>
    /// Removes extra fields by id. The extra fields are returned as they are when they're invalid (not a sequence of
    /// fields).
    /// </summary>
    public static byte[] RemoveExtraFields(byte[] extra, params ushort[] ids)
    {
        var result = new MemoryStream();
        var position = 0;
        while (position + 4 <= extra.Length)
        {
            var fieldId = BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(position));
            var fieldLength = BinaryPrimitives.ReadUInt16LittleEndian(extra.AsSpan(position + 2));
            if (position + 4 + fieldLength > extra.Length)
            {
                return extra;
            }

            if (!ids.Contains(fieldId))
            {
                result.Write(extra, position, 4 + fieldLength);
            }

            position += 4 + fieldLength;
        }

        return position == extra.Length ? result.ToArray() : extra;
    }

    /// <summary>
    /// Writes the central directory, the zip64 end of central directory record and locator when needed and the end
    /// of central directory record at an offset, and cuts the stream off after them.
    /// </summary>
    public static void WriteCentralDirectory(Stream stream, IReadOnlyList<ZipRecord> records, long offset,
        byte[] comment)
    {
        var data = new MemoryStream();
        foreach (var record in records)
        {
            WriteRecord(data, record);
        }

        var size = data.Length;
        var zip64 = records.Count >= Max16 || size >= Max32 || offset >= Max32;
        if (zip64)
        {
            var zip64Offset = offset + data.Length;
            var zip64Record = new byte[Zip64EndOfCentralDirectorySize];
            var span = zip64Record.AsSpan();
            BinaryPrimitives.WriteUInt32LittleEndian(span, Zip64EndOfCentralDirectorySignature);
            BinaryPrimitives.WriteUInt64LittleEndian(span[4..], Zip64EndOfCentralDirectorySize - 12);
            BinaryPrimitives.WriteUInt16LittleEndian(span[12..], VersionZip64);
            BinaryPrimitives.WriteUInt16LittleEndian(span[14..], VersionZip64);
            BinaryPrimitives.WriteUInt64LittleEndian(span[24..], (ulong)records.Count);
            BinaryPrimitives.WriteUInt64LittleEndian(span[32..], (ulong)records.Count);
            BinaryPrimitives.WriteUInt64LittleEndian(span[40..], (ulong)size);
            BinaryPrimitives.WriteUInt64LittleEndian(span[48..], (ulong)offset);
            data.Write(zip64Record);

            var locator = new byte[Zip64LocatorSize];
            BinaryPrimitives.WriteUInt32LittleEndian(locator, Zip64LocatorSignature);
            BinaryPrimitives.WriteUInt64LittleEndian(locator.AsSpan(8), (ulong)zip64Offset);
            BinaryPrimitives.WriteUInt32LittleEndian(locator.AsSpan(16), 1);
            data.Write(locator);
        }

        var eocd = new byte[EndOfCentralDirectorySize];
        BinaryPrimitives.WriteUInt32LittleEndian(eocd, EndOfCentralDirectorySignature);
        BinaryPrimitives.WriteUInt16LittleEndian(eocd.AsSpan(8), (ushort)Math.Min(records.Count, Max16));
        BinaryPrimitives.WriteUInt16LittleEndian(eocd.AsSpan(10), (ushort)Math.Min(records.Count, Max16));
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(12), (uint)Math.Min(size, Max32));
        BinaryPrimitives.WriteUInt32LittleEndian(eocd.AsSpan(16), (uint)Math.Min(offset, Max32));
        BinaryPrimitives.WriteUInt16LittleEndian(eocd.AsSpan(20), (ushort)comment.Length);
        data.Write(eocd);
        data.Write(comment);

        stream.Position = offset;
        data.Position = 0;
        data.CopyTo(stream);
        stream.SetLength(stream.Position);
        stream.Flush();
    }

    private static void WriteRecord(Stream stream, ZipRecord record)
    {
        // the zip64 extra field has the values that don't fit, in this order
        var zip64Field = new MemoryStream();
        var buffer = new byte[8];
        foreach (var value in new[] { record.UncompressedSize, record.CompressedSize, record.LocalHeaderOffset })
        {
            if (value >= Max32)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(buffer, (ulong)value);
                zip64Field.Write(buffer);
            }
        }

        var extra = record.Extra;
        if (zip64Field.Length > 0)
        {
            var zip64Extra = new byte[4 + zip64Field.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(zip64Extra, Zip64ExtraId);
            BinaryPrimitives.WriteUInt16LittleEndian(zip64Extra.AsSpan(2), (ushort)zip64Field.Length);
            zip64Field.ToArray().CopyTo(zip64Extra, 4);
            extra = [..zip64Extra, ..extra];
        }

        if (extra.Length > Max16)
        {
            throw new IOException("The extra fields of a zip entry are too long");
        }

        var header = new byte[CentralDirectoryRecordSize];
        var span = header.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, CentralDirectorySignature);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], record.VersionMadeBy);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..],
            zip64Field.Length > 0 ? Math.Max(record.VersionNeeded, VersionZip64) : record.VersionNeeded);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], record.Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], record.Method);
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], (ushort)record.DosDateTime);
        BinaryPrimitives.WriteUInt16LittleEndian(span[14..], (ushort)(record.DosDateTime >> 16));
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], record.Crc);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], (uint)Math.Min(record.CompressedSize, Max32));
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)Math.Min(record.UncompressedSize, Max32));
        BinaryPrimitives.WriteUInt16LittleEndian(span[28..], (ushort)record.NameBytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span[30..], (ushort)extra.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span[32..], (ushort)record.Comment.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span[36..], record.InternalAttributes);
        BinaryPrimitives.WriteUInt32LittleEndian(span[38..], record.ExternalAttributes);
        BinaryPrimitives.WriteUInt32LittleEndian(span[42..], (uint)Math.Min(record.LocalHeaderOffset, Max32));
        stream.Write(header);
        stream.Write(record.NameBytes);
        stream.Write(extra);
        stream.Write(record.Comment);
    }

    /// <summary>Reads the local file header of an entry, with its name and extra fields.</summary>
    public static byte[] ReadLocalHeader(Stream stream, long offset)
    {
        var header = ReadAt(stream, offset, LocalHeaderSize);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != LocalHeaderSignature)
        {
            throw new IOException("The local header of a zip entry is invalid");
        }

        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26));
        var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28));
        return [..header, ..ReadAt(stream, offset + LocalHeaderSize, nameLength + extraLength)];
    }

    /// <summary>
    /// Creates the local file header of a changed entry from its current one: the flags, date and name of the
    /// record, the other fields and the extra fields as they are, without timestamp extra fields when the date
    /// changed (so it isn't read instead of the date).
    /// </summary>
    public static byte[] ChangeLocalHeader(byte[] localHeader, ZipRecord record, bool dateChanged)
    {
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(localHeader.AsSpan(26));
        var extra = localHeader[(LocalHeaderSize + nameLength)..];
        if (dateChanged)
        {
            extra = RemoveExtraFields(extra, NtfsExtraId, ExtendedTimestampExtraId);
        }

        var header = localHeader[..LocalHeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), record.Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), (ushort)record.DosDateTime);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), (ushort)(record.DosDateTime >> 16));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26), (ushort)record.NameBytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(28), (ushort)extra.Length);
        return [..header, ..record.NameBytes, ..extra];
    }

    /// <summary>
    /// Creates the local file header of a new entry. A zip64 extra field with both sizes is added when the
    /// uncompressed size doesn't fit 32 bits (the compressed size is never larger, as such data is stored).
    /// </summary>
    public static byte[] CreateLocalHeader(ZipRecord record)
    {
        var zip64 = record.UncompressedSize >= Max32;
        var header = new byte[LocalHeaderSize + record.NameBytes.Length + (zip64 ? 20 : 0)];
        var span = header.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, LocalHeaderSignature);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], zip64 ? VersionZip64 : record.VersionNeeded);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], record.Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(span[8..], record.Method);
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], (ushort)record.DosDateTime);
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], (ushort)(record.DosDateTime >> 16));
        BinaryPrimitives.WriteUInt32LittleEndian(span[14..], record.Crc);
        BinaryPrimitives.WriteUInt32LittleEndian(span[18..], zip64 ? Max32 : (uint)record.CompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(span[22..], zip64 ? Max32 : (uint)record.UncompressedSize);
        BinaryPrimitives.WriteUInt16LittleEndian(span[26..], (ushort)record.NameBytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span[28..], (ushort)(zip64 ? 20 : 0));
        record.NameBytes.CopyTo(header, LocalHeaderSize);
        if (zip64)
        {
            var extra = span[(LocalHeaderSize + record.NameBytes.Length)..];
            BinaryPrimitives.WriteUInt16LittleEndian(extra, Zip64ExtraId);
            BinaryPrimitives.WriteUInt16LittleEndian(extra[2..], 16);
            BinaryPrimitives.WriteUInt64LittleEndian(extra[4..], (ulong)record.UncompressedSize);
            BinaryPrimitives.WriteUInt64LittleEndian(extra[12..], (ulong)record.CompressedSize);
        }

        return header;
    }

    /// <summary>
    /// MS-DOS date and time of a date in local time, as zip archives store it: 2 second precision, from 1980 to
    /// 2107 (dates outside are clamped).
    /// </summary>
    public static uint ToDosDateTime(DateTime date)
    {
        var local = date.Kind == DateTimeKind.Local
            ? date
            : DateTime.SpecifyKind(date, DateTimeKind.Utc).ToLocalTime();
        var min = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Local);
        var max = new DateTime(2107, 12, 31, 23, 59, 58, DateTimeKind.Local);
        local = local < min ? min : local > max ? max : local;
        var dosDate = (local.Year - 1980) << 9 | local.Month << 5 | local.Day;
        var dosTime = local.Hour << 11 | local.Minute << 5 | local.Second / 2;
        return (uint)dosDate << 16 | (uint)dosTime;
    }

    private static byte[] ReadAt(Stream stream, long offset, int length)
    {
        var buffer = new byte[length];
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }
}
