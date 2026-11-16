using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.DirectoryOpus;

namespace Hst.Commander.Core.Tests;

public class GivenDirectoryOpus4Theme : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private DirectoryOpus4Theme CreateTheme() => new() { Directory = _directory };

    [Fact]
    public void When_NoButtonsAreSaved_Then_DefaultLayoutIsUsed()
    {
        // arrange
        var theme = CreateTheme();

        // act
        var layout = theme.GetBottomPanel();

        // assert
        Assert.Same(theme.DefaultLayout, layout);
        Assert.Equal(3, layout.Rows);
    }

    [Fact]
    public void When_ButtonsAreSaved_Then_TheyAreReadByAnotherInstance()
    {
        // arrange
        var layout = new ButtonPanel(
        [
            new ButtonColumn([new PanelButton("Copy", CommanderActions.Copy, null, "#6688BB", "#FFFFFF")]),
            new ButtonColumn([
                new PanelButton("Parent", CommanderActions.Parent), new PanelButton("Root", CommanderActions.Root)
            ])
        ]);

        // act
        CreateTheme().SaveLayout(layout);
        var read = CreateTheme().Layout;

        // assert
        Assert.True(File.Exists(Path.Combine(_directory, DirectoryOpus4Theme.ButtonsFileName)));
        Assert.Equal(2, read.Columns.Count);
        Assert.Equal(layout.Columns[0].Buttons, read.Columns[0].Buttons);
        Assert.Equal(layout.Columns[1].Buttons, read.Columns[1].Buttons);
    }

    [Fact]
    public void When_ButtonsFileIsInvalid_Then_DefaultLayoutIsUsed()
    {
        // arrange
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, DirectoryOpus4Theme.ButtonsFileName), "{ not json");
        var theme = CreateTheme();

        // act
        var layout = theme.Layout;

        // assert
        Assert.Same(theme.DefaultLayout, layout);
    }

    [Fact]
    public void When_PluginIsInitialized_Then_ButtonsAreSavedInPluginDirectory()
    {
        // arrange
        var plugin = new DirectoryOpusPlugin();
        plugin.Initialize(new PluginHost(() => [], null, _directory));
        var theme = Assert.IsType<DirectoryOpus4Theme>(Assert.Single(plugin.Themes));

        // act
        theme.SaveLayout(new ButtonPanel([new ButtonColumn([new PanelButton("All", CommanderActions.SelectAll)])]));

        // assert
        Assert.True(File.Exists(Path.Combine(_directory, "directoryopus", DirectoryOpus4Theme.ButtonsFileName)));
    }

    [Fact]
    public void When_Theme_Then_TopazFontIsUsedAtTwiceItsHeight()
    {
        // act
        ITheme theme = new DirectoryOpus4Theme();

        // assert
        Assert.StartsWith("Topaz", theme.FontFamily);
        Assert.Contains("MicroKnight", theme.FontFamily);
        Assert.Equal(16, theme.FontSize);
    }

    [Fact]
    public void When_DefaultLayout_Then_EveryButtonRunsAKnownCommand()
    {
        // arrange
        var theme = CreateTheme();

        // assert
        Assert.All(theme.DefaultLayout.Columns.SelectMany(column => column.Buttons), button =>
            Assert.Contains(CommanderActions.All, action => action.Id == button.Action));
    }
}
