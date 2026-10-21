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

    /// <summary>
    ///     Keys opening the volume provider list of the left and right pane, null for the default of the host operating
    ///     system (see <see cref="VolumeProviderKeyBindings.Default" />).
    /// </summary>
    public VolumeProviderKeys? VolumeProviderKeys { get; init; }

    /// <summary>What Enter does with files: browse images as volumes or start them with the host operating system.</summary>
    public EnterFileAction EnterFileAction { get; init; } = EnterFileAction.OpenAsVolume;

    /// <summary>Keys opening the volume provider lists, the chosen ones or the default of the host operating system.</summary>
    [JsonIgnore]
    public VolumeProviderKeys EffectiveVolumeProviderKeys => VolumeProviderKeys ?? VolumeProviderKeyBindings.Default;

    /// <summary>
    ///     Settings with the volume provider keys chosen in the settings dialog. Choosing the keys already in effect
    ///     keeps them unset, so the default of the host operating system still applies.
    /// </summary>
    public GuiSettings WithVolumeProviderKeys(VolumeProviderKeys keys) => this with
    {
        VolumeProviderKeys = keys == EffectiveVolumeProviderKeys ? VolumeProviderKeys : keys
    };

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
