using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     A file opened in the viewer (F3), read in chunks so files of any size can be viewed: only the blocks of the lines
///     or rows shown are read (a few blocks are cached). Text is shown as lines split at line breaks (LF, CR LF or CR)
///     or after <see cref="MaxLineLength" /> bytes, decoded as UTF-8 or ISO-8859-1 (<see cref="Encoding" />, detected
///     from the start of the file) with tabs expanded and control characters shown as '.'. Positions are byte offsets,
///     so the viewer can go to any part of the file without reading what is before it. The hex view shows
///     <see cref="BytesPerRow" /> bytes per row (<see cref="FormatHexRow" />). Reading is thread safe, so searching can
///     run on a background thread while lines are shown.
/// </summary>
public sealed class ViewerDocument : IDisposable
{
    /// <summary>Bytes of a line before it is split, so a file without line breaks is still shown in lines.</summary>
    public const int MaxLineLength = 1024;

    public const int TabSize = 8;

    /// <summary>Bytes shown per row in the hex view.</summary>
    public const int BytesPerRow = 16;

    private const int BlockSize = 65536;
    private const int CachedBlocks = 8;
    private const int DetectLength = 65536;

    /// <summary>Largest file whose images are read, read into memory for the image providers.</summary>
    public const int MaxImageLength = 64 * 1024 * 1024;

    /// <summary>
    ///     Largest file without zero bytes at its start (<see cref="IsBinary" />) whose images are read; image formats
    ///     have zero bytes in their headers, so larger text files aren't read for nothing.
    /// </summary>
    public const int MaxTextImageLength = 1024 * 1024;

    /// <summary>Most bytes copied from the viewer at once.</summary>
    public const int MaxCopyLength = 16 * 1024 * 1024;

    /// <summary>How far back the start of a line is looked for; longer lines are split from there.</summary>
    private const int MaxLineSearchLength = 65536;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    private readonly Stream _stream;
    private readonly IDisposable? _resource;
    private readonly object _lock = new();
    private readonly LinkedList<Block> _blocks = new();

    private ViewerDocument(Stream stream, IDisposable? resource)
    {
        _stream = stream;
        _resource = resource;
        Length = stream.Length;
    }

    /// <summary>
    ///     Opens a seekable stream for viewing, detecting the text encoding. The stream (and the resource it belongs to,
    ///     disposed after it, e.g. the volume the file is read from) is owned by the document, also when opening fails.
    /// </summary>
    public static ViewerDocument Open(Stream stream, IDisposable? resource = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var document = new ViewerDocument(stream, resource);
        try
        {
            if (!stream.CanSeek)
            {
                throw new ArgumentException("The stream must be seekable", nameof(stream));
            }

            document.Detect();
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    /// <summary>Size of the file in bytes.</summary>
    public long Length { get; }

    public ViewerEncoding Encoding { get; private set; }

    public string EncodingName => Encoding == ViewerEncoding.Utf8 ? "UTF-8" : "ISO-8859-1";

    /// <summary>Offset of the first line, after a UTF-8 byte order mark.</summary>
    public long TextStart { get; private set; }

    /// <summary>True when the start of the file contains zero bytes, so it is shown in the hex view first.</summary>
    public bool IsBinary { get; private set; }

    /// <summary>Digits of the offsets in the hex view: 8, or 16 for files larger than 4 GB.</summary>
    public int HexOffsetDigits => Length > 0x1_0000_0000L ? 16 : 8;

    /// <summary>Offset of the last row in the hex view.</summary>
    public long LastHexRowOffset => Math.Max(0, (Length - 1) / BytesPerRow * BytesPerRow);

    public void Dispose()
    {
        _stream.Dispose();
        _resource?.Dispose();
    }

    /// <summary>Reads lines from a line offset, at most <paramref name="count" />; fewer at the end of the file.</summary>
    public IReadOnlyList<ViewerLine> ReadLines(long offset, int count)
    {
        var lines = new List<ViewerLine>(Math.Clamp(count, 0, 256));
        offset = Math.Max(offset, TextStart);
        var buffer = new byte[MaxLineLength + 2];
        while (lines.Count < count && offset < Length)
        {
            var read = ReadAt(offset, buffer);
            var (contentLength, lineLength) = FindLineEnd(buffer.AsSpan(0, read));
            lines.Add(new ViewerLine(offset, lineLength, contentLength,
                ToDisplayText(buffer.AsSpan(0, contentLength))));
            offset += lineLength;
        }

        return lines;
    }

    /// <summary>Offset of the line after the line at an offset, or the offset itself for the last line.</summary>
    public long GetNextLineOffset(long offset)
    {
        offset = Math.Max(offset, TextStart);
        var buffer = new byte[MaxLineLength + 2];
        var read = ReadAt(offset, buffer);
        var next = offset + FindLineEnd(buffer.AsSpan(0, read)).LineLength;
        return next < Length ? next : offset;
    }

    /// <summary>Offset of the line before the line at an offset, or the first line.</summary>
    public long GetPreviousLineOffset(long offset) => offset <= TextStart ? TextStart : GetLineOffset(offset - 1);

    /// <summary>Moves a line offset a number of lines down (positive) or up (negative), stopping at the first or last line.</summary>
    public long MoveLines(long offset, int lines)
    {
        for (var index = 0; index < Math.Abs(lines); index++)
        {
            var next = lines > 0 ? GetNextLineOffset(offset) : GetPreviousLineOffset(offset);
            if (next == offset)
            {
                break;
            }

            offset = next;
        }

        return offset;
    }

    /// <summary>Offset of the first line of the last page of lines, the last offset the top of the view can be at.</summary>
    public long GetLastPageOffset(int lines) => MoveLines(GetLineOffset(Length), -(Math.Max(1, lines) - 1));

    /// <summary>
    ///     Offset of the line containing a byte, e.g. to show the text at a row of the hex view. Its line start is
    ///     looked for at most 64 KB back, a longer line is split from there.
    /// </summary>
    public long GetLineOffset(long position)
    {
        if (Length <= TextStart)
        {
            return TextStart;
        }

        position = Math.Clamp(position, TextStart, Length - 1);

        // the lf of a cr lf belongs to the line ending with it
        var end = position;
        if (end > TextStart && ReadByteAt(end) == '\n' && ReadByteAt(end - 1) == '\r')
        {
            end--;
        }

        var offset = FindLineStart(end);
        var buffer = new byte[MaxLineLength + 2];
        while (true)
        {
            var read = ReadAt(offset, buffer);
            var lineLength = FindLineEnd(buffer.AsSpan(0, read)).LineLength;
            if (lineLength == 0 || offset + lineLength > position)
            {
                return offset;
            }

            offset += lineLength;
        }
    }

    /// <summary>
    ///     Reads the images of the file with the first image provider reading any on the thread pool, e.g. the images
    ///     of an icon file; empty when it isn't an image, larger than <see cref="MaxImageLength" /> (or
    ///     <see cref="MaxTextImageLength" /> without zero bytes at its start) or the providers fail. Without providers
    ///     (e.g. the TUI, which doesn't view images) the file isn't read.
    /// </summary>
    public async Task<IReadOnlyList<EntryImage>> ReadImagesAsync(string name, IEnumerable<IImageProvider> providers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var providerList = providers.ToList();
        if (providerList.Count == 0 || Length == 0 || Length > (IsBinary ? MaxImageLength : MaxTextImageLength))
        {
            return [];
        }

        var data = await Task.Run(() => ReadBytes(0, (int)Length), cancellationToken);
        foreach (var provider in providerList)
        {
            try
            {
                var images = await Task.Run(async () =>
                {
                    using var stream = new MemoryStream(data, false);
                    return await provider.ReadImages(name, stream, cancellationToken);
                }, cancellationToken);
                if (images.Count > 0)
                {
                    return images;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // an invalid image or a failing plugin, the next provider is asked
            }
        }

        return [];
    }

    /// <summary>Reads bytes from an offset, fewer at the end of the file.</summary>
    public byte[] ReadBytes(long offset, int count)
    {
        var buffer = new byte[Math.Max(0, count)];
        var read = ReadAt(offset, buffer);
        return read == buffer.Length ? buffer : buffer[..read];
    }

    /// <summary>
    ///     Formats a row of the hex view: the offset, the bytes as hex values (an extra space after 8) and the bytes as
    ///     text, where only printable ASCII characters are shown and others as '.', e.g.
    ///     <c>00000010  48 65 6C 6C 6F 0A 00 ...  Hello..</c>. A short last row is padded so the text lines up.
    /// </summary>
    public static string FormatHexRow(long offset, ReadOnlySpan<byte> bytes, int offsetDigits)
    {
        var builder = new StringBuilder(GetHexRowLength(offsetDigits));
        builder.Append(offset.ToString("X" + offsetDigits));
        builder.Append(' ');
        for (var index = 0; index < BytesPerRow; index++)
        {
            builder.Append(index == BytesPerRow / 2 ? "  " : " ");
            builder.Append(index < bytes.Length ? bytes[index].ToString("X2") : "  ");
        }

        builder.Append("  ");
        foreach (var value in bytes)
        {
            builder.Append(ToHexText(value));
        }

        return builder.ToString();
    }

    /// <summary>Character of a byte in the text part of the hex view: printable ASCII as it is, otherwise '.'.</summary>
    public static char ToHexText(byte value) => value is >= 0x20 and < 0x7F ? (char)value : '.';

    /// <summary>Column of the hex value of a byte (0-15) in a row formatted by <see cref="FormatHexRow" />.</summary>
    public static int GetHexColumn(int offsetDigits, int index) =>
        offsetDigits + 2 + index * 3 + (index >= BytesPerRow / 2 ? 1 : 0);

    /// <summary>Column of the text of a byte (0-15) in a row formatted by <see cref="FormatHexRow" />.</summary>
    public static int GetHexTextColumn(int offsetDigits, int index) => GetHexRowLength(offsetDigits) - BytesPerRow + index;

    /// <summary>Length of a full row formatted by <see cref="FormatHexRow" />.</summary>
    public static int GetHexRowLength(int offsetDigits) => offsetDigits + 1 + BytesPerRow * 3 + 1 + 2 + BytesPerRow;

    /// <summary>
    ///     Searches the lines for text or a regular expression (matching within a line, case-insensitive unless case
    ///     sensitive) on a background thread. Searching forward starts in the line at <paramref name="lineOffset" /> at
    ///     <paramref name="column" />, backwards before it. Returns null when not found. Columns are of the shown text
    ///     (tabs expanded), the match also has its byte offset and length, e.g. to show it in the hex view.
    /// </summary>
    /// <exception cref="IOException">The text is empty or an invalid regular expression.</exception>
    public Task<ViewerMatch?> FindAsync(ViewerSearchOptions options, long lineOffset, int column,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var matcher = new Matcher(options);
        return Task.Run(() => Find(matcher, options.Backwards, lineOffset, column, progress, cancellationToken),
            cancellationToken);
    }

    private ViewerMatch? Find(Matcher matcher, bool backwards, long lineOffset, int column,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var offset = GetLineOffset(lineOffset);
        var first = true;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed >= ProgressInterval)
            {
                progress?.Report(Length == 0 ? 1 : (double)offset / Length);
                stopwatch.Restart();
            }

            var line = ReadLines(offset, 1) is [var read] ? read : null;
            if (line != null)
            {
                var match = backwards
                    ? matcher.FindLast(line.Text, first ? column : int.MaxValue)
                    : matcher.FindFirst(line.Text, first ? column : 0);
                if (match is var (matchColumn, matchLength))
                {
                    return CreateMatch(line, matchColumn, matchLength);
                }
            }

            first = false;
            var next = backwards ? GetPreviousLineOffset(offset) : GetNextLineOffset(offset);
            if (line == null || next == offset)
            {
                return null;
            }

            offset = next;
        }
    }

    private ViewerMatch CreateMatch(ViewerLine line, int column, int length)
    {
        var buffer = new byte[MaxLineLength + 2];
        var read = ReadAt(line.Offset, buffer);
        var bytes = buffer.AsSpan(0, FindLineEnd(buffer.AsSpan(0, read)).ContentLength);
        var start = GetByteIndex(bytes, column);
        var end = GetByteIndex(bytes, column + length);
        return new ViewerMatch(line.Offset, column, length, line.Offset + start, Math.Max(1, end - start));
    }

    /// <summary>
    ///     Bytes of the character shown at a column of a line, e.g. under the mouse when selecting: its offset and the
    ///     offset after it. A column after the end of the text is the line break, the end of the line.
    /// </summary>
    public (long Start, long End) GetCharacterRange(ViewerLine line, int column)
    {
        if (column >= line.Text.Length)
        {
            return (line.Offset + line.ContentLength, line.Offset + line.Length);
        }

        var bytes = ReadContent(line);
        var start = GetByteIndex(bytes, Math.Max(0, column));
        var length = start < bytes.Length ? DecodeRune(bytes.AsSpan(start), out _) : 0;
        return (line.Offset + start, line.Offset + start + length);
    }

    /// <summary>Column in the shown text of a line of a byte offset, the length of the text after its end.</summary>
    public int GetColumn(ViewerLine line, long offset)
    {
        if (offset <= line.Offset)
        {
            return 0;
        }

        return offset >= line.Offset + line.ContentLength
            ? line.Text.Length
            : GetColumnOfByte(ReadContent(line), (int)(offset - line.Offset));
    }

    /// <summary>Text of bytes, decoded with the encoding as they are (tabs and line breaks kept), e.g. to copy it.</summary>
    /// <exception cref="IOException">More than <see cref="MaxCopyLength" /> bytes.</exception>
    public string ReadText(long start, long end)
    {
        var bytes = ReadRange(start, end);
        return Encoding == ViewerEncoding.Utf8
            ? System.Text.Encoding.UTF8.GetString(bytes)
            : System.Text.Encoding.Latin1.GetString(bytes);
    }

    /// <summary>
    ///     Bytes formatted like the hex view, e.g. to copy them: as hex values separated by spaces, a line per row
    ///     (<paramref name="hexValues" />), or as the text of the hex view (printable ASCII, others as '.').
    /// </summary>
    /// <exception cref="IOException">More than <see cref="MaxCopyLength" /> bytes.</exception>
    public string FormatHexBytes(long start, long end, bool hexValues)
    {
        var bytes = ReadRange(start, end);
        var builder = new StringBuilder(hexValues ? bytes.Length * 3 : bytes.Length);
        for (var index = 0; index < bytes.Length; index++)
        {
            if (!hexValues)
            {
                builder.Append(ToHexText(bytes[index]));
                continue;
            }

            if (index > 0)
            {
                builder.Append((start + index) % BytesPerRow == 0 ? Environment.NewLine : " ");
            }

            builder.Append(bytes[index].ToString("X2"));
        }

        return builder.ToString();
    }

    private byte[] ReadRange(long start, long end)
    {
        start = Math.Clamp(start, 0, Length);
        end = Math.Clamp(end, start, Length);
        if (end - start > MaxCopyLength)
        {
            throw new IOException(
                $"{end - start:N0} bytes are selected, at most {MaxCopyLength:N0} bytes can be copied");
        }

        return ReadBytes(start, (int)(end - start));
    }

    private byte[] ReadContent(ViewerLine line) => ReadBytes(line.Offset, line.ContentLength);

    private void Detect()
    {
        var buffer = new byte[DetectLength];
        var read = ReadAt(0, buffer);
        var span = buffer.AsSpan(0, read);
        if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            TextStart = 3;
            Encoding = ViewerEncoding.Utf8;
            return;
        }

        IsBinary = span.Contains((byte)0);
        if (read < Length)
        {
            // a character may continue after the bytes read
            var end = span.Length;
            while (end > 0 && span.Length - end < 3 && (span[end - 1] & 0xC0) == 0x80)
            {
                end--;
            }

            if (end > 0 && span[end - 1] >= 0xC0)
            {
                end--;
            }

            span = span[..end];
        }

        Encoding = System.Text.Unicode.Utf8.IsValid(span) ? ViewerEncoding.Utf8 : ViewerEncoding.Latin1;
    }

    /// <summary>
    ///     Finds the end of the line at the start of the bytes: the length of its content and of the line with its line
    ///     break. A line without a line break within <see cref="MaxLineLength" /> bytes is split there (before a UTF-8
    ///     character continuing after it).
    /// </summary>
    private (int ContentLength, int LineLength) FindLineEnd(ReadOnlySpan<byte> bytes)
    {
        var index = bytes[..Math.Min(bytes.Length, MaxLineLength + 1)].IndexOfAny((byte)'\n', (byte)'\r');
        if (index >= 0)
        {
            var lineLength = index + 1;
            if (bytes[index] == '\r' && index + 1 < bytes.Length && bytes[index + 1] == '\n')
            {
                lineLength++;
            }

            return (index, lineLength);
        }

        if (bytes.Length <= MaxLineLength)
        {
            return (bytes.Length, bytes.Length);
        }

        var split = MaxLineLength;
        if (Encoding == ViewerEncoding.Utf8)
        {
            while (split > MaxLineLength - 3 && (bytes[split] & 0xC0) == 0x80)
            {
                split--;
            }
        }

        return (split, split);
    }

    /// <summary>Start of the line ending before an offset: after the closest line break at most 64 KB back.</summary>
    private long FindLineStart(long end)
    {
        var limit = Math.Max(TextStart, end - MaxLineSearchLength);
        var chunk = new byte[4096];
        var chunkEnd = end;
        while (chunkEnd > limit)
        {
            var chunkStart = Math.Max(limit, chunkEnd - chunk.Length);
            var read = ReadAt(chunkStart, chunk.AsSpan(0, (int)(chunkEnd - chunkStart)));
            var index = chunk.AsSpan(0, read).LastIndexOfAny((byte)'\n', (byte)'\r');
            if (index >= 0)
            {
                return chunkStart + index + 1;
            }

            chunkEnd = chunkStart;
        }

        return limit;
    }

    private string ToDisplayText(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);
        Decode(bytes, builder, -1);
        return builder.ToString();
    }

    /// <summary>Byte index in the content of a line of a column of its shown text.</summary>
    private int GetByteIndex(ReadOnlySpan<byte> bytes, int column) => Decode(bytes, null, column);

    /// <summary>Column in the shown text of a line of a byte index in its content.</summary>
    private int GetColumnOfByte(ReadOnlySpan<byte> bytes, int index)
    {
        var position = 0;
        var length = 0;
        while (position < bytes.Length && position < index)
        {
            position += DecodeRune(bytes[position..], out var rune);
            length += GetWidth(rune, length);
        }

        return length;
    }

    /// <summary>Decodes the character at the start of bytes, returning the number of bytes it takes.</summary>
    private int DecodeRune(ReadOnlySpan<byte> bytes, out Rune rune)
    {
        if (Encoding == ViewerEncoding.Utf8)
        {
            Rune.DecodeFromUtf8(bytes, out rune, out var consumed);
            return Math.Max(1, consumed);
        }

        rune = new Rune(bytes[0]);
        return 1;
    }

    /// <summary>Columns a character takes at a column of the shown text: tabs to the next tab stop.</summary>
    private static int GetWidth(Rune rune, int column) =>
        rune.Value == '\t' ? TabSize - column % TabSize : rune.Utf16SequenceLength;

    /// <summary>
    ///     Decodes the content of a line into the text shown, tabs expanded to <see cref="TabSize" /> and control
    ///     characters as '.'. Returns the byte index of a column, or the length when none is given or it is after the end.
    /// </summary>
    private int Decode(ReadOnlySpan<byte> bytes, StringBuilder? text, int column)
    {
        Span<char> chars = stackalloc char[2];
        var position = 0;
        var length = 0;
        while (position < bytes.Length)
        {
            var consumed = DecodeRune(bytes[position..], out var rune);
            var width = GetWidth(rune, length);
            if (column >= 0 && column < length + width)
            {
                return position;
            }

            if (text != null)
            {
                if (rune.Value == '\t')
                {
                    text.Append(' ', width);
                }
                else if (Rune.IsControl(rune))
                {
                    text.Append('.');
                }
                else
                {
                    text.Append(chars[..rune.EncodeToUtf16(chars)]);
                }
            }

            length += width;
            position += consumed;
        }

        return position;
    }

    private int ReadByteAt(long offset)
    {
        Span<byte> buffer = stackalloc byte[1];
        return ReadAt(offset, buffer) == 1 ? buffer[0] : -1;
    }

    /// <summary>Reads bytes from an offset through the block cache, returning the number of bytes read.</summary>
    private int ReadAt(long offset, Span<byte> buffer)
    {
        lock (_lock)
        {
            var total = 0;
            while (total < buffer.Length && offset + total < Length)
            {
                var position = offset + total;
                var block = GetBlock(position / BlockSize);
                var start = (int)(position % BlockSize);
                var count = Math.Min(block.Length - start, buffer.Length - total);
                if (count <= 0)
                {
                    break;
                }

                block.Data.AsSpan(start, count).CopyTo(buffer[total..]);
                total += count;
            }

            return total;
        }
    }

    private Block GetBlock(long index)
    {
        for (var node = _blocks.First; node != null; node = node.Next)
        {
            if (node.Value.Index != index)
            {
                continue;
            }

            _blocks.Remove(node);
            _blocks.AddFirst(node);
            return node.Value;
        }

        var data = _blocks.Count >= CachedBlocks ? _blocks.Last!.Value.Data : new byte[BlockSize];
        if (_blocks.Count >= CachedBlocks)
        {
            _blocks.RemoveLast();
        }

        _stream.Seek(index * BlockSize, SeekOrigin.Begin);
        var length = 0;
        while (length < data.Length)
        {
            var read = _stream.Read(data, length, data.Length - length);
            if (read == 0)
            {
                break;
            }

            length += read;
        }

        var block = new Block(index, data, length);
        _blocks.AddFirst(block);
        return block;
    }

    private sealed record Block(long Index, byte[] Data, int Length);

    /// <summary>Finds text or a regular expression in the shown text of a line, ignoring empty matches.</summary>
    private sealed class Matcher
    {
        private readonly string _text;
        private readonly StringComparison _comparison;
        private readonly Regex? _regex;

        public Matcher(ViewerSearchOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            _text = options.Text ?? string.Empty;
            if (_text.Length == 0)
            {
                throw new IOException("Enter the text to search for");
            }

            _comparison = options.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (!options.IsRegex)
            {
                return;
            }

            var regexOptions = RegexOptions.CultureInvariant;
            try
            {
                _regex = new Regex(_text,
                    options.CaseSensitive ? regexOptions : regexOptions | RegexOptions.IgnoreCase, RegexTimeout);
            }
            catch (ArgumentException exception)
            {
                throw new IOException($"Invalid regular expression '{_text}': {exception.Message}", exception);
            }
        }

        /// <summary>First match starting at or after a column.</summary>
        public (int Column, int Length)? FindFirst(string line, int start)
        {
            start = Math.Clamp(start, 0, line.Length);
            if (_regex == null)
            {
                var index = line.IndexOf(_text, start, _comparison);
                return index >= 0 ? (index, _text.Length) : null;
            }

            try
            {
                for (var match = _regex.Match(line, start); match.Success; match = match.NextMatch())
                {
                    if (match.Length > 0)
                    {
                        return (match.Index, match.Length);
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
            }

            return null;
        }

        /// <summary>Last match starting before a column.</summary>
        public (int Column, int Length)? FindLast(string line, int before)
        {
            (int Column, int Length)? last = null;
            var start = 0;
            while (FindFirst(line, start) is var (column, length) && column < before)
            {
                last = (column, length);
                start = column + 1;
                if (start > line.Length)
                {
                    break;
                }
            }

            return last;
        }
    }
}

/// <summary>Encoding text is decoded with in the viewer.</summary>
public enum ViewerEncoding
{
    Utf8,
    Latin1
}

/// <summary>
///     A line shown in the viewer: its byte offset, length (with the line break) and length without the line break and
///     the text shown.
/// </summary>
public sealed record ViewerLine(long Offset, int Length, int ContentLength, string Text);

/// <summary>
///     What to search for in the viewer: text or a regular expression, case sensitive or not, forward or backwards.
/// </summary>
public sealed record ViewerSearchOptions(
    string Text,
    bool IsRegex = false,
    bool CaseSensitive = false,
    bool Backwards = false);

/// <summary>
///     Text found in the viewer: the offset of its line, the column and length in the shown text of the line and its
///     byte offset and length in the file.
/// </summary>
public sealed record ViewerMatch(long LineOffset, int Column, int Length, long ByteOffset, int ByteLength);
