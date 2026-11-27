﻿using Hst.Commander.Abstractions;
using Hst.Compression.Lha;

namespace Hst.Commander.Plugins.LhaLzx;

/// <summary>
/// Volume browsing the entries of a lha archive (.lha, .lzh), read and written with Hst.Compression. The volume is
/// writable when its stream is (a writable image, see <see cref="LhaFileVolumeProvider.CanWrite" />): files and
/// directories added are appended as Amiga entries (protection bits and comments) before the end of the archive, and
/// changing, renaming or deleting entries rewrites the archive from the first entry changed, copying the compressed
/// data of the entries after it as it is. A header changed without changing its size (e.g. a date) is written in
/// place.
/// </summary>
public class LhaVolume : ArchiveVolume<LhaEntry>
{
    /// <summary>Header level of entries added to an empty archive.</summary>
    public const int DefaultHeaderLevel = 2;

    private const string ReadOnlyMessage = "The lha archive is read-only";

    // operating systems of level 1 and 2 headers storing their own attributes instead of Amiga protection bits
    private static readonly byte[] NonAmigaOsTypes =
    [
        LhaOsType.MsDos, LhaOsType.Unix, LhaOsType.Windows95, LhaOsType.WindowsNt, LhaOsType.MacOs, LhaOsType.Os2,
        LhaOsType.Java
    ];

    private static readonly char[] InvalidNameCharacters = ['/', '\\', '\0', ':'];

    private readonly Stream _stream;
    private readonly LhaArchive _lhaArchive;
    private List<LhaEntry> _entries;
    private long _endOffset;

    private LhaVolume(Stream stream, LhaArchive lhaArchive, List<LhaEntry> entries) : base("Lha", true)
    {
        _stream = stream;
        _lhaArchive = lhaArchive;
        _entries = entries;
        _endOffset = entries.Count > 0 ? entries.Max(GetEndOffset) : 0;
        AddEntries();
    }

    /// <summary>True when the archive can be changed, false when its stream is read-only.</summary>
    public bool IsWritable => _stream.CanWrite;

    /// <summary>Creates a volume reading the entries of a lha archive.</summary>
    /// <param name="stream">
    /// Readable and seekable stream with the archive, kept open while the volume is used. The volume can change the
    /// archive when the stream is writable.
    /// </param>
    public static async Task<LhaVolume> Create(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.Position = 0;

        // the stream is left open, as it's owned by Hst Commander
        var lhaArchive = new LhaArchive(stream, LhaOptions.AmigaLhaOptions, leaveOpen: true);
        var entries = (await lhaArchive.EntriesAsync()).ToList();
        return new LhaVolume(stream, lhaArchive, entries);
    }

    /// <summary>Adds the entries of the archive to the directory tree of the volume again.</summary>
    private void AddEntries()
    {
        ClearEntries();
        foreach (var lhaEntry in _entries)
        {
            // directories end with a path separator; symbolic links are directory entries (-lhd-) without one
            if (IsLink(lhaEntry))
            {
                continue;
            }

            AddEntry(GetPath(lhaEntry.Name), lhaEntry.UnpackedSize, lhaEntry.Date, lhaEntry,
                GetAmigaMetadata(lhaEntry));
        }
    }

    /// <summary>Path of a lha entry: Amiga lha archives store a comment after a zero character in the name.</summary>
    private static string GetPath(string name)
    {
        var index = name.IndexOf('\0');
        return index >= 0 ? name[..index] : name;
    }

    /// <summary>Comment of a lha entry stored after a zero character in the name, empty when none.</summary>
    private static string GetComment(string name)
    {
        var index = name.IndexOf('\0');
        return index >= 0 ? name[(index + 1)..] : string.Empty;
    }

    /// <summary>
    /// Amiga protection bits and comment of a lha entry: Amiga lha archives store the protection bits as they are on
    /// disk in the attribute and the comment after a zero character in the name.
    /// </summary>
    private static AmigaMetadata GetAmigaMetadata(LhaEntry lhaEntry)
    {
        var protectionBits = NonAmigaOsTypes.Contains(lhaEntry.OsType) ? 0 : lhaEntry.Attributes & 0xff;
        return new AmigaMetadata(protectionBits, GetComment(lhaEntry.Name));
    }

    private static bool IsDirectoryEntry(LhaEntry lhaEntry)
    {
        var path = GetPath(lhaEntry.Name);
        return path.EndsWith('\\') || path.EndsWith('/');
    }

    private static bool IsLink(LhaEntry lhaEntry) =>
        !IsDirectoryEntry(lhaEntry) && (lhaEntry.Method == LhaMethod.Lhd || lhaEntry.LinkTarget != null);

    /// <summary>Path components of a lha entry, skipping empty, "." and ".." like the directory tree.</summary>
    private static List<string> GetPathComponents(LhaEntry lhaEntry) =>
        GetPath(lhaEntry.Name)
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Where(pathComponent => pathComponent != "." && pathComponent != Paths.Parent)
            .ToList();

    private static long GetEndOffset(LhaEntry lhaEntry) =>
        lhaEntry.HeaderOffset + lhaEntry.HeaderSize + lhaEntry.PackedSize;

    private static bool StartsWith(IReadOnlyList<string> pathComponents, IReadOnlyList<string> prefix) =>
        pathComponents.Count >= prefix.Count &&
        prefix.Select((pathComponent, index) => string.Equals(pathComponent, pathComponents[index],
            StringComparison.Ordinal)).All(equal => equal);

    private static bool IsPath(IReadOnlyList<string> pathComponents, IReadOnlyList<string> path) =>
        pathComponents.Count == path.Count && StartsWith(pathComponents, path);

    /// <summary>
    /// Name of a lha entry: path components separated by "\", ending with "\" for a directory, followed by a comment
    /// after a zero character or the target of a symbolic link after "|".
    /// </summary>
    private static string GetLhaName(IEnumerable<string> pathComponents, bool isDirectory, string comment,
        string? linkTarget = null)
    {
        var name = string.Join('\\', pathComponents) + (isDirectory ? "\\" : string.Empty);
        if (linkTarget != null)
        {
            return name + "|" + linkTarget;
        }

        return string.IsNullOrEmpty(comment) ? name : name + "\0" + comment;
    }

    private static LhaEntry Clone(LhaEntry lhaEntry) => new()
    {
        HeaderOffset = lhaEntry.HeaderOffset,
        HeaderSize = lhaEntry.HeaderSize,
        HeaderLevel = lhaEntry.HeaderLevel,
        HeaderCrc = lhaEntry.HeaderCrc,
        Method = lhaEntry.Method,
        Name = lhaEntry.Name,
        LinkTarget = lhaEntry.LinkTarget,
        Date = lhaEntry.Date,
        PackedSize = lhaEntry.PackedSize,
        UnpackedSize = lhaEntry.UnpackedSize,
        Crc = lhaEntry.Crc,
        HasCrc = lhaEntry.HasCrc,
        Attributes = lhaEntry.Attributes,
        OsType = lhaEntry.OsType,
        UnixMode = lhaEntry.UnixMode,
        UnixUserId = lhaEntry.UnixUserId,
        UnixGroupId = lhaEntry.UnixGroupId,
        UnixUserName = lhaEntry.UnixUserName,
        UnixGroupName = lhaEntry.UnixGroupName
    };

    private static DateTime ToUtc(DateTime date) => date.Kind switch
    {
        DateTimeKind.Local => date.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(date, DateTimeKind.Utc),
        _ => date
    };

    protected override async Task<Stream> OpenArchiveEntry(string name, LhaEntry archiveEntry,
        CancellationToken cancellationToken) =>
        await _lhaArchive.OpenEntryAsync(archiveEntry, cancellationToken);

    public override Task<bool> CanSetAmigaMetadata(CancellationToken cancellationToken = default) =>
        Task.FromResult(IsWritable);

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

        var path = GetEntryPath(name);
        Append(lhaWriter => lhaWriter.AddDirectory(GetLhaName(path, true, string.Empty),
            new LhaEntryOptions { LastModified = DateTime.UtcNow, Attributes = 0 }));
        return Task.CompletedTask;
    }

    public override Task Delete(string name, CancellationToken cancellationToken = default)
    {
        EnsureWritable();

        Dictionary<int, LhaEntry?> changes;
        if (FindFile(name) is var (fileName, _))
        {
            changes = FindFileEntries(GetEntryPath(fileName)).ToDictionary(index => index, _ => (LhaEntry?)null);
        }
        else if (FindDirectory(name) is { } directoryName)
        {
            if (!IsDirectoryEmpty(directoryName))
            {
                throw new IOException($"Directory '{directoryName}' is not empty");
            }

            // explicit directory entries and symbolic links in the directory, which aren't listed
            var path = GetEntryPath(directoryName);
            changes = _entries
                .Select((lhaEntry, index) => (lhaEntry, index))
                .Where(x => StartsWith(GetPathComponents(x.lhaEntry), path))
                .ToDictionary(x => x.index, _ => (LhaEntry?)null);
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
        var changes = new Dictionary<int, LhaEntry?>();
        for (var index = 0; index < _entries.Count; index++)
        {
            var lhaEntry = _entries[index];
            var pathComponents = GetPathComponents(lhaEntry);
            var isDirectory = IsDirectoryEntry(lhaEntry);
            var isMatch = fileName != null
                ? !isDirectory && IsPath(pathComponents, oldPath)
                : StartsWith(pathComponents, oldPath);
            if (!isMatch)
            {
                continue;
            }

            var renamed = Clone(lhaEntry);
            renamed.Name = GetLhaName(newPath.Concat(pathComponents.Skip(oldPath.Count)), isDirectory,
                GetComment(lhaEntry.Name), IsLink(lhaEntry) ? lhaEntry.LinkTarget : null);
            changes[index] = renamed;
        }

        ApplyChanges(changes);
        return Task.CompletedTask;
    }

    public override Task SetModifiedDate(string name, DateTime modifiedDate,
        CancellationToken cancellationToken = default)
    {
        EnsureWritable();

        var date = ToUtc(modifiedDate);
        ChangeEntry(name, lhaEntry =>
        {
            if (lhaEntry.Date == date)
            {
                return false;
            }

            lhaEntry.Date = date;
            return true;
        }, new LhaEntryOptions { LastModified = date, Attributes = 0 });
        return Task.CompletedTask;
    }

    public override Task SetAmigaMetadata(string name, AmigaMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        EnsureWritable();

        var protectionBits = metadata.ProtectionBits & 0xff;
        var comment = metadata.Comment ?? string.Empty;
        ChangeEntry(name, lhaEntry =>
        {
            if (GetAmigaMetadata(lhaEntry) == new AmigaMetadata(protectionBits, comment) &&
                !NonAmigaOsTypes.Contains(lhaEntry.OsType))
            {
                return false;
            }

            lhaEntry.Attributes = protectionBits;
            lhaEntry.Name = GetLhaName(GetPathComponents(lhaEntry), IsDirectoryEntry(lhaEntry), comment);
            if (NonAmigaOsTypes.Contains(lhaEntry.OsType))
            {
                // Amiga protection bits aren't read from entries of other operating systems
                lhaEntry.OsType = LhaOsType.Amiga;
            }

            return true;
        }, metadata.IsDefault ? null : new LhaEntryOptions { Attributes = protectionBits, Comment = comment });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Changes the lha entry of a file, or of a directory, in the current directory. A directory without an entry of
    /// its own (it's only in the paths of its files) gets one added with the options.
    /// </summary>
    /// <param name="name">Name of the file or directory.</param>
    /// <param name="change">Changes a copy of the entry, returning false when nothing changed.</param>
    /// <param name="directoryOptions">
    /// Options of a directory entry added for a directory without one, null to add none.
    /// </param>
    private void ChangeEntry(string name, Func<LhaEntry, bool> change, LhaEntryOptions? directoryOptions)
    {
        int index;
        if (FindFile(name) is var (fileName, _))
        {
            index = FindFileEntries(GetEntryPath(fileName)).Last();
        }
        else if (FindDirectory(name) is { } directoryName)
        {
            var path = GetEntryPath(directoryName);
            index = _entries.FindLastIndex(lhaEntry =>
                IsDirectoryEntry(lhaEntry) && IsPath(GetPathComponents(lhaEntry), path));
            if (index < 0)
            {
                if (directoryOptions != null)
                {
                    directoryOptions.LastModified ??= DateTime.UtcNow;
                    Append(lhaWriter => lhaWriter.AddDirectory(GetLhaName(path, true, string.Empty),
                        directoryOptions));
                }

                return;
            }
        }
        else
        {
            throw new FileNotFoundException($"'{name}' not found");
        }

        var changed = Clone(_entries[index]);
        if (change(changed))
        {
            ApplyChanges(new Dictionary<int, LhaEntry?> { [index] = changed });
        }
    }

    /// <summary>Path components of an entry in the current directory.</summary>
    private List<string> GetEntryPath(string name) => [..CurrentPathComponents, name];

    /// <summary>Indexes of the file entries with a path, in archive order.</summary>
    private IEnumerable<int> FindFileEntries(IReadOnlyList<string> path) =>
        _entries
            .Select((lhaEntry, index) => (lhaEntry, index))
            .Where(x => !IsDirectoryEntry(x.lhaEntry) && !IsLink(x.lhaEntry) &&
                        IsPath(GetPathComponents(x.lhaEntry), path))
            .Select(x => x.index);

    private void EnsureWritable()
    {
        if (!IsWritable)
        {
            throw new NotSupportedException(ReadOnlyMessage);
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

    /// <summary>
    /// Adds a file at the end of the archive with the data written to <see cref="OpenWrite" />, replacing a file with
    /// the same path.
    /// </summary>
    private void AddFile(IReadOnlyList<string> path, Stream data)
    {
        var existing = FindFileEntries(path).ToDictionary(index => index, _ => (LhaEntry?)null);
        if (existing.Count > 0)
        {
            ApplyChanges(existing);
        }

        data.Position = 0;
        Append(lhaWriter =>
        {
            data.Position = 0;
            return lhaWriter.AddFile(GetLhaName(path, false, string.Empty), data,
                new LhaEntryOptions { LastModified = DateTime.UtcNow, Attributes = 0 });
        });
    }

    /// <summary>Header level of entries added: the level of the last entry, or the default for an empty archive.</summary>
    private int GetNewHeaderLevel() =>
        _entries.Count > 0 && _entries[^1].HeaderLevel is >= 0 and <= 2 and var headerLevel
            ? headerLevel
            : DefaultHeaderLevel;

    private static LhaWriterOptions CreateWriterOptions(int headerLevel)
    {
        var options = LhaWriterOptions.Amiga;
        options.HeaderLevel = headerLevel;
        return options;
    }

    /// <summary>
    /// Appends an entry before the end of the archive, with level 2 headers when the name is too long for the level
    /// of the last entry.
    /// </summary>
    private void Append(Func<LhaWriter, LhaEntry> add)
    {
        var headerLevel = GetNewHeaderLevel();
        LhaEntry lhaEntry;
        try
        {
            lhaEntry = Append(add, headerLevel);
        }
        catch (ArgumentException) when (headerLevel < 2)
        {
            lhaEntry = Append(add, 2);
        }

        _entries.Add(lhaEntry);
        _endOffset = GetEndOffset(lhaEntry);
        AddEntries();
    }

    private LhaEntry Append(Func<LhaWriter, LhaEntry> add, int headerLevel)
    {
        _stream.Position = _endOffset;
        try
        {
            using var lhaWriter = new LhaWriter(_stream, CreateWriterOptions(headerLevel), leaveOpen: true);
            var lhaEntry = add(lhaWriter);
            lhaWriter.Finish();
            _stream.SetLength(_stream.Position);
            return lhaEntry;
        }
        catch
        {
            // the archive ends before the entry again
            WriteEndOfArchive(_endOffset);
            throw;
        }
    }

    private void WriteEndOfArchive(long offset)
    {
        _stream.Position = offset;
        _stream.WriteByte(0);
        _stream.SetLength(offset + 1);
        _stream.Flush();
    }

    /// <summary>
    /// Creates the header of a changed entry, with a level 2 header when the entry has a level 3 header (not
    /// written by Hst.Compression) or the name is too long for a level 0 or 1 header.
    /// </summary>
    private static byte[] CreateHeader(LhaEntry lhaEntry)
    {
        if (lhaEntry.HeaderLevel is < 0 or > 2)
        {
            lhaEntry.HeaderLevel = 2;
        }

        var lhaWriter = new LhaWriter(Stream.Null, CreateWriterOptions(lhaEntry.HeaderLevel), leaveOpen: true);
        try
        {
            return lhaWriter.CreateHeader(lhaEntry);
        }
        catch (ArgumentException) when (lhaEntry.HeaderLevel < 2)
        {
            lhaEntry.HeaderLevel = 2;
            return CreateHeader(lhaEntry);
        }
        catch (ArgumentException exception)
        {
            throw new IOException(exception.Message, exception);
        }
    }

    /// <summary>
    /// Changes and removes entries by index: a changed entry is a copy with a new name, date, attributes or comment
    /// and the offsets of the entry it replaces, null removes the entry. Headers changed without changing their size
    /// are written in place and entries removed at the end are cut off; otherwise the archive is written again from
    /// the first entry changed, copying the compressed data of each entry as it is.
    /// </summary>
    private void ApplyChanges(IReadOnlyDictionary<int, LhaEntry?> changes)
    {
        if (changes.Count == 0)
        {
            return;
        }

        var headers = changes
            .Where(change => change.Value != null)
            .ToDictionary(change => change.Key, change => CreateHeader(change.Value!));
        var removed = changes.Where(change => change.Value == null).Select(change => change.Key).ToList();
        var first = changes.Keys.Min();

        if (removed.Count == 0 && headers.All(header => header.Value.Length == _entries[header.Key].HeaderSize))
        {
            foreach (var (index, header) in headers)
            {
                _stream.Position = _entries[index].HeaderOffset;
                _stream.Write(header);
                _entries[index] = changes[index]!;
            }

            _stream.Flush();
        }
        else if (headers.Count == 0 && removed.Count == _entries.Count - first)
        {
            var offset = _entries[first].HeaderOffset;
            WriteEndOfArchive(offset);
            _entries.RemoveRange(first, _entries.Count - first);
            _endOffset = offset;
        }
        else
        {
            RewriteFrom(first, changes, headers);
        }

        AddEntries();
    }

    /// <summary>
    /// Writes the archive again from an entry: the entries after it are written to a temporary file with their
    /// changed headers or their headers as they are, followed by their compressed data, and copied back.
    /// </summary>
    private void RewriteFrom(int first, IReadOnlyDictionary<int, LhaEntry?> changes,
        IReadOnlyDictionary<int, byte[]> headers)
    {
        var startOffset = _entries[first].HeaderOffset;
        var entries = new List<LhaEntry>();
        using var tail = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite,
            FileShare.None, 81920, FileOptions.DeleteOnClose);
        for (var index = first; index < _entries.Count; index++)
        {
            var original = _entries[index];
            if (changes.TryGetValue(index, out var changed) && changed == null)
            {
                continue;
            }

            var lhaEntry = changed ?? Clone(original);
            var headerOffset = startOffset + tail.Position;
            if (headers.TryGetValue(index, out var header))
            {
                tail.Write(header);
                lhaEntry.HeaderSize = header.Length;
            }
            else
            {
                CopyRange(original.HeaderOffset, original.HeaderSize, tail);
            }

            CopyRange(original.HeaderOffset + original.HeaderSize, original.PackedSize, tail);
            lhaEntry.HeaderOffset = headerOffset;
            entries.Add(lhaEntry);
        }

        var endOffset = startOffset + tail.Length;
        tail.Position = 0;
        _stream.Position = startOffset;
        tail.CopyTo(_stream);
        WriteEndOfArchive(endOffset);

        _entries = [.._entries.Take(first), ..entries];
        _endOffset = endOffset;
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
                throw new IOException("Unexpected end of lha archive");
            }

            destination.Write(buffer, 0, bytesRead);
            length -= bytesRead;
        }
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
