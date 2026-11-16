using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Editor of the buttons of a theme in the settings (see <see cref="ICustomizableButtonPanel" />): the columns side
///     by side with their buttons from top to bottom. Buttons are dragged to other places in their column or to another
///     column, and columns are dragged by their header to other places. The toolbar adds and removes columns and
///     buttons, and the fields below change the label, command and colors of the selected button. Changes are made with
///     a <see cref="ButtonPanelEditor" />, shared with the TUI.
/// </summary>
public sealed class ButtonPanelEditorView : UserControl
{
    private const double ColumnWidth = 116;
    private const double DragThreshold = 4;

    private static readonly DataFormat<EditorDrag> EditorDragFormat =
        DataFormat.CreateInProcessFormat<EditorDrag>("hst-commander-button-editor-drag");

    private readonly ButtonPanel _defaultLayout;
    private readonly StackPanel _columnsPanel = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    private readonly ButtonPanelView _preview = new() { CenterLabels = true, IsHitTestVisible = false };
    private readonly TextBox _labelTextBox = new();
    private readonly ComboBox _actionComboBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _backgroundTextBox = new() { PlaceholderText = "theme", Width = 90 };
    private readonly TextBox _foregroundTextBox = new() { PlaceholderText = "theme", Width = 90 };
    private readonly Border _backgroundSwatch = CreateSwatch();
    private readonly Border _foregroundSwatch = CreateSwatch();
    private readonly Grid _fields;
    private readonly Button _removeColumnButton;
    private readonly Button _addButtonButton;
    private readonly Button _removeButtonButton;

    private int _selectedColumn;
    private int? _selectedRow;
    private bool _updatingFields;
    private PointerPressedEventArgs? _dragPressed;
    private EditorDrag? _dragCandidate;
    private Point _dragStart;
    private int? _dropColumn;

    public ButtonPanelEditorView(ButtonPanel layout, ButtonPanel defaultLayout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _defaultLayout = defaultLayout ?? throw new ArgumentNullException(nameof(defaultLayout));
        Editor = new ButtonPanelEditor(layout);

        var addColumnButton = ToolbarButton("Add column", "Adds a column right of the selected column", () =>
        {
            _selectedColumn = Editor.AddColumn(_selectedColumn + 1);
            _selectedRow = null;
        });
        _removeColumnButton = ToolbarButton("Remove column", "Removes the selected column with its buttons", () =>
        {
            Editor.RemoveColumn(_selectedColumn);
            _selectedColumn = Math.Min(_selectedColumn, Editor.Columns.Count - 1);
            _selectedRow = null;
        });
        _addButtonButton = ToolbarButton("Add button", "Adds a button below the selected button", () =>
        {
            var row = _selectedRow + 1 ?? Editor.Columns[_selectedColumn].Count;
            _selectedRow = Editor.AddButton(_selectedColumn, row);
        });
        _removeButtonButton = ToolbarButton("Remove button", "Removes the selected button", () =>
        {
            if (_selectedRow is not { } row)
            {
                return;
            }

            Editor.RemoveButton(_selectedColumn, row);
            var count = Editor.Columns[_selectedColumn].Count;
            _selectedRow = count == 0 ? null : Math.Min(row, count - 1);
        });
        var resetButton = ToolbarButton("Reset", "Restores the buttons of the theme", () =>
        {
            Editor.Reset(_defaultLayout);
            _selectedColumn = 0;
            _selectedRow = null;
        });
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var button in new[] { addColumnButton, _removeColumnButton, _addButtonButton, _removeButtonButton, resetButton })
        {
            button.Margin = new Thickness(0, 0, 6, 4);
            toolbar.Children.Add(button);
        }

        var help = new TextBlock
        {
            Text = "Drag buttons within or between columns, and columns by their header. Click a button to change it.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 6)
        };

        _actionComboBox.ItemsSource = CommanderActions.All.Select(action => action.Description).ToList();
        _fields = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto,Auto,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnSpacing = 6, RowSpacing = 6, Margin = new Thickness(0, 8, 0, 0)
        };
        AddField(new TextBlock { Text = "Label:" }, 0, 0);
        AddField(_labelTextBox, 0, 1);
        AddField(new TextBlock { Text = "Background:" }, 0, 2);
        AddField(_backgroundTextBox, 0, 3);
        AddField(_backgroundSwatch, 0, 4);
        AddField(new TextBlock { Text = "Text:" }, 0, 5);
        AddField(_foregroundTextBox, 0, 6);
        AddField(_foregroundSwatch, 0, 7);
        AddField(new TextBlock { Text = "Command:" }, 1, 0);
        AddField(_actionComboBox, 1, 1);
        Grid.SetColumnSpan(_actionComboBox, 7);
        ToolTip.SetTip(_backgroundTextBox, "Color as #RRGGBB, empty for the theme's color");
        ToolTip.SetTip(_foregroundTextBox, "Color as #RRGGBB, empty for the theme's color");
        _labelTextBox.TextChanged += (_, _) => UpdateSelectedButton();
        _backgroundTextBox.TextChanged += (_, _) => UpdateSelectedButton();
        _foregroundTextBox.TextChanged += (_, _) => UpdateSelectedButton();
        _actionComboBox.SelectionChanged += (_, _) => UpdateSelectedButton();

        var columnsScrollViewer = new ScrollViewer
        {
            Content = _columnsPanel,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        DragDrop.SetAllowDrop(_columnsPanel, true);
        _columnsPanel.Background = Brushes.Transparent;
        DragDrop.AddDragEnterHandler(_columnsPanel, OnDragOver);
        DragDrop.AddDragOverHandler(_columnsPanel, OnDragOver);
        DragDrop.AddDragLeaveHandler(_columnsPanel, (_, _) => ShowDropColumn(null));
        DragDrop.AddDropHandler(_columnsPanel, OnDrop);

        var previewLabel = new TextBlock { Text = "Preview:", Opacity = 0.7, Margin = new Thickness(0, 8, 0, 2) };
        var bottom = new StackPanel { Children = { _fields, previewLabel, _preview } };
        var root = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(help, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(help);
        root.Children.Add(bottom);
        root.Children.Add(columnsScrollViewer);
        Content = root;

        Editor.Changed += (_, _) => Rebuild();
        Rebuild();
    }

    /// <summary>The buttons as changed by the user, see <see cref="ButtonPanelEditor.ToPanel" />.</summary>
    public ButtonPanelEditor Editor { get; }

    private void AddField(Control control, int row, int column)
    {
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        _fields.Children.Add(control);
    }

    private Button ToolbarButton(string text, string tip, Action action)
    {
        var button = new Button { Content = text };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    private static Border CreateSwatch() => new()
    {
        Width = 18, Height = 18, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray
    };

    /// <summary>Draws the columns and buttons again and updates the toolbar, fields and preview.</summary>
    private void Rebuild()
    {
        _selectedColumn = Math.Clamp(_selectedColumn, 0, Editor.Columns.Count - 1);
        if (_selectedRow is { } selectedRow && selectedRow >= Editor.Columns[_selectedColumn].Count)
        {
            _selectedRow = null;
        }

        _columnsPanel.Children.Clear();
        for (var column = 0; column < Editor.Columns.Count; column++)
        {
            _columnsPanel.Children.Add(CreateColumn(column));
        }

        _removeButtonButton.IsEnabled = _selectedRow != null;
        _removeColumnButton.IsEnabled = Editor.Columns.Count > 1 || Editor.Columns[0].Count > 0;
        _addButtonButton.IsEnabled = Editor.Columns.Count > 0;
        _preview.Panel = Editor.ToPanel();
        UpdateFields();
    }

    private Border CreateColumn(int column)
    {
        var isSelected = column == _selectedColumn;
        var headerText = new TextBlock { Text = $"Column {column + 1}", FontWeight = FontWeight.SemiBold };
        if (isSelected)
        {
            headerText.Foreground = Brushes.White;
        }

        var header = new Border
        {
            Padding = new Thickness(4, 2), Margin = new Thickness(0, 0, 0, 4), Cursor = new Cursor(StandardCursorType.SizeAll),
            Background = isSelected ? AccentBrush() : Brushes.Transparent,
            Child = headerText
        };
        ToolTip.SetTip(header, "Drag to move the column");
        header.PointerPressed += (_, e) => OnPointerPressed(e, new EditorDrag(column, null));
        header.PointerMoved += OnPointerMoved;
        header.PointerReleased += OnPointerReleased;

        var buttons = new StackPanel { Spacing = 3 };
        buttons.Children.Add(header);
        var columnButtons = Editor.Columns[column];
        for (var row = 0; row < columnButtons.Count; row++)
        {
            buttons.Children.Add(CreateTile(column, row, columnButtons[row]));
        }

        var border = new Border
        {
            Width = ColumnWidth, Padding = new Thickness(4), MinHeight = 140,
            BorderThickness = new Thickness(isSelected || column == _dropColumn ? 2 : 1),
            BorderBrush = column == _dropColumn || isSelected ? AccentBrush() : Brushes.Gray,
            Background = Brushes.Transparent,
            Child = buttons, Tag = column
        };
        border.PointerPressed += (_, e) =>
        {
            if (e.Handled)
            {
                return;
            }

            _selectedColumn = column;
            _selectedRow = null;
            Rebuild();
        };
        return border;
    }

    private Border CreateTile(int column, int row, PanelButton button)
    {
        var isSelected = column == _selectedColumn && row == _selectedRow;
        var label = new TextBlock
        {
            Text = button.Label, HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var tile = new Border
        {
            Padding = new Thickness(4, 3), Cursor = new Cursor(StandardCursorType.Hand),
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            BorderBrush = isSelected ? AccentBrush() : Brushes.DimGray,
            Child = label
        };
        if (button.Background != null)
        {
            tile.Background = ThemeManager.Brush(button.Background);
        }
        else
        {
            tile.Bind(Border.BackgroundProperty, tile.GetResourceObservable("ButtonBackground"));
        }

        if (button.Foreground != null)
        {
            label.Foreground = ThemeManager.Brush(button.Foreground);
        }
        else
        {
            label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("ButtonForeground"));
        }

        ToolTip.SetTip(tile, CommanderActions.GetDescription(button.Action));
        tile.PointerPressed += (_, e) => OnPointerPressed(e, new EditorDrag(column, row));
        tile.PointerMoved += OnPointerMoved;
        tile.PointerReleased += OnPointerReleased;
        return tile;
    }

    private IBrush AccentBrush() =>
        this.TryFindResource("SystemAccentColor", ActualThemeVariant, out var accent) && accent is Color color
            ? new SolidColorBrush(color)
            : Brushes.SteelBlue;

    private void OnPointerPressed(PointerPressedEventArgs e, EditorDrag drag)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;
        _dragPressed = e;
        _dragCandidate = drag;
        _dragStart = e.GetPosition(this);
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragPressed is not { } pressed || _dragCandidate is not { } drag)
        {
            return;
        }

        var delta = e.GetPosition(this) - _dragStart;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
        {
            return;
        }

        _dragPressed = null;
        _dragCandidate = null;
        // selected without drawing the columns again, which would remove the element being dragged
        _selectedColumn = drag.Column;
        _selectedRow = drag.Row;
        var item = new DataTransferItem();
        item.Set(EditorDragFormat, drag);
        var dataTransfer = new DataTransfer();
        dataTransfer.Add(item);
        await DragDrop.DoDragDropAsync(pressed, dataTransfer, DragDropEffects.Move);
        _dropColumn = null;
        Rebuild();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragCandidate is { } drag)
        {
            Select(drag);
        }

        _dragPressed = null;
        _dragCandidate = null;
    }

    private void Select(EditorDrag drag)
    {
        _selectedColumn = drag.Column;
        _selectedRow = drag.Row;
        Rebuild();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var drag = e.DataTransfer.TryGetValue(EditorDragFormat);
        if (drag == null)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Move;
        ShowDropColumn(drag.Row == null ? null : GetDropColumn(e, false));
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var drag = e.DataTransfer.TryGetValue(EditorDragFormat);
        _dropColumn = null;
        if (drag == null)
        {
            Rebuild();
            return;
        }

        if (drag.Row is not { } row)
        {
            // a column dropped before the column under the pointer, or after it in its right half
            var to = GetDropColumn(e, true);
            if (to > drag.Column)
            {
                to--;
            }

            _selectedColumn = Editor.MoveColumn(drag.Column, to);
            _selectedRow = null;
            Rebuild();
            return;
        }

        var column = GetDropColumn(e, false);
        var toRow = GetDropRow(e, column);
        if (column == drag.Column && toRow > row)
        {
            toRow--;
        }

        _selectedColumn = column;
        _selectedRow = Editor.MoveButton(drag.Column, row, column, toRow);
        Rebuild();
    }

    /// <summary>
    ///     The column under the pointer, the nearest at the sides. With <paramref name="insert" />, the index a column
    ///     is inserted at: after the column when the pointer is in its right half.
    /// </summary>
    private int GetDropColumn(DragEventArgs e, bool insert)
    {
        var x = e.GetPosition(_columnsPanel).X;
        for (var index = 0; index < _columnsPanel.Children.Count; index++)
        {
            var bounds = _columnsPanel.Children[index].Bounds;
            if (x < bounds.Right + _columnsPanel.Spacing / 2)
            {
                return insert && x > bounds.Center.X ? index + 1 : index;
            }
        }

        return insert ? _columnsPanel.Children.Count : _columnsPanel.Children.Count - 1;
    }

    /// <summary>The row a button dropped in a column is inserted at: before the first button below the pointer.</summary>
    private int GetDropRow(DragEventArgs e, int column)
    {
        if (_columnsPanel.Children[column] is not Border { Child: StackPanel buttons })
        {
            return 0;
        }

        var y = e.GetPosition(buttons).Y;
        // the first child is the header
        for (var index = 1; index < buttons.Children.Count; index++)
        {
            if (y < buttons.Children[index].Bounds.Center.Y)
            {
                return index - 1;
            }
        }

        return buttons.Children.Count - 1;
    }

    private void ShowDropColumn(int? column)
    {
        if (_dropColumn == column)
        {
            return;
        }

        _dropColumn = column;
        for (var index = 0; index < _columnsPanel.Children.Count; index++)
        {
            if (_columnsPanel.Children[index] is Border border)
            {
                var highlighted = index == column || index == _selectedColumn;
                border.BorderThickness = new Thickness(highlighted ? 2 : 1);
                border.BorderBrush = highlighted ? AccentBrush() : Brushes.Gray;
            }
        }
    }

    /// <summary>Shows the selected button in the fields, which are disabled without a selected button.</summary>
    private void UpdateFields()
    {
        _updatingFields = true;
        try
        {
            var button = _selectedRow is { } row ? Editor.Columns[_selectedColumn][row] : null;
            _fields.IsEnabled = button != null;
            if (button == null)
            {
                _labelTextBox.Text = string.Empty;
                _backgroundTextBox.Text = string.Empty;
                _foregroundTextBox.Text = string.Empty;
                _actionComboBox.SelectedIndex = -1;
            }
            else
            {
                // the text boxes keep the text typed, e.g. an incomplete color, while the button is changed
                SetText(_labelTextBox, button.Label);
                if (!_backgroundTextBox.IsKeyboardFocusWithin)
                {
                    SetText(_backgroundTextBox, button.Background ?? string.Empty);
                }

                if (!_foregroundTextBox.IsKeyboardFocusWithin)
                {
                    SetText(_foregroundTextBox, button.Foreground ?? string.Empty);
                }

                _actionComboBox.SelectedIndex = CommanderActions.All.ToList()
                    .FindIndex(action => action.Id == button.Action);
            }

            UpdateSwatches(button);
        }
        finally
        {
            _updatingFields = false;
        }
    }

    private static void SetText(TextBox textBox, string text)
    {
        if (textBox.Text != text)
        {
            textBox.Text = text;
        }
    }

    private void UpdateSwatches(PanelButton? button)
    {
        _backgroundSwatch.Background = button?.Background != null ? ThemeManager.Brush(button.Background) : null;
        _foregroundSwatch.Background = button?.Foreground != null ? ThemeManager.Brush(button.Foreground) : null;
    }

    /// <summary>Changes the selected button to the values of the fields; invalid colors are kept until corrected.</summary>
    private void UpdateSelectedButton()
    {
        if (_updatingFields || _selectedRow is not { } row)
        {
            return;
        }

        var button = Editor.Columns[_selectedColumn][row];
        var background = ButtonPanelEditor.TryParseColor(_backgroundTextBox.Text, out var parsedBackground)
            ? parsedBackground
            : button.Background;
        var foreground = ButtonPanelEditor.TryParseColor(_foregroundTextBox.Text, out var parsedForeground)
            ? parsedForeground
            : button.Foreground;
        var action = _actionComboBox.SelectedIndex >= 0
            ? CommanderActions.All[_actionComboBox.SelectedIndex].Id
            : button.Action;
        var changed = button with
        {
            Label = _labelTextBox.Text ?? string.Empty, Action = action, Background = background,
            Foreground = foreground
        };
        if (changed != button)
        {
            Editor.SetButton(_selectedColumn, row, changed);
        }
    }

    /// <summary>A dragged button (with its row) or column (row null).</summary>
    private sealed record EditorDrag(int Column, int? Row);
}
