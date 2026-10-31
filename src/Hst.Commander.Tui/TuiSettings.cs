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

    /// <summary>What Enter does with files: browse images as volumes or start them with the host operating system.</summary>
    public EnterFileAction EnterFileAction { get; init; } = EnterFileAction.OpenAsVolume;

    /// <summary>How Amiga names, protection bits and comments are stored when copying from Amiga volumes to others.</summary>
    public UaeMetadataFormat UaeMetadata { get; init; } = UaeMetadataFormat.UaeFsDb;

    /// <summary>
    ///     Largest file in megabytes copied to a temporary file to open it, e.g. a zip archive inside a zip archive (see
    ///     <see cref="NavigationContext.TemporaryCopyLimit" />); 0 for no limit.
    /// </summary>
    public long TemporaryCopyLimit { get; init; } = NavigationContext.DefaultTemporaryCopyLimit / Megabyte;

    /// <summary><see cref="TemporaryCopyLimit" /> in bytes.</summary>
    [JsonIgnore]
    public long TemporaryCopyLimitBytes => Math.Clamp(TemporaryCopyLimit, 0, long.MaxValue / Megabyte) * Megabyte;

    private const long Megabyte = 1024 * 1024;

    /// <summary>Which editor F4 opens files in: the built-in editor, the host operating system's or a command.</summary>
    public EditorKind Editor { get; init; } = EditorKind.BuiltIn;

    /// <summary>Command of the custom editor (see <see cref="ExternalEditor.ParseCommand" />), e.g. <c>code --wait</c>.</summary>
    public string? EditorCommand { get; init; }

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
