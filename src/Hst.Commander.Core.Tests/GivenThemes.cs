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
        Assert.IsType<JsonTheme>(theme.Theme);
        Assert.False(theme.CenterButtonLabels);
        Assert.Equal(FunctionKeyBar.Default, theme.GetBottomPanel());
        Assert.False(theme.ShowParentButtons);
        Assert.False(theme.HighlightActivePathPanel);
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
        Assert.Equal(["MidnightCommander", "Light", "Dark", "One", "Two"], themes.Take(5).Select(theme => theme.Id));
        Assert.Equal("Test One", themes[3].Name);
        Assert.IsType<TestTheme>(themes[3].Theme);
        Assert.True(themes[3].CenterButtonLabels);
        Assert.Equal(themes[3].Theme.Colors, themes[3].TuiColors);
        Assert.Equal(CommanderActions.Copy, themes[3].GetBottomPanel().Columns[0].Buttons[0].Action);
    }

    [Fact]
    public void When_ThemesAreSortedByName_Then_UnknownThemeStillFallsBackToMidnightCommander()
    {
        // arrange
        var plugin = new TestThemePlugin(new TestTheme("aaa"));

        // act
        var themes = Themes.SortByName(Themes.List([plugin]));

        // assert
        var names = themes.Select(theme => theme.Name).ToList();
        Assert.Equal(names.Order(StringComparer.OrdinalIgnoreCase), names);
        Assert.NotEqual(Themes.MidnightCommanderId, themes[0].Id);
        Assert.Equal(Themes.MidnightCommanderId, Themes.Find(themes, "NotLoaded").Id);
    }

    [Fact]
    public void When_BuiltInPluginsAreListed_Then_DirectoryOpus4IsIncluded()
    {
        // act
        var theme = Themes.Find("DirectoryOpus4");

        // assert
        Assert.Equal("Directory Opus 4", theme.Name);
        Assert.True(theme.ShowParentButtons);
        Assert.True(theme.HighlightActivePathPanel);
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
