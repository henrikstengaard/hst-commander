using System.Text.Json;
using System.Text.Json.Serialization;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Settings for the GUI, stored in <c>gui-settings.json</c> in the <see cref="SettingsPaths.Directory" />.
/// </summary>
public sealed record GuiSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string FilePath => Path.Combine(SettingsPaths.Directory, "gui-settings.json");

    public GuiTheme Theme { get; init; } = GuiTheme.MidnightCommander;

    /// <summary>Loads the settings, falling back to defaults when the file is missing or invalid.</summary>
    public static GuiSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new GuiSettings()
                : new GuiSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new GuiSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsPaths.Directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
