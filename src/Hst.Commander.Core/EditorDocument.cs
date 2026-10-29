using System.Text;

namespace Hst.Commander.Core;

/// <summary>
///     The text of a file opened in the built-in editor (F4). The encoding is detected from the start of the file: a
///     UTF-8 or UTF-16 byte order mark, valid UTF-8, otherwise ISO-8859-1. The line breaks of the file (LF, CR LF or CR,
///     the first one found) are kept: <see cref="Text" /> has LF line breaks for editing and <see cref="Encode" /> writes
///     the text with the line breaks and encoding (and byte order mark) it was read with.
/// </summary>
public sealed class EditorDocument
{
    /// <summary>Largest file opened in the built-in editor, which holds the whole text in memory.</summary>
    public const int MaxLength = 16 * 1024 * 1024;

    private static readonly Encoding Latin1 = Encoding.Latin1;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    private EditorDocument(string text, Encoding encoding, bool hasByteOrderMark, string lineBreak)
    {
        Text = text;
        Encoding = encoding;
        HasByteOrderMark = hasByteOrderMark;
        LineBreak = lineBreak;
    }

    /// <summary>The text read, with LF line breaks.</summary>
    public string Text { get; }

    public Encoding Encoding { get; }

    /// <summary>True when the file started with a byte order mark, which is written again.</summary>
    public bool HasByteOrderMark { get; }

    /// <summary>Line break of the file: "\n", "\r\n" or "\r"; the host operating system's for files without one.</summary>
    public string LineBreak { get; }

    /// <summary>Name of the encoding and line breaks shown in the status line, e.g. "UTF-8, CR LF".</summary>
    public string Description => $"{EncodingName}, {LineBreakName}";

    public string EncodingName => Encoding switch
    {
        UTF8Encoding => HasByteOrderMark ? "UTF-8 BOM" : "UTF-8",
        UnicodeEncoding { CodePage: 1201 } => "UTF-16 BE",
        UnicodeEncoding => "UTF-16 LE",
        _ => "ISO-8859-1"
    };

    public string LineBreakName => LineBreak switch
    {
        "\r\n" => "CR LF",
        "\r" => "CR",
        _ => "LF"
    };

    /// <summary>An empty document, e.g. for a new file, with the line breaks of the host operating system.</summary>
    public static EditorDocument Empty { get; } = new(string.Empty, Utf8, false, Environment.NewLine);

    /// <summary>
    ///     Reads the text of a file. Throws <see cref="IOException" /> when it's larger than <see cref="MaxLength" /> or
    ///     binary (has zero bytes and no UTF-16 byte order mark).
    /// </summary>
    public static EditorDocument Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length > MaxLength)
        {
            throw new IOException(
                $"The file is too large for the editor ({SizeFormatter.FormatHumanized(bytes.Length)}, at most {SizeFormatter.FormatHumanized(MaxLength)})");
        }

        var (encoding, preambleLength) = DetectEncoding(bytes);
        if (preambleLength == 0 && encoding is not UnicodeEncoding && Array.IndexOf(bytes, (byte)0) >= 0)
        {
            throw new IOException("The file is binary and can't be edited as text");
        }

        var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        var lineBreak = DetectLineBreak(text) ?? Environment.NewLine;
        return new EditorDocument(NormalizeLineBreaks(text), encoding, preambleLength > 0, lineBreak);
    }

    /// <summary>Reads the text of a stream (see <see cref="Read(byte[])" />).</summary>
    public static async Task<EditorDocument> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream.CanSeek && stream.Length > MaxLength)
        {
            throw new IOException(
                $"The file is too large for the editor ({SizeFormatter.FormatHumanized(stream.Length)}, at most {SizeFormatter.FormatHumanized(MaxLength)})");
        }

        using var memoryStream = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            memoryStream.Write(buffer, 0, read);
            if (memoryStream.Length > MaxLength)
            {
                throw new IOException(
                    $"The file is too large for the editor (more than {SizeFormatter.FormatHumanized(MaxLength)})");
            }
        }

        return Read(memoryStream.ToArray());
    }

    /// <summary>
    ///     Encodes edited text with the encoding, byte order mark and line breaks of the document. Line breaks of any
    ///     kind in the text are written as <see cref="LineBreak" />. Characters ISO-8859-1 can't hold throw
    ///     <see cref="IOException" />, so they aren't lost silently.
    /// </summary>
    public byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var normalized = NormalizeLineBreaks(text);
        var fileText = LineBreak == "\n" ? normalized : normalized.Replace("\n", LineBreak, StringComparison.Ordinal);
        if (Encoding is not UTF8Encoding and not UnicodeEncoding)
        {
            foreach (var character in fileText.Where(character => character > 0xFF))
            {
                throw new IOException(
                    $"'{character}' (U+{(int)character:X4}) can't be saved in the file's encoding {EncodingName}");
            }
        }

        var preamble = HasByteOrderMark ? Encoding.GetPreamble() : [];
        var bytes = Encoding.GetBytes(fileText);
        return preamble.Length == 0 ? bytes : [..preamble, ..bytes];
    }

    /// <summary>Converts CR LF and CR line breaks to LF.</summary>
    public static string NormalizeLineBreaks(string text) => text.Contains('\r')
        ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
        : text;

    private static (Encoding Encoding, int PreambleLength) DetectEncoding(byte[] bytes)
    {
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
        {
            return (new UTF8Encoding(true), 3);
        }

        if (bytes is [0xFF, 0xFE, ..])
        {
            return (new UnicodeEncoding(false, true), 2);
        }

        if (bytes is [0xFE, 0xFF, ..])
        {
            return (new UnicodeEncoding(true, true), 2);
        }

        try
        {
            Utf8.GetCharCount(bytes);
            return (new UTF8Encoding(false), 0);
        }
        catch (DecoderFallbackException)
        {
            return (Latin1, 0);
        }
    }

    private static string? DetectLineBreak(string text)
    {
        var index = text.IndexOfAny(['\r', '\n']);
        if (index < 0)
        {
            return null;
        }

        return text[index] == '\n' ? "\n" : index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r";
    }
}
