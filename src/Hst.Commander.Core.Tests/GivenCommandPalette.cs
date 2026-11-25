using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenCommandPalette
{
    private static readonly IReadOnlyList<PaletteCommand> Commands =
    [
        new("File", "View", CommanderActions.View, "F3"),
        new("File", "Copy", CommanderActions.Copy, "F5"),
        new("Mark", "Select all", CommanderActions.SelectAll, "Ctrl+A"),
        new("Go", "Swap panes", CommanderActions.SwapPanes, "Ctrl+U"),
        new("Go", "Same location in other pane", CommanderActions.SameLocation, "Ctrl+D")
    ];

    private static IReadOnlyList<string> Texts(IReadOnlyList<PaletteRow> rows) =>
        rows.Select(row => row.IsCommand ? row.Text : $"[{row.Text}]").ToList();

    [Fact]
    public void When_QueryIsEmpty_Then_AllCommandsAreListedUnderGroupHeaders()
    {
        // act
        var rows = CommandPalette.Filter(Commands, string.Empty);

        // assert
        Assert.Equal(["[File]", "View", "Copy", "[Mark]", "Select all", "[Go]", "Swap panes",
            "Same location in other pane"], Texts(rows));
    }

    [Fact]
    public void When_QueryIsTyped_Then_MatchingCommandsAreListedIgnoringCase()
    {
        // act
        var rows = CommandPalette.Filter(Commands, "COP");

        // assert
        Assert.Equal(["[File]", "Copy"], Texts(rows));
    }

    [Fact]
    public void When_QueryHasWords_Then_EveryWordMustMatch()
    {
        // act
        var rows = CommandPalette.Filter(Commands, "pane same");

        // assert
        Assert.Equal(["[Go]", "Same location in other pane"], Texts(rows));
    }

    [Fact]
    public void When_QueryMatchesGroup_Then_CommandsOfGroupAreListed()
    {
        // act
        var rows = CommandPalette.Filter(Commands, "mark");

        // assert
        Assert.Equal(["[Mark]", "Select all"], Texts(rows));
    }

    [Fact]
    public void When_NothingMatches_Then_NoRowsAndNoCommandIsSelected()
    {
        // act
        var rows = CommandPalette.Filter(Commands, "xyz");

        // assert
        Assert.Empty(rows);
        Assert.Equal(-1, CommandPalette.FirstCommandIndex(rows));
    }

    [Fact]
    public void When_RowsAreListed_Then_FirstCommandIsSelectedNotHeader()
    {
        // arrange
        var rows = CommandPalette.Filter(Commands, "a");

        // act
        var index = CommandPalette.FirstCommandIndex(rows);

        // assert
        Assert.Equal(1, index);
        Assert.True(rows[index].IsCommand);
    }

    [Fact]
    public void When_SelectionMoves_Then_HeadersAreSkipped()
    {
        // arrange
        var rows = CommandPalette.Filter(Commands, string.Empty);

        // act
        var down = CommandPalette.MoveIndex(rows, 2, 1);
        var up = CommandPalette.MoveIndex(rows, down, -1);

        // assert
        Assert.Equal("Select all", rows[down].Text);
        Assert.Equal("Copy", rows[up].Text);
    }

    [Fact]
    public void When_SelectionMovesPastEnds_Then_ItStaysAtFirstAndLastCommand()
    {
        // arrange
        var rows = CommandPalette.Filter(Commands, string.Empty);

        // act
        var first = CommandPalette.MoveIndex(rows, 1, -1);
        var last = CommandPalette.MoveIndex(rows, rows.Count - 1, 1);

        // assert
        Assert.Equal(1, first);
        Assert.Equal(rows.Count - 1, last);
    }

    [Fact]
    public void When_MenuCommandsAreListed_Then_EachRunsKnownCommandOnce()
    {
        // act
        var commands = CommandPalette.MenuCommands(true);

        // assert
        Assert.All(commands, command => Assert.Contains(CommanderActions.All, action => action.Id == command.Action));
        Assert.Equal(commands.Count, commands.Select(command => command.Action).Distinct().Count());
        Assert.DoesNotContain(CommandPalette.MenuCommands(false),
            command => command.Action == CommanderActions.RestartElevated);
    }

    [Fact]
    public void When_MenuTextHasHotKeyAndDots_Then_TheyAreRemoved()
    {
        Assert.Equal("Settings", CommandPalette.MenuText("_Settings..."));
    }
}
