using Hst.Commander.Core;

namespace Hst.Commander.Core.Tests;

public class GivenQuickSearch
{
    private static readonly string?[] Names = [null, "docs", "readme.md", "release.ps1", "src", "unreal.txt"];

    private static QuickSearch CreateStarted()
    {
        var quickSearch = new QuickSearch();
        quickSearch.Start();
        return quickSearch;
    }

    [Fact]
    public void When_TextMatchesPrefix_Then_FirstPrefixMatchIsSelected()
    {
        var quickSearch = CreateStarted();

        Assert.Equal(2, quickSearch.TryAppend("r", Names));
        Assert.Equal(2, quickSearch.TryAppend("e", Names));
        Assert.Equal(3, quickSearch.TryAppend("l", Names));
        Assert.Equal("rel", quickSearch.Text);
    }

    [Fact]
    public void When_NoPrefixMatch_Then_TextIsRejected()
    {
        var quickSearch = CreateStarted();

        Assert.Null(quickSearch.TryAppend("real", Names));
        Assert.Equal(string.Empty, quickSearch.Text);
    }

    [Fact]
    public void When_TextHasWildcardCharacters_Then_TheyMatchLiterally()
    {
        var quickSearch = CreateStarted();

        Assert.Null(quickSearch.TryAppend("*", Names));
        Assert.Null(quickSearch.TryAppend("r*", Names));
        Assert.Equal(1, quickSearch.TryAppend("a*?", [null, "a*?.txt", "ab.txt"]));
    }

    [Fact]
    public void When_CharacterHasNoMatch_Then_ItIsRejectedAndTextIsUnchanged()
    {
        var quickSearch = CreateStarted();
        quickSearch.TryAppend("s", Names);

        Assert.Null(quickSearch.TryAppend("x", Names));
        Assert.Equal("s", quickSearch.Text);
    }

    [Fact]
    public void When_Next_Then_WrapsWithinMatches()
    {
        var quickSearch = CreateStarted();
        quickSearch.TryAppend("re", Names);

        Assert.Equal(3, quickSearch.Next(Names, 2, true));
        Assert.Equal(2, quickSearch.Next(Names, 3, true));
    }

    [Fact]
    public void When_Next_Then_NamesContainingTextAreSkipped()
    {
        var quickSearch = CreateStarted();
        quickSearch.TryAppend("re", Names);

        // "unreal.txt" contains "re" but doesn't start with it
        Assert.Equal(2, quickSearch.Next(Names, 3, true));
    }

    [Fact]
    public void When_Previous_Then_WrapsBackwards()
    {
        var quickSearch = CreateStarted();
        quickSearch.TryAppend("re", Names);

        Assert.Equal(3, quickSearch.Next(Names, 2, false));
        Assert.Equal(2, quickSearch.Next(Names, 3, false));
    }

    [Fact]
    public void When_RemoveLast_Then_MatchesAgain()
    {
        var quickSearch = CreateStarted();
        quickSearch.TryAppend("rel", Names);

        Assert.Equal(2, quickSearch.RemoveLast(Names));
        Assert.Equal("re", quickSearch.Text);
    }

    [Fact]
    public void When_RemoveLastCharacter_Then_TextIsEmptyAndSearchIsActive()
    {
        var quickSearch = CreateStarted();
        quickSearch.TryAppend("r", Names);

        Assert.Null(quickSearch.RemoveLast(Names));
        Assert.Equal(string.Empty, quickSearch.Text);
        Assert.True(quickSearch.IsActive);
    }

    [Fact]
    public void When_NameIsNull_Then_ItIsNeverMatched()
    {
        var quickSearch = CreateStarted();

        Assert.Null(quickSearch.TryAppend(".", [null, "a"]));
    }

    [Fact]
    public void When_MatchingIgnoresCase_Then_UpperCaseMatchesLowerCase()
    {
        var quickSearch = CreateStarted();

        Assert.Equal(4, quickSearch.TryAppend("SR", Names));
    }

    [Fact]
    public void When_Stopped_Then_TextIsClearedAndInactive()
    {
        var quickSearch = CreateStarted();
        quickSearch.TryAppend("s", Names);

        quickSearch.Stop();

        Assert.False(quickSearch.IsActive);
        Assert.Equal(string.Empty, quickSearch.Text);
    }
}
