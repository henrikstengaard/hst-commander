namespace Hst.Commander.Core.Tests;

public class GivenTerminalProgress
{
    [Theory]
    [InlineData(null, null, null, false)]
    [InlineData("ON", null, null, true)]
    [InlineData(null, "WezTerm", null, true)]
    [InlineData(null, "ghostty", null, true)]
    [InlineData(null, "iTerm.app", "3.6.0", true)]
    [InlineData(null, "iTerm.app", "3.6.2beta1", true)]
    [InlineData(null, "iTerm.app", "3.5.14", false)]
    [InlineData(null, "iTerm.app", null, false)]
    [InlineData(null, "Apple_Terminal", "455", false)]
    [InlineData(null, "vscode", "1.95.0", false)]
    public void When_CheckingTheTerminal_Then_OnlyTerminalsShowingProgressAreSupported(string? conEmuAnsi,
        string? termProgram, string? termProgramVersion, bool expected)
    {
        // arrange
        var variables = new Dictionary<string, string?>
        {
            ["ConEmuANSI"] = conEmuAnsi,
            ["TERM_PROGRAM"] = termProgram,
            ["TERM_PROGRAM_VERSION"] = termProgramVersion
        };

        // act
        var supported = TerminalProgress.IsSupported(name => variables.GetValueOrDefault(name), false);

        // assert
        Assert.Equal(expected, supported);
    }

    [Fact]
    public void When_InWindowsTerminal_Then_ProgressIsSupported() =>
        Assert.True(TerminalProgress.IsSupported(_ => null, true));

    [Theory]
    [InlineData(TaskbarProgressState.None, 0.5, "\u001b]9;4;0;0\u0007")]
    [InlineData(TaskbarProgressState.Normal, 0.426, "\u001b]9;4;1;43\u0007")]
    [InlineData(TaskbarProgressState.Error, 1.2, "\u001b]9;4;2;100\u0007")]
    [InlineData(TaskbarProgressState.Indeterminate, 0.5, "\u001b]9;4;3;0\u0007")]
    [InlineData(TaskbarProgressState.Paused, -1, "\u001b]9;4;4;0\u0007")]
    public void When_MakingTheSequence_Then_StateAndPercentAreWritten(TaskbarProgressState state, double fraction,
        string expected) =>
        Assert.Equal(expected, TerminalProgress.Sequence(state, fraction));
}
