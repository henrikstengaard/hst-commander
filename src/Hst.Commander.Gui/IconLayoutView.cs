using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Hst.Commander.Abstractions;
using Hst.Commander.Gui.Icons;

namespace Hst.Commander.Gui;

/// <summary>
///     Shows the entries of a pane as icons placed at positions, for the navigation modes of plugins (see
///     <see cref="INavigationMode" />), e.g. the Amiga Workbench: each icon in its own size with the name centered
///     below, on the background of the mode. Icons without a position are placed from the upper left corner to the
///     right, continuing on the next row at the right edge, skipping places taken by icons with a position. Icons can be
///     dragged to other positions when <see cref="CanMove" />; <see cref="ChangedPositions" /> holds the moved icons
///     until they are saved or <see cref="Reset" />. A small toolbar above the icons has a ".." button going to the
///     parent (<see cref="ParentClicked" />), check boxes for the options of the mode
///     (<see cref="OptionChanged" />) and a "Sort by name" button (<see cref="SortByName" />), drawn in the colors of the mode with raised (Workbench style) edges.
/// </summary>
internal sealed class IconLayoutView : UserControl
{
    /// <summary>Margin around the icons, so icons at position 0 aren't drawn on the edge.</summary>
    private const double IconMargin = 8;

    /// <summary>Distance in pixels the pointer moves before an icon is dragged instead of clicked.</summary>
    private const double DragDistance = 4;

    private readonly ScrollViewer _scrollViewer;
    private readonly Canvas _canvas;
    private readonly Border _toolbar;
    private readonly ToolbarButton _parentButton;
    private readonly ToolbarButton _sortButton;
    private readonly StackPanel _optionsPanel;
    private readonly List<CheckBox> _optionCheckBoxes = [];
    private IReadOnlyList<NavigationModeOption> _options = [];
    private readonly List<IconVisual> _icons = [];
    private readonly Dictionary<string, PixelPoint> _changedPositions = new(StringComparer.Ordinal);
    private NavigationModeStyle _style = new("#AAAAAA", "#000000");
    private EntryItem? _currentItem;
    private bool _isActive;
    private IconVisual? _pressedIcon;
    private Point _pressedPoint;
    private PixelPoint _pressedPosition;
    private bool _isDragging;
    private double _placedWidth;

    public IconLayoutView()
    {
        Focusable = true;
        // the canvas starts in the upper left corner, it's sized to the icons
        _canvas = new Canvas
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        _scrollViewer = new ScrollViewer
        {
            Content = _canvas,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        _parentButton = new ToolbarButton(Paths.Parent, "Parent");
        _parentButton.Clicked += (_, _) => ParentClicked?.Invoke(this, EventArgs.Empty);
        _sortButton = new ToolbarButton("Sort by name", "Place the icons in rows sorted by name")
        {
            Margin = new Thickness(8, 0, 0, 0),
            IsButtonEnabled = false
        };
        _sortButton.Clicked += (_, _) =>
        {
            Focus(NavigationMethod.Pointer);
            SortByName();
        };
        _optionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        var toolbarPanel = new StackPanel { Orientation = Orientation.Horizontal };
        toolbarPanel.Children.Add(_parentButton);
        toolbarPanel.Children.Add(_optionsPanel);
        toolbarPanel.Children.Add(_sortButton);
        _toolbar = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4, 2),
            Child = toolbarPanel
        };
        var panel = new DockPanel();
        DockPanel.SetDock(_toolbar, Dock.Top);
        panel.Children.Add(_toolbar);
        panel.Children.Add(_scrollViewer);
        Content = panel;
        ClipToBounds = true;
        ApplyStyle();

        // clicking the background deselects the current icon, like the Workbench
        _canvas.PointerPressed += (_, e) =>
        {
            if (e.Source == _canvas)
            {
                Focus(NavigationMethod.Pointer);
                BackgroundClicked?.Invoke(this, EventArgs.Empty);
            }
        };
        _scrollViewer.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ViewportProperty && Math.Abs(GetAvailableWidth() - _placedWidth) >= 1)
            {
                PlaceIcons();
            }
        };
    }

    /// <summary>Raised when an icon is clicked, making its entry the current entry.</summary>
    public event EventHandler<EntryItem>? ItemClicked;

    /// <summary>Raised when an icon is clicked with Ctrl or Shift, toggling the mark of its entry.</summary>
    public event EventHandler<EntryItem>? ItemToggled;

    /// <summary>Raised when an icon is double-clicked, opening its entry.</summary>
    public event EventHandler<EntryItem>? ItemActivated;

    /// <summary>Raised when the background is clicked.</summary>
    public event EventHandler? BackgroundClicked;

    /// <summary>Raised when the ".." button in the toolbar is clicked, going to the parent.</summary>
    public event EventHandler? ParentClicked;

    /// <summary>Raised when the check box of an option of the mode is changed by the user.</summary>
    public event EventHandler<(NavigationModeOption Option, bool Value)>? OptionChanged;

    /// <summary>Raised when an icon was dragged to another position, or the positions were reset.</summary>
    public event EventHandler? PositionsChanged;

    /// <summary>True when icons can be dragged to other positions, e.g. not in read-only images.</summary>
    public bool CanMove { get; set; }

    /// <summary>Positions of the icons moved since they were shown or reset, of the image in pixels.</summary>
    public IReadOnlyList<NavigationIconPosition> ChangedPositions => _changedPositions
        .Select(position => new NavigationIconPosition(position.Key, position.Value.X, position.Value.Y))
        .ToList();

    /// <summary>The entries shown, in the order of the layout.</summary>
    public IReadOnlyList<EntryItem> Items => _icons.Select(icon => icon.Item).ToList();

    /// <summary>Enables the ".." button in the toolbar when the directory has a parent.</summary>
    public void SetToolbar(bool canGoParent) => _parentButton.IsButtonEnabled = canGoParent;

    /// <summary>
    ///     Shows a check box for each option of the mode next to the ".." button, checked by the values, kept while the
    ///     options are the same.
    /// </summary>
    public void SetOptions(IReadOnlyList<NavigationModeOption> options, IReadOnlyDictionary<string, bool> values)
    {
        if (!_options.SequenceEqual(options))
        {
            _options = options;
            _optionsPanel.Children.Clear();
            _optionCheckBoxes.Clear();
            foreach (var option in options)
            {
                // not focusable, so the icons keep the keyboard focus
                var checkBox = new CheckBox
                {
                    Content = option.Label,
                    Focusable = false,
                    Margin = new Thickness(8, 0, 0, 0),
                    Padding = new Thickness(4, 0, 0, 0),
                    MinHeight = 0,
                    VerticalAlignment = VerticalAlignment.Center
                };
                ToolTip.SetTip(checkBox, option.Description);
                checkBox.IsCheckedChanged += (_, _) =>
                {
                    if (checkBox.Tag is not true)
                    {
                        OptionChanged?.Invoke(this, (option, checkBox.IsChecked == true));
                    }
                };
                _optionCheckBoxes.Add(checkBox);
                _optionsPanel.Children.Add(checkBox);
            }

            ApplyStyle();
        }

        for (var i = 0; i < options.Count; i++)
        {
            // the tag tells the change isn't made by the user
            var checkBox = _optionCheckBoxes[i];
            checkBox.Tag = true;
            checkBox.IsChecked = options[i].GetValue(values);
            checkBox.Tag = null;
        }
    }

    /// <summary>Colors and font of the mode for the background, the toolbar and the names.</summary>
    private void ApplyStyle()
    {
        var background = ParseBrush(_style.Background, Brushes.Gray);
        var foreground = ParseBrush(_style.Foreground, Brushes.Black);
        Background = background;
        _toolbar.Background = background;
        _toolbar.BorderBrush = foreground;
        _parentButton.SetColors(background, foreground);
        _sortButton.SetColors(background, foreground);
        foreach (var text in new Control[] { _parentButton, _sortButton }.Concat(_optionCheckBoxes))
        {
            TextElement.SetForeground(text, foreground);
            if (_style.FontFamily is { } fontFamily)
            {
                TextElement.SetFontFamily(text, new FontFamily(fontFamily));
            }

            if (_style.FontSize is { } fontSize)
            {
                TextElement.SetFontSize(text, fontSize);
            }
        }
    }

    /// <summary>
    ///     Shows the icons of a layout for the listed entries, replacing the icons shown, e.g. when a directory is opened.
    ///     Icons without an image show the entry's icon.
    /// </summary>
    public void Show(NavigationModeStyle style, NavigationLayout layout, IReadOnlyList<EntryItem> items)
    {
        Clear();
        _style = style;
        ApplyStyle();
        var itemsByName = items
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var icon in layout.Icons)
        {
            if (itemsByName.TryGetValue(icon.Name, out var item))
            {
                AddIcon(item, icon);
            }
        }

        _sortButton.IsButtonEnabled = _icons.Count > 0;
        PlaceIcons();
    }

    /// <summary>Removes all icons, e.g. before another directory is shown.</summary>
    public void Clear()
    {
        foreach (var icon in _icons)
        {
            icon.Item.PropertyChanged -= OnItemPropertyChanged;
        }

        _icons.Clear();
        _sortButton.IsButtonEnabled = false;
        _canvas.Children.Clear();
        _changedPositions.Clear();
        _pressedIcon = null;
        _isDragging = false;
    }

    /// <summary>Moves the icons back to the positions they were shown at, discarding the changed positions.</summary>
    public void Reset()
    {
        _changedPositions.Clear();
        foreach (var icon in _icons)
        {
            icon.Position = icon.LayoutPosition;
        }

        PlaceIcons();
        PositionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Shows the icon of the current entry selected while the pane is active, like the list shows the current row.
    ///     Marked entries are always shown selected.
    /// </summary>
    public void UpdateSelection(EntryItem? currentItem, bool isActive)
    {
        _currentItem = currentItem;
        _isActive = isActive;
        foreach (var icon in _icons)
        {
            UpdateImage(icon);
        }

        if (currentItem != null && _icons.FirstOrDefault(icon => icon.Item == currentItem) is { } currentIcon)
        {
            currentIcon.Image.BringIntoView();
        }
    }

    /// <summary>The entry an offset of icons from an entry in the order of the layout, the first when not shown.</summary>
    public EntryItem? GetItem(EntryItem? item, int offset)
    {
        if (_icons.Count == 0)
        {
            return null;
        }

        var index = _icons.FindIndex(icon => icon.Item == item);
        return index < 0
            ? GetUpperLeftIcon()?.Item
            : _icons[Math.Clamp(index + offset, 0, _icons.Count - 1)].Item;
    }

    /// <summary>
    ///     The entry of the nearest icon in a direction from the icon of an entry, by the centers of their images: the
    ///     distance in the direction plus twice the distance across it, so icons in line are preferred over closer
    ///     icons beside it. The entry itself when there is no icon in the direction, the icon in the upper left corner
    ///     when the entry isn't shown.
    /// </summary>
    public EntryItem? GetItem(EntryItem? item, NavigationDirection direction)
    {
        if (_icons.FirstOrDefault(icon => icon.Item == item) is not { } currentIcon)
        {
            return GetUpperLeftIcon()?.Item;
        }

        var center = GetCenter(currentIcon);
        IconVisual? nearestIcon = null;
        var nearestDistance = double.MaxValue;
        foreach (var icon in _icons.Where(icon => icon != currentIcon))
        {
            var offset = GetCenter(icon) - center;
            var (along, across) = direction switch
            {
                NavigationDirection.Up => (-offset.Y, offset.X),
                NavigationDirection.Down => (offset.Y, offset.X),
                NavigationDirection.Left => (-offset.X, offset.Y),
                _ => (offset.X, offset.Y)
            };
            // icons beside it (less than a pixel in the direction) aren't in the direction
            if (along < 1)
            {
                continue;
            }

            var distance = along + 2 * Math.Abs(across);
            if (distance < nearestDistance)
            {
                nearestIcon = icon;
                nearestDistance = distance;
            }
        }

        return (nearestIcon ?? currentIcon).Item;
    }

    private IconVisual? GetUpperLeftIcon() => _icons
        .OrderBy(icon => icon.Position?.Y ?? 0)
        .ThenBy(icon => icon.Position?.X ?? 0)
        .FirstOrDefault();

    private static Point GetCenter(IconVisual icon)
    {
        var position = icon.Position ?? new PixelPoint(0, 0);
        return new Point(position.X + icon.Image.Width / 2, position.Y + icon.Image.Height / 2);
    }

    /// <summary>True when an entry is shown as an icon.</summary>
    public bool Contains(EntryItem item) => _icons.Any(icon => icon.Item == item);

    private void AddIcon(EntryItem item, NavigationIcon navigationIcon)
    {
        var normalImage = FileIcons.CreateBitmap(navigationIcon.Image);
        var image = new Image
        {
            Width = normalImage?.Size.Width ?? 32,
            Height = normalImage?.Size.Height ?? 32,
            Stretch = Stretch.Fill
        };
        // the pixels of Amiga icons stay sharp
        RenderOptions.SetBitmapInterpolationMode(image, normalImage != null
            ? Avalonia.Media.Imaging.BitmapInterpolationMode.None
            : Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        var label = new TextBlock
        {
            Text = item.DisplayName,
            Foreground = ParseBrush(_style.Foreground, Brushes.Black),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        if (_style.FontFamily is { } fontFamily)
        {
            label.FontFamily = new FontFamily(fontFamily);
        }

        if (_style.FontSize is { } fontSize)
        {
            label.FontSize = fontSize;
        }

        // the label is measured once added, so it has the pane's font when the mode has none
        _canvas.Children.Add(image);
        _canvas.Children.Add(label);
        var layoutPosition = navigationIcon.X is { } x && navigationIcon.Y is { } y
            ? new PixelPoint(x, y)
            : (PixelPoint?)null;
        var icon = new IconVisual(item, image, label, normalImage, FileIcons.CreateBitmap(navigationIcon.SelectedImage),
            layoutPosition, MeasureLabel(label), navigationIcon.IsMovable);
        _icons.Add(icon);
        UpdateImage(icon);

        image.PointerPressed += (_, e) => OnIconPressed(icon, e);
        label.PointerPressed += (_, e) => OnIconPressed(icon, e);
        image.PointerMoved += (_, e) => OnIconMoved(icon, e);
        label.PointerMoved += (_, e) => OnIconMoved(icon, e);
        image.PointerReleased += (_, e) => OnIconReleased(icon, e);
        label.PointerReleased += (_, e) => OnIconReleased(icon, e);
        item.PropertyChanged += OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is EntryItem item && e.PropertyName is nameof(EntryItem.IsMarked) or nameof(EntryItem.Icon) &&
            _icons.FirstOrDefault(icon => icon.Item == item) is { } icon)
        {
            UpdateImage(icon);
        }
    }

    /// <summary>
    ///     Shows the normal or selected image of an icon, the entry's icon when the layout has no image, drawn
    ///     transparent while selected when there is no selected image.
    /// </summary>
    private void UpdateImage(IconVisual icon)
    {
        var isSelected = icon.Item.IsMarked || (_isActive && icon.Item == _currentItem);
        icon.Image.Source = icon.NormalImage == null
            ? icon.Item.Icon
            : isSelected && icon.SelectedImage != null
                ? icon.SelectedImage
                : icon.NormalImage;
        icon.Image.Opacity = isSelected && (icon.SelectedImage == null || icon.NormalImage == null) ? 0.5 : 1;
    }

    private void OnIconPressed(IconVisual icon, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_canvas);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;
        Focus(NavigationMethod.Pointer);
        if (e.ClickCount == 2)
        {
            ItemActivated?.Invoke(this, icon.Item);
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            ItemToggled?.Invoke(this, icon.Item);
        }
        else
        {
            ItemClicked?.Invoke(this, icon.Item);
        }

        _pressedIcon = icon;
        _pressedPoint = point.Position;
        _pressedPosition = icon.Position ?? new PixelPoint(0, 0);
        _isDragging = false;
        e.Pointer.Capture(icon.Image);
    }

    private void OnIconMoved(IconVisual icon, PointerEventArgs e)
    {
        if (_pressedIcon != icon || !CanMove || !icon.IsMovable || icon.Position == null)
        {
            return;
        }

        var delta = e.GetPosition(_canvas) - _pressedPoint;
        if (!_isDragging && Math.Abs(delta.X) < DragDistance && Math.Abs(delta.Y) < DragDistance)
        {
            return;
        }

        _isDragging = true;
        icon.Position = new PixelPoint(Math.Max(0, _pressedPosition.X + (int)Math.Round(delta.X)),
            Math.Max(0, _pressedPosition.Y + (int)Math.Round(delta.Y)));
        PositionIcon(icon);
        UpdateCanvasSize();
    }

    private void OnIconReleased(IconVisual icon, PointerReleasedEventArgs e)
    {
        if (_pressedIcon != icon)
        {
            return;
        }

        e.Pointer.Capture(null);
        _pressedIcon = null;
        if (!_isDragging || icon.Position is not { } position)
        {
            return;
        }

        _isDragging = false;
        // an icon placed without a position keeps the position it was dragged to
        icon.IsPlaced = false;
        _changedPositions[icon.Item.Name] = position;
        PositionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Places the icons without a position (or only placed) from the upper left corner, row by row in cells of the
    ///     largest icon, skipping cells taken by icons with a position, and positions all icons.
    /// </summary>
    private void PlaceIcons()
    {
        _placedWidth = GetAvailableWidth();
        foreach (var icon in _icons.Where(icon => icon.IsPlaced || icon.Position == null))
        {
            icon.Position = null;
            icon.IsPlaced = false;
        }

        var (cellWidth, cellHeight, maxImageHeight, columns) = GetCells();
        var taken = _icons.Where(icon => icon.Position != null).Select(GetBounds).ToList();
        var cell = 0;
        foreach (var icon in _icons.Where(icon => icon.Position == null))
        {
            // the icons with a position are a finite set, so a free cell is always found
            while (true)
            {
                var x = cell % columns * cellWidth + (cellWidth - icon.Image.Width) / 2;
                var y = cell / columns * cellHeight + (maxImageHeight - icon.Image.Height);
                cell++;
                icon.Position = new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
                var bounds = GetBounds(icon);
                if (taken.Any(other => other.Intersects(bounds)))
                {
                    continue;
                }

                icon.IsPlaced = true;
                taken.Add(bounds);
                break;
            }
        }

        foreach (var icon in _icons)
        {
            PositionIcon(icon);
        }

        UpdateCanvasSize();
    }

    /// <summary>
    ///     Size of the cells icons are placed in, fitting the largest image and a label of up to 120 pixels, the height
    ///     of the largest image and the number of cells in a row of the view.
    /// </summary>
    private (double Width, double Height, double MaxImageHeight, int Columns) GetCells()
    {
        var maxImageWidth = _icons.Select(icon => icon.Image.Width).DefaultIfEmpty(32).Max();
        var maxImageHeight = _icons.Select(icon => icon.Image.Height).DefaultIfEmpty(32).Max();
        var labelHeight = _icons.Select(icon => icon.LabelSize.Height).DefaultIfEmpty(16).Max();
        var cellWidth = Math.Max(maxImageWidth, Math.Min(_icons.Select(icon => icon.LabelSize.Width).DefaultIfEmpty(0)
            .Max(), 120)) + 16;
        var cellHeight = maxImageHeight + labelHeight + 12;
        return (cellWidth, cellHeight, maxImageHeight, Math.Max(1, (int)(GetAvailableWidth() / cellWidth)));
    }

    /// <summary>
    ///     Places all icons sorted by name from the upper left corner to the right in rows of cells, continuing on the
    ///     next row at the right edge of the view, each image centered in its cell horizontally and on the middle of its
    ///     row vertically, like Clean Up by name on the Workbench. The positions of movable icons become changed
    ///     positions when icons can be moved, so they can be saved or reset.
    /// </summary>
    public void SortByName()
    {
        if (_icons.Count == 0)
        {
            return;
        }

        var (cellWidth, cellHeight, maxImageHeight, columns) = GetCells();
        var sortedIcons = _icons
            .OrderBy(icon => icon.Item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(icon => icon.Item.DisplayName, StringComparer.Ordinal)
            .ToList();
        for (var cell = 0; cell < sortedIcons.Count; cell++)
        {
            var icon = sortedIcons[cell];
            var x = cell % columns * cellWidth + (cellWidth - icon.Image.Width) / 2;
            var y = cell / columns * cellHeight + (maxImageHeight - icon.Image.Height) / 2;
            var position = new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
            icon.Position = position;
            icon.IsPlaced = false;
            if (CanMove && icon.IsMovable)
            {
                _changedPositions[icon.Item.Name] = position;
            }

            PositionIcon(icon);
        }

        UpdateCanvasSize();
        PositionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private double GetAvailableWidth() =>
        Math.Max(0, (_scrollViewer.Viewport.Width > 0 ? _scrollViewer.Viewport.Width : Bounds.Width) - 2 * IconMargin);

    /// <summary>Area of an icon with its label, relative to the positions.</summary>
    private static Rect GetBounds(IconVisual icon)
    {
        var position = icon.Position ?? new PixelPoint(0, 0);
        var width = Math.Max(icon.Image.Width, icon.LabelSize.Width);
        var left = position.X + icon.Image.Width / 2 - width / 2;
        return new Rect(left, position.Y, width, icon.Image.Height + 1 + icon.LabelSize.Height);
    }

    private static void PositionIcon(IconVisual icon)
    {
        var position = icon.Position ?? new PixelPoint(0, 0);
        Canvas.SetLeft(icon.Image, IconMargin + position.X);
        Canvas.SetTop(icon.Image, IconMargin + position.Y);
        // the name is centered below the image
        icon.Label.Width = icon.LabelSize.Width;
        Canvas.SetLeft(icon.Label, IconMargin + position.X + icon.Image.Width / 2 - icon.LabelSize.Width / 2);
        Canvas.SetTop(icon.Label, IconMargin + position.Y + icon.Image.Height + 1);
    }

    private void UpdateCanvasSize()
    {
        var right = 0.0;
        var bottom = 0.0;
        foreach (var icon in _icons)
        {
            var bounds = GetBounds(icon);
            right = Math.Max(right, bounds.Right);
            bottom = Math.Max(bottom, bounds.Bottom);
        }

        _canvas.Width = right + 2 * IconMargin;
        _canvas.Height = bottom + 2 * IconMargin;
    }

    private static Size MeasureLabel(TextBlock label)
    {
        var text = new FormattedText(label.Text ?? string.Empty, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(label.FontFamily, label.FontStyle, label.FontWeight), label.FontSize, Brushes.Black);
        return new Size(Math.Ceiling(text.Width) + 2, Math.Ceiling(text.Height));
    }

    private static IBrush ParseBrush(string color, IBrush fallback) =>
        Color.TryParse(color, out var parsedColor) ? new SolidColorBrush(parsedColor) : fallback;

    /// <summary>
    ///     Button of the toolbar with raised (Workbench style) edges, white at the top and left and the text color at the
    ///     bottom and right, swapped while pressed; dimmed and not clickable when not enabled. Not focusable, so the
    ///     icons keep the keyboard focus.
    /// </summary>
    private sealed class ToolbarButton : Border
    {
        private readonly Border _inner;
        private IBrush _dark = Brushes.Black;
        private bool _isButtonEnabled = true;

        public ToolbarButton(string text, string toolTip)
        {
            _inner = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(8, 0),
                Child = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
            };
            BorderThickness = new Thickness(1, 1, 0, 0);
            Child = _inner;
            Cursor = new Cursor(StandardCursorType.Hand);
            VerticalAlignment = VerticalAlignment.Center;
            ToolTip.SetTip(this, toolTip);
            SetPressed(false);

            PointerPressed += (_, e) =>
            {
                if (_isButtonEnabled && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    e.Handled = true;
                    e.Pointer.Capture(this);
                    SetPressed(true);
                }
            };
            PointerReleased += (_, e) =>
            {
                if (e.Pointer.Captured != this)
                {
                    return;
                }

                e.Pointer.Capture(null);
                SetPressed(false);
                // released on the button, not after moving off it
                if (new Rect(Bounds.Size).Contains(e.GetPosition(this)))
                {
                    Clicked?.Invoke(this, EventArgs.Empty);
                }
            };
        }

        public event EventHandler? Clicked;

        /// <summary>False to dim the button and ignore clicks, e.g. the ".." button without a parent.</summary>
        public bool IsButtonEnabled
        {
            get => _isButtonEnabled;
            set
            {
                _isButtonEnabled = value;
                Opacity = value ? 1 : 0.4;
                SetPressed(false);
            }
        }

        /// <summary>Background (so the whole button can be clicked) and the color of the dark edges.</summary>
        public void SetColors(IBrush background, IBrush foreground)
        {
            Background = background;
            _dark = foreground;
            SetPressed(false);
        }

        private void SetPressed(bool isPressed)
        {
            BorderBrush = isPressed ? _dark : Brushes.White;
            _inner.BorderBrush = isPressed ? Brushes.White : _dark;
        }
    }

    /// <summary>An icon shown: its image and label controls, images, position and the size of its label.</summary>
    private sealed class IconVisual(EntryItem item, Image image, TextBlock label, Bitmap? normalImage,
        Bitmap? selectedImage, PixelPoint? layoutPosition, Size labelSize, bool isMovable)
    {
        public EntryItem Item { get; } = item;
        public Image Image { get; } = image;
        public TextBlock Label { get; } = label;
        public Bitmap? NormalImage { get; } = normalImage;
        public Bitmap? SelectedImage { get; } = selectedImage;

        /// <summary>Position from the layout, null when the icon has none.</summary>
        public PixelPoint? LayoutPosition { get; } = layoutPosition;

        /// <summary>Current position, null until placed.</summary>
        public PixelPoint? Position { get; set; } = layoutPosition;

        /// <summary>True when the position was chosen by <see cref="PlaceIcons" />, so it's placed again on resize.</summary>
        public bool IsPlaced { get; set; }

        public Size LabelSize { get; } = labelSize;

        /// <summary>False when the icon can't be dragged, e.g. the parent "..".</summary>
        public bool IsMovable { get; } = isMovable;
    }
}
