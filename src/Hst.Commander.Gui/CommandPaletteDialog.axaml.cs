using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     The command palette (Ctrl+Z), like opencode's: a search box filtering the commands while typing, above the
///     commands grouped under headers that can't be selected (<see cref="CommandPalette" />). The first matching command
///     is selected, Up and Down move between the commands, Enter or double-click runs the selected one and Esc cancels.
///     The dialog returns the chosen command's action, or null when cancelled.
/// </summary>
public partial class CommandPaletteDialog : Window
{
    private readonly IReadOnlyList<PaletteCommand> _commands;
    private IReadOnlyList<PaletteRow> _rows = [];
    private int _selectedIndex = -1;

    public CommandPaletteDialog() : this([])
    {
    }

    /// <param name="commands">Commands to choose from, in the order they are listed.</param>
    public CommandPaletteDialog(IReadOnlyList<PaletteCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        InitializeComponent();
        _commands = commands;
        SearchBox.TextChanged += (_, _) => ShowRows(SearchBox.Text);
        // headers can't be selected, e.g. by clicking them
        CommandsList.SelectionChanged += (_, _) =>
        {
            var index = CommandsList.SelectedIndex;
            if (index >= 0 && index < _rows.Count && !_rows[index].IsCommand)
            {
                CommandsList.SelectedIndex = _selectedIndex;
                return;
            }

            _selectedIndex = index;
        };
        CommandsList.DoubleTapped += (_, _) => Choose();
        // tunnelling, before the search box and the list handle the keys themselves
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => SearchBox.Focus();
        ShowRows(string.Empty);
    }

    /// <summary>Lists the commands matching the typed text, selecting the first command.</summary>
    private void ShowRows(string? query)
    {
        _rows = CommandPalette.Filter(_commands, query);
        _selectedIndex = -1;
        CommandsList.ItemsSource = _rows;
        Select(CommandPalette.FirstCommandIndex(_rows));
    }

    /// <summary>Up and Down move between the commands, Enter runs the selected one and Esc cancels.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
            case Key.Down:
                e.Handled = true;
                Select(CommandPalette.MoveIndex(_rows, _selectedIndex, e.Key == Key.Up ? -1 : 1));
                break;
            case Key.PageUp:
            case Key.PageDown:
                e.Handled = true;
                var index = _selectedIndex;
                // about a page of rows
                var rows = Math.Max(1, (int)(CommandsList.Bounds.Height / 28) - 1);
                for (var i = 0; i < rows; i++)
                {
                    index = CommandPalette.MoveIndex(_rows, index, e.Key == Key.PageUp ? -1 : 1);
                }

                Select(index);
                break;
            case Key.Enter:
                e.Handled = true;
                Choose();
                break;
            case Key.Escape:
                e.Handled = true;
                Close(null);
                break;
        }
    }

    /// <summary>Selects a command row and scrolls it into view, with the header of a group's first command.</summary>
    private void Select(int index)
    {
        if (index < 0 || index >= _rows.Count)
        {
            CommandsList.SelectedIndex = -1;
            return;
        }

        _selectedIndex = index;
        CommandsList.SelectedIndex = index;
        if (index > 0 && !_rows[index - 1].IsCommand)
        {
            CommandsList.ScrollIntoView(index - 1);
        }

        CommandsList.ScrollIntoView(index);
    }

    /// <summary>Closes with the selected command, staying open when no command matches.</summary>
    private void Choose()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _rows.Count || _rows[_selectedIndex].Command is not { } command)
        {
            return;
        }

        Close(command.Action);
    }
}
