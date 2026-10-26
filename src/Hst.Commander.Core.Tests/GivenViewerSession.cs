using System.Text;

namespace Hst.Commander.Core.Tests;

public class GivenViewerSession
{
    private static ViewerSession Open(string text) =>
        new(ViewerDocument.Open(new MemoryStream(new UTF8Encoding(false).GetBytes(text))));

    private static ViewerSession Open(byte[] data) => new(ViewerDocument.Open(new MemoryStream(data)));

    [Fact]
    public void When_SelectingInTextView_Then_CharactersOfBothCellsAreSelectedAndCopiedAsInFile()
    {
        // arrange
        using var session = Open("ab\tæc\nsecond line");

        // act: from "b" to "æ" (after the tab, column 8)
        session.StartSelection(0, 1, 10);
        session.ExtendSelection(0, 8, 10);
        var text = session.GetSelectedText();
        var columns = session.GetSelectionColumns(session.GetLines(10)[0]);

        // assert
        Assert.Equal("b\tæ", text);
        Assert.Equal((1, 8), columns);
        Assert.Equal(new ViewerSelection(1, 5, false), session.Selection);
    }

    [Fact]
    public void When_SelectingBackwardsOverLines_Then_LineBreaksAreSelected()
    {
        // arrange
        using var session = Open("first\r\nsecond\r\nthird");

        // act: from "c" in "second" back to after the end of "first"
        session.StartSelection(1, 2, 10);
        session.ExtendSelection(0, 20, 10);
        var lines = session.GetLines(10);

        // assert
        Assert.Equal("\r\nsec", session.GetSelectedText());
        Assert.Equal((5, 1), session.GetSelectionColumns(lines[0]));
        Assert.Equal((0, 3), session.GetSelectionColumns(lines[1]));
        Assert.Null(session.GetSelectionColumns(lines[2]));
        Assert.Contains("5 bytes selected", session.GetStatusText(10));
    }

    [Fact]
    public void When_SelectingBelowLastLine_Then_SelectionGoesToEnd()
    {
        // arrange
        using var session = Open("one\ntwo");

        // act
        session.StartSelection(1, 0, 10);
        session.ExtendSelection(5, 0, 10);

        // assert
        Assert.Equal("two", session.GetSelectedText());
    }

    [Fact]
    public void When_SelectingHexValuesOrHexText_Then_HexValuesOrTextAreCopied()
    {
        // arrange
        var data = new byte[40];
        Encoding.ASCII.GetBytes("Hello\0").CopyTo(data, 14);
        using var session = Open(data);
        var digits = session.Document.HexOffsetDigits;

        // act: hex values of bytes 14 to 17 (over two rows)
        session.StartSelection(0, ViewerDocument.GetHexColumn(digits, 14) + 1, 10);
        session.ExtendSelection(1, ViewerDocument.GetHexColumn(digits, 1), 10);
        var hexValues = session.GetSelectedText();

        // and the text of bytes 14 to 19
        session.StartSelection(0, ViewerDocument.GetHexTextColumn(digits, 14), 10);
        session.ExtendSelection(1, ViewerDocument.GetHexTextColumn(digits, 3), 10);
        var hexText = session.GetSelectedText();

        // assert
        Assert.True(session.IsHex);
        Assert.Equal($"48 65{Environment.NewLine}6C 6C", hexValues);
        Assert.Equal("Hello.", hexText);
        Assert.True(session.IsSelected(19));
        Assert.False(session.IsSelected(20));
    }

    [Fact]
    public void When_SelectAllAndClear_Then_WholeFileIsSelectedAndCleared()
    {
        // arrange
        using var session = Open("all\ntext");

        // act
        session.SelectAll();
        var text = session.GetSelectedText();
        session.ClearSelection();

        // assert
        Assert.Equal("all\ntext", text);
        Assert.Null(session.Selection);
        Assert.Null(session.GetSelectedText());
    }

    [Fact]
    public void When_SelectionIsLargerThanMaxCopyLength_Then_IOExceptionIsThrown()
    {
        // arrange
        using var session = Open(new byte[ViewerDocument.MaxCopyLength + 1]);

        // act
        session.SelectAll();

        // assert
        Assert.Throws<IOException>(() => session.GetSelectedText());
    }
}
