using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenThemes
{
    private sealed class TestTheme(string id) : ITheme
    {
        public string Id => id;
        public string Name => $"Test {id}";
        public bool IsDark => true;

        public ThemeColors Colors { get; } = new("#000000", "#111111", "#222222", "#333333", "#444444", "#555555",
            "#666666", "#777777", "#888888", "#999999", "#AAAAAA", "#BBBBBB", "#CCCCCC", "#DDDDDD", "#EEEEEE",
            "#FFFFFF", "#000000", "#111111", "#222222");

        public ButtonPanel GetBottomPanel() => new([new ButtonColumn([new PanelButton("Copy", CommanderActions.Copy)])]);
    }

    private sealed class TestThemePlugin(params ITheme[] themes) : IPlugin
    {
        public string Name => "TestThemes";
        public IEnumerable<ITheme> Themes => themes;
    }

    [Theory]
    [InlineData("MidnightCommander")]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void When_ThemeOfEarlierSettingsIsFound_Then_BuiltInThemeIsReturned(string id)
    {
        // act
        var theme = Themes.Find(id);

        // assert
        Assert.Equal(id, theme.Id);
        Assert.Null(theme.Plugin);
        Assert.Equal(FunctionKeyBar.Default, theme.GetBottomPanel());
        Assert.False(theme.ShowParentButtons);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("NotLoaded")]
    public void When_UnknownThemeIsFound_Then_MidnightCommanderIsReturned(string? id)
    {
        // act
        var theme = Themes.Find(id);

        // assert
        Assert.Equal(Themes.MidnightCommanderId, theme.Id);
    }

    [Fact]
    public void When_PluginsHaveThemes_Then_TheyAreListedAfterBuiltInThemes()
    {
        // arrange
        var plugin = new TestThemePlugin(new TestTheme("One"), new TestTheme("Dark"), new TestTheme("one"),
            new TestTheme("Two"));

        // act
        var themes = Themes.List([plugin]);

        // assert
        Assert.Equal(["MidnightCommander", "Light", "Dark", "One", "Two"], themes.Select(theme => theme.Id));
        Assert.Equal("Test One", themes[3].Name);
        Assert.NotNull(themes[3].Plugin);
        Assert.Equal(CommanderActions.Copy, themes[3].GetBottomPanel().Columns[0].Buttons[0].Action);
    }

    [Fact]
    public void When_BuiltInPluginsAreListed_Then_DirectoryOpus4IsIncluded()
    {
        // act
        var theme = Themes.Find("DirectoryOpus4");

        // assert
        Assert.Equal("Directory Opus 4", theme.Name);
        Assert.True(theme.ShowParentButtons);
        Assert.NotNull(theme.CustomizableButtons);
    }

    [Fact]
    public void When_FunctionKeyBar_Then_TenColumnsHaveKeysF1ToF10()
    {
        // act
        var panel = FunctionKeyBar.Default;

        // assert
        Assert.Equal(10, panel.Columns.Count);
        Assert.Equal(1, panel.Rows);
        Assert.Equal(Enumerable.Range(1, 10).Select(number => $"F{number}"),
            panel.Columns.Select(column => Assert.Single(column.Buttons).Key));
        Assert.All(panel.Columns, column =>
            Assert.Contains(CommanderActions.All, action => action.Id == column.Buttons[0].Action));
    }
}
