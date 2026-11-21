namespace Hst.Commander.Core.Tests;

public class GivenShellHere
{
    [Fact]
    public void When_GettingAvailableKinds_Then_WindowsHasAllAndOthersOnlyTheTerminal()
    {
        // act
        var windows = ShellHere.GetAvailable(true);
        var others = ShellHere.GetAvailable(false);

        // assert
        Assert.Equal([ShellHereKind.PowerShell, ShellHereKind.Terminal, ShellHereKind.CommandPrompt], windows);
        Assert.Equal([ShellHereKind.Terminal], others);
    }

    [Fact]
    public void When_OpeningPowerShellOnWindows_Then_PwshIsPreferredOverWindowsPowerShell()
    {
        // act
        var withPwsh = ShellHere.CreateStartInfo(ShellHereKind.PowerShell, @"C:\work", true, false,
            name => name == "pwsh.exe" ? @"C:\Program Files\PowerShell\7\pwsh.exe" : null, null);
        var withoutPwsh = ShellHere.CreateStartInfo(ShellHereKind.PowerShell, @"C:\work", true, false,
            _ => null, null);

        // assert
        Assert.Equal(@"C:\Program Files\PowerShell\7\pwsh.exe", withPwsh.FileName);
        Assert.Equal("powershell.exe", withoutPwsh.FileName);
        Assert.Equal(@"C:\work", withPwsh.WorkingDirectory);
        Assert.True(withPwsh.UseShellExecute);
    }

    [Fact]
    public void When_OpeningCommandPromptOnWindows_Then_ComSpecOrCmdIsStarted()
    {
        // act
        var withComSpec = ShellHere.CreateStartInfo(ShellHereKind.CommandPrompt, @"C:\work", true, false,
            _ => null, @"C:\Windows\system32\cmd.exe");
        var withoutComSpec = ShellHere.CreateStartInfo(ShellHereKind.CommandPrompt, @"C:\work", true, false,
            _ => null, null);

        // assert
        Assert.Equal(@"C:\Windows\system32\cmd.exe", withComSpec.FileName);
        Assert.Equal("cmd.exe", withoutComSpec.FileName);
        Assert.Equal(@"C:\work", withoutComSpec.WorkingDirectory);
    }

    [Fact]
    public void When_OpeningTerminalOnWindows_Then_WindowsTerminalIsStartedInTheDirectory()
    {
        // act
        var startInfo = ShellHere.CreateStartInfo(ShellHereKind.Terminal, @"C:\a;b", true, false,
            name => name == "wt.exe" ? @"C:\Users\user\AppData\Local\Microsoft\WindowsApps\wt.exe" : null, null);

        // assert
        Assert.Equal(@"C:\Users\user\AppData\Local\Microsoft\WindowsApps\wt.exe", startInfo.FileName);
        Assert.Equal(["-d", @"C:\a\;b"], startInfo.ArgumentList);
    }

    [Fact]
    public void When_OpeningTerminalOnWindowsWithoutWindowsTerminal_Then_ItFails()
    {
        // act and assert
        Assert.Throws<InvalidOperationException>(() =>
            ShellHere.CreateStartInfo(ShellHereKind.Terminal, @"C:\work", true, false, _ => null, null));
    }

    [Fact]
    public void When_OpeningTerminalOnMacOs_Then_TerminalAppIsOpenedWithTheDirectory()
    {
        // act
        var startInfo = ShellHere.CreateStartInfo(ShellHereKind.Terminal, "/Users/user/work", false, true,
            _ => null, null);

        // assert
        Assert.Equal("open", startInfo.FileName);
        Assert.Equal(["-a", "Terminal", "/Users/user/work"], startInfo.ArgumentList);
    }

    [Fact]
    public void When_OpeningTerminalOnLinux_Then_TerminalVariableOrFirstTerminalFoundIsStartedInTheDirectory()
    {
        // act
        var withVariable = ShellHere.CreateStartInfo(ShellHereKind.Terminal, "/home/user", false, false,
            _ => "/usr/bin/xterm", "foot");
        var found = ShellHere.CreateStartInfo(ShellHereKind.Terminal, "/home/user", false, false,
            name => name is "konsole" or "xterm" ? $"/usr/bin/{name}" : null, null);

        // assert
        Assert.Equal("foot", withVariable.FileName);
        Assert.Equal("/usr/bin/konsole", found.FileName);
        Assert.Equal("/home/user", found.WorkingDirectory);
        Assert.False(found.UseShellExecute);
    }

    [Fact]
    public void When_OpeningPowerShellOnLinux_Then_ItIsNotAvailable()
    {
        // act and assert
        Assert.Throws<InvalidOperationException>(() =>
            ShellHere.CreateStartInfo(ShellHereKind.PowerShell, "/home/user", false, false, _ => null, null));
    }
}
