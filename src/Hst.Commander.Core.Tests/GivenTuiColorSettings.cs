using System.Text.Json;
using Hst.Commander.Tui;

namespace Hst.Commander.Core.Tests;

public class GivenTuiColorSettings : IDisposable
{
    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    public void Dispose()
    {
        Directory.Delete(_directory, true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void When_OldSettingsHaveNoColorMode_Then_AutoIsUsedAndUnknownFieldsArePreserved()
    {
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, """{"Version":1,"Theme":"Dark","FutureOption":42}""");
        var settings = TuiSettings.Load(path);
        Assert.Equal(TuiColorMode.Auto, settings.ColorMode);
        (settings with { ColorMode = TuiColorMode.Colors256 }).Save(path);
        var loaded = TuiSettings.Load(path);
        Assert.Equal(TuiColorMode.Colors256, loaded.ColorMode);
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal(42, loaded.ExtensionData!["FutureOption"].GetInt32());
        Assert.Equal(1, loaded.Version);
    }

    [Theory]
    [InlineData(TuiColorMode.Auto)]
    [InlineData(TuiColorMode.Colors16)]
    [InlineData(TuiColorMode.Colors256)]
    [InlineData(TuiColorMode.TrueColor)]
    public void When_ColorModeIsSaved_Then_ItRoundTripsAsAString(TuiColorMode mode)
    {
        var path = Path.Combine(_directory, "nested", "settings.json");
        new TuiSettings { ColorMode = mode }.Save(path);
        Assert.Equal(mode, TuiSettings.Load(path).ColorMode);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(mode.ToString(), json.RootElement.GetProperty("ColorMode").GetString());
    }
}
