using System.IO.Compression;
using System.Text;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Zip;

/// <summary>
/// Volume browsing the entries of a zip archive. Directories are the directory entries of the archive and the
/// directories in the paths of its files, as archives don't need to contain entries for directories. Entries are read
/// with <see cref="ZipArchive" />. The volume is writable when its stream is (a writable image, see
/// <see cref="ZipFileVolumeProvider.CanWrite" />): files and directories added are appended after the last entry
/// followed by the central directory written again, and renaming, deleting or changing the date of entries writes
/// their headers again, in place when their size is the same, otherwise the archive is written again from the first
/// entry changed, copying the compressed data of the entries after it as it is.
/// </summary>
public class ZipVolume : ArchiveVolume<int>, IDisposable
{
    private const string ReadOnlyMessage = "The zip archive is read-only";

    private static readonly char[] InvalidNameCharacters = ['/', '\\', '\0'];

    private readonly Stream _stream;
    private readonly string? _readOnlyMessage;
    private readonly byte[] _comment = [];
    private ZipArchive? _zipArchive;
    private List<ZipRecord> _records = [];
    private long _centralDirectoryOffset;

    private ZipVolume(Stream stream, ZipArchive? zipArchive) : base("Zip")
    {
        _stream = stream;
        _zipArchive = zipArchive;

        if (!stream.CanWrite)
        {
            _readOnlyMessage = ReadOnlyMessage;
        }
        else
        {
            try
            {
                var centralDirectory = ZipFormat.ReadCentralDirectory(stream);
                var zipArchiveEntries = ArchiveEntries;
                if (centralDirectory.Records.Count != zipArchiveEntries.Count)
                {
                    throw new InvalidDataException("The central directory of the zip archive is invalid");
                }

                _records = centralDirectory.Records
                    .Select((record, index) => record with { FullName = zipArchiveEntries[index].FullName })
                    .ToList();
                _centralDirectoryOffset = centralDirectory.Offset;
                _comment = centralDirectory.Comment;
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException)
            {
                _readOnlyMessage = $"{ReadOnlyMessage}: {exception.Message}";
                _records = [];
            }
        }

        AddEntries();
    }

    /// <summary>Entries of the archive in the order of the central directory, none for an empty stream.</summary>
    private IReadOnlyList<ZipArchiveEntry> ArchiveEntries => _zipArchive != null ? _zipArchive.Entries : [];

    /// <summary>True when the archive can be changed, false when its stream is read-only.</summary>
    public bool IsWritable => _readOnlyMessage == null;

    /// <summary>Creates a volume reading the entries of a zip archive.</summary>
    /// <param name="stream">
    /// Readable and seekable stream with the archive, kept open while the volume is used. An empty stream is an empty
    /// archive. The volume can change the archive when the stream is writable.
    /// </param>
    public static ZipVolume Create(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // the stream is left open, as it's owned by Hst Commander
        stream.Position = 0;
        var zipArchive = stream.Length == 0 ? null : new ZipArchive(stream, ZipArchiveMode.Read, true);
        return new ZipVolume(stream, zipArchive);
    }

    /// <summary>Adds the entries of the archive to the directory tree of the volume again.</summary>
    private void AddEntries()
    {
        ClearEntries();
        var zipArchiveEntries = ArchiveEntries;
        for (var index = 0; index < zipArchiveEntries.Count; index++)
        {
            var zipArchiveEntry = zipArchiveEntries[index];
            AddEntry(zipArchiveEntry.FullName, zipArchiveEntry.Length, GetModifiedDate(zipArchiveEntry), index);
        }
    }

    /// <summary>Modified date of an archive entry, stored in local time without a time zone in zip archives.</summary>
    private static DateTime GetModifiedDate(ZipArchiveEntry zipArchiveEntry) =>
        zipArchiveEntry.LastWriteTime.UtcDateTime;

    protected override Task<Stream> OpenArchiveEntry(string name, int archiveEntry,
        CancellationToken cancellationToken) =>
        Task.FromResult(_zipArchive!.Entries[archiveEntry].Open());

    private static bool IsDirectoryName(string fullName) => fullName.EndsWith('/') || fullName.EndsWith('\\');

    /// <summary>Path components of an entry, skipping empty, "." and ".." like the directory tree.</summary>
    private static List<string> GetPathComponents(string fullName) =>
        fullName
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Where(pathComponent => pathComponent != "." && pathComponent != Paths.Parent)
            .ToList();

    private static bool StartsWith(IReadOnlyList<string> pathComponents, IReadOnlyList<string> prefix) =>
        pathComponents.Count >= prefix.Count &&
        prefix.Select((pathComponent, index) => string.Equals(pathComponent, pathComponents[index],
            StringComparison.Ordinal)).All(equal => equal);

    private static bool IsPath(IReadOnlyList<string> pathComponents, IReadOnlyList<string> path) =>
        pathComponents.Count == path.Count && StartsWith(pathComponents, path);

    /// <summary>Name of a zip entry: path components separated by "/", ending with "/" for a directory.</summary>
    private static string GetFullName(IEnumerable<string> pathComponents, bool isDirectory) =>
        string.Join('/', pathComponents) + (isDirectory ? "/" : string.Empty);

    /// <summary>
    /// Changes the name of a record: UTF-8 with the UTF-8 flag set when the name isn't ASCII, otherwise ASCII with
    /// the flag as it was.
    /// </summary>
    private static ZipRecord WithName(ZipRecord record, string fullName)
    {
        var nameBytes = Encoding.UTF8.GetBytes(fullName);
        if (nameBytes.Length > ushort.MaxValue)
        {
            throw new IOException($"Name '{fullName}' is too long");
        }

        var isAscii = nameBytes.Length == fullName.Length;
        return record with
        {
            NameBytes = nameBytes,
            FullName = fullName,
            Flags = isAscii ? record.Flags : (ushort)(record.Flags | ZipFormat.Utf8Flag)
        };
    }

    private static DateTime ToUtc(DateTime date) => date.Kind switch
    {
        DateTimeKind.Local => date.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(date, DateTimeKind.Utc),
        _ => date
    };

    public override Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        ValidateName(name);

        if (FindDirectory(name) is { } directoryName)
        {
            throw new IOException($"'{directoryName}' is a directory");
        }

        // the path is kept, as the current directory may change before the file is written
        var path = GetEntryPath(FindFile(name)?.Name ?? name);
        var dataStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite,
            FileShare.None, 81920, FileOptions.DeleteOnClose);
        return Task.FromResult<Stream>(new WriteStream(dataStream, () => AddFile(path, dataStream)));
    }

    public override Task CreateContainer(string name, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        ValidateName(name);

        if (FindDirectory(name) != null)
        {
            return Task.CompletedTask;
        }

        if (FindFile(name) is var (fileName, _))
        {
            throw new IOException($"A file named '{fileName}' exists");
        }

        AppendDirectory(GetEntryPath(name), DateTime.UtcNow);
        return Task.CompletedTask;
    }

    public override Task Delete(string name, CancellationToken cancellationToken = default)
    {
        EnsureWritable();

        Dictionary<int, ZipRecord?> changes;
        if (FindFile(name) is var (fileName, _))
        {
            changes = FindFileRecords(GetEntryPath(fileName)).ToDictionary(index => index, _ => (ZipRecord?)null);
        }
        else if (FindDirectory(name) is { } directoryName)
        {
            if (!IsDirectoryEmpty(directoryName))
            {
                throw new IOException($"Directory '{directoryName}' is not empty");
            }

            // the directory entries of the directory, as it's empty
            var path = GetEntryPath(directoryName);
            changes = _records
                .Select((record, index) => (record, index))
                .Where(x => StartsWith(GetPathComponents(x.record.FullName), path))
                .ToDictionary(x => x.index, _ => (ZipRecord?)null);
        }
        else
        {
            throw new FileNotFoundException($"'{name}' not found");
        }

        ApplyChanges(changes);
        return Task.CompletedTask;
    }

    public override Task Rename(string name, string newName, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        ValidateName(newName);

        var fileName = FindFile(name)?.Name;
        var directoryName = fileName == null ? FindDirectory(name) : null;
        var oldName = fileName ?? directoryName ?? throw new FileNotFoundException($"'{name}' not found");
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        if (!string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase) &&
            (FindFile(newName) != null || FindDirectory(newName) != null))
        {
            throw new IOException($"'{newName}' already exists");
        }

        var oldPath = GetEntryPath(oldName);
        var newPath = GetEntryPath(newName);
        var changes = new Dictionary<int, ZipRecord?>();
        for (var index = 0; index < _records.Count; index++)
        {
            var record = _records[index];
            var pathComponents = GetPathComponents(record.FullName);
            var isDirectory = IsDirectoryName(record.FullName);
            var isMatch = fileName != null
                ? !isDirectory && IsPath(pathComponents, oldPath)
                : StartsWith(pathComponents, oldPath);
            if (isMatch)
            {
                changes[index] = WithName(record,
                    GetFullName(newPath.Concat(pathComponents.Skip(oldPath.Count)), isDirectory));
            }
        }

        ApplyChanges(changes);
        return Task.CompletedTask;
    }

    public override Task SetModifiedDate(string name, DateTime modifiedDate,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();

        var date = ToUtc(modifiedDate);
        var dosDateTime = ZipFormat.ToDosDateTime(date);
        int index;
        if (FindFile(name) is var (fileName, _))
        {
            index = FindFileRecords(GetEntryPath(fileName)).Last();
        }
        else if (FindDirectory(name) is { } directoryName)
        {
            var path = GetEntryPath(directoryName);
            index = _records.FindLastIndex(record =>
                IsDirectoryName(record.FullName) && IsPath(GetPathComponents(record.FullName), path));
            if (index < 0)
            {
                // a directory only in the paths of its files gets a directory entry with the date
                AppendDirectory(path, date);
                return Task.CompletedTask;
            }
        }
        else
        {
            throw new FileNotFoundException($"'{name}' not found");
        }

        var record = _records[index];
        var extra = ZipFormat.RemoveExtraFields(record.Extra, ZipFormat.NtfsExtraId,
            ZipFormat.ExtendedTimestampExtraId);
        if (record.DosDateTime != dosDateTime || extra.Length != record.Extra.Length)
        {
            ApplyChanges(new Dictionary<int, ZipRecord?>
            {
                [index] = record with { DosDateTime = dosDateTime, Extra = extra }
            });
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _zipArchive?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Path components of an entry in the current directory.</summary>
    private List<string> GetEntryPath(string name) => [..CurrentPathComponents, name];

    /// <summary>Indexes of the file records with a path, in central directory order.</summary>
    private IEnumerable<int> FindFileRecords(IReadOnlyList<string> path) =>
        _records
            .Select((record, index) => (record, index))
            .Where(x => !IsDirectoryName(x.record.FullName) && IsPath(GetPathComponents(x.record.FullName), path))
            .Select(x => x.index);

    private void EnsureWritable()
    {
        if (_readOnlyMessage != null)
        {
            throw new NotSupportedException(_readOnlyMessage);
        }
    }

    private static void ValidateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (string.IsNullOrWhiteSpace(name) || name == "." || name == Paths.Parent ||
            name.IndexOfAny(InvalidNameCharacters) >= 0)
        {
            throw new IOException($"Name '{name}' is not valid");
        }
    }

    private static ZipRecord CreateRecord(string fullName, DateTime date)
    {
        var record = WithName(new ZipRecord
        {
            VersionMadeBy = ZipFormat.VersionZip64,
            VersionNeeded = ZipFormat.VersionDefault,
            DosDateTime = ZipFormat.ToDosDateTime(date)
        }, fullName);
        return record;
    }

    private void AppendDirectory(IReadOnlyList<string> path, DateTime date)
    {
        var record = CreateRecord(GetFullName(path, true), date) with
        {
            Method = ZipFormat.MethodStored,
            ExternalAttributes = ZipFormat.DirectoryAttribute
        };
        Append(record, null);
    }

    /// <summary>
    /// Adds a file after the last entry with the data written to <see cref="OpenWrite" />, replacing a file with the
    /// same path.
    /// </summary>
    private void AddFile(IReadOnlyList<string> path, Stream data)
    {
        var existing = FindFileRecords(path).ToDictionary(index => index, _ => (ZipRecord?)null);
        if (existing.Count > 0)
        {
            ApplyChanges(existing);
        }

        Append(CreateRecord(GetFullName(path, false), DateTime.UtcNow), data);
    }

    /// <summary>
    /// Appends an entry after the last entry, followed by the central directory. File data is compressed with
    /// deflate, or stored when that isn't smaller.
    /// </summary>
    private void Append(ZipRecord record, Stream? data)
    {
        var offset = _centralDirectoryOffset;
        try
        {
            var uncompressedSize = data?.Length ?? 0;
            var headerSize = ZipFormat.CreateLocalHeader(record with { UncompressedSize = uncompressedSize }).Length;
            var dataOffset = offset + headerSize;
            var method = ZipFormat.MethodStored;
            var crc = new Crc32();
            long compressedSize = 0;
            if (data != null && uncompressedSize > 0)
            {
                _stream.Position = dataOffset;
                using (var deflateStream = new DeflateStream(_stream, CompressionLevel.Optimal, true))
                {
                    Copy(data, deflateStream, crc);
                }

                compressedSize = _stream.Position - dataOffset;
                method = ZipFormat.MethodDeflated;
                if (compressedSize >= uncompressedSize)
                {
                    _stream.Position = dataOffset;
                    Copy(data, _stream, null);
                    compressedSize = uncompressedSize;
                    method = ZipFormat.MethodStored;
                }
            }

            record = record with
            {
                Method = method,
                VersionNeeded = method == ZipFormat.MethodDeflated || data == null
                    ? ZipFormat.VersionDefault
                    : ZipFormat.VersionStored,
                Crc = data != null ? crc.Value : 0,
                CompressedSize = compressedSize,
                UncompressedSize = uncompressedSize,
                LocalHeaderOffset = offset
            };
            _stream.Position = offset;
            _stream.Write(ZipFormat.CreateLocalHeader(record));

            var records = new List<ZipRecord>(_records) { record };
            var centralDirectoryOffset = dataOffset + compressedSize;
            ZipFormat.WriteCentralDirectory(_stream, records, centralDirectoryOffset, _comment);
            _records = records;
            _centralDirectoryOffset = centralDirectoryOffset;
        }
        catch
        {
            // the archive ends with the central directory before the entry again
            ZipFormat.WriteCentralDirectory(_stream, _records, offset, _comment);
            throw;
        }

        Reopen();
    }

    private static void Copy(Stream source, Stream destination, Crc32? crc)
    {
        var buffer = new byte[81920];
        source.Position = 0;
        int bytesRead;
        while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            crc?.Append(buffer.AsSpan(0, bytesRead));
            destination.Write(buffer, 0, bytesRead);
        }
    }

    /// <summary>
    /// Changes and removes entries by index: a changed record has a new name or date, null removes the entry. Local
    /// headers changed without changing their size are written in place and entries removed at the end are cut off;
    /// otherwise the archive is written again from the first entry changed, copying the compressed data of each entry
    /// as it is. The central directory is written again after the entries.
    /// </summary>
    private void ApplyChanges(IReadOnlyDictionary<int, ZipRecord?> changes)
    {
        if (changes.Count == 0)
        {
            return;
        }

        // local headers of changed entries, current and changed
        var headers = changes
            .Where(change => change.Value != null)
            .ToDictionary(change => change.Key, change =>
            {
                var current = ZipFormat.ReadLocalHeader(_stream, _records[change.Key].LocalHeaderOffset);
                var record = _records[change.Key];
                var changed = ZipFormat.ChangeLocalHeader(current, change.Value!,
                    change.Value!.DosDateTime != record.DosDateTime ||
                    change.Value.Extra.Length != record.Extra.Length);
                return (Current: current, Changed: changed);
            });
        var removed = changes.Where(change => change.Value == null).Select(change => change.Key).ToHashSet();

        // entries in the order of their data, which may differ from the central directory
        var order = Enumerable.Range(0, _records.Count).OrderBy(index => _records[index].LocalHeaderOffset).ToList();
        var first = order.FindIndex(changes.ContainsKey);

        if (removed.Count == 0 && headers.All(header => header.Value.Changed.Length == header.Value.Current.Length))
        {
            foreach (var (index, header) in headers)
            {
                _stream.Position = _records[index].LocalHeaderOffset;
                _stream.Write(header.Changed);
            }

            var records = _records.Select((record, index) => changes.GetValueOrDefault(index) ?? record).ToList();
            ZipFormat.WriteCentralDirectory(_stream, records, _centralDirectoryOffset, _comment);
            _records = records;
        }
        else if (headers.Count == 0 && order.Skip(first).All(removed.Contains))
        {
            var offset = _records[order[first]].LocalHeaderOffset;
            var records = _records.Where((_, index) => !removed.Contains(index)).ToList();
            ZipFormat.WriteCentralDirectory(_stream, records, offset, _comment);
            _records = records;
            _centralDirectoryOffset = offset;
        }
        else
        {
            RewriteFrom(order, first, changes, headers.ToDictionary(header => header.Key,
                header => (header.Value.Current.Length, header.Value.Changed)));
        }

        Reopen();
    }

    /// <summary>
    /// Writes the archive again from an entry in data order: the entries after it are written to a temporary file
    /// with their changed local headers or their local headers as they are, followed by their compressed data (and
    /// data descriptor), and copied back, followed by the central directory.
    /// </summary>
    private void RewriteFrom(IReadOnlyList<int> order, int first, IReadOnlyDictionary<int, ZipRecord?> changes,
        IReadOnlyDictionary<int, (int CurrentLength, byte[] Changed)> headers)
    {
        var startOffset = _records[order[first]].LocalHeaderOffset;
        var records = _records.ToArray<ZipRecord?>();
        using var tail = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite,
            FileShare.None, 81920, FileOptions.DeleteOnClose);
        for (var position = first; position < order.Count; position++)
        {
            var index = order[position];
            var record = _records[index];
            if (changes.TryGetValue(index, out var changed) && changed == null)
            {
                records[index] = null;
                continue;
            }

            // an entry's data reaches to the next entry or the central directory
            var offset = record.LocalHeaderOffset;
            var endOffset = position + 1 < order.Count
                ? _records[order[position + 1]].LocalHeaderOffset
                : _centralDirectoryOffset;
            var newOffset = startOffset + tail.Position;
            if (headers.TryGetValue(index, out var header))
            {
                tail.Write(header.Changed);
                offset += header.CurrentLength;
            }

            CopyRange(offset, endOffset - offset, tail);
            records[index] = (changed ?? record) with { LocalHeaderOffset = newOffset };
        }

        var centralDirectoryOffset = startOffset + tail.Length;
        tail.Position = 0;
        _stream.Position = startOffset;
        tail.CopyTo(_stream);

        _records = records.OfType<ZipRecord>().ToList();
        _centralDirectoryOffset = centralDirectoryOffset;
        ZipFormat.WriteCentralDirectory(_stream, _records, _centralDirectoryOffset, _comment);
    }

    private void CopyRange(long offset, long length, Stream destination)
    {
        var buffer = new byte[Math.Min(81920, Math.Max(1, length))];
        _stream.Position = offset;
        while (length > 0)
        {
            var bytesRead = _stream.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
            if (bytesRead == 0)
            {
                throw new IOException("Unexpected end of zip archive");
            }

            destination.Write(buffer, 0, bytesRead);
            length -= bytesRead;
        }
    }

    /// <summary>
    /// Opens the archive written again with <see cref="ZipArchive" />, which reads the entries in the order of the
    /// central directory, and adds its entries to the directory tree again.
    /// </summary>
    private void Reopen()
    {
        _zipArchive?.Dispose();
        _zipArchive = null;
        _stream.Position = 0;
        _zipArchive = new ZipArchive(_stream, ZipArchiveMode.Read, true);
        if (_zipArchive.Entries.Count != _records.Count)
        {
            throw new IOException("The zip archive written can't be read");
        }

        AddEntries();
    }

    /// <summary>
    /// Stream of a file written to the archive: the data is written to a temporary file and added to the archive
    /// when the stream is closed.
    /// </summary>
    private sealed class WriteStream(FileStream dataStream, Action closed) : Stream
    {
        private bool _disposed;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_disposed;
        public override long Length => dataStream.Length;

        public override long Position
        {
            get => dataStream.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => dataStream.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => dataStream.Write(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => dataStream.Write(buffer);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            dataStream.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            dataStream.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                try
                {
                    closed();
                }
                finally
                {
                    dataStream.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }
}
