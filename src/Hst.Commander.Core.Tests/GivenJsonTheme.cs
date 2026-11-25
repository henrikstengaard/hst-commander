namespace Hst.Commander.Core.Tests;

public class GivenJsonTheme
{
    private const string DraculaJson = """
        {
          "$schema": "https://raw.githubusercontent.com/henrikstengaard/hst-commander/main/docs/theme.schema.json",
          "id": "dracula",
          "name": "Dracula",
          "light": {
            "palette": { "neutral": "#f8f8f2", "ink": "#1f1f2f", "primary": "#7c6bf5" }
          },
          "dark": {
            "palette": {
              "neutral": "#1d1e28", "ink": "#f8f8f2", "primary": "#bd93f9", "accent": "#ff79c6",
              "success": "#50fa7b", "warning": "#ffb86c", "diffAdd": "#2fb27d"
            },
            "overrides": { "syntax-comment": "#6272a4" }
          }
        }
        """;

    private static string CreateTempDirectory() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    [Fact]
    public void When_ThemeHasDarkAndLightVariants_Then_TwoThemesAreReadWithTheDarkFirst()
    {
        // act
        var themes = JsonTheme.Parse(DraculaJson, "file");

        // assert
        Assert.Equal(["dracula-dark", "dracula-light"], themes.Select(theme => theme.Id));
        Assert.Equal(["Dracula Dark", "Dracula Light"], themes.Select(theme => theme.Name));
        Assert.True(themes[0].IsDark);
        Assert.False(themes[1].IsDark);
        Assert.Equal(FunctionKeyBar.Default, themes[0].GetBottomPanel());
    }

    [Fact]
    public void When_ThemeIsReadFromPalette_Then_ColorsAreDerivedFromIt()
    {
        // act
        var colors = JsonTheme.Parse(DraculaJson, "file")[0].Colors;

        // assert
        Assert.Equal("#1D1E28", colors.PaneBackground);
        Assert.Equal("#F8F8F2", colors.Container);
        Assert.Equal("#FF79C6", colors.Header);
        Assert.Equal("#50FA7B", colors.Executable);
        Assert.Equal("#FFB86C", colors.Marked);
        Assert.Equal("#BD93F9", colors.CursorBackground);
        Assert.Equal("#BD93F9", colors.ActivePathBackground);
        // the neutral color has more contrast to the primary color than the ink
        Assert.Equal("#1D1E28", colors.CursorForeground);
        Assert.Equal(ThemeColor.Mix("#F8F8F2", "#1D1E28", 0.75), colors.Item);
        Assert.Null(colors.MarkedContainerBackground);
    }

    [Fact]
    public void When_ThemeHasOneVariant_Then_ItKeepsItsIdAndName()
    {
        // arrange
        const string json = """{ "light": { "palette": { "neutral": "#FFFFFF", "ink": "#000000" } } }""";

        // act
        var theme = Assert.Single(JsonTheme.Parse(json, "my-theme"));

        // assert
        Assert.Equal("my-theme", theme.Id);
        Assert.Equal("my-theme", theme.Name);
        Assert.False(theme.IsDark);
    }

    [Fact]
    public void When_ThemeHasOverrides_Then_TheyReplaceDerivedColorsForTheirFrontEnd()
    {
        // arrange
        const string json = """
            {
              "id": "test",
              "dark": {
                "palette": { "neutral": "#000000", "ink": "#FFFFFF", "primary": "#3366FF" },
                "overrides": {
                  "header": "#ABC",
                  "tui-header": "#F9F1A5",
                  "gui-item": "#123456",
                  "marked-item-background": "#202020",
                  "path": "var(--v2-text-muted)",
                  "container": "#FFFFFF80"
                }
              }
            }
            """;

        // act
        var theme = Assert.Single(JsonTheme.Parse(json, "file"));

        // assert
        Assert.Equal("#AABBCC", theme.Colors.Header);
        Assert.Equal("#F9F1A5", theme.TuiColors.Header);
        Assert.Equal("#123456", theme.Colors.Item);
        Assert.NotEqual("#123456", theme.TuiColors.Item);
        Assert.Equal("#202020", theme.Colors.MarkedItemBackground);
        // not a color, so the derived color
        Assert.Equal(ThemeColor.Mix("#FFFFFF", "#000000", 0.75), theme.Colors.Path);
        // half transparent white over black
        Assert.Equal("#808080", theme.Colors.Container);
    }

    [Fact]
    public void When_ThemeHasOptions_Then_TheyAreRead()
    {
        // arrange
        const string json = """
            {
              "id": "options",
              "showParentButtons": true,
              "highlightActivePathPanel": true,
              "fontFamily": "Topaz a600a1200a400",
              "fontSize": 16,
              "dark": { "systemChrome": true, "palette": {} }
            }
            """;

        // act
        var theme = Assert.Single(JsonTheme.Parse(json, "file"));

        // assert
        Assert.True(theme.ShowParentButtons);
        Assert.True(theme.HighlightActivePathPanel);
        Assert.True(theme.SystemChrome);
        Assert.Equal("Topaz a600a1200a400", theme.FontFamily);
        Assert.Equal(16, theme.FontSize);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "id": "none" }""")]
    public void When_FileIsNotATheme_Then_FormatExceptionIsThrown(string json)
    {
        Assert.Throws<FormatException>(() => JsonTheme.Parse(json, "file"));
    }

    [Fact]
    public void When_BuiltInThemesAreRead_Then_TheyKeepTheirIdsAndTuiColors()
    {
        // act
        var themes = Themes.BuiltIn;

        // assert
        Assert.Equal([Themes.MidnightCommanderId, Themes.LightId, Themes.DarkId], themes.Select(theme => theme.Id));
        Assert.All(themes, theme => Assert.True(theme.SystemChrome));
        var midnightCommander = themes[0];
        Assert.True(midnightCommander.IsDark);
        Assert.Equal("#0000AA", midnightCommander.Colors.PaneBackground);
        // the blue and cyan of the 16 colors in the TUI (TuiTrueColors)
        Assert.Equal("#0037DA", midnightCommander.TuiColors.PaneBackground);
        Assert.Equal("#3A96DD", midnightCommander.TuiColors.MenuBackground);
        Assert.Equal("#0C0C0C", midnightCommander.TuiColors.KeyBackground);
        Assert.False(themes[1].IsDark);
    }

    [Fact]
    public void When_IncludedThemesAreRead_Then_EachHasADarkAndLightThemeWithAUniqueId()
    {
        // act
        var themes = Themes.Included;

        // assert
        Assert.Equal(72, themes.Count);
        Assert.Equal(themes.Count, themes.Select(theme => theme.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(themes, theme => theme is { Id: "dracula-dark", Name: "Dracula Dark", IsDark: true });
        Assert.Contains(themes, theme => theme is { Id: "dracula-light", Name: "Dracula Light", IsDark: false });
    }

    [Fact]
    public void When_UserThemesAreListed_Then_TheyReplaceThemesWithTheirIdAndAreSortedWithTheIncludedThemes()
    {
        // arrange
        var light = Assert.Single(JsonTheme.Parse("""{ "id": "Light", "name": "My Light", "light": {} }""", "x"));
        var dracula = Assert.Single(JsonTheme.Parse("""{ "id": "dracula-dark", "name": "Dracula Dark", "dark": {} }""", "x"));
        var own = Assert.Single(JsonTheme.Parse("""{ "id": "own", "name": "Ayu Own", "dark": {} }""", "x"));

        // act
        var themes = Themes.List([], [light, dracula, own]);

        // assert
        Assert.Same(light, themes[1].Theme);
        Assert.Same(dracula, Themes.Find(themes, "dracula-dark").Theme);
        Assert.Equal(3 + 72 + 1, themes.Count);
        var names = themes.Skip(3).Select(theme => theme.Name).ToList();
        Assert.Equal(names.Order(StringComparer.OrdinalIgnoreCase), names);
        Assert.Contains("Ayu Own", names);
    }

    [Fact]
    public void When_DirectoryHasThemeFiles_Then_ValidOnesAreLoaded()
    {
        // arrange
        var directory = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "good.json"), """{ "dark": { "palette": {} } }""");
            File.WriteAllText(Path.Combine(directory, "bad.json"), "{");
            File.WriteAllText(Path.Combine(directory, "readme.txt"), "not a theme");

            // act
            var themes = Themes.LoadDirectory(directory);

            // assert
            Assert.Equal("good", Assert.Single(themes).Id);
            Assert.Empty(Themes.LoadDirectory(Path.Combine(directory, "missing")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("#a1b2c3", "#A1B2C3")]
    [InlineData("#a1b2c3ff", "#A1B2C3")]
    public void When_ColorIsNormalized_Then_ItIsUpperCaseRrggbb(string text, string expected)
    {
        Assert.True(ThemeColor.TryNormalize(text, out var color));
        Assert.Equal(expected, color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("var(--x)")]
    public void When_ColorIsInvalid_Then_ItIsNotNormalized(string? text)
    {
        Assert.False(ThemeColor.TryNormalize(text, out _));
    }
}
