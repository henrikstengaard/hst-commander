using System.Text;

namespace Hst.Commander.Core.Tests;

public class GivenViewerDocument
{
    private static ViewerDocument Open(string text, Encoding? encoding = null) =>
        Open((encoding ?? new UTF8Encoding(false)).GetBytes(text));

    private static ViewerDocument Open(byte[] data) => ViewerDocument.Open(new MemoryStream(data));

    private static IReadOnlyList<string> ReadAllLines(ViewerDocument document) =>
        document.ReadLines(0, int.MaxValue).Select(line => line.Text).ToList();

    [Fact]
    public void When_ReadLines_Then_LinesAreSplitAtLfCrLfAndCr()
    {
        // arrange
        using var document = Open("line1\nline2\r\nline3\rline4");

        // act
        var lines = document.ReadLines(0, 10);

        // assert
        Assert.Equal(["line1", "line2", "line3", "line4"], lines.Select(line => line.Text));
        Assert.Equal([0L, 6L, 13L, 19L], lines.Select(line => line.Offset));
    }

    [Fact]
    public void When_ReadLinesWithTabsAndControlCharacters_Then_TabsAreExpandedAndControlCharactersShownAsDots()
    {
        // arrange
        using var document = Open("a\tb\u0001c");

        // act
        var lines = ReadAllLines(document);

        // assert
        Assert.Equal(["a       b.c"], lines);
    }

    [Fact]
    public void When_LineIsLongerThanMaxLineLength_Then_ItIsSplitAndPreviousLineGoesBackToTheSplits()
    {
        // arrange
        var text = new string('a', ViewerDocument.MaxLineLength * 2 + 10) + "\nend";
        using var document = Open(text);

        // act
        var lines = document.ReadLines(0, 10);
        var previous = document.GetPreviousLineOffset(lines[3].Offset);
        var previous2 = document.GetPreviousLineOffset(previous);

        // assert
        Assert.Equal(4, lines.Count);
        Assert.Equal(ViewerDocument.MaxLineLength, lines[0].Text.Length);
        Assert.Equal(10, lines[2].Text.Length);
        Assert.Equal("end", lines[3].Text);
        Assert.Equal(lines[2].Offset, previous);
        Assert.Equal(lines[1].Offset, previous2);
    }

    [Fact]
    public void When_LongUtf8LineIsSplit_Then_CharactersAreNotCut()
    {
        // arrange
        var text = new string('a', ViewerDocument.MaxLineLength - 1) + "æøå";
        using var document = Open(text);

        // act
        var lines = ReadAllLines(document);

        // assert
        Assert.Equal(ViewerEncoding.Utf8, document.Encoding);
        Assert.Equal(text, string.Concat(lines));
    }

    [Fact]
    public void When_MovingLinesInLargeFile_Then_OffsetsAreLineStarts()
    {
        // arrange
        var builder = new StringBuilder();
        for (var index = 0; index < 100000; index++)
        {
            builder.Append("line ").Append(index).Append('\n');
        }

        using var document = Open(builder.ToString());

        // act
        var offset = document.MoveLines(0, 50000);
        var line = document.ReadLines(offset, 1)[0];
        var back = document.MoveLines(offset, -49999);
        var lastPage = document.GetLastPageOffset(10);

        // assert
        Assert.Equal("line 50000", line.Text);
        Assert.Equal("line 1", document.ReadLines(back, 1)[0].Text);
        Assert.Equal("line 99990", document.ReadLines(lastPage, 1)[0].Text);
        Assert.Equal(10, document.ReadLines(lastPage, 20).Count);
    }

    [Fact]
    public void When_GetLineOffsetOfByteInLine_Then_StartOfLineIsReturned()
    {
        // arrange
        using var document = Open("first\r\nsecond\r\nthird");

        // act & assert
        Assert.Equal(0, document.GetLineOffset(3));
        Assert.Equal(0, document.GetLineOffset(6));
        Assert.Equal(7, document.GetLineOffset(7));
        Assert.Equal(7, document.GetLineOffset(14));
        Assert.Equal(15, document.GetLineOffset(100));
    }

    [Fact]
    public void When_FileIsNotValidUtf8_Then_Latin1IsUsed()
    {
        // arrange
        using var document = Open(Encoding.Latin1.GetBytes("æøå"));

        // act
        var lines = ReadAllLines(document);

        // assert
        Assert.Equal(ViewerEncoding.Latin1, document.Encoding);
        Assert.Equal(["æøå"], lines);
    }

    [Fact]
    public void When_FileStartsWithByteOrderMark_Then_ItIsSkipped()
    {
        // arrange
        using var document = Open([0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i']);

        // act
        var lines = document.ReadLines(0, 10);

        // assert
        Assert.Equal(3, document.TextStart);
        Assert.Equal(["hi"], lines.Select(line => line.Text));
        Assert.Equal(3, lines[0].Offset);
    }

    [Fact]
    public void When_FileContainsZeroBytes_Then_ItIsBinary()
    {
        // arrange
        using var binary = Open([1, 0, 2]);
        using var text = Open("text");

        // assert
        Assert.True(binary.IsBinary);
        Assert.False(text.IsBinary);
    }

    [Fact]
    public void When_FormatHexRow_Then_HexValuesAndPrintableTextAreShown()
    {
        // arrange
        var bytes = Encoding.ASCII.GetBytes("Hello, World!\n\0\u007f");

        // act
        var row = ViewerDocument.FormatHexRow(0x10, bytes, 8);
        var shortRow = ViewerDocument.FormatHexRow(0x20, bytes.AsSpan(0, 2), 8);

        // assert
        Assert.Equal("00000010  48 65 6C 6C 6F 2C 20 57  6F 72 6C 64 21 0A 00 7F  Hello, World!...", row);
        Assert.Equal(ViewerDocument.GetHexRowLength(8), row.Length);
        Assert.Equal("00000020  48 65" + new string(' ', 14 * 3 + 1) + "  He", shortRow);
        Assert.Equal("48", row.Substring(ViewerDocument.GetHexColumn(8, 0), 2));
        Assert.Equal("6F", row.Substring(ViewerDocument.GetHexColumn(8, 8), 2));
        Assert.Equal('!', row[ViewerDocument.GetHexTextColumn(8, 12)]);
    }

    [Fact]
    public async Task When_FindText_Then_MatchesAreFoundForwardCaseInsensitive()
    {
        // arrange
        using var document = Open("one\ntwo Two\nthree two");
        var options = new ViewerSearchOptions("TWO");

        // act
        var first = await document.FindAsync(options, 0, 0);
        var second = await document.FindAsync(options, first!.LineOffset, first.Column + 1);
        var third = await document.FindAsync(options, second!.LineOffset, second.Column + 1);
        var none = await document.FindAsync(options, third!.LineOffset, third.Column + 1);

        // assert
        Assert.Equal(new ViewerMatch(4, 0, 3, 4, 3), first);
        Assert.Equal(new ViewerMatch(4, 4, 3, 8, 3), second);
        Assert.Equal(new ViewerMatch(12, 6, 3, 18, 3), third);
        Assert.Null(none);
    }

    [Fact]
    public async Task When_FindCaseSensitive_Then_OnlyExactCaseMatches()
    {
        // arrange
        using var document = Open("two\nTwo");

        // act
        var match = await document.FindAsync(new ViewerSearchOptions("Two", CaseSensitive: true), 0, 0);

        // assert
        Assert.Equal(4, match!.LineOffset);
    }

    [Fact]
    public async Task When_FindBackwards_Then_PreviousMatchIsFound()
    {
        // arrange
        using var document = Open("ab ab\nxx\nab");
        var options = new ViewerSearchOptions("ab", Backwards: true);

        // act
        var first = await document.FindAsync(options, 9, int.MaxValue);
        var second = await document.FindAsync(options, first!.LineOffset, first.Column);
        var third = await document.FindAsync(options, second!.LineOffset, second.Column);

        // assert
        Assert.Equal((9L, 0), (first.LineOffset, first.Column));
        Assert.Equal((0L, 3), (second.LineOffset, second.Column));
        Assert.Equal((0L, 0), (third!.LineOffset, third.Column));
    }

    [Fact]
    public async Task When_FindRegex_Then_ColumnsAndBytesOfMatchAreReturned()
    {
        // arrange
        using var document = Open("x\tæ12 y");

        // act
        var match = await document.FindAsync(new ViewerSearchOptions(@"\d+", IsRegex: true), 0, 0);

        // assert
        Assert.Equal(new ViewerMatch(0, 9, 2, 4, 2), match);
    }

    [Fact]
    public async Task When_FindInvalidRegexOrEmptyText_Then_IOExceptionIsThrown()
    {
        // arrange
        using var document = Open("text");

        // act & assert
        await Assert.ThrowsAsync<IOException>(() => document.FindAsync(new ViewerSearchOptions("(", true), 0, 0));
        await Assert.ThrowsAsync<IOException>(() => document.FindAsync(new ViewerSearchOptions(string.Empty), 0, 0));
    }

    [Fact]
    public async Task When_OpenViewerOfLocalFileAndFileInZip_Then_ContentIsRead()
    {
        // arrange
        var rootPath = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(rootPath);
        await File.WriteAllTextAsync(Path.Combine(rootPath, "local.txt"), "local text");
        await File.WriteAllBytesAsync(Path.Combine(rootPath, "archive.zip"),
            ZipTestData.CreateZip(("zipped.txt", "zipped text")));
        try
        {
            using var navigationContext = new NavigationContext(rootPath);
            var entries = await navigationContext.ListEntriesAsync();

            // act
            string localText;
            using (var local = await navigationContext.OpenViewerAsync(entries.First(e => e.Name == "local.txt")))
            {
                localText = local.Document.ReadLines(0, 1)[0].Text;
            }

            await navigationContext.OpenEntryAsync(entries.First(e => e.Name == "archive.zip"));
            var zipEntries = await navigationContext.ListEntriesAsync();
            string zippedText;
            using (var zipped = await navigationContext.OpenViewerAsync(zipEntries.First(e => e.Name == "zipped.txt")))
            {
                zippedText = zipped.Document.ReadLines(0, 1)[0].Text;
            }

            // assert
            Assert.Equal("local text", localText);
            Assert.Equal("zipped text", zippedText);
            await Assert.ThrowsAsync<IOException>(() => navigationContext.OpenViewerAsync(zipEntries[0]));
        }
        finally
        {
            Directory.Delete(rootPath, true);
        }
    }
}
