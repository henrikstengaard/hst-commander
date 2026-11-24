namespace Hst.Commander.Core.Tests;

public class GivenPathText
{
    [Fact]
    public void When_PathFits_Then_PathIsUnchanged()
    {
        Assert.Equal(@"C:\games", PathText.Shorten(@"C:\games", 8));
    }

    [Fact]
    public void When_PathIsTooLong_Then_EndFromSeparatorIsKept()
    {
        // act
        var text = PathText.Shorten(@"C:\Users\name\amiga\games\whdload", 20);

        // assert
        Assert.Equal(@"...\games\whdload", text);
    }

    [Fact]
    public void When_LastNameDoesNotFit_Then_ItIsCut()
    {
        Assert.Equal("...ry-long-name", PathText.Shorten(@"C:\a-very-long-name", 15));
    }

    [Fact]
    public void When_MessageWithLongPath_Then_WordsBeforePathAreKept()
    {
        // act
        var text = PathText.ShortenMessage(@"Reading C:\Users\name\amiga\games\whdload...", 30);

        // assert
        Assert.Equal(@"Reading ...\games\whdload...", text);
        Assert.True(text.Length <= 30);
    }

    [Fact]
    public void When_MessageWithUncPath_Then_AllWordsBeforePathAreKept()
    {
        // act
        var text = PathText.ShortenMessage(@"Connecting to \\server\share\amiga\games...", 30);

        // assert
        Assert.Equal(@"Connecting to ...\games...", text);
    }

    [Fact]
    public void When_MessageWithPathWithSpaces_Then_PathStartsAtItsFirstWord()
    {
        // act
        var text = PathText.ShortenMessage(@"Reading C:\Program Files\Hst Commander\plugins...", 30);

        // assert
        Assert.Equal(@"Reading ...\plugins...", text);
    }

    [Fact]
    public void When_MessageWithoutPathIsTooLong_Then_EndIsCut()
    {
        Assert.Equal("Reading Phys...", PathText.ShortenMessage("Reading Physical disks...", 15));
    }

    [Fact]
    public void When_MessageFits_Then_MessageIsUnchanged()
    {
        Assert.Equal(@"Reading C:\...", PathText.ShortenMessage(@"Reading C:\...", 14));
    }
}
