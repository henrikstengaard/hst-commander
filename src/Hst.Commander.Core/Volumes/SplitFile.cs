using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Volumes;

/// <summary>
///     A file split in parts by 7-Zip or similar tools, e.g. "archive.7z.001", "archive.7z.002" and so on in the same
///     directory, read as the joined file, so it can be opened as an image like the file before splitting (e.g. a 7z
///     archive whose list of files is in its last part). The first part is "name.001"; the parts follow without gaps.
/// </summary>
public static class SplitFile
{
    private const string FirstPartExtension = ".001";

    /// <summary>True when the name is the name of a first part, e.g. "archive.7z.001".</summary>
    public static bool IsFirstPart(string name) =>
        name.Length > FirstPartExtension.Length && name.EndsWith(FirstPartExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Opens the parts of a split file in the current directory of a volume as one read-only stream, seekable when
    ///     the parts are. Null when the file isn't the first part or has no second part.
    /// </summary>
    public static async Task<Stream?> OpenAsync(IVolume volume, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(volume);
        ArgumentNullException.ThrowIfNull(name);

        if (!IsFirstPart(name))
        {
            return null;
        }

        var files = await volume.ListEntries(cancellationToken)
            .Where(entry => entry.Type == EntryType.Item)
            .ToListAsync(cancellationToken);
        var partNames = GetPartNames(name, partName => files.FirstOrDefault(file =>
                            file.Name.Equals(partName, StringComparison.Ordinal))?.Name ??
                        files.FirstOrDefault(file =>
                            file.Name.Equals(partName, StringComparison.OrdinalIgnoreCase))?.Name);
        if (partNames.Count < 2)
        {
            return null;
        }

        var streams = new List<Stream>();
        try
        {
            foreach (var partName in partNames)
            {
                streams.Add(await volume.OpenRead(partName, cancellationToken));
            }

            return new SplitFileStream(streams);
        }
        catch
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }

            throw;
        }
    }

    /// <summary>Opens the parts of a split local file like <see cref="OpenAsync" />.</summary>
    public static Stream? OpenLocal(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var directory = Path.GetDirectoryName(path);
        if (directory == null || !IsFirstPart(Path.GetFileName(path)))
        {
            return null;
        }

        var partNames = GetPartNames(Path.GetFileName(path),
            partName => File.Exists(Path.Combine(directory, partName)) ? partName : null);
        if (partNames.Count < 2)
        {
            return null;
        }

        var streams = new List<Stream>();
        try
        {
            foreach (var partName in partNames)
            {
                streams.Add(new FileStream(Path.Combine(directory, partName), FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite));
            }

            return new SplitFileStream(streams);
        }
        catch
        {
            foreach (var stream in streams)
            {
                stream.Dispose();
            }

            throw;
        }
    }

    /// <summary>
    ///     The names of the parts found by <paramref name="find" /> (returning the name as it is in the directory, or
    ///     null), from the first part until a part is missing: "name.002", "name.003", ..., "name.999", "name.1000".
    /// </summary>
    private static List<string> GetPartNames(string firstPartName, Func<string, string?> find)
    {
        var baseName = firstPartName[..^FirstPartExtension.Length];
        var partNames = new List<string> { firstPartName };
        for (var number = 2; find($"{baseName}.{number:D3}") is { } partName; number++)
        {
            partNames.Add(partName);
        }

        return partNames;
    }

    /// <summary>The parts read one after another as one read-only stream.</summary>
    private sealed class SplitFileStream : Stream
    {
        private readonly List<Stream> _parts;
        private readonly long[] _offsets;
        private readonly long _length;
        private int _index;
        private long _position;

        public SplitFileStream(List<Stream> parts)
        {
            _parts = parts;
            CanSeek = parts.All(part => part.CanSeek);
            _offsets = new long[parts.Count];
            if (CanSeek)
            {
                for (var index = 0; index < parts.Count; index++)
                {
                    _offsets[index] = _length;
                    _length += parts[index].Length;
                }
            }
        }

        public override bool CanRead => true;

        public override bool CanSeek { get; }

        public override bool CanWrite => false;

        public override long Length => CanSeek ? _length : throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            while (buffer.Length > 0 && PreparePart() is { } part)
            {
                var read = part.Read(buffer);
                if (read > 0)
                {
                    _position += read;
                    return read;
                }

                _index++;
            }

            return 0;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (buffer.Length > 0 && PreparePart() is { } part)
            {
                var read = await part.ReadAsync(buffer, cancellationToken);
                if (read > 0)
                {
                    _position += read;
                    return read;
                }

                _index++;
            }

            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        /// <summary>The part to read the position from, positioned there when seekable; null at the end.</summary>
        private Stream? PreparePart()
        {
            if (_index >= _parts.Count)
            {
                return null;
            }

            var part = _parts[_index];
            if (CanSeek)
            {
                part.Position = _position - _offsets[_index];
            }

            return part;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!CanSeek)
            {
                throw new NotSupportedException();
            }

            var position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => _length + offset
            };
            if (position < 0)
            {
                throw new IOException("An attempt was made to move the position before the beginning of the stream.");
            }

            _position = position;
            // the last part starting at or before the position, past the end when beyond it
            _index = position >= _length ? _parts.Count : Array.FindLastIndex(_offsets, start => start <= position);
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var part in _parts)
                {
                    part.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            foreach (var part in _parts)
            {
                await part.DisposeAsync();
            }

            await base.DisposeAsync();
        }
    }
}
