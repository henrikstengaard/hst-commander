using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Hst.Commander.Core;
using Hst.Commander.Gui.Icons;

namespace Hst.Commander.Gui;

/// <summary>
///     Draws the lines (text view) or rows (hex view) of a <see cref="ViewerSession" /> in a monospace font, reading only
///     the lines that fit. The text found by a search and the selection are highlighted, hex offsets are drawn in their
///     own color. Dragging with the left mouse button selects (scrolling when dragged outside the view), Shift+click
///     extends the selection and a click clears it. The mouse wheel scrolls 3 lines. The image view draws the image
///     shown fitted to the view or zoomed (Ctrl+mouse wheel zooms); the mouse wheel shows the next or previous image, or
///     moves an image larger than the view, which can also be dragged. The image view can have its own background.
/// </summary>
public sealed class ViewerControl : Control
{
    public const double FontSize = 13;

    /// <summary>Monospace fonts like a terminal, the first installed one is used.</summary>
    private static readonly Typeface Typeface = new(new FontFamily(
        "Cascadia Mono, Consolas, Menlo, SF Mono, DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, Ubuntu Mono, Courier New"));

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<ViewerControl, IBrush?>(nameof(Background));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<ViewerControl, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<IBrush?> OffsetForegroundProperty =
        AvaloniaProperty.Register<ViewerControl, IBrush?>(nameof(OffsetForeground));

    public static readonly StyledProperty<IBrush?> MatchBackgroundProperty =
        AvaloniaProperty.Register<ViewerControl, IBrush?>(nameof(MatchBackground));

    public static readonly StyledProperty<IBrush?> MatchForegroundProperty =
        AvaloniaProperty.Register<ViewerControl, IBrush?>(nameof(MatchForeground));

    public static readonly StyledProperty<IBrush?> SelectionBackgroundProperty =
        AvaloniaProperty.Register<ViewerControl, IBrush?>(nameof(SelectionBackground));

    public static readonly StyledProperty<IBrush?> SelectionForegroundProperty =
        AvaloniaProperty.Register<ViewerControl, IBrush?>(nameof(SelectionForeground));

    public static readonly StyledProperty<IBrush?> ImageBackgroundProperty =
        AvaloniaProperty.Register<ViewerControl, IBrush?>(nameof(ImageBackground));

    private readonly Dictionary<int, Bitmap?> _bitmaps = new();
    private ViewerSession? _session;
    private bool _selecting;
    private Point? _panPosition;
    private Size? _cellSize;

    static ViewerControl()
    {
        AffectsRender<ViewerControl>(BackgroundProperty, ForegroundProperty, OffsetForegroundProperty,
            MatchBackgroundProperty, MatchForegroundProperty, SelectionBackgroundProperty,
            SelectionForegroundProperty, ImageBackgroundProperty);
        FocusableProperty.OverrideDefaultValue<ViewerControl>(true);
        ClipToBoundsProperty.OverrideDefaultValue<ViewerControl>(true);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? OffsetForeground
    {
        get => GetValue(OffsetForegroundProperty);
        set => SetValue(OffsetForegroundProperty, value);
    }

    public IBrush? MatchBackground
    {
        get => GetValue(MatchBackgroundProperty);
        set => SetValue(MatchBackgroundProperty, value);
    }

    public IBrush? MatchForeground
    {
        get => GetValue(MatchForegroundProperty);
        set => SetValue(MatchForegroundProperty, value);
    }

    public IBrush? SelectionBackground
    {
        get => GetValue(SelectionBackgroundProperty);
        set => SetValue(SelectionBackgroundProperty, value);
    }

    /// <summary>Color of selected text, the normal foreground when not set.</summary>
    public IBrush? SelectionForeground
    {
        get => GetValue(SelectionForegroundProperty);
        set => SetValue(SelectionForegroundProperty, value);
    }

    /// <summary>Background of the image view, e.g. a color or the checkerboard, <see cref="Background" /> when not set.</summary>
    public IBrush? ImageBackground
    {
        get => GetValue(ImageBackgroundProperty);
        set => SetValue(ImageBackgroundProperty, value);
    }

    public ViewerSession? Session
    {
        get => _session;
        set
        {
            _session = value;
            ClearBitmaps();
            InvalidateVisual();
        }
    }

    /// <summary>Rows shown, at least 1.</summary>
    public int Rows => Math.Max(1, (int)(Bounds.Height / CellSize.Height));

    /// <summary>Columns shown, at least 1.</summary>
    public int Columns => Math.Max(1, (int)(Bounds.Width / CellSize.Width));

    /// <summary>Layout of the image shown, null when not in the image view.</summary>
    public ViewerImageLayout? ImageLayout => _session?.GetImageLayout(Bounds.Width, Bounds.Height);

    /// <summary>Raised when the view was scrolled or resized or the selection changed with the mouse.</summary>
    public event EventHandler? ViewChanged;

    /// <summary>Size of a character of the monospace font.</summary>
    private Size CellSize => _cellSize ??= MeasureCell();

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_session == null || e.Delta.Y == 0)
        {
            return;
        }

        e.Handled = true;
        if (_session.IsImage && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Delta.Y > 0)
            {
                _session.ZoomIn(Bounds.Width, Bounds.Height);
            }
            else
            {
                _session.ZoomOut(Bounds.Width, Bounds.Height);
            }
        }
        else if (!_session.IsImage ||
                 !_session.PanImage(0, (e.Delta.Y < 0 ? 3 : -3) * CellSize.Height, Bounds.Width, Bounds.Height))
        {
            // an image larger than the view is moved, otherwise the next or previous image is shown
            _session.Scroll(e.Delta.Y < 0 ? 3 : -3, Rows);
        }

        Changed();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_session == null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (_session.IsImage)
        {
            // dragging moves an image larger than the view
            _panPosition = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        var (row, column) = GetCell(e.GetPosition(this));
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _session.Selection != null)
        {
            _session.ExtendSelection(row, column, Rows);
        }
        else
        {
            _session.StartSelection(row, column, Rows);
        }

        _selecting = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        Changed();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_session != null && _panPosition is { } previous)
        {
            var position = e.GetPosition(this);
            _panPosition = position;
            if (_session.PanImage(previous.X - position.X, previous.Y - position.Y, Bounds.Width, Bounds.Height))
            {
                Changed();
            }

            return;
        }

        if (_session == null || !_selecting)
        {
            return;
        }

        var (row, column) = GetCell(e.GetPosition(this));
        _session.ExtendSelection(row, column, Rows);
        Changed();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_selecting || _panPosition != null)
        {
            _selecting = false;
            _panPosition = null;
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _selecting = false;
        _panPosition = null;
    }

    /// <summary>Cell of the view at a position, scrolling a line or column when it is outside the view.</summary>
    private (int Row, int Column) GetCell(Point position)
    {
        var cell = CellSize;
        var row = (int)Math.Floor(position.Y / cell.Height);
        var column = (int)Math.Floor(position.X / cell.Width);
        if (_session != null && (position.Y < 0 || row >= Rows))
        {
            _session.Scroll(position.Y < 0 ? -1 : 1, Rows);
            row = Math.Clamp(row, 0, Rows - 1);
        }

        if (_session is { IsHex: false } && (position.X < 0 || column >= Columns))
        {
            _session.ScrollColumns(position.X < 0 ? -1 : 1, Rows, Columns);
            column = Math.Clamp(column, 0, Columns - 1);
        }

        return (row, Math.Max(0, column));
    }

    private void Changed()
    {
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    public override void Render(DrawingContext context)
    {
        var background = _session is { IsImage: true } ? ImageBackground ?? Background : Background;
        context.FillRectangle(background ?? Brushes.Transparent, new Rect(Bounds.Size));
        if (_session == null)
        {
            return;
        }

        if (_session.IsImage)
        {
            RenderImage(context, _session);
            return;
        }

        // a row more, partly shown at the bottom
        var rows = Rows + 1;
        if (_session.IsHex)
        {
            RenderHexRows(context, _session, rows);
        }
        else
        {
            RenderLines(context, _session, rows);
        }
    }

    /// <summary>
    ///     Draws the image shown at its layout (fitted to the view or zoomed, centered when smaller than the view):
    ///     enlarged without smoothing (pixels stay sharp, e.g. icons), made smaller with smoothing.
    /// </summary>
    private void RenderImage(DrawingContext context, ViewerSession session)
    {
        var index = session.ImageIndex;
        if (!_bitmaps.TryGetValue(index, out var bitmap))
        {
            bitmap = FileIcons.CreateBitmap(session.CurrentImage!.Image);
            _bitmaps[index] = bitmap;
        }

        if (bitmap == null)
        {
            DrawText(context, "The image can't be shown", 0, 0, Foreground);
            return;
        }

        if (session.GetImageLayout(Bounds.Width, Bounds.Height) is not { } layout)
        {
            return;
        }

        var size = bitmap.PixelSize;
        // whole pixels, so enlarged pixels all get the same size
        var target = new Rect(Math.Round(layout.X), Math.Round(layout.Y), layout.Width, layout.Height);
        var mode = layout.Scale >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality;
        using (context.PushClip(new Rect(Bounds.Size)))
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = mode }))
        {
            context.DrawImage(bitmap, new Rect(0, 0, size.Width, size.Height), target);
        }
    }

    private void ClearBitmaps()
    {
        foreach (var bitmap in _bitmaps.Values)
        {
            bitmap?.Dispose();
        }

        _bitmaps.Clear();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ClearBitmaps();
    }

    private void RenderLines(DrawingContext context, ViewerSession session, int rows)
    {
        var columns = Columns + 1;
        var lines = session.GetLines(rows);
        for (var row = 0; row < lines.Count; row++)
        {
            var line = lines[row];
            var text = line.Text.Length <= session.LeftColumn ? string.Empty : line.Text[session.LeftColumn..];
            if (text.Length > columns)
            {
                text = text[..columns];
            }

            var selection = session.GetSelectionColumns(line) is var (selectionColumn, selectionLength)
                ? (Start: Math.Max(0, selectionColumn - session.LeftColumn),
                    End: Math.Min(columns, selectionColumn - session.LeftColumn + selectionLength))
                : (Start: 0, End: 0);
            // the selection is painted over the text, so the selected text is only drawn in its own color
            DrawText(context, text, 0, row, Foreground);
            FillCells(context, SelectionBackground, selection.Start, selection.End - selection.Start, row);
            if (selection.Start < Math.Min(selection.End, text.Length))
            {
                DrawText(context, text[selection.Start..Math.Min(selection.End, text.Length)], selection.Start, row,
                    SelectionForeground ?? Foreground);
            }

            if (session.GetMatchColumns(line) is var (column, length))
            {
                var start = Math.Max(0, column - session.LeftColumn);
                var end = Math.Min(text.Length, column - session.LeftColumn + length);
                if (start < end)
                {
                    DrawMatch(context, text[start..end], start, row);
                }
            }
        }
    }

    private void RenderHexRows(DrawingContext context, ViewerSession session, int rows)
    {
        var digits = session.Document.HexOffsetDigits;
        foreach (var (hexRow, row) in session.GetHexRows(rows).Select((hexRow, row) => (hexRow, row)))
        {
            DrawText(context, hexRow.Text[..digits], 0, row, OffsetForeground ?? Foreground);
            DrawText(context, hexRow.Text[digits..], digits, row, Foreground);
            for (var index = 0; index < hexRow.Bytes.Length; index++)
            {
                if (!session.IsSelected(hexRow.Offset + index))
                {
                    continue;
                }

                // painted over the text, the space after a selected byte too when the next byte is selected, so it
                // reads as one block
                var hexColumn = ViewerDocument.GetHexColumn(digits, index);
                var textColumn = ViewerDocument.GetHexTextColumn(digits, index);
                var next = index + 1 < hexRow.Bytes.Length && session.IsSelected(hexRow.Offset + index + 1);
                var width = next ? ViewerDocument.GetHexColumn(digits, index + 1) - hexColumn : 2;
                var brush = SelectionForeground ?? Foreground;
                FillCells(context, SelectionBackground, hexColumn, width, row);
                FillCells(context, SelectionBackground, textColumn, 1, row);
                DrawText(context, hexRow.Text.Substring(hexColumn, 2), hexColumn, row, brush);
                DrawText(context, hexRow.Text.Substring(textColumn, 1), textColumn, row, brush);
            }

            for (var index = 0; index < hexRow.Bytes.Length; index++)
            {
                if (!session.IsMatchByte(hexRow.Offset + index))
                {
                    continue;
                }

                var hexColumn = ViewerDocument.GetHexColumn(digits, index);
                var textColumn = ViewerDocument.GetHexTextColumn(digits, index);
                DrawMatch(context, hexRow.Text.Substring(hexColumn, 2), hexColumn, row);
                DrawMatch(context, hexRow.Text.Substring(textColumn, 1), textColumn, row);
            }
        }
    }

    private void DrawMatch(DrawingContext context, string text, int column, int row)
    {
        FillCells(context, MatchBackground ?? Brushes.Yellow, column, text.Length, row);
        DrawText(context, text, column, row, MatchForeground ?? Foreground);
    }

    private void FillCells(DrawingContext context, IBrush? brush, int column, int length, int row)
    {
        if (length <= 0)
        {
            return;
        }

        var cell = CellSize;
        context.FillRectangle(brush ?? Brushes.LightBlue,
            new Rect(column * cell.Width, row * cell.Height, length * cell.Width, cell.Height));
    }

    private void DrawText(DrawingContext context, string text, int column, int row, IBrush? brush)
    {
        if (text.Length == 0)
        {
            return;
        }

        var cell = CellSize;
        context.DrawText(CreateText(text, brush ?? Brushes.Black), new Point(column * cell.Width, row * cell.Height));
    }

    private static FormattedText CreateText(string text, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface, FontSize, brush);

    private static Size MeasureCell()
    {
        var text = CreateText(new string('M', 10), Brushes.Black);
        return new Size(Math.Max(1, text.WidthIncludingTrailingWhitespace / 10), Math.Max(1, Math.Ceiling(text.Height)));
    }
}
