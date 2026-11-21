using System.Text.Json;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.DirectoryOpus;

/// <summary>
/// Theme looking like Directory Opus 4 on the Amiga: the eight colors of its default configuration, a bank
/// of colored buttons in columns below the panes and parent buttons at the outer side of the panes. The user changes
/// the buttons in the settings; they are saved in "buttons.json" in the plugin's directory, shared by the TUI and GUI.
/// </summary>
public sealed class DirectoryOpus4Theme : ITheme, ICustomizableButtonPanel
{
    /// <summary>Name of the file in the plugin's directory holding the buttons changed by the user.</summary>
    public const string ButtonsFileName = "buttons.json";

    private const string Grey = "#AAAAAA";
    private const string Black = "#000000";
    private const string White = "#FFFFFF";
    private const string Blue = "#0055BB";
    private const string Red = "#CC2200";
    private const string Purple = "#770077";
    private const string Orange = "#EEAA44";
    private const string Yellow = "#FFFF00";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private ButtonPanel? _layout;

    /// <summary>
    /// Directory holding <see cref="ButtonsFileName" />, the plugin's directory set when the plugin is initialized; null
    /// when there is none, so the buttons changed by the user are only kept while running.
    /// </summary>
    public string? Directory { get; set; }

    public string Id => "DirectoryOpus4";

    public string Name => "Directory Opus 4";

    public bool IsDark => false;

    public bool ShowParentButtons => true;

    /// <summary>The whole panel above the active pane is white on red, not only its path.</summary>
    public bool HighlightActivePathPanel => true;

    /// <summary>
    /// TrueType versions of Topaz, the Amiga font Directory Opus 4 used, and MicroKnight, used in the GUI when the user
    /// has installed or downloaded one of them (see <see cref="FontDownloads" />). The family names are those of the
    /// fonts of https://github.com/rewtnull/amigafonts as stored in the font files (Topaz of Kickstart 2.x is
    /// "a600a1200a400", though its readme says "a4000").
    /// </summary>
    public string FontFamily =>
        "Topaz a600a1200a400, Topaz a500a1000a2000, TopazPlus a600a1200a4000, TopazPlus a500a1000a2000, Topaz Unicode, Topaz, MicroKnight";

    /// <summary>Twice the 8 pixel height of Topaz, so it stays sharp.</summary>
    public double? FontSize => 16;

    /// <summary>Commit of https://github.com/rewtnull/amigafonts the fonts are downloaded from (v1.02, 2020-01-17).</summary>
    private const string AmigaFontsCommit = "d42987535d94ebbfb51e64fee76f9974c6ae145a";

    private const string AmigaFontsUrl = "https://github.com/rewtnull/amigafonts";

    private const string AmigaFontsRawUrl = "https://raw.githubusercontent.com/rewtnull/amigafonts/" + AmigaFontsCommit;

    private const string AmigaFontsLicense =
        "GPL with font exception. The TrueType version is \u00a9 dMG/t!s^dS!; Topaz is \u00a9 AmigaInc.";

    /// <summary>
    /// Topaz of Kickstart 2.x, downloaded by the user's choice from the amigafonts project, with its readme holding the
    /// license and credits. The fonts aren't included with Hst Commander because of their licenses.
    /// </summary>
    public IReadOnlyList<ThemeFontDownload> FontDownloads { get; } =
    [
        new("Topaz_a1200_v1.0.ttf", AmigaFontsRawUrl + "/ttf/Topaz_a1200_v1.0.ttf",
            "da95bfdd6d16ac3602f64ae01699808efede94665fa2a7ac5f33e61641a67e14", "Topaz (Kickstart 2.x)",
            AmigaFontsLicense, AmigaFontsUrl),
        new("amigafonts-README.txt", AmigaFontsRawUrl + "/README",
            "eb2a92ecf5d371c786a6505aa8f6784b3f55faa2c8d9b323f45ead4124edccae", "Readme with the license and credits",
            AmigaFontsLicense, AmigaFontsUrl)
    ];

    /// <summary>
    /// Colors of the default Directory Opus 4 configuration: files in black and directories in blue on grey, marked
    /// (selected) directories in white on blue, marked files in white on black and the path of the active pane in white
    /// on red. Directory Opus 4 has no cursor; the entry under the cursor is black on orange.
    /// </summary>
    public ThemeColors Colors { get; } = new(
        PaneBackground: Grey,
        PaneBorder: Black,
        Header: Black,
        Container: Blue,
        Item: Black,
        Executable: Black,
        Marked: White,
        Path: Black,
        ActivePathBackground: Red,
        ActivePathForeground: White,
        CursorBackground: Orange,
        CursorForeground: Black,
        MenuBackground: Grey,
        MenuForeground: Black,
        BottomBackground: Grey,
        ButtonBackground: Grey,
        ButtonForeground: Black,
        DialogBackground: Grey,
        DialogForeground: Black)
    {
        MarkedContainerBackground = Blue,
        MarkedItemBackground = Black
    };

    public string SettingsTitle => "Buttons";

    /// <summary>
    /// Bank of buttons like the one of Directory Opus 4, with three buttons in each column and the commands of Hst
    /// Commander.
    /// </summary>
    public ButtonPanel DefaultLayout { get; } = new(
    [
        Column(Button("All", CommanderActions.SelectAll, Blue, Yellow),
            Button("None", CommanderActions.SelectNone, Blue, Yellow),
            Button("Toggle", CommanderActions.InvertSelection, Blue, Yellow)),
        Column(Button("Parent", CommanderActions.Parent, Blue, Yellow),
            Button("Root", CommanderActions.Root, Blue, Yellow),
            Button("Rescan", CommanderActions.Refresh, Blue, Yellow)),
        Column(Button("Copy", CommanderActions.Copy, Purple, Yellow),
            Button("Move", CommanderActions.Move, Purple, Yellow),
            Button("MakeDir", CommanderActions.MakeDirectory, Orange, Purple)),
        Column(Button("Delete", CommanderActions.Delete, Red, White),
            Button("Info", CommanderActions.Properties, Orange, Purple),
            Button("Create", CommanderActions.CreateVolume, Orange, Purple)),
        Column(Button("Read", CommanderActions.View, Red, Yellow),
            Button("Edit", CommanderActions.Edit, Red, Yellow),
            Button("Swap", CommanderActions.SwapPanes)),
        Column(Button("Location", CommanderActions.Location, foreground: Purple),
            Button("Bookmark", CommanderActions.AddBookmark, foreground: Purple),
            Button("Config", CommanderActions.Settings, Black, Orange))
    ]);

    public ButtonPanel Layout => _layout ??= Load() ?? DefaultLayout;

    public ButtonPanel GetBottomPanel() => Layout;

    public void SaveLayout(ButtonPanel layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layout = layout;
        if (Directory == null)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(Directory);
        var data = new ButtonsData(layout.Columns
            .Select(column => column.Buttons
                .Select(button => new ButtonData(button.Label, button.Action, button.Background, button.Foreground))
                .ToList())
            .ToList());
        File.WriteAllText(Path.Combine(Directory, ButtonsFileName), JsonSerializer.Serialize(data, JsonOptions));
    }

    /// <summary>The buttons saved by the user, null when there are none or they can't be read.</summary>
    private ButtonPanel? Load()
    {
        if (Directory == null)
        {
            return null;
        }

        try
        {
            var path = Path.Combine(Directory, ButtonsFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var data = JsonSerializer.Deserialize<ButtonsData>(File.ReadAllText(path), JsonOptions);
            if (data?.Columns == null)
            {
                return null;
            }

            var columns = data.Columns
                .Where(column => column != null)
                .Select(column => new ButtonColumn(column
                    .Where(button => button is { Label: not null, Action: not null })
                    .Select(button => new PanelButton(button.Label!, button.Action!)
                    {
                        Background = button.Background,
                        Foreground = button.Foreground
                    })
                    .ToList()))
                .Where(column => column.Buttons.Count > 0)
                .ToList();
            return columns.Count == 0 ? null : new ButtonPanel(columns);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static ButtonColumn Column(params PanelButton[] buttons) => new(buttons);

    private static PanelButton Button(string label, string action, string? background = null,
        string? foreground = null) => new(label, action) { Background = background, Foreground = foreground };

    private sealed record ButtonsData(List<List<ButtonData>>? Columns);

    private sealed record ButtonData(string? Label, string? Action, string? Background, string? Foreground);
}
