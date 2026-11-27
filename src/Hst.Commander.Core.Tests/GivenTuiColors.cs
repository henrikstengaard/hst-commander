using System.Text;
using Hst.Commander.Abstractions;
using Hst.Commander.Tui;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace Hst.Commander.Core.Tests;

[CollectionDefinition("TUI colors", DisableParallelization = true)]
public sealed class TuiColorsCollection;

[Collection("TUI colors")]
public class GivenTuiColors : IDisposable
{
    private readonly Dictionary<ColorName16, string> _originalColors = new(Color.Colors16);
    private readonly TuiColorMode _originalMode = TuiColors.RequestedMode;

    public GivenTuiColors() => TuiTrueColors.Apply();

    public void Dispose()
    {
        Color.Colors16 = _originalColors;
        TuiColors.Apply(_originalMode);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(null, "truecolor", false, TuiColorMode.TrueColor)]
    [InlineData("xterm-256color", "24BIT", false, TuiColorMode.TrueColor)]
    [InlineData(null, null, true, TuiColorMode.TrueColor)]
    [InlineData("xterm-direct", null, false, TuiColorMode.TrueColor)]
    [InlineData("xterm-256color", null, false, TuiColorMode.Colors256)]
    [InlineData("SCREEN-256COLOR", null, false, TuiColorMode.Colors256)]
    [InlineData(null, null, false, TuiColorMode.Colors16)]
    [InlineData("xterm", null, false, TuiColorMode.Colors16)]
    [InlineData("linux", null, false, TuiColorMode.Colors16)]
    [InlineData("unknown", "yes", false, TuiColorMode.Colors16)]
    [InlineData("dumb", "truecolor", false, TuiColorMode.Colors16)]
    public void When_AutoModeIsResolved_Then_OnlyAdvertisedRichColorsAreUsed(
        string? term, string? colorTerm, bool windowsTerminal, TuiColorMode expected) =>
        Assert.Equal(expected, TuiColors.Resolve(TuiColorMode.Auto, term, colorTerm, windowsTerminal));

    [Theory]
    [InlineData(TuiColorMode.Colors16, "truecolor", TuiColorMode.Colors16)]
    [InlineData(TuiColorMode.Colors256, null, TuiColorMode.Colors256)]
    [InlineData(TuiColorMode.TrueColor, null, TuiColorMode.TrueColor)]
    public void When_ModeIsForced_Then_EnvironmentDoesNotOverrideIt(
        TuiColorMode requested, string? colorTerm, TuiColorMode expected) =>
        Assert.Equal(expected, TuiColors.Resolve(requested, "unknown", colorTerm, false));

    [Theory]
    [InlineData(TuiColorMode.Auto)]
    [InlineData(TuiColorMode.Colors256)]
    [InlineData(TuiColorMode.TrueColor)]
    public void When_ConsoleIsLegacy_Then_RichModesFallBackTo16Colors(TuiColorMode requested) =>
        Assert.Equal(TuiColorMode.Colors16,
            TuiColors.Resolve(requested, "xterm-256color", "truecolor", true, legacyConsole: true));

    [Fact]
    public void When_DriverCannotWriteIndexedColors_Then_OnlyIndexedModeIsDowngraded()
    {
        Assert.Equal(TuiColorMode.Colors16,
            TuiColors.Resolve(TuiColorMode.Auto, "xterm-256color", null, false, indexedOutput: false));
        Assert.Equal(TuiColorMode.TrueColor,
            TuiColors.Resolve(TuiColorMode.TrueColor, null, null, false, indexedOutput: false));
    }

    [Theory]
    [InlineData("#000000", 16)]
    [InlineData("#5F87AF", 67)]
    [InlineData("#FF0000", 196)]
    [InlineData("#FFFFFF", 231)]
    [InlineData("#080808", 232)]
    [InlineData("#808080", 244)]
    [InlineData("#EEEEEE", 255)]
    public void When_ColorsAreIndexed_Then_StableCubeAndGrayscaleEntriesAreUsed(string value, int expected)
    {
        Assert.Equal(expected, TuiColors.GetIndex(new Color(value)));
        Assert.Equal(expected, TuiColors.GetIndex(TuiColors.Parse(value, TuiColorMode.Colors256)));
    }

    [Fact]
    public void When_ThemeIsConverted_Then_TrueColorIsPreservedAndLimitedModesAreQuantized()
    {
        var colors = Themes.BuiltIn[0].TuiColors with { Item = "#5E86AE" };
        Assert.Equal(new Color("#5E86AE"), TuiPalette.From(colors, TuiColorMode.TrueColor).Item);
        Assert.Equal(new Color("#5F87AF"), TuiPalette.From(colors, TuiColorMode.Colors256).Item);
        Assert.Equal(new Color(new Color("#5E86AE").GetClosestNamedColor16()),
            TuiPalette.From(colors, TuiColorMode.Colors16).Item);
        Assert.Equal(new Color(ColorName16.Blue),
            TuiPalette.From(Themes.BuiltIn[0].TuiColors, TuiColorMode.Colors16).PaneBackground);
    }

    [Theory]
    [InlineData(TuiColorMode.Colors16)]
    [InlineData(TuiColorMode.Colors256)]
    public void When_MarkedCursorColorCollapsesIntoBackground_Then_CursorForegroundIsUsed(TuiColorMode mode)
    {
        var colors = Themes.BuiltIn[0].TuiColors with
        {
            MarkedCursorForeground = "#010102",
            CursorBackground = "#020101",
            CursorForeground = "#FFFFFF"
        };
        var palette = TuiPalette.From(colors, mode);
        Assert.Equal(palette.Cursor.Foreground, palette.MarkedCursor.Foreground);
        Assert.NotEqual(palette.MarkedCursor.Background, palette.MarkedCursor.Foreground);
    }

    [Theory]
    [InlineData(TuiColorMode.Colors16)]
    [InlineData(TuiColorMode.Colors256)]
    [InlineData(TuiColorMode.TrueColor)]
    public void When_ButtonsOverrideOneColor_Then_TheyUseTheThemeConversionAndKeepTheOther(TuiColorMode mode)
    {
        TuiColors.Apply(mode);
        var normal = new TuiAttribute(new Color("#FFFFFF"), new Color("#000000"));
        var button = new PanelButton("Copy", CommanderActions.Copy) { Foreground = "#5E86AE" };
        var attribute = TuiColors.ButtonAttribute(button, normal);
        Assert.Equal(TuiColors.Parse(button.Foreground, mode), attribute.Foreground);
        Assert.Equal(normal.Background, attribute.Background);
        Assert.Equal(normal, TuiColors.ButtonAttribute(button with { Foreground = null }, normal));
    }

    [Theory]
    [InlineData(TuiColorMode.Colors16, false, true)]
    [InlineData(TuiColorMode.Colors256, false, false)]
    [InlineData(TuiColorMode.TrueColor, false, false)]
    [InlineData(TuiColorMode.TrueColor, true, true)]
    public void When_ColorModeIsAppliedAgain_Then_DriverAndPalettesFollowTheRequestedMode(
        TuiColorMode requested, bool legacy, bool force16)
    {
        using var output = new InspectableOutput();
        output.IsLegacyConsole = legacy;

        TuiColors.Apply(TuiColorMode.Colors16, output);
        TuiColors.Apply(requested, output);
        TuiColors.Apply(requested, output);

        Assert.Equal(force16, output.Force16Colors);
        Assert.Equal(requested, TuiColors.RequestedMode);
        Assert.Equal(force16 ? TuiColorMode.Colors16 : requested, TuiColors.EffectiveMode);
        Assert.Equal(legacy, TuiColors.Notice != null);
        Assert.Equal(TuiColors.Parse("#5E86AE"),
            TuiPalette.From(Themes.BuiltIn[0].TuiColors with { Item = "#5E86AE" }).Item);
    }

    [Fact]
    public void When_IndexedAttributeIsWritten_Then_ItUsesIndexedSgrAndTransitionsStyles()
    {
        TuiColors.Apply(TuiColorMode.Colors256);
        using var output = new InspectableOutput();
        var result = output.Render(new TuiAttribute(new Color("#5E86AE"), new Color("#808080"), TextStyle.Italic),
            TextStyle.Bold);
        Assert.Contains("\x1b[38;5;67m", result);
        Assert.Contains("\x1b[48;5;244m", result);
        Assert.Matches("\x1b\\[(?:[0-9]+;)*22(?:;[0-9]+)*m", result);
        Assert.Matches("\x1b\\[(?:[0-9]+;)*3(?:;[0-9]+)*m", result);
        Assert.DoesNotContain("38;2;", result);
        Assert.DoesNotContain("48;2;", result);

        var reset = output.Render(new TuiAttribute(Color.None, Color.None), TextStyle.Italic);
        Assert.Contains("\x1b[39m", reset);
        Assert.Contains("\x1b[49m", reset);
        Assert.Contains("\x1b[23m", reset);
        Assert.DoesNotContain(";5;", reset);
    }

    [Fact]
    public void When_OutputModeChanges_Then_IndexedRgbAnd16ColorSequencesStayDistinct()
    {
        using var output = new InspectableOutput();
        var attribute = new TuiAttribute(new Color("#5E86AE"), new Color("#808080"));
        TuiColors.Apply(TuiColorMode.TrueColor);
        var rgb = output.Render(attribute);
        Assert.Contains("\x1b[38;2;94;134;174m", rgb);
        Assert.Contains("\x1b[48;2;128;128;128m", rgb);

        TuiColors.Apply(TuiColorMode.Colors256);
        Assert.Contains(";5;", output.Render(attribute));
        output.Force16Colors = true;
        var legacy = output.Render(attribute);
        Assert.DoesNotContain(";5;", legacy);
        Assert.DoesNotContain(";2;", legacy);
    }

    [Fact]
    public void When_DotNetOutputUsesIndexedColors_Then_ItDoesNotEmitRgbSequences()
    {
        using var output = new InspectableNetOutput();
        TuiColors.Apply(TuiColorMode.Colors256, output);
        var result = output.Render(new TuiAttribute(new Color("#FF0000"), new Color("#808080")));
        Assert.Contains("\x1b[38;5;196m", result);
        Assert.Contains("\x1b[48;5;244m", result);
        Assert.DoesNotContain(";2;", result);
    }

    [Theory]
    [InlineData(DriverRegistry.Names.ANSI)]
    [InlineData(DriverRegistry.Names.DOTNET)]
    public void When_OutputFactoriesAreRegistered_Then_DriverIdentityAndIndexedOutputArePreserved(string name)
    {
        Assert.True(DriverRegistry.TryGetDriver(DriverRegistry.Names.ANSI, out var originalAnsi));
        Assert.True(DriverRegistry.TryGetDriver(DriverRegistry.Names.DOTNET, out var originalNet));
        try
        {
            TuiColorOutput.Register();
            Assert.True(DriverRegistry.TryGetDriver(name, out var registered));
            var factory = registered!.CreateFactory();
            Assert.Equal(name, factory.GetDriverName());
            using var output = factory.CreateOutput();
            Assert.True(output is TuiAnsiOutput or TuiNetOutput);
        }
        finally
        {
            DriverRegistry.Register(originalAnsi!);
            DriverRegistry.Register(originalNet!);
        }
    }

    [Fact]
    public void When_ColorChoiceChanges_Then_SettingsPreviewReceivesBothModeAndTheme()
    {
        TuiColors.Apply(TuiColorMode.Colors16);
        var settings = new TuiSettings();
        var previewed = false;
        using var dialog = new SettingsDialog(settings, (theme, mode) =>
        {
            Assert.Equal(settings.Theme, theme.Id);
            Assert.Equal(TuiColorMode.Colors256, mode);
            TuiColors.Apply(mode);
            previewed = true;
        }, TuiPalette.For(Themes.Find(settings.Theme)));
        var selector = Descendants(dialog).OfType<ChoiceField>().Single();
        selector.SelectedIndex = 2;
        Assert.True(previewed);
        Assert.Null(dialog.Result);
        Assert.Equal(TuiPalette.For(Themes.Find(settings.Theme)).Dialog, dialog.GetScheme().Normal);
    }

    [Fact]
    public void When_SettingsFitAn80By24Terminal_Then_ThemeAndColorControlsDoNotOverlap()
    {
        using var dialog = new SettingsDialog(new TuiSettings(), (_, _) => { },
            TuiPalette.From(Themes.BuiltIn[0].TuiColors));
        dialog.BeginInit();
        dialog.EndInit();
        dialog.Layout(new System.Drawing.Size(80, 24));
        var appearance = Descendants(dialog).Single(view => view.Title == "Appearance" && view.SubViews.Count > 0);
        var themeList = Descendants(appearance).OfType<ListView>().Single();
        var selector = Descendants(appearance).OfType<ChoiceField>().Single();
        Assert.True(themeList.Frame.Height >= 3, $"Theme list height: {themeList.Frame.Height}");
        Assert.True(themeList.Frame.Bottom <= selector.Frame.Y,
            $"Theme list {themeList.Frame} overlaps color selector {selector.Frame}");
        Assert.Equal(1, selector.Frame.Height);
    }

    private static IEnumerable<View> Descendants(View view) =>
        view.SubViews.SelectMany(child => new[] { child }.Concat(Descendants(child)));

    private sealed class InspectableOutput : TuiAnsiOutput
    {
        public string Render(TuiAttribute attribute, TextStyle previousStyle = TextStyle.None)
        {
            var builder = new StringBuilder();
            AppendOrWriteAttribute(builder, attribute, previousStyle);
            return builder.ToString();
        }
    }

    private sealed class InspectableNetOutput : TuiNetOutput
    {
        public string Render(TuiAttribute attribute)
        {
            var builder = new StringBuilder();
            AppendOrWriteAttribute(builder, attribute, TextStyle.None);
            return builder.ToString();
        }
    }
}
