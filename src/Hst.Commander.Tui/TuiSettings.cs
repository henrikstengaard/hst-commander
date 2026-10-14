using System.Text.Json;
using System.Text.Json.Serialization;
using Hst.Commander.Core;

namespace Hst.Commander.Tui;

/// <summary>
///     Settings for the TUI, stored in <c>tui-settings.json</c> in the <see cref="SettingsPaths.Directory" />.
/// </summary>
public sealed record TuiSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string FilePath => Path.Combine(SettingsPaths.Directory, "tui-settings.json");

    public TuiTheme Theme { get; init; } = TuiTheme.MidnightCommander;

    /// <summary>
    ///     Keys opening the volume provider list of the left and right pane, null for the default of the host operating
    ///     system (see <see cref="VolumeProviderKeyBindings.Default" />).
    /// </summary>
    public VolumeProviderKeys? VolumeProviderKeys { get; init; }

    /// <summary>Keys opening the volume provider lists, the chosen ones or the default of the host operating system.</summary>
    [JsonIgnore]
    public VolumeProviderKeys EffectiveVolumeProviderKeys => VolumeProviderKeys ?? VolumeProviderKeyBindings.Default;

    /// <summary>
    ///     Settings with the volume provider keys chosen in the settings dialog. Choosing the keys already in effect
    ///     keeps them unset, so the default of the host operating system still applies.
    /// </summary>
    public TuiSettings WithVolumeProviderKeys(VolumeProviderKeys keys) => this with
    {
        VolumeProviderKeys = keys == EffectiveVolumeProviderKeys ? VolumeProviderKeys : keys
    };

    /// <summary>Loads the settings, falling back to defaults when the file is missing or invalid.</summary>
    public static TuiSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<TuiSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new TuiSettings()
                : new TuiSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new TuiSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsPaths.Directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
