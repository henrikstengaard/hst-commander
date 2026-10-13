using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Core.Tests;

public class GivenEntryFilter
{
    [Theory]
    [InlineData("*", "file.txt", true)]
    [InlineData("*", "file", true)]
    [InlineData("*.*", "file", true)]
    [InlineData("", "file.txt", true)]
    [InlineData("*.txt", "file.txt", true)]
    [InlineData("*.txt", "FILE.TXT", true)]
    [InlineData("*.txt", "file.txt.bak", false)]
    [InlineData("file?.txt", "file1.txt", true)]
    [InlineData("file?.txt", "file10.txt", false)]
    [InlineData("*.txt; *.md", "readme.md", true)]
    [InlineData("*.txt;*.md", "image.png", false)]
    [InlineData("a+b(1).txt", "a+b(1).txt", true)]
    public void When_IsMatch_Then_WildcardsAreMatched(string filter, string name, bool expected)
    {
        // act
        var isMatch = new EntryFilter(filter).IsMatch(name);

        // assert
        Assert.Equal(expected, isMatch);
    }
}
