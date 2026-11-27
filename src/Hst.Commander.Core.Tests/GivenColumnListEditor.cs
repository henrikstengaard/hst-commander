namespace Hst.Commander.Core.Tests;

public class GivenColumnListEditor
{
    [Fact]
    public void When_Created_Then_ShownColumnsAreFirstFollowedByHiddenOnes()
    {
        var editor = new ColumnListEditor(["Modified", "Name"]);

        Assert.Equal(["Modified", "Name"], editor.ShownIds);
        Assert.Equal(["Modified", "Name"], editor.Items.Take(2).Select(item => item.Column.Id));
        Assert.All(editor.Items.Skip(2), item => Assert.False(item.IsShown));
        Assert.Contains(editor.Items, item => item.Column.Id == "Size");
        Assert.Contains(editor.Items, item => item.Column.Id == "Amiga.Attributes");
    }

    [Fact]
    public void When_ColumnIsToggled_Then_ItIsShownOrHiddenButNameStaysShown()
    {
        var editor = new ColumnListEditor(null);
        var attributesIndex = editor.Items.ToList().FindIndex(item => item.Column == EntryColumns.Attributes);
        var nameIndex = editor.Items.ToList().FindIndex(item => item.Column == EntryColumns.Name);

        Assert.True(editor.Toggle(attributesIndex));
        Assert.True(editor.Toggle(1));
        Assert.False(editor.Toggle(nameIndex));

        Assert.Equal(["Name", "Modified", "Attributes"], editor.ShownIds);
    }

    [Fact]
    public void When_ColumnIsMoved_Then_ItsPositionChangesClampedToTheList()
    {
        var editor = new ColumnListEditor(null);

        Assert.Equal(2, editor.Move(0, 2));
        Assert.Equal(["Size", "Modified", "Name"], editor.ShownIds);
        Assert.Equal(0, editor.Move(1, -5));
        Assert.Equal(["Modified", "Size", "Name"], editor.ShownIds);
        Assert.Equal(editor.Items.Count - 1, editor.Move(0, 100));
    }

    [Fact]
    public void When_Reset_Then_DefaultColumnsAreShown()
    {
        var editor = new ColumnListEditor(["Attributes", "Name"]);

        editor.Reset(null);

        Assert.Equal(EntryColumns.Default, editor.ShownIds);
    }
}
