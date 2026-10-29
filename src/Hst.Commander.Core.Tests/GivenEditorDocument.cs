using System.Text;

namespace Hst.Commander.Core.Tests;

public class GivenEditorDocument
{
    [Fact]
    public void When_ReadingUtf8WithCrLf_Then_TextHasLfAndEncodingKeepsCrLf()
    {
        // arrange
        var bytes = Encoding.UTF8.GetBytes("line 1\r\nlíne 2\r\n");

        // act
        var document = EditorDocument.Read(bytes);

        // assert
        Assert.Equal("line 1\nlíne 2\n", document.Text);
        Assert.Equal("UTF-8, CR LF", document.Description);
        Assert.Equal(bytes, document.Encode(document.Text));
    }

    [Fact]
    public void When_ReadingUtf8WithByteOrderMark_Then_ByteOrderMarkIsWrittenAgain()
    {
        // arrange
        byte[] bytes = [0xEF, 0xBB, 0xBF, .."abc\n"u8.ToArray()];

        // act
        var document = EditorDocument.Read(bytes);

        // assert
        Assert.Equal("abc\n", document.Text);
        Assert.Equal("UTF-8 BOM", document.EncodingName);
        Assert.Equal(bytes, document.Encode("abc\n"));
    }

    [Fact]
    public void When_ReadingLatin1_Then_CharactersAreKeptAndUnsupportedCharactersThrow()
    {
        // arrange: 0xE6 isn't valid utf-8 on its own
        byte[] bytes = [(byte)'a', 0xE6, (byte)'\n'];

        // act
        var document = EditorDocument.Read(bytes);

        // assert
        Assert.Equal("aæ\n", document.Text);
        Assert.Equal("ISO-8859-1", document.EncodingName);
        Assert.Equal(bytes, document.Encode(document.Text));
        Assert.Throws<IOException>(() => document.Encode("€"));
    }

    [Fact]
    public void When_ReadingUtf16_Then_TextIsDecodedAndEncodedAgain()
    {
        // arrange
        byte[] bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("a\rb")];

        // act
        var document = EditorDocument.Read(bytes);

        // assert
        Assert.Equal("a\nb", document.Text);
        Assert.Equal("UTF-16 LE, CR", document.Description);
        Assert.Equal(bytes, document.Encode("a\nb"));
    }

    [Fact]
    public void When_ReadingBinary_Then_IOExceptionIsThrown()
    {
        Assert.Throws<IOException>(() => EditorDocument.Read([0x00, 0x01, 0x02]));
    }

    [Fact]
    public void When_EncodingTextWithMixedLineBreaks_Then_TheFilesLineBreaksAreWritten()
    {
        // arrange
        var document = EditorDocument.Read("a\nb\n"u8.ToArray());

        // act
        var bytes = document.Encode("1\r\n2\r3\n");

        // assert
        Assert.Equal("1\n2\n3\n"u8.ToArray(), bytes);
    }
}
