using System.Text;

namespace Hst.Commander.Core.Tests;

public class GivenViewerWordWrap
{
    private static ViewerSession Open(string text) =>
        new(ViewerDocument.Open(new MemoryStream(new UTF8Encoding(false).GetBytes(text))));

    private static List<string> Texts(ViewerSession session, int rows) =>
        session.GetLines(rows).Select(line => line.Text).ToList();

    [Fact]
    public void When_WrappingLongLine_Then_ItIsWrappedAfterSpacesAndLongWordsAreSplit()
    {
        // arrange
        using var session = Open("one two three\nabcdefghijkl\nend");
        session.SetColumns(8);

        // act
        session.ToggleWordWrap();

        // assert
        Assert.True(session.IsWordWrap);
        Assert.Equal(["one two ", "three", "abcdefgh", "ijkl", "end"], Texts(session, 10));
        Assert.Contains(", Wrap", session.GetStatusText(10));
    }

    [Fact]
    public void When_ScrollingWrappedLines_Then_RowsAreScrolledAndLastPageStopsAtEnd()
    {
        // arrange
        using var session = Open("one two three\nabcdefghijkl\nend");
        session.SetColumns(8);
        session.ToggleWordWrap();

        // act
        session.Scroll(1, 2);
        var afterOne = Texts(session, 2);
        session.ScrollToEnd(2);
        var lastPage = Texts(session, 2);
        session.Scroll(-1, 2);

        // assert
        Assert.Equal(["three", "abcdefgh"], afterOne);
        Assert.Equal(["ijkl", "end"], lastPage);
        Assert.Equal(["abcdefgh", "ijkl"], Texts(session, 2));
    }

    [Fact]
    public void When_ResizingOrUnwrapping_Then_TopIsKeptInItsLine()
    {
        // arrange: the top at the second row of the first line
        using var session = Open("one two three\nabcdefghijkl\nend");
        session.SetColumns(8);
        session.ToggleWordWrap();
        session.Scroll(1, 2);

        // act & assert
        session.SetColumns(4);
        Assert.Equal("thre", Texts(session, 1)[0]);
        session.ToggleWordWrap();
        Assert.Equal("one two three", Texts(session, 1)[0]);
        Assert.Equal(0, session.TopOffset);
    }

    [Fact]
    public async Task When_SearchingWrappedLines_Then_MatchAcrossRowsIsFoundAndHighlightedInBoth()
    {
        // arrange
        using var session = Open("abcdefghijkl\nend");
        session.SetColumns(8);
        session.ToggleWordWrap();

        // act
        var found = await session.FindAsync(new ViewerSearchOptions("ghij"), 5, 8);
        var lines = session.GetLines(5);

        // assert
        Assert.True(found);
        Assert.Equal((6, 2), session.GetMatchColumns(lines[0]));
        Assert.Equal((0, 2), session.GetMatchColumns(lines[1]));
        Assert.Null(session.GetMatchColumns(lines[2]));
    }

    [Fact]
    public void When_ScrollingColumnsWhileWrapped_Then_LeftColumnStaysZero()
    {
        // arrange
        using var session = Open(new string('x', 100));
        session.SetColumns(10);
        session.ToggleWordWrap();

        // act
        session.ScrollColumns(5, 5, 10);
        session.ScrollToColumn(5);

        // assert
        Assert.Equal(0, session.LeftColumn);
        Assert.Equal(10, Texts(session, 20).Count);
    }
}
