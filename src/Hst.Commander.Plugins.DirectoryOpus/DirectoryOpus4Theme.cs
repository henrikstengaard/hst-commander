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

    /// <summary>
    /// Colors of the default Directory Opus 4 configuration: files in black and directories in blue on grey, the
    /// selected entry in white on black and the path of the active pane in white on red.
    /// </summary>
    public ThemeColors Colors { get; } = new(
        PaneBackground: Grey,
        PaneBorder: Black,
        Header: Black,
        Container: Blue,
        Item: Black,
        Executable: Black,
        Marked: Red,
        Path: Black,
        ActivePathBackground: Red,
        ActivePathForeground: White,
        CursorBackground: Black,
        CursorForeground: White,
        MenuBackground: Grey,
        MenuForeground: Black,
        BottomBackground: Grey,
        ButtonBackground: Grey,
        ButtonForeground: Black,
        DialogBackground: Grey,
        DialogForeground: Black);

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
                    .Select(button => new PanelButton(button.Label!, button.Action!, null, button.Background,
                        button.Foreground))
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
        string? foreground = null) => new(label, action, null, background, foreground);

    private sealed record ButtonsData(List<List<ButtonData>>? Columns);

    private sealed record ButtonData(string? Label, string? Action, string? Background, string? Foreground);
}
