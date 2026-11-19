namespace Hst.Commander.Core.Tests;

public class GivenCommandLine
{
    [Fact]
    public void When_InsertingNamesInEmptyCommand_Then_NamesAreSeparatedBySpaces()
    {
        // arrange
        var commandLine = new CommandLine();

        // act
        commandLine.Insert("file1.txt");
        var position = commandLine.Insert("file2.txt");

        // assert
        Assert.Equal("file1.txt file2.txt ", commandLine.Text);
        Assert.Equal(commandLine.Text.Length, position);
    }

    [Fact]
    public void When_InsertingNameAfterCommand_Then_SpaceIsAddedBeforeName()
    {
        // arrange
        var commandLine = new CommandLine { Text = "copy" };

        // act
        commandLine.Insert("file1.txt");

        // assert
        Assert.Equal("copy file1.txt ", commandLine.Text);
    }

    [Fact]
    public void When_InsertingNameAtPositionBeforeSpace_Then_NameIsInsertedWithoutExtraSpace()
    {
        // arrange
        var commandLine = new CommandLine { Text = "copy  dest" };

        // act
        var position = commandLine.Insert("file1.txt", 5);

        // assert
        Assert.Equal("copy file1.txt dest", commandLine.Text);
        Assert.Equal(14, position);
    }

    [Theory]
    [InlineData("file.txt", "file.txt")]
    [InlineData("my file.txt", "\"my file.txt\"")]
    [InlineData("a&b.txt", "\"a&b.txt\"")]
    public void When_QuotingForWindows_Then_NamesWithSpacesOrShellCharactersAreDoubleQuoted(string name,
        string expected) =>
        Assert.Equal(expected, CommandLine.Quote(name, true));

    [Theory]
    [InlineData("file.txt", "file.txt")]
    [InlineData("/home/user/file-1.txt", "/home/user/file-1.txt")]
    [InlineData("my file.txt", "'my file.txt'")]
    [InlineData("it's.txt", "'it'\\''s.txt'")]
    [InlineData("$HOME", "'$HOME'")]
    public void When_QuotingForUnix_Then_NamesWithSpacesOrShellCharactersAreSingleQuoted(string name,
        string expected) =>
        Assert.Equal(expected, CommandLine.Quote(name, false));

    [Fact]
    public void When_TakingCommand_Then_CommandIsAddedToHistoryAndCleared()
    {
        // arrange
        var commandLine = new CommandLine { Text = " dir " };

        // act
        var command = commandLine.TakeCommand();

        // assert
        Assert.Equal("dir", command);
        Assert.Equal(string.Empty, commandLine.Text);
        Assert.Equal(["dir"], commandLine.History);
    }

    [Fact]
    public void When_TakingEmptyCommand_Then_NullIsReturned()
    {
        // arrange
        var commandLine = new CommandLine { Text = "  " };

        // act
        var command = commandLine.TakeCommand();

        // assert
        Assert.Null(command);
        Assert.Empty(commandLine.History);
    }

    [Fact]
    public void When_TakingSameCommandTwice_Then_HistoryHasItOnce()
    {
        // arrange
        var commandLine = new CommandLine { Text = "dir" };
        commandLine.TakeCommand();
        commandLine.Text = "dir";

        // act
        commandLine.TakeCommand();

        // assert
        Assert.Equal(["dir"], commandLine.History);
    }

    [Fact]
    public void When_GoingThroughHistory_Then_CommandsAreShownAndCommandBeingTypedIsKept()
    {
        // arrange
        var commandLine = new CommandLine { Text = "first" };
        commandLine.TakeCommand();
        commandLine.Text = "second";
        commandLine.TakeCommand();
        commandLine.Text = "typing";

        // act & assert
        Assert.True(commandLine.Previous());
        Assert.Equal("second", commandLine.Text);
        Assert.True(commandLine.Previous());
        Assert.Equal("first", commandLine.Text);
        Assert.False(commandLine.Previous());
        Assert.True(commandLine.Next());
        Assert.Equal("second", commandLine.Text);
        Assert.True(commandLine.Next());
        Assert.Equal("typing", commandLine.Text);
        Assert.False(commandLine.Next());
    }
}
