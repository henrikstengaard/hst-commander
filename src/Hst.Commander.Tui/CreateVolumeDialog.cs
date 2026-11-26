using System.Collections.ObjectModel;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal list of the volumes that can be created (F9), the volume creators of the plugins. Like the command palette,
///     a search field above the list filters the creators while typing (<see cref="VolumeCreation.Filter" />),
///     selecting the first match, and Up and Down move between them. OK, Enter or double-click chooses the selected
///     volume creator, Cancel or Esc cancels. <see cref="Result" /> holds the chosen volume creator or null on cancel.
/// </summary>
public sealed class CreateVolumeDialog : TuiDialog
{
    // rows below the list taken by the OK and Cancel buttons and their shadow
    private const int ButtonsHeight = 2;

    // rows above the list taken by the search field and the empty row below it
    private const int SearchHeight = 2;

    // the OK and Cancel buttons with their shadows and spacing
    private const int ButtonsWidth = 22;

    private readonly IReadOnlyList<IVolumeCreator> _creators;
    private readonly TextField _searchField;
    private readonly ListView _listView;
    private readonly int _nameWidth;
    private IReadOnlyList<IVolumeCreator> _shown = [];

    public CreateVolumeDialog(IReadOnlyList<IVolumeCreator> creators, TuiPalette palette)
    {
        _creators = creators;
        _nameWidth = creators.Max(creator => creator.Name.Length);
        var items = CreateItems(creators);
        Title = "Create";

        // the dialog's border, padding and shadow take space around the search field, list and buttons
        var adornments = GetAdornmentsThickness();
        Width = Math.Max(Math.Max(items.Max(item => item.Length) + 2, Title.Length + 4), ButtonsWidth) +
                adornments.Horizontal;
        Height = Math.Min(creators.Count + SearchHeight + ButtonsHeight + adornments.Vertical, 22);
        SetScheme(palette.DialogColorScheme);

        _searchField = new TextField { X = 0, Y = 0, Width = Dim.Fill() };
        _listView = new ListView
        {
            X = 0,
            Y = SearchHeight,
            Width = Dim.Fill(),
            // the buttons are below the content, in the dialog's padding
            Height = Dim.Fill()
        };
        // the search field keeps the focus, so the list draws its selected item in the Active role: the dialog's
        // colors inverted
        var selected = new TuiAttribute(palette.Dialog.Background, palette.Dialog.Foreground);
        _listView.SetScheme(TuiPalette.CreateScheme(palette.Dialog, palette.DialogFocus, active: selected));
        _listView.Accepting += (_, e) =>
        {
            e.Handled = true;
            CloseWithSelected();
        };
        _listView.KeyDown += (_, key) => HandleKey(key);
        _searchField.KeyDown += (_, key) => HandleKey(key);
        _searchField.TextChanged += (_, _) => ShowCreators();

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            CloseWithSelected();
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Handled = true;
            Application.RequestStop(this);
        };

        Add(_searchField, _listView);
        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) =>
        {
            ShowCreators();
            _searchField.SetFocus();
        };
    }

    /// <summary>Chosen volume creator, null when cancelled.</summary>
    public new IVolumeCreator? Result { get; private set; }

    /// <summary>Rows of the creators: the name followed by the description, aligned for all creators.</summary>
    private List<string> CreateItems(IReadOnlyList<IVolumeCreator> creators) =>
        creators.Select(creator => $"{creator.Name.PadRight(_nameWidth)}  {creator.Description}").ToList();

    /// <summary>Lists the creators matching the typed text, selecting the first.</summary>
    private void ShowCreators()
    {
        _shown = VolumeCreation.Filter(_creators, _searchField.Text);
        _listView.SetSource(new ObservableCollection<string>(CreateItems(_shown)));
        _listView.SelectedItem = _shown.Count == 0 ? null : 0;
        _listView.EnsureSelectedItemVisible();
        _listView.SetNeedsDraw();
    }

    /// <summary>Up, Down, PgUp and PgDn move between the creators and Enter chooses the selected one.</summary>
    private void HandleKey(Key key)
    {
        if (key.KeyCode is KeyCode.CursorUp or KeyCode.CursorDown)
        {
            key.Handled = true;
            Move(key.KeyCode == KeyCode.CursorUp ? -1 : 1);
        }
        else if (key.KeyCode is KeyCode.PageUp or KeyCode.PageDown)
        {
            key.Handled = true;
            var rows = Math.Max(1, _listView.Viewport.Height - 1);
            Move(key.KeyCode == KeyCode.PageUp ? -rows : rows);
        }
        else if (key == Key.Enter)
        {
            key.Handled = true;
            CloseWithSelected();
        }
    }

    private void Move(int rows)
    {
        if (_shown.Count == 0)
        {
            return;
        }

        _listView.SelectedItem = Math.Clamp((_listView.SelectedItem ?? 0) + rows, 0, _shown.Count - 1);
        _listView.EnsureSelectedItemVisible();
        _listView.SetNeedsDraw();
    }

    /// <summary>Closes with the selected creator, staying open when no creator matches.</summary>
    private void CloseWithSelected()
    {
        if (_listView.SelectedItem is not { } index || index < 0 || index >= _shown.Count)
        {
            return;
        }

        Result = _shown[index];
        Application.RequestStop(this);
    }
}
