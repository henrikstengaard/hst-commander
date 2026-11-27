using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Hst.Commander.Abstractions;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     The cells of a row of a pane: the columns of the row's <see cref="EntryItem" /> (<see cref="EntryItem.Columns" />)
///     with its texts, the name cell (its first child, from the template, with the icon) in the Name column. The widths
///     of the columns are the resources of the pane (<see cref="PaneView.GetColumnWidthKey" />), so rows, header and
///     dividers line up. Rows are reused for other entries, so the cells follow the data context.
/// </summary>
public sealed class EntryRowCells : Grid
{
    private readonly List<TextBlock> _cells = [];
    private readonly List<IDisposable> _widthBindings = [];
    private IReadOnlyList<IEntryColumn>? _columns;
    private EntryItem? _item;

    // styled like the other grids of the rows, e.g. "Grid.row TextBlock" still matches its texts
    protected override Type StyleKeyOverride => typeof(Grid);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        SetItem(DataContext as EntryItem);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        SetItem(null);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SetItem(DataContext as EntryItem);
    }

    private void SetItem(EntryItem? item)
    {
        if (ReferenceEquals(_item, item))
        {
            return;
        }

        if (_item != null)
        {
            _item.PropertyChanged -= OnItemPropertyChanged;
        }

        _item = item;
        if (item == null)
        {
            return;
        }

        item.PropertyChanged += OnItemPropertyChanged;
        if (!ReferenceEquals(_columns, item.Columns))
        {
            CreateCells(item.Columns);
        }

        UpdateTexts();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EntryItem.Texts))
        {
            UpdateTexts();
        }
    }

    private void CreateCells(IReadOnlyList<IEntryColumn> columns)
    {
        _columns = columns;
        var nameCell = Children.OfType<DockPanel>().FirstOrDefault();
        foreach (var cell in _cells)
        {
            Children.Remove(cell);
        }

        _cells.Clear();
        ColumnDefinitions.Clear();
        for (var index = 0; index < columns.Count; index++)
        {
            ColumnDefinitions.Add(new ColumnDefinition(columns[index] == EntryColumns.Name
                ? new GridLength(1, GridUnitType.Star)
                : GridLength.Auto));
            if (columns[index] == EntryColumns.Name)
            {
                if (nameCell != null)
                {
                    SetColumn(nameCell, index);
                }

                continue;
            }

            var cell = new TextBlock
            {
                TextAlignment = columns[index].AlignEnd ? TextAlignment.Right : TextAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            SetColumn(cell, index);
            _cells.Add(cell);
            Children.Add(cell);
        }

        BindWidths();
    }

    private void BindWidths()
    {
        foreach (var binding in _widthBindings)
        {
            binding.Dispose();
        }

        _widthBindings.Clear();
        if (_columns == null)
        {
            return;
        }

        for (var index = 0; index < _columns.Count; index++)
        {
            if (_columns[index] != EntryColumns.Name)
            {
                _widthBindings.Add(ColumnDefinitions[index].Bind(ColumnDefinition.WidthProperty,
                    this.GetResourceObservable(PaneView.GetColumnWidthKey(index))));
            }
        }
    }

    private void UpdateTexts()
    {
        if (_item == null || _columns == null)
        {
            return;
        }

        var cellIndex = 0;
        for (var index = 0; index < _columns.Count; index++)
        {
            if (_columns[index] != EntryColumns.Name)
            {
                _cells[cellIndex++].Text = _item.Texts[index];
            }
        }
    }
}
