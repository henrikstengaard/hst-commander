using System.Diagnostics;

namespace Hst.Commander.Core.Tests;

public class GivenShellCommand
{
    [Fact]
    public void When_CreatingStartInfoForWindows_Then_CommandIsRunWithCmd()
    {
        // act
        var startInfo = ShellCommand.CreateStartInfo("copy \"a b.txt\" c", @"C:\work", true, null);

        // assert
        Assert.Equal("cmd.exe", startInfo.FileName);
        Assert.Equal("/d /s /c \"copy \"a b.txt\" c\"", startInfo.Arguments);
        Assert.Equal(@"C:\work", startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
    }

    [Fact]
    public void When_CreatingStartInfoForUnix_Then_CommandIsRunWithShell()
    {
        // act
        var startInfo = ShellCommand.CreateStartInfo("ls -l", "/home/user", false, "/bin/zsh");

        // assert
        Assert.Equal("/bin/zsh", startInfo.FileName);
        Assert.Equal(["-c", "ls -l"], startInfo.ArgumentList);
        Assert.Equal("/home/user", startInfo.WorkingDirectory);
    }

    [Fact]
    public void When_CreatingStartInfoForUnixWithoutShell_Then_CommandIsRunWithSh()
    {
        // act
        var startInfo = ShellCommand.CreateStartInfo("ls", "/home/user", false, null);

        // assert
        Assert.Equal("/bin/sh", startInfo.FileName);
    }

    [Fact]
    public async Task When_RunningCommand_Then_ItRunsInWorkingDirectory()
    {
        // arrange
        var directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            // act
            using var process = Process.Start(ShellCommand.CreateStartInfo("echo hello> \"out file.txt\"", directory))!;
            await process.WaitForExitAsync();

            // assert
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("hello", (await File.ReadAllTextAsync(Path.Combine(directory, "out file.txt"))).Trim());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
