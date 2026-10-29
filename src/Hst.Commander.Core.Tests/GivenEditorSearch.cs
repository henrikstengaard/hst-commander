namespace Hst.Commander.Core.Tests;

public class GivenEditorSearch
{
    private const string Text = "Hello world\nhello World\nhelloworld";

    [Fact]
    public void When_FindingFromStart_Then_FirstMatchIsFoundCaseInsensitive()
    {
        // act
        var match = EditorSearch.Find(Text, new EditorSearchOptions("world"), 0);

        // assert
        Assert.Equal(new EditorMatch(6, 5), match);
    }

    [Fact]
    public void When_FindingPastLastMatch_Then_SearchWrapsAround()
    {
        // act
        var match = EditorSearch.Find(Text, new EditorSearchOptions("hello", CaseSensitive: true), 20);

        // assert
        Assert.Equal(new EditorMatch(24, 5), match);
        Assert.Equal(new EditorMatch(12, 5, true),
            EditorSearch.Find(Text, new EditorSearchOptions("hello", CaseSensitive: true), 25));
    }

    [Fact]
    public void When_FindingBackwards_Then_MatchBeforeStartIsFound()
    {
        // act
        var match = EditorSearch.Find(Text, new EditorSearchOptions("world", Backwards: true), 17);

        // assert
        Assert.Equal(new EditorMatch(6, 5), match);
        Assert.Equal(new EditorMatch(29, 5, true),
            EditorSearch.Find(Text, new EditorSearchOptions("world", Backwards: true), 3));
    }

    [Fact]
    public void When_FindingWholeWords_Then_PartsOfWordsDontMatch()
    {
        // act
        var match = EditorSearch.Find(Text, new EditorSearchOptions("world", WholeWords: true), 20);

        // assert: "helloworld" isn't a match, so it wraps to the first
        Assert.Equal(new EditorMatch(6, 5, true), match);
    }

    [Fact]
    public void When_ReplacingAllWithRegex_Then_GroupsAreSubstituted()
    {
        // act
        var (text, count) = EditorSearch.ReplaceAll(Text,
            new EditorSearchOptions(@"^(\w+) (\w+)$", "$2 $1", IsRegex: true));

        // assert
        Assert.Equal(2, count);
        Assert.Equal("world Hello\nWorld hello\nhelloworld", text);
    }

    [Fact]
    public void When_SelectionIsMatch_Then_ReplacementIsReturned()
    {
        // arrange
        var options = new EditorSearchOptions(@"w(or)ld", "W$1LD", IsRegex: true);

        // act & assert
        Assert.True(EditorSearch.IsMatch(Text, options, 6, 5));
        Assert.False(EditorSearch.IsMatch(Text, options, 6, 4));
        Assert.Equal("WorLD", EditorSearch.GetReplacement(Text, options, new EditorMatch(6, 5)));
    }

    [Fact]
    public void When_SearchingEmptyTextOrInvalidRegex_Then_ArgumentExceptionIsThrown()
    {
        Assert.Throws<ArgumentException>(() => EditorSearch.Find(Text, new EditorSearchOptions(""), 0));
        Assert.Throws<ArgumentException>(() =>
            EditorSearch.Find(Text, new EditorSearchOptions("(", IsRegex: true), 0));
    }

    [Fact]
    public void When_GettingLineAndColumn_Then_TheyAreOneBased()
    {
        Assert.Equal((1, 1), EditorSession.GetLineColumn(Text, 0));
        Assert.Equal((2, 7), EditorSession.GetLineColumn(Text, 18));
        Assert.Equal(12, EditorSession.GetLineIndex(Text, 2));
        Assert.Equal(24, EditorSession.GetLineIndex(Text, 100));
    }
}
