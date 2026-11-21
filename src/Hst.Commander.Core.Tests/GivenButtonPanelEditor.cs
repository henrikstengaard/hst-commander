using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenButtonPanelEditor
{
    private static readonly PanelButton Copy = new("Copy", CommanderActions.Copy);
    private static readonly PanelButton Move = new("Move", CommanderActions.Move);
    private static readonly PanelButton Delete = new("Delete", CommanderActions.Delete);
    private static readonly PanelButton View = new("Read", CommanderActions.View);

    private static ButtonPanelEditor CreateEditor() => new(new ButtonPanel(
    [
        new ButtonColumn([Copy, Move]),
        new ButtonColumn([Delete]),
        new ButtonColumn([View])
    ]));

    private static IReadOnlyList<string> Labels(ButtonPanelEditor editor, int column) =>
        editor.Columns[column].Select(button => button.Label).ToList();

    [Fact]
    public void When_ButtonIsMovedWithinColumn_Then_ItIsAtNewRow()
    {
        // arrange
        var editor = CreateEditor();

        // act
        var row = editor.MoveButton(0, 0, 0, 1);

        // assert
        Assert.Equal(1, row);
        Assert.Equal(["Move", "Copy"], Labels(editor, 0));
    }

    [Fact]
    public void When_ButtonIsMovedToOtherColumn_Then_ItIsRemovedAndInserted()
    {
        // arrange
        var editor = CreateEditor();

        // act
        var row = editor.MoveButton(0, 1, 1, 0);

        // assert
        Assert.Equal(0, row);
        Assert.Equal(["Copy"], Labels(editor, 0));
        Assert.Equal(["Move", "Delete"], Labels(editor, 1));
    }

    [Fact]
    public void When_ButtonIsMovedPastEnd_Then_ItIsAddedLast()
    {
        // arrange
        var editor = CreateEditor();

        // act
        var row = editor.MoveButton(0, 0, 2, 10);

        // assert
        Assert.Equal(1, row);
        Assert.Equal(["Read", "Copy"], Labels(editor, 2));
    }

    [Fact]
    public void When_ColumnIsMoved_Then_ColumnsAreReordered()
    {
        // arrange
        var editor = CreateEditor();

        // act
        var column = editor.MoveColumn(0, 2);

        // assert
        Assert.Equal(2, column);
        Assert.Equal(["Delete"], Labels(editor, 0));
        Assert.Equal(["Read"], Labels(editor, 1));
        Assert.Equal(["Copy", "Move"], Labels(editor, 2));
    }

    [Fact]
    public void When_ColumnAndButtonAreAdded_Then_NewButtonIsInNewColumn()
    {
        // arrange
        var editor = CreateEditor();

        // act
        var column = editor.AddColumn(1);
        var row = editor.AddButton(column, 0);

        // assert
        Assert.Equal(4, editor.Columns.Count);
        Assert.Equal(1, column);
        Assert.Equal(0, row);
        Assert.Equal([ButtonPanelEditor.NewButtonLabel], Labels(editor, 1));
    }

    [Fact]
    public void When_ButtonAndColumnAreRemoved_Then_TheyAreGone()
    {
        // arrange
        var editor = CreateEditor();

        // act
        editor.RemoveButton(0, 0);
        editor.RemoveColumn(1);

        // assert
        Assert.Equal(2, editor.Columns.Count);
        Assert.Equal(["Move"], Labels(editor, 0));
        Assert.Equal(["Read"], Labels(editor, 1));
    }

    [Fact]
    public void When_LastColumnIsRemoved_Then_AnEmptyColumnIsKept()
    {
        // arrange
        var editor = new ButtonPanelEditor(new ButtonPanel([new ButtonColumn([Copy])]));

        // act
        editor.RemoveColumn(0);

        // assert
        Assert.Empty(Assert.Single(editor.Columns));
        Assert.Empty(editor.ToPanel().Columns);
    }

    [Fact]
    public void When_ButtonIsSet_Then_ItIsReplaced()
    {
        // arrange
        var editor = CreateEditor();
        var changed = new PanelButton("Info", CommanderActions.Properties) { Background = "#AA0000", Foreground = "#FFFFFF" };

        // act
        editor.SetButton(1, 0, changed);

        // assert
        Assert.Equal(changed, editor.Columns[1][0]);
    }

    [Fact]
    public void When_ToPanel_Then_EmptyColumnsAreLeftOut()
    {
        // arrange
        var editor = CreateEditor();
        editor.AddColumn(0);

        // act
        var panel = editor.ToPanel();

        // assert
        Assert.Equal(3, panel.Columns.Count);
        Assert.Equal(2, panel.Rows);
        Assert.Equal([Copy, Move], panel.Columns[0].Buttons);
    }

    [Fact]
    public void When_Reset_Then_ColumnsOfPanelAreUsed()
    {
        // arrange
        var editor = CreateEditor();
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        // act
        editor.Reset(new ButtonPanel([new ButtonColumn([View])]));

        // assert
        Assert.Equal(1, changes);
        Assert.Equal(["Read"], Labels(editor, 0));
        Assert.Single(editor.Columns);
    }

    [Theory]
    [InlineData("", true, null)]
    [InlineData("  ", true, null)]
    [InlineData("#aabbcc", true, "#AABBCC")]
    [InlineData("AABBCC", true, "#AABBCC")]
    [InlineData("#abc", true, "#AABBCC")]
    [InlineData("#abcd", false, null)]
    [InlineData("red", false, null)]
    public void When_ColorIsParsed_Then_ItIsNormalized(string text, bool expectedResult, string? expectedColor)
    {
        // act
        var result = ButtonPanelEditor.TryParseColor(text, out var color);

        // assert
        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedColor, color);
    }
}
