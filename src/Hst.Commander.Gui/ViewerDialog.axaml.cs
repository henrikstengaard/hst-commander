using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal viewer (F3) of a file in the text or hex view (F4) in a monospace font, reading only the part of the file
///     shown, so files of any size can be viewed. Images start in the image view (F4 switches to the hex view and back);
///     the image of a file with several (e.g. an icon file) is chosen in a list or with the keys that scroll. The image
///     is zoomed with the zoom buttons and list, + and - (or Ctrl+mouse wheel), 0 fits it to the view and 1 shows it in
///     100%; the arrow keys and the scroll bars move an image larger than the view. F7, Ctrl+F or / searches for text or a regular
///     expression, F5 finds the next match and F6 the previous one; searching runs in the background and Esc cancels
///     it. A match is highlighted, "not found" is shown in the status line. Text is selected by dragging with the mouse (Shift+click
///     extends, Ctrl+A or the context menu selects all) and copied to the clipboard with Ctrl+C, Ctrl+Insert or the
///     context menu; a click clears the selection. Esc or F10 closes the viewer.
/// </summary>
public partial class ViewerDialog : Window
{
    private const double VerticalScrollMaximum = 1000;

    /// <summary>Thickness of a scroll bar until one has been shown in the image view.</summary>
    private const double DefaultScrollBarSize = 16;

    private readonly ViewerSession? _session;
    private CancellationTokenSource? _searchCancellation;
    private string? _message;
    private bool _updating;
    private bool _closed;
    private readonly Thickness _textMargin;
    private readonly double _textSmallChange;
    private readonly double _textLargeChange;
    private double _scrollBarSize = DefaultScrollBarSize;

    public ViewerDialog()
    {
        InitializeComponent();
    }

    public ViewerDialog(string name, ViewerSession session) : this()
    {
        _session = session;
        Title = $"View - {name}";
        View.Session = session;
        View.ViewChanged += (_, _) => UpdateView();
        _textMargin = View.Margin;
        _textSmallChange = VerticalScrollBar.SmallChange;
        _textLargeChange = VerticalScrollBar.LargeChange;
        VerticalScrollBar.Scroll += (_, e) =>
        {
            if (session.IsImage)
            {
                // the scroll bar's value is the part of the image above the view
                if (View.ImageLayout is { } layout)
                {
                    session.PanImage(0, e.NewValue + layout.Y, View.Bounds.Width, View.Bounds.Height);
                }
            }
            else
            {
                session.ScrollTo(e.NewValue / VerticalScrollMaximum, View.Rows);
            }

            UpdateView();
        };
        ImageComboBox.ItemsSource = Enumerable.Range(0, session.Images.Count).Select(session.GetImageText).ToList();
        ImageComboBox.SelectionChanged += (_, _) =>
        {
            if (!_updating && ImageComboBox.SelectedIndex >= 0)
            {
                session.ShowImage(ImageComboBox.SelectedIndex);
                UpdateView();
            }
        };
        ZoomComboBox.ItemsSource = ViewerSession.ZoomLevels.Select(level => $"{level * 100:0}%").Prepend("Fit").ToList();
        ZoomComboBox.SelectionChanged += (_, _) =>
        {
            if (!_updating && ZoomComboBox.SelectedIndex >= 0)
            {
                session.SetZoom(ZoomComboBox.SelectedIndex == 0
                    ? null
                    : ViewerSession.ZoomLevels[ZoomComboBox.SelectedIndex - 1]);
                UpdateView();
            }
        };
        BackgroundPicker.BackgroundChanged += (_, _) => View.ImageBackground = BackgroundPicker.SelectedBrush;
        BackgroundPicker.FlyoutClosed += (_, _) => View.Focus();
        ZoomInButton.Click += (_, _) => Zoom(true);
        ZoomOutButton.Click += (_, _) => Zoom(false);
        HorizontalScrollBar.Scroll += (_, e) =>
        {
            if (session.IsImage)
            {
                // the scroll bar's value is the part of the image left of the view
                if (View.ImageLayout is { } layout)
                {
                    session.PanImage(e.NewValue + layout.X, 0, View.Bounds.Width, View.Bounds.Height);
                }
            }
            else
            {
                session.ScrollToColumn((int)Math.Round(e.NewValue));
            }

            UpdateView();
        };
        CloseButton.Click += (_, _) => Close();
        HexButton.Click += (_, _) => ToggleHex();
        SearchButton.Click += async (_, _) => await SearchAsync();
        NextButton.Click += async (_, _) => await FindNextAsync(false);
        PreviousButton.Click += async (_, _) => await FindNextAsync(true);
        CopyButton.Click += async (_, _) => await CopyAsync();
        CopyMenuItem.Click += async (_, _) => await CopyAsync();
        SelectAllMenuItem.Click += (_, _) => SelectAll();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            View.Focus();
            UpdateHexButton();
            UpdateView();
        };
        Closing += (_, _) => _searchCancellation?.Cancel();
        Closed += (_, _) => _closed = true;
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_session == null)
        {
            return;
        }

        if (e.Key == Key.Escape && _searchCancellation != null)
        {
            e.Handled = true;
            _searchCancellation.Cancel();
            return;
        }

        if ((e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control) ||
            (e.Key == Key.Insert && e.KeyModifiers == KeyModifiers.Control))
        {
            e.Handled = true;
            await CopyAsync();
            return;
        }

        if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            SelectAll();
            return;
        }

        if (e.Key == Key.F5)
        {
            e.Handled = true;
            await FindNextAsync(false);
            return;
        }

        if (e.Key == Key.F6)
        {
            e.Handled = true;
            await FindNextAsync(true);
            return;
        }

        if (e.Key == Key.F7 || (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control) || e.KeySymbol == "/")
        {
            e.Handled = true;
            await SearchAsync();
            return;
        }

        if (e.Key is Key.Escape or Key.F10)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (_session.IsImage && HandleImageKey(e))
        {
            e.Handled = true;
            UpdateView();
            return;
        }

        var rows = View.Rows;
        switch (e.Key)
        {
            case Key.F4:
                ToggleHex();
                break;
            case Key.Up:
                _session.Scroll(-1, rows);
                break;
            case Key.Down:
                _session.Scroll(1, rows);
                break;
            case Key.PageUp:
                _session.Scroll(-rows, rows);
                break;
            case Key.PageDown:
            case Key.Space:
                _session.Scroll(rows, rows);
                break;
            case Key.Home:
                _session.ScrollToStart();
                break;
            case Key.End:
                _session.ScrollToEnd(rows);
                break;
            case Key.Left:
                _session.ScrollColumns(-1, rows, View.Columns);
                break;
            case Key.Right:
                _session.ScrollColumns(1, rows, View.Columns);
                break;
            default:
                return;
        }

        e.Handled = true;
        UpdateView();
    }

    /// <summary>
    ///     Keys of the image view: zooming, and the arrow keys moving an image larger than the view (a tenth of the view)
    ///     instead of showing the next or previous image.
    /// </summary>
    private bool HandleImageKey(KeyEventArgs e)
    {
        var control = e.KeyModifiers == KeyModifiers.Control;
        if (e.KeySymbol == "+" || e.Key == Key.Add || (control && e.Key == Key.OemPlus))
        {
            Zoom(true);
            return true;
        }

        if (e.KeySymbol == "-" || e.Key == Key.Subtract || (control && e.Key == Key.OemMinus))
        {
            Zoom(false);
            return true;
        }

        if (e.KeySymbol == "0" || e.Key == Key.NumPad0)
        {
            _session!.SetZoom(null);
            return true;
        }

        if (e.KeySymbol == "1" || e.Key == Key.NumPad1)
        {
            _session!.SetZoom(1);
            return true;
        }

        var (width, height) = (View.Bounds.Width, View.Bounds.Height);
        var (deltaX, deltaY) = e.Key switch
        {
            Key.Left => (-width / 10, 0.0),
            Key.Right => (width / 10, 0.0),
            Key.Up => (0.0, -height / 10),
            Key.Down => (0.0, height / 10),
            _ => (0.0, 0.0)
        };
        return (deltaX != 0 || deltaY != 0) && _session!.PanImage(deltaX, deltaY, width, height);
    }

    private void Zoom(bool zoomIn)
    {
        if (zoomIn)
        {
            _session?.ZoomIn(View.Bounds.Width, View.Bounds.Height);
        }
        else
        {
            _session?.ZoomOut(View.Bounds.Width, View.Bounds.Height);
        }

        UpdateView();
    }

    private void SelectAll()
    {
        _session?.SelectAll();
        UpdateView();
    }

    /// <summary>Copies the selected text to the clipboard, showing what was copied in the status line.</summary>
    private async Task CopyAsync()
    {
        if (_session == null)
        {
            return;
        }

        try
        {
            if (_session.IsImage)
            {
                _message = "Switch to the hex view (F4) to select and copy";
            }
            else if (_session.GetSelectedText() is not { } text)
            {
                _message = "Select text to copy by dragging with the mouse or Ctrl+A";
            }
            else if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
                _message = $"Copied {text.Length:N0} characters";
            }
            else
            {
                _message = "The clipboard isn't available";
            }
        }
        catch (IOException exception)
        {
            _message = exception.Message;
        }

        UpdateView();
        View.Focus();
    }

    private void ToggleHex()
    {
        _session?.ToggleHex();
        UpdateHexButton();
        UpdateView();
    }

    private async Task SearchAsync()
    {
        if (_session == null || _searchCancellation != null)
        {
            return;
        }

        var options = await new ViewerSearchDialog(_session.LastSearch).ShowDialog<ViewerSearchOptions?>(this);
        View.Focus();
        if (options != null)
        {
            await RunSearchAsync(options.Text, (progress, cancellationToken) =>
                _session.FindAsync(options, View.Rows, View.Columns, progress, cancellationToken));
        }
    }

    private async Task FindNextAsync(bool backwards)
    {
        if (_session == null || _searchCancellation != null)
        {
            return;
        }

        if (_session.LastSearch is not { } search)
        {
            await SearchAsync();
            return;
        }

        await RunSearchAsync(search.Text, (progress, cancellationToken) =>
            _session.FindNextAsync(backwards, View.Rows, View.Columns, progress, cancellationToken));
    }

    /// <summary>Searches in the background, showing the progress in the status line until found or cancelled.</summary>
    private async Task RunSearchAsync(string text, Func<IProgress<double>, CancellationToken, Task<bool>> find)
    {
        using var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        StatusText.Text = "Searching... (Esc cancels)";
        var progress = new Progress<double>(fraction =>
        {
            if (!cancellation.IsCancellationRequested)
            {
                StatusText.Text = $"Searching... {fraction:P0} (Esc cancels)";
            }
        });
        try
        {
            if (!await find(progress, cancellation.Token))
            {
                _message = $"'{text}' not found";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!_closed)
            {
                await new ErrorDialog(exception.Message).ShowDialog(this);
            }
        }
        finally
        {
            _searchCancellation = null;
        }

        if (!_closed)
        {
            UpdateView();
            View.Focus();
        }
    }

    private void UpdateHexButton() => HexButtonText.Text = _session?.IsHex == true
        ? _session.Images.Count > 0 ? "Image" : "Text"
        : "Hex";

    /// <summary>Redraws the view and updates the scroll bars and the status line.</summary>
    private void UpdateView()
    {
        if (_session == null)
        {
            return;
        }

        View.InvalidateVisual();
        UpdateHexButton();
        var rows = View.Rows;
        _updating = true;
        ImageComboBox.IsVisible = _session.IsImage && _session.Images.Count > 1;
        ImageComboBox.SelectedIndex = _session.IsImage ? _session.ImageIndex : -1;
        ZoomPanel.IsVisible = _session.IsImage;
        ZoomComboBox.SelectedIndex = _session.ImageZoom is { } zoom
            ? ViewerSession.ZoomLevels.ToList().IndexOf(zoom) + 1
            : 0;
        _updating = false;
        // the image view's background fills the area, the text keeps a margin
        View.Margin = _session.IsImage ? default : _textMargin;
        if (_session.IsImage)
        {
            UpdateImageScrollBars();
        }
        else
        {
            VerticalScrollBar.Visibility = ScrollBarVisibility.Visible;
            VerticalScrollBar.Maximum = VerticalScrollMaximum;
            VerticalScrollBar.SmallChange = _textSmallChange;
            VerticalScrollBar.LargeChange = _textLargeChange;
            var length = Math.Max(1, _session.Document.Length);
            VerticalScrollBar.ViewportSize = Math.Max(1, VerticalScrollMaximum * Math.Min(length,
                (long)rows * (_session.IsHex ? ViewerDocument.BytesPerRow : 80)) / length);
            VerticalScrollBar.Value = _session.TopFraction * VerticalScrollMaximum;

            HorizontalScrollBar.Visibility = _session.IsHex ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Visible;
            if (HorizontalScrollBar.IsVisible)
            {
                var columns = View.Columns;
                HorizontalScrollBar.SmallChange = _textSmallChange;
                HorizontalScrollBar.LargeChange = _textLargeChange;
                HorizontalScrollBar.ViewportSize = columns;
                HorizontalScrollBar.Maximum = Math.Max(_session.LeftColumn, _session.GetMaxLineWidth(rows) - columns);
                HorizontalScrollBar.Value = _session.LeftColumn;
            }
        }

        if (_searchCancellation != null)
        {
            return;
        }

        // a search message is shown until the next change
        StatusText.Text = _message ?? _session.GetStatusText(rows, View.ImageLayout);
        _message = null;
    }

    /// <summary>
    ///     Shows the scroll bars of the image view when the zoomed image is wider or taller than the view, with the part
    ///     of the image shown in pixels of the view. Whether they are needed is decided for the area of the view and the
    ///     scroll bars shown, so they are hidden again when zooming out; showing or hiding them resizes the view, which
    ///     updates the view again.
    /// </summary>
    private void UpdateImageScrollBars()
    {
        if (VerticalScrollBar.IsVisible && VerticalScrollBar.Bounds.Width > 0)
        {
            _scrollBarSize = VerticalScrollBar.Bounds.Width;
        }
        else if (HorizontalScrollBar.IsVisible && HorizontalScrollBar.Bounds.Height > 0)
        {
            _scrollBarSize = HorizontalScrollBar.Bounds.Height;
        }

        var width = View.Bounds.Width + (VerticalScrollBar.IsVisible ? VerticalScrollBar.Bounds.Width : 0);
        var height = View.Bounds.Height + (HorizontalScrollBar.IsVisible ? HorizontalScrollBar.Bounds.Height : 0);
        var showHorizontal = false;
        var showVertical = false;
        if (_session!.GetImageLayout(width, height) is { IsFitted: false } area)
        {
            showHorizontal = area.Width > width;
            showVertical = area.Height > height || (showHorizontal && area.Height > height - _scrollBarSize);
            showHorizontal |= showVertical && area.Width > width - _scrollBarSize;
        }

        HorizontalScrollBar.Visibility = showHorizontal ? ScrollBarVisibility.Visible : ScrollBarVisibility.Disabled;
        VerticalScrollBar.Visibility = showVertical ? ScrollBarVisibility.Visible : ScrollBarVisibility.Disabled;
        if (View.ImageLayout is not { } layout)
        {
            return;
        }

        var viewWidth = View.Bounds.Width;
        var viewHeight = View.Bounds.Height;
        HorizontalScrollBar.Maximum = Math.Max(0, layout.Width - viewWidth);
        HorizontalScrollBar.ViewportSize = viewWidth;
        HorizontalScrollBar.SmallChange = viewWidth / 10;
        HorizontalScrollBar.LargeChange = viewWidth;
        HorizontalScrollBar.Value = Math.Clamp(-layout.X, 0, HorizontalScrollBar.Maximum);
        VerticalScrollBar.Maximum = Math.Max(0, layout.Height - viewHeight);
        VerticalScrollBar.ViewportSize = viewHeight;
        VerticalScrollBar.SmallChange = viewHeight / 10;
        VerticalScrollBar.LargeChange = viewHeight;
        VerticalScrollBar.Value = Math.Clamp(-layout.Y, 0, VerticalScrollBar.Maximum);
    }
}
