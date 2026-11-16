using System.Text.Json;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.DirectoryOpus;

/// <summary>
/// Theme looking like Directory Opus 4 on the Amiga: the grey, black, white and blue of the Workbench 2 palette, a bank
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
    private const string Blue = "#6688BB";

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

    public ThemeColors Colors { get; } = new(
        PaneBackground: Black,
        PaneBorder: Grey,
        Header: Grey,
        Container: White,
        Item: Grey,
        Executable: "#66BB66",
        Marked: "#EEBB00",
        Path: Grey,
        ActivePathBackground: Blue,
        ActivePathForeground: White,
        CursorBackground: Blue,
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
        Column(Button("All", CommanderActions.SelectAll), Button("None", CommanderActions.SelectNone),
            Button("Toggle", CommanderActions.InvertSelection)),
        Column(Button("Parent", CommanderActions.Parent), Button("Root", CommanderActions.Root),
            Button("Rescan", CommanderActions.Refresh)),
        Column(Button("Copy", CommanderActions.Copy, Blue, White), Button("Move", CommanderActions.Move, Blue, White),
            Button("MakeDir", CommanderActions.MakeDirectory, Blue, White)),
        Column(Button("Delete", CommanderActions.Delete, "#BB3333", White), Button("Info", CommanderActions.Properties),
            Button("Create", CommanderActions.CreateVolume)),
        Column(Button("Read", CommanderActions.View, Black, White), Button("Edit", CommanderActions.Edit, Black, White),
            Button("Swap", CommanderActions.SwapPanes)),
        Column(Button("Location", CommanderActions.Location), Button("Bookmark", CommanderActions.AddBookmark),
            Button("Config", CommanderActions.Settings))
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
