using System.Collections.ObjectModel;
using Hst.Commander.Core;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     The command palette (Ctrl+Z), like opencode's: a search field filtering the commands while typing, above the
///     commands grouped under headers that can't be selected (<see cref="CommandPalette" />). The first matching command
///     is selected, Up and Down move between the commands, Enter or double-click runs the selected one and Esc cancels.
///     <see cref="Result" /> holds the chosen command's action, null when cancelled.
/// </summary>
public sealed class CommandPaletteDialog : TuiDialog
{
    private const int MaximumWidth = 64;
    private const int MaximumHeight = 26;

    private readonly IReadOnlyList<PaletteCommand> _commands;
    private readonly ListView _listView;
    private IReadOnlyList<PaletteRow> _rows = [];
    private int _listWidth;

    public CommandPaletteDialog(IReadOnlyList<PaletteCommand> commands, TuiPalette palette)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(palette);
        _commands = commands;
        Title = "Commands";
        var width = Math.Max(Math.Min(MaximumWidth, Application.Screen.Width - 2), 30);
        var height = Math.Max(Math.Min(MaximumHeight, Application.Screen.Height - 2), 8);
        X = Pos.Center();
        Y = Math.Max(0, (Application.Screen.Height - height) / 3);
        Width = width;
        Height = height;
        SetScheme(palette.DialogColorScheme);

        var searchField = new TextField { X = 0, Y = 0, Width = Dim.Fill() };
        _listView = new ListView { X = 0, Y = 2, Width = Dim.Fill(), Height = Dim.Fill() };
        // the search field keeps the focus, so the list draws its selected item in the Active role: the dialog's
        // colors inverted
        var selected = new TuiAttribute(palette.Dialog.Background, palette.Dialog.Foreground);
        _listView.SetScheme(TuiPalette.CreateScheme(palette.Dialog, palette.DialogFocus, active: selected));
        var header = new TuiAttribute(palette.Dialog.Foreground, palette.Dialog.Background, TextStyle.Bold);
        _listView.RowRender += (_, e) =>
        {
            if (e.Row >= 0 && e.Row < _rows.Count && !_rows[e.Row].IsCommand)
            {
                e.RowAttribute = header;
            }
        };
        // headers can't be selected, e.g. by clicking them
        _listView.ValueChanging += (_, e) =>
        {
            if (e.NewValue is { } index && index >= 0 && index < _rows.Count && !_rows[index].IsCommand)
            {
                e.Handled = true;
            }
        };
        _listView.Accepting += (_, e) =>
        {
            e.Handled = true;
            Choose();
        };
        _listView.KeyDown += (_, key) => HandleKey(key);
        searchField.KeyDown += (_, key) => HandleKey(key);
        searchField.TextChanged += (_, _) => ShowRows(searchField.Text);
        _listView.FrameChanged += (_, _) =>
        {
            if (_listView.Viewport.Width != _listWidth)
            {
                ShowRows(searchField.Text);
            }
        };

        Add(searchField, _listView);
        Initialized += (_, _) =>
        {
            ShowRows(string.Empty);
            searchField.SetFocus();
        };
    }

    /// <summary>The action of the chosen command, null when cancelled.</summary>
    public new string? Result { get; private set; }

    /// <summary>Lists the commands matching the typed text, selecting the first command.</summary>
    private void ShowRows(string query)
    {
        _rows = CommandPalette.Filter(_commands, query);
        _listWidth = _listView.Viewport.Width;
        var width = Math.Max(_listWidth, 20);
        _listView.SetSource(new ObservableCollection<string>(_rows.Select(row => FormatRow(row, width))));
        var index = CommandPalette.FirstCommandIndex(_rows);
        _listView.SelectedItem = index < 0 ? null : index;
        _listView.EnsureSelectedItemVisible();
        _listView.SetNeedsDraw();
    }

    /// <summary>A header as is, a command indented with its shortcut at the right.</summary>
    private static string FormatRow(PaletteRow row, int width)
    {
        if (row.Command is not { } command)
        {
            return row.Text;
        }

        var name = $"  {command.Name}";
        var shortcut = command.Shortcut ?? string.Empty;
        var padding = Math.Max(1, width - name.Length - shortcut.Length - 1);
        return $"{name}{new string(' ', padding)}{shortcut}";
    }

    /// <summary>Up and Down move between the commands, Enter runs the selected one and Esc cancels.</summary>
    private void HandleKey(Key key)
    {
        if (key.KeyCode is KeyCode.CursorUp or KeyCode.CursorDown)
        {
            var direction = key.KeyCode == KeyCode.CursorUp ? -1 : 1;
            key.Handled = true;
            Move(direction);
        }
        else if (key.KeyCode is KeyCode.PageUp or KeyCode.PageDown)
        {
            var direction = key.KeyCode == KeyCode.PageUp ? -1 : 1;
            key.Handled = true;
            for (var i = 0; i < Math.Max(1, _listView.Viewport.Height - 1); i++)
            {
                Move(direction);
            }
        }
        else if (key.KeyCode == KeyCode.Enter)
        {
            key.Handled = true;
            Choose();
        }
        else if (key.KeyCode == KeyCode.Esc)
        {
            key.Handled = true;
            Application.RequestStop(this);
        }
    }

    private void Move(int direction)
    {
        var index = CommandPalette.MoveIndex(_rows, _listView.SelectedItem ?? -1, direction);
        if (index < 0)
        {
            return;
        }

        _listView.SelectedItem = index;
        _listView.EnsureSelectedItemVisible();
        // the header of a group's first command is shown with it
        if (index > 0 && !_rows[index - 1].IsCommand && _listView.Viewport.Y > index - 1)
        {
            _listView.Viewport = _listView.Viewport with { Y = index - 1 };
        }

        _listView.SetNeedsDraw();
    }

    /// <summary>Closes with the selected command, staying open when no command matches.</summary>
    private void Choose()
    {
        if (_listView.SelectedItem is not { } index || index < 0 || index >= _rows.Count ||
            _rows[index].Command is not { } command)
        {
            return;
        }

        Result = command.Action;
        Application.RequestStop(this);
    }
}
