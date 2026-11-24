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

    /// <summary>Version of the file format written by this version of Hst Commander.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    ///     Version of the file format the settings were read from: 0 for a file written before gui-settings.json had a
    ///     version (the same format as 1), higher than <see cref="CurrentVersion" /> for a file of a newer Hst
    ///     Commander, read as far as this version knows it. <see cref="Save" /> writes <see cref="CurrentVersion" />.
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    ///     Settings of the file this version doesn't know, e.g. added by a newer Hst Commander, written back unchanged
    ///     by <see cref="Save" /> so running an older version doesn't lose them.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }

    /// <summary>
    ///     Identifier of the theme (see <see cref="Themes" />), e.g. "MidnightCommander" or the identifier of a theme of a
    ///     plugin; the names of the earlier theme enum are the identifiers of the built-in themes.
    /// </summary>
    public string Theme { get; init; } = Themes.MidnightCommanderId;

    /// <summary>
    ///     Font family of the panes and the bottom area, null for the theme's font or the default (see
    ///     <see cref="GuiFonts" />).
    /// </summary>
    public string? FontFamily { get; init; }

    /// <summary>Font size of the panes and the bottom area, null for the theme's size or the default.</summary>
    public double? FontSize { get; init; }

    /// <summary>True to start with the main window maximized, otherwise it starts centered at its default size.</summary>
    public bool StartMaximized { get; init; }

    /// <summary>
    ///     Keys opening the volume provider list of the left and right pane, null for the default
    ///     (see <see cref="VolumeProviderKeyBindings.Default" />).
    /// </summary>
    public VolumeProviderKeys? VolumeProviderKeys { get; init; }

    /// <summary>What Enter does with files: browse images as volumes or start them with the host operating system.</summary>
    public EnterFileAction EnterFileAction { get; init; } = EnterFileAction.OpenAsVolume;

    /// <summary>
    ///     True when the panes read listed files in the background to show which can be opened as volumes (see
    ///     <see cref="NavigationContext.DetectVolumeFiles" />).
    /// </summary>
    public bool DetectVolumeFiles { get; init; } = true;

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

    /// <summary>Name of the navigation mode of the left pane (see <see cref="PaneNavigationMode" />), null for list.</summary>
    public string? LeftNavigationMode { get; init; }

    /// <summary>Name of the navigation mode of the right pane (see <see cref="PaneNavigationMode" />), null for list.</summary>
    public string? RightNavigationMode { get; init; }

    /// <summary>Order of the entries of the left pane (see <see cref="EntrySort" />), null for by name ascending.</summary>
    public EntrySort? LeftSort { get; init; }

    /// <summary>Order of the entries of the right pane (see <see cref="EntrySort" />), null for by name ascending.</summary>
    public EntrySort? RightSort { get; init; }

    /// <summary>
    ///     Where the panes were when last quit, restored on the next start (see
    ///     <see cref="StartupOptions.WithSavedPanes" />); null when never saved.
    /// </summary>
    public PaneLocations? Panes { get; init; }

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
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this with { Version = CurrentVersion }, JsonOptions));
    }
}
