namespace Hst.Commander.Core.Tests;

public class GivenExternalEditor
{
    [Fact]
    public void When_ParsingCommandWithQuotesAndPlaceholder_Then_FileReplacesPlaceholder()
    {
        // act
        var command = ExternalEditor.ParseCommand("\"C:\\Program Files\\My Editor\\edit.exe\" -n %f --wait");

        // assert
        Assert.NotNull(command);
        Assert.Equal("C:\\Program Files\\My Editor\\edit.exe", command.FileName);
        Assert.Equal("edit", command.Name);
        var startInfo = command.CreateStartInfo(Path.Combine("dir", "a b.txt"));
        Assert.Equal(["-n", Path.Combine("dir", "a b.txt"), "--wait"], startInfo.ArgumentList);
    }

    [Fact]
    public void When_ParsingCommandWithoutPlaceholder_Then_FileIsAddedLast()
    {
        // act
        var command = ExternalEditor.ParseCommand("  code   --wait ");

        // assert
        Assert.NotNull(command);
        Assert.Equal("code", command.FileName);
        Assert.Equal(["--wait", "file.txt"], command.CreateStartInfo("file.txt").ArgumentList);
    }

    [Theory]
    [InlineData("/usr/local/bin/nvim", "nvim")]
    [InlineData("\"C:\\Tools\\Sublime Text\\subl.exe\" -w", "subl")]
    [InlineData("code.cmd --wait", "code")]
    public void When_ParsingCommandWithPath_Then_NameIsProgramWithoutDirectoryAndExtension(string command,
        string name)
    {
        Assert.Equal(name, ExternalEditor.ParseCommand(command)?.Name);
    }

    [Fact]
    public void When_ParsingEmptyCommand_Then_NullIsReturned()
    {
        Assert.Null(ExternalEditor.ParseCommand("   "));
        Assert.Null(ExternalEditor.Get(EditorKind.Custom, null, false));
        Assert.Null(ExternalEditor.Get(EditorKind.BuiltIn, "vim", true));
    }
}
