using SharpCompress.Archives;
using SharpCompress.Readers;

namespace Hst.Commander.Plugins.RarSevenZip;

/// <summary>
/// Reads the files of a solid archive in one pass. SharpCompress decompresses a solid block from its start for each
/// file opened with <see cref="IArchiveEntry.OpenEntryStream" />, so copying many files would decompress the block
/// over and over (quadratic time). This reader keeps one sequential reader (<see cref="IArchive.ExtractAllEntries" />)
/// that is only restarted when a file before its position is opened, and caches the files it passes in temporary
/// files (up to <see cref="MaxCacheSize" /> bytes), so they can be read later without decompressing the block again,
/// e.g. when files are copied in another order than the archive's. Only files near the opened file are cached (see
/// <see cref="GetCacheScope" />), as copying a directory reads the files in it, not the rest of the archive. Cached
/// files are deleted when read and when the reader is disposed.
/// </summary>
internal sealed class SolidArchiveReader : IDisposable
{
    /// <summary>Maximum size of the cached files; files passed beyond it are skipped and decompressed again later.</summary>
    internal const long MaxCacheSize = 1L << 30;

    private const int BufferSize = 81920;

    private readonly IArchive _archive;
    private readonly IReadOnlyList<IArchiveEntry> _entries;
    private readonly Dictionary<IArchiveEntry, int> _indexes = new(ReferenceEqualityComparer.Instance);
    private readonly string[][] _directories;
    private readonly Dictionary<int, CachedFile> _cache = [];
    private IReader? _reader;
    private int _position = -1;
    private long _cacheSize;
    private ReaderEntryStream? _openStream;

    /// <param name="archive">Solid archive, left open when the reader is disposed.</param>
    /// <param name="entries">Entries of the archive in their order, the instances opened with <see cref="Open" />.</param>
    public SolidArchiveReader(IArchive archive, IReadOnlyList<IArchiveEntry> entries)
    {
        _archive = archive;
        _entries = entries;
        _directories = new string[entries.Count][];
        for (var index = 0; index < entries.Count; index++)
        {
            _indexes[entries[index]] = index;
            _directories[index] = GetDirectory(entries[index]);
        }
    }

    /// <summary>Directory of an entry as path components, the same as the volume's directory tree.</summary>
    private static string[] GetDirectory(IArchiveEntry entry)
    {
        var path = entry.Key ?? string.Empty;
        var pathComponents = path
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Where(pathComponent => pathComponent != "." && pathComponent != "..")
            .ToArray();
        var isDirectory = entry.IsDirectory || path.EndsWith('/') || path.EndsWith('\\');
        return isDirectory ? pathComponents : pathComponents[..Math.Max(0, pathComponents.Length - 1)];
    }

    /// <summary>
    /// Directory whose files (also in subdirectories) are cached when passed on the way to the file at an index: the
    /// parent of the file's directory, so the files of a directory being copied and of its subdirectories are cached,
    /// but not the files of the rest of the archive. Files at the root and in top-level directories cache the files of
    /// their own directory only (<c>exact</c>), as their parent would be the whole archive.
    /// </summary>
    private (string[] Directory, bool Exact) GetCacheScope(int index)
    {
        var directory = _directories[index];
        return directory.Length >= 2 ? (directory[..^1], false) : (directory, true);
    }

    private bool IsInScope(int index, (string[] Directory, bool Exact) scope)
    {
        var directory = _directories[index];
        if (scope.Exact ? directory.Length != scope.Directory.Length : directory.Length < scope.Directory.Length)
        {
            return false;
        }

        for (var component = 0; component < scope.Directory.Length; component++)
        {
            if (!directory[component].Equals(scope.Directory[component], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Number of restarts of the sequential reader, for tests.</summary>
    internal int Restarts { get; private set; }

    /// <summary>Size of the cached files, for tests.</summary>
    internal long CacheSize => _cacheSize;

    /// <summary>Opens a file of the archive for reading, from the cache or by moving the sequential reader to it.</summary>
    public Stream Open(IArchiveEntry entry, CancellationToken cancellationToken)
    {
        if (!_indexes.TryGetValue(entry, out var index))
        {
            DisposeReader();
            return entry.OpenEntryStream();
        }

        if (_cache.Remove(index, out var cachedFile))
        {
            _cacheSize -= cachedFile.Size;
            return OpenCachedFile(cachedFile.Path);
        }

        // the sequential reader moves on, so the rest of a file still being read is moved to a temporary file
        DetachOpenStream(cancellationToken);

        try
        {
            if (_reader == null || _position >= index)
            {
                DisposeReader();
                _reader = _archive.ExtractAllEntries();
                Restarts++;
            }

            var scope = GetCacheScope(index);
            while (_reader.MoveToNextEntry())
            {
                _position++;
                var readerEntry = _reader.Entry;
                if (_position >= _entries.Count || readerEntry.Key != _entries[_position].Key)
                {
                    // entries in another order than the archive's, read the file without the sequential reader
                    break;
                }

                if (_position == index)
                {
                    _openStream = new ReaderEntryStream(_reader.OpenEntryStream(), this);
                    return _openStream;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (IsInScope(_position, scope))
                {
                    CacheEntry(readerEntry, cancellationToken);
                }
            }
        }
        catch
        {
            // the reader can't go on after an error, e.g. decompressing a damaged file it passes
            ResetReader();
            throw;
        }

        DisposeReader();
        return entry.OpenEntryStream();
    }

    /// <summary>Writes the file the sequential reader is at to the cache, if it fits.</summary>
    private void CacheEntry(SharpCompress.Common.IEntry readerEntry, CancellationToken cancellationToken)
    {
        if (readerEntry.IsDirectory || readerEntry.IsEncrypted || _cache.ContainsKey(_position) ||
            _cacheSize + readerEntry.Size > MaxCacheSize)
        {
            return;
        }

        var path = Path.GetTempFileName();
        try
        {
            using (var entryStream = _reader!.OpenEntryStream())
            using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                       BufferSize))
            {
                CopyTo(entryStream, fileStream, cancellationToken);
            }

            _cache[_position] = new CachedFile(path, readerEntry.Size);
            _cacheSize += readerEntry.Size;
        }
        catch
        {
            File.Delete(path);
            throw;
        }
    }

    /// <summary>
    /// Moves the rest of the file still being read from the sequential reader to a temporary file. When the rest
    /// can't be decompressed (a damaged archive), the error is thrown reading that file, and the reader is reset.
    /// </summary>
    private void DetachOpenStream(CancellationToken cancellationToken)
    {
        var openStream = _openStream;
        _openStream = null;
        if (openStream != null && !openStream.Detach(cancellationToken))
        {
            ResetReader();
        }
    }

    private static void CopyTo(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        int bytesRead;
        while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination.Write(buffer, 0, bytesRead);
        }
    }

    private static FileStream OpenCachedFile(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Delete, BufferSize,
            FileOptions.DeleteOnClose | FileOptions.SequentialScan);

    private void DisposeReader()
    {
        DetachOpenStream(CancellationToken.None);
        ResetReader();
    }

    /// <summary>
    /// Disposes the sequential reader without moving the file being read to a temporary file, e.g. after an error
    /// decompressing a damaged archive, so the next file opened starts a new reader.
    /// </summary>
    private void ResetReader()
    {
        _openStream = null;
        try
        {
            _reader?.Dispose();
        }
        catch (Exception e) when (ArchiveEntryStream.IsDamage(e))
        {
            // the reader can't skip the rest of a damaged file
        }

        _reader = null;
        _position = -1;
    }

    public void Dispose()
    {
        ResetReader();
        foreach (var cachedFile in _cache.Values)
        {
            try
            {
                File.Delete(cachedFile.Path);
            }
            catch (IOException)
            {
                // left in the temporary directory
            }
        }

        _cache.Clear();
        _cacheSize = 0;
    }

    private sealed record CachedFile(string Path, long Size);

    /// <summary>
    /// Stream of the file the sequential reader is at. When the reader moves on before the stream is read and
    /// disposed, the rest of the file is moved to a temporary file (<see cref="Detach" />) and read from there.
    /// </summary>
    private sealed class ReaderEntryStream(Stream stream, SolidArchiveReader solidArchiveReader) : Stream
    {
        private Stream _stream = stream;
        private bool _detached;
        private Exception? _detachError;
        private long _position;

        /// <summary>
        /// Moves the rest of the file to a temporary file. Returns false when it can't be decompressed (a damaged
        /// archive): reading the file then throws the error.
        /// </summary>
        public bool Detach(CancellationToken cancellationToken)
        {
            if (_detached)
            {
                return true;
            }

            var path = Path.GetTempFileName();
            try
            {
                using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                           BufferSize))
                {
                    SolidArchiveReader.CopyTo(_stream, fileStream, cancellationToken);
                }

                _stream.Dispose();
                _stream = OpenCachedFile(path);
                _detached = true;
                return true;
            }
            catch (Exception e) when (ArchiveEntryStream.IsDamage(e))
            {
                File.Delete(path);
                _detached = true;
                _detachError = e;
                _stream = Null;
                return false;
            }
            catch
            {
                File.Delete(path);
                throw;
            }
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_detachError != null)
            {
                // rethrown as damage by ArchiveEntryStream
                throw new InvalidOperationException(_detachError.Message, _detachError);
            }

            int bytesRead;
            try
            {
                bytesRead = _stream.Read(buffer);
            }
            catch when (!_detached && solidArchiveReader._openStream == this)
            {
                // the sequential reader can't go on after an error, e.g. in a damaged archive
                solidArchiveReader.ResetReader();
                throw;
            }

            _position += bytesRead;
            return bytesRead;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var isOpenStream = solidArchiveReader._openStream == this;
                if (isOpenStream)
                {
                    solidArchiveReader._openStream = null;
                }

                try
                {
                    // the reader decompresses the rest of the file, so it can move on to the next file
                    _stream.Dispose();
                }
                catch (Exception)
                {
                    // a damaged archive, or the stream was disposed with the reader after an error
                    if (isOpenStream)
                    {
                        solidArchiveReader.ResetReader();
                    }
                }
            }

            base.Dispose(disposing);
        }
    }
}
