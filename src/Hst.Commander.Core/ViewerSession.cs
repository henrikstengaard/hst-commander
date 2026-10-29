using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     State of the viewer (F3) shared by both front-ends: the <see cref="ViewerDocument" /> shown, text, hex or image
///     view (the images of the file read by the image providers, one shown at a time),
///     the position (the offset of the top line or row, the first column shown in the text view) and the text found by
///     the last search and the selection (byte offsets, so it is kept while scrolling and switching views). Scrolling
///     and selecting take the number of rows and columns the front-end shows. Images start in the image view, other
///     binary files in the hex view. In the image view scrolling (lines, pages, columns, start and end) changes the
///     image shown; the image is fitted to the view or zoomed (<see cref="ImageZoom" />) and moved when it is larger
///     than the view (<see cref="PanImage" />). With word wrap (<see cref="ToggleWordWrap" />) the text view shows lines
///     wrapped at the width of the view (<see cref="SetColumns" />), scrolled by rows. Must be used on one thread,
///     searching reads on a background thread.
/// </summary>
public sealed class ViewerSession(ViewerDocument document, IReadOnlyList<EntryImage>? images = null) : IDisposable
{
    /// <summary>Zoom factors of the image view, from 10% to 1600%.</summary>
    public static readonly IReadOnlyList<double> ZoomLevels = [0.1, 0.25, 0.5, 1, 2, 3, 4, 6, 8, 12, 16];

    /// <summary>Bytes of the character or byte where selecting started.</summary>
    private (long Start, long End)? _anchor;

    private bool _anchorHexValues;

    /// <summary>Point of the image in the center of the view, as fractions of its width and height.</summary>
    private double _imageCenterX = 0.5;

    private double _imageCenterY = 0.5;

    public ViewerDocument Document { get; } = document ?? throw new ArgumentNullException(nameof(document));

    /// <summary>Images of the file, empty when it isn't an image.</summary>
    public IReadOnlyList<EntryImage> Images { get; } = images ?? [];

    /// <summary>True in the image view, showing <see cref="CurrentImage" />.</summary>
    public bool IsImage { get; private set; } = images is { Count: > 0 };

    /// <summary>True in the hex view, false in the text and image views.</summary>
    public bool IsHex { get; private set; } = images is not { Count: > 0 } && document.IsBinary;

    /// <summary>Index of the image shown in the image view.</summary>
    public int ImageIndex { get; private set; }

    /// <summary>The image shown, null when not in the image view.</summary>
    public EntryImage? CurrentImage => IsImage ? Images[ImageIndex] : null;

    /// <summary>Factor the image is zoomed by (one of <see cref="ZoomLevels" />), null when it is fitted to the view.</summary>
    public double? ImageZoom { get; private set; }

    /// <summary>Offset of the top line (text view) or row (hex view, a multiple of 16).</summary>
    public long TopOffset { get; private set; } = document.IsBinary ? 0 : document.TextStart;

    /// <summary>First column of the lines shown in the text view.</summary>
    public int LeftColumn { get; private set; }

    /// <summary>Text found by the last search, highlighted; null when none.</summary>
    public ViewerMatch? Match { get; private set; }

    /// <summary>Options of the last search, used by <see cref="FindNextAsync" />.</summary>
    public ViewerSearchOptions? LastSearch { get; private set; }

    /// <summary>Selected bytes, highlighted and copied; null when nothing is selected.</summary>
    public ViewerSelection? Selection { get; private set; }

    /// <summary>True when the text view wraps long lines at the width of the view instead of scrolling horizontally.</summary>
    public bool IsWordWrap { get; private set; }

    /// <summary>Columns of the view, set by the front-end, which lines are wrapped at.</summary>
    public int Columns { get; private set; } = 80;

    /// <summary>Columns lines are wrapped at in the text view, 0 when not wrapping.</summary>
    private int WrapColumns => IsWordWrap && !IsHex && !IsImage ? Math.Max(1, Columns) : 0;

    /// <summary>
    ///     Turns word wrap of the text view on or off (F2), keeping the position: the top is the row or line containing
    ///     the top shown before.
    /// </summary>
    public void ToggleWordWrap()
    {
        IsWordWrap = !IsWordWrap;
        LeftColumn = 0;
        AlignTop();
    }

    /// <summary>
    ///     Sets the columns of the view, e.g. when it's resized; with word wrap the lines are wrapped again, keeping the
    ///     top row.
    /// </summary>
    public void SetColumns(int columns)
    {
        columns = Math.Max(1, columns);
        if (columns == Columns)
        {
            return;
        }

        Columns = columns;
        if (IsWordWrap)
        {
            AlignTop();
        }
    }

    /// <summary>Moves the top of the text view to the start of the line or wrapped row containing it.</summary>
    private void AlignTop()
    {
        if (!IsHex && !IsImage)
        {
            TopOffset = Document.GetLineOffset(TopOffset, WrapColumns);
        }
    }

    public void Dispose() => Document.Dispose();

    /// <summary>
    ///     Switches between the hex view and the text view (the image view for images), keeping the position.
    /// </summary>
    public void ToggleHex()
    {
        if (IsHex)
        {
            IsHex = false;
            IsImage = Images.Count > 0;
            TopOffset = Document.GetLineOffset(TopOffset, WrapColumns);
            return;
        }

        IsImage = false;
        IsHex = true;
        TopOffset = AlignRow(TopOffset);
    }

    /// <summary>Shows an image of the file in the image view, e.g. chosen from a list, centered with the same zoom.</summary>
    public void ShowImage(int index)
    {
        ImageIndex = Math.Clamp(index, 0, Math.Max(0, Images.Count - 1));
        _imageCenterX = 0.5;
        _imageCenterY = 0.5;
    }

    /// <summary>
    ///     Where the image shown is drawn in a view of a size (in pixels of the view): fitted to the view
    ///     (<see cref="ImagePixels.GetFitScale" />) or zoomed, centered when smaller than the view, otherwise at the part
    ///     moved into view. Null when not in the image view.
    /// </summary>
    public ViewerImageLayout? GetImageLayout(double viewWidth, double viewHeight)
    {
        if (CurrentImage is not { } image || image.Image.Width <= 0 || image.Image.Height <= 0)
        {
            return null;
        }

        var scale = ImageZoom ?? ImagePixels.GetFitScale(image.Image.Width, image.Image.Height, viewWidth, viewHeight);
        var width = image.Image.Width * scale;
        var height = image.Image.Height * scale;
        return new ViewerImageLayout(scale, GetImagePosition(_imageCenterX, width, viewWidth),
            GetImagePosition(_imageCenterY, height, viewHeight), width, height, ImageZoom == null);
    }

    /// <summary>Position of an image's edge in the view, centered when it fits, otherwise keeping the view filled.</summary>
    private static double GetImagePosition(double center, double length, double viewLength) => length <= viewLength
        ? (viewLength - length) / 2
        : Math.Clamp(viewLength / 2 - center * length, viewLength - length, 0);

    /// <summary>Zooms the image shown to the next zoom level larger than shown in a view of a size.</summary>
    public void ZoomIn(double viewWidth, double viewHeight)
    {
        if (GetImageLayout(viewWidth, viewHeight) is { } layout)
        {
            SetZoom(ZoomLevels.FirstOrDefault(level => level > layout.Scale * 1.001, ZoomLevels[^1]));
        }
    }

    /// <summary>Zooms the image shown to the next zoom level smaller than shown in a view of a size.</summary>
    public void ZoomOut(double viewWidth, double viewHeight)
    {
        if (GetImageLayout(viewWidth, viewHeight) is { } layout)
        {
            SetZoom(ZoomLevels.LastOrDefault(level => level < layout.Scale * 0.999, ZoomLevels[0]));
        }
    }

    /// <summary>Zooms the images by a factor (clamped to the zoom levels), null fits them to the view.</summary>
    public void SetZoom(double? zoom) =>
        ImageZoom = zoom is { } factor ? Math.Clamp(factor, ZoomLevels[0], ZoomLevels[^1]) : null;

    /// <summary>
    ///     Moves the image shown in a view of a size by pixels of the view (positive shows more of the right and bottom
    ///     part of the image), e.g. with the arrow keys or by dragging. Returns false when the image fits the view, so
    ///     there is nothing to move (the keys then show the next or previous image).
    /// </summary>
    public bool PanImage(double deltaX, double deltaY, double viewWidth, double viewHeight)
    {
        if (GetImageLayout(viewWidth, viewHeight) is not { } layout ||
            (layout.Width <= viewWidth && layout.Height <= viewHeight))
        {
            return false;
        }

        // from where it is shown, as the center may be outside what can be shown after zooming
        _imageCenterX = (viewWidth / 2 - layout.X + deltaX) / layout.Width;
        _imageCenterY = (viewHeight / 2 - layout.Y + deltaY) / layout.Height;
        return true;
    }

    /// <summary>Text of the zoom of a layout, e.g. "200%" or "Fit 44%".</summary>
    public static string GetZoomText(ViewerImageLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var percent = $"{Math.Round(layout.Scale * 100):0}%";
        return layout.IsFitted ? $"Fit {percent}" : percent;
    }

    /// <summary>Name, size and color depth of an image, e.g. "Selected (GlowIcon), 46 × 46, 8-bit".</summary>
    public string GetImageText(int index) =>
        $"{Images[index].Name}, {ImagePixels.FormatSize(Images[index].Image)}";

    /// <summary>Lines shown in the text view.</summary>
    public IReadOnlyList<ViewerLine> GetLines(int rows) => Document.ReadLines(TopOffset, rows, WrapColumns);

    /// <summary>Rows shown in the hex view.</summary>
    public IReadOnlyList<ViewerHexRow> GetHexRows(int rows)
    {
        var bytes = Document.ReadBytes(TopOffset, Math.Max(0, rows) * ViewerDocument.BytesPerRow);
        var hexRows = new List<ViewerHexRow>();
        for (var index = 0; index < bytes.Length; index += ViewerDocument.BytesPerRow)
        {
            var rowBytes = bytes[index..Math.Min(bytes.Length, index + ViewerDocument.BytesPerRow)];
            var offset = TopOffset + index;
            hexRows.Add(new ViewerHexRow(offset, rowBytes,
                ViewerDocument.FormatHexRow(offset, rowBytes, Document.HexOffsetDigits)));
        }

        return hexRows;
    }

    /// <summary>True when a byte is in the text found, highlighted in the hex view.</summary>
    public bool IsMatchByte(long offset) =>
        Match is { } match && offset >= match.ByteOffset && offset < match.ByteOffset + match.ByteLength;

    /// <summary>
    ///     Columns of the text found in a line of the text view, null when not in the line. Found by its bytes, so a
    ///     match continuing on the next wrapped row is highlighted in both.
    /// </summary>
    public (int Column, int Length)? GetMatchColumns(ViewerLine line)
    {
        if (Match is not { } match || match.ByteOffset >= line.Offset + line.ContentLength ||
            match.ByteOffset + match.ByteLength <= line.Offset)
        {
            return null;
        }

        var start = Document.GetColumn(line, match.ByteOffset);
        var end = Document.GetColumn(line, match.ByteOffset + match.ByteLength);
        return end > start ? (start, end - start) : null;
    }

    /// <summary>Scrolls a number of lines or rows down (positive) or up (negative), e.g. a page.</summary>
    public void Scroll(int lines, int rows)
    {
        if (IsImage)
        {
            ShowImage(ImageIndex + Math.Sign(lines));
            return;
        }

        if (IsHex)
        {
            TopOffset = Math.Clamp(TopOffset + (long)lines * ViewerDocument.BytesPerRow, 0, GetLastHexTop(rows));
            return;
        }

        var offset = Document.MoveLines(TopOffset, lines, WrapColumns);
        TopOffset = lines > 0
            ? Math.Min(offset, Math.Max(TopOffset, Document.GetLastPageOffset(rows, WrapColumns)))
            : offset;
    }

    /// <summary>Scrolls to the start of the file.</summary>
    public void ScrollToStart()
    {
        if (IsImage)
        {
            ShowImage(0);
        }

        TopOffset = IsHex ? 0 : Document.TextStart;
        LeftColumn = 0;
    }

    /// <summary>Scrolls to the last page of the file (the last image in the image view).</summary>
    public void ScrollToEnd(int rows)
    {
        if (IsImage)
        {
            ShowImage(Images.Count - 1);
            return;
        }

        TopOffset = IsHex ? GetLastHexTop(rows) : Document.GetLastPageOffset(rows, WrapColumns);
    }

    /// <summary>Scrolls to a part of the file, from 0 (start) to 1 (end), e.g. with a scroll bar.</summary>
    public void ScrollTo(double fraction, int rows)
    {
        if (IsImage)
        {
            ShowImage((int)Math.Round(Math.Clamp(fraction, 0, 1) * (Images.Count - 1)));
            return;
        }

        var position = (long)(Math.Clamp(fraction, 0, 1) * Document.Length);
        TopOffset = IsHex
            ? Math.Min(AlignRow(position), GetLastHexTop(rows))
            : Math.Min(Document.GetLineOffset(position, WrapColumns), Document.GetLastPageOffset(rows, WrapColumns));
    }

    /// <summary>
    ///     Scrolls the text view a number of columns right (positive) or left (negative), at most until the end of the
    ///     longest line shown is in view. Wrapped lines fit the view, so they aren't scrolled.
    /// </summary>
    public void ScrollColumns(int delta, int rows, int columns)
    {
        if (IsImage)
        {
            ShowImage(ImageIndex + Math.Sign(delta));
            return;
        }

        if (WrapColumns > 0)
        {
            return;
        }

        LeftColumn = Math.Clamp(LeftColumn + delta, 0, Math.Max(LeftColumn, GetMaxLineWidth(rows) - columns));
    }

    /// <summary>Scrolls the text view to a first column, e.g. with a scroll bar.</summary>
    public void ScrollToColumn(int column) => LeftColumn = WrapColumns > 0 ? 0 : Math.Max(0, column);

    /// <summary>Width of the longest line shown in the text view, e.g. for a scroll bar.</summary>
    public int GetMaxLineWidth(int rows) =>
        IsHex ? 0 : GetLines(rows).Select(line => line.Text.Length).DefaultIfEmpty().Max();

    /// <summary>Part of the file before the top line or row, from 0 to 1, e.g. for a scroll bar.</summary>
    public double TopFraction => Document.Length == 0 ? 0 : (double)TopOffset / Document.Length;

    /// <summary>
    ///     Status shown with the view: the offset of the top line or row, the size, how much of the file has been shown
    ///     and the encoding (text view), e.g. <c>Offset 1,024 of 4,096 bytes, 50%, UTF-8</c>; in the image view the image
    ///     shown and the zoom of its layout when given, e.g. <c>Image 1 of 2: ILBM, 320 × 256, 5-bit, 4,096 bytes, 200%</c>.
    /// </summary>
    public string GetStatusText(int rows, ViewerImageLayout? imageLayout = null)
    {
        if (IsImage)
        {
            var image = $"Image {ImageIndex + 1} of {Images.Count}: {GetImageText(ImageIndex)}, {Document.Length:N0} bytes";
            return imageLayout != null ? $"{image}, {GetZoomText(imageLayout)}" : image;
        }

        var end = IsHex
            ? Math.Min(Document.Length, TopOffset + (long)rows * ViewerDocument.BytesPerRow)
            : GetLines(rows) is { Count: > 0 } lines ? lines[^1].Offset + lines[^1].Length : Document.Length;
        var percent = Document.Length == 0 ? 100 : (int)(end * 100 / Document.Length);
        var status = $"Offset {TopOffset:N0} of {Document.Length:N0} bytes, {percent}%";
        status = IsHex ? $"{status}, Hex" : $"{status}, {Document.EncodingName}";
        status = WrapColumns > 0 ? $"{status}, Wrap" : status;
        return Selection is { } selection ? $"{status}, {selection.End - selection.Start:N0} bytes selected" : status;
    }

    /// <summary>
    ///     Starts selecting at a cell of the view (row and column from the upper left corner), e.g. where the mouse
    ///     button is pressed, clearing the selection. In the hex view the selection is of the hex values or of the text,
    ///     by the column it is started in.
    /// </summary>
    public void StartSelection(int row, int column, int rows)
    {
        if (IsImage)
        {
            return;
        }

        _anchor = GetCell(row, column, rows);
        _anchorHexValues = IsHex && column < ViewerDocument.GetHexTextColumn(Document.HexOffsetDigits, 0) - 1;
        Selection = null;
    }

    /// <summary>
    ///     Selects from where selecting started to a cell of the view, both characters (or bytes) included, e.g. while
    ///     the mouse is dragged. A cell after the end of a line selects its line break.
    /// </summary>
    public void ExtendSelection(int row, int column, int rows)
    {
        if (IsImage)
        {
            return;
        }

        if (_anchor is not var (anchorStart, anchorEnd))
        {
            return;
        }

        var (start, end) = GetCell(row, column, rows);
        start = Math.Min(start, anchorStart);
        end = Math.Max(end, anchorEnd);
        Selection = start < end ? new ViewerSelection(start, end, _anchorHexValues) : null;
    }

    /// <summary>Selects the whole file (the hex values in the hex view).</summary>
    public void SelectAll()
    {
        if (IsImage)
        {
            return;
        }

        var start = IsHex ? 0 : Document.TextStart;
        _anchor = (start, start);
        _anchorHexValues = IsHex;
        Selection = Document.Length > start ? new ViewerSelection(start, Document.Length, IsHex) : null;
    }

    public void ClearSelection()
    {
        _anchor = null;
        Selection = null;
    }

    /// <summary>
    ///     The selected text to copy: in the text view as it is in the file (tabs and line breaks kept), in the hex view
    ///     the hex values (a line per row) or the text of the hex view, by where it was selected. Null when nothing is
    ///     selected.
    /// </summary>
    /// <exception cref="IOException">The selection is larger than <see cref="ViewerDocument.MaxCopyLength" />.</exception>
    public string? GetSelectedText() => Selection switch
    {
        null => null,
        _ when IsImage => null,
        { IsHexValues: true } selection => Document.FormatHexBytes(selection.Start, selection.End, true),
        var selection when IsHex => Document.FormatHexBytes(selection.Start, selection.End, false),
        var selection => Document.ReadText(selection.Start, selection.End)
    };

    /// <summary>True when a byte is selected, highlighted in the hex view.</summary>
    public bool IsSelected(long offset) =>
        Selection is { } selection && offset >= selection.Start && offset < selection.End;

    /// <summary>
    ///     Columns of the selection in a line of the text view, one more than the text when its line break is selected;
    ///     null when nothing of the line is selected.
    /// </summary>
    public (int Column, int Length)? GetSelectionColumns(ViewerLine line)
    {
        if (Selection is not { } selection || selection.End <= line.Offset ||
            selection.Start >= line.Offset + line.Length)
        {
            return null;
        }

        var start = Document.GetColumn(line, selection.Start);
        var contentEnd = line.Offset + line.ContentLength;
        var end = selection.End > contentEnd
            ? line.Text.Length + (line.Length > line.ContentLength ? 1 : 0)
            : Document.GetColumn(line, selection.End);
        return end > start ? (start, end - start) : null;
    }

    /// <summary>The bytes of the character or byte at a cell of the view: its offset and the offset after it.</summary>
    private (long Start, long End) GetCell(int row, int column, int rows)
    {
        column = Math.Max(0, column);
        if (!IsHex)
        {
            var lines = GetLines(Math.Max(rows, row + 1));
            return row < 0
                ? (TopOffset, TopOffset)
                : row < lines.Count
                    ? Document.GetCharacterRange(lines[row], LeftColumn + column)
                    : (Document.Length, Document.Length);
        }

        var digits = Document.HexOffsetDigits;
        var textColumn = ViewerDocument.GetHexTextColumn(digits, 0);
        int index;
        if (column >= textColumn - 1)
        {
            index = column - textColumn;
        }
        else
        {
            // the hex values take 3 columns, with an extra space after 8
            var hexColumn = column - (digits + 2);
            index = (hexColumn >= ViewerDocument.BytesPerRow / 2 * 3 ? hexColumn - 1 : hexColumn) / 3;
        }

        var offset = TopOffset + (long)Math.Max(0, row) * ViewerDocument.BytesPerRow +
                     Math.Clamp(index, 0, ViewerDocument.BytesPerRow - 1);
        return offset < Document.Length ? (offset, offset + 1) : (Document.Length, Document.Length);
    }

    /// <summary>
    ///     Searches from the position shown with new options: from the top of the view, backwards from before it. The
    ///     text found is highlighted and scrolled into view. Returns false when not found.
    /// </summary>
    /// <exception cref="IOException">The text is empty or an invalid regular expression.</exception>
    public Task<bool> FindAsync(ViewerSearchOptions options, int rows, int columns, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        LastSearch = options;
        Match = null;
        ShowHexForSearch();
        var (lineOffset, column) = GetSearchStart();
        return FindAsync(options, lineOffset, column, rows, columns, progress, cancellationToken);
    }

    /// <summary>
    ///     Searches for the next (or previous) match of the last search, from the text found. Returns false when not
    ///     found or nothing was searched for yet.
    /// </summary>
    public Task<bool> FindNextAsync(bool backwards, int rows, int columns, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (LastSearch is not { } search)
        {
            return Task.FromResult(false);
        }

        ShowHexForSearch();
        var options = search with { Backwards = backwards };
        if (Match is not { } match)
        {
            var (lineOffset, column) = GetSearchStart();
            return FindAsync(options, lineOffset, column, rows, columns, progress, cancellationToken);
        }

        return FindAsync(options, match.LineOffset, backwards ? match.Column : match.Column + 1, rows, columns,
            progress, cancellationToken);
    }

    /// <summary>
    ///     Where searching from the top of the view starts: searching uses lines that aren't wrapped, so a wrapped row at
    ///     the top starts at its column in its line.
    /// </summary>
    private (long LineOffset, int Column) GetSearchStart()
    {
        var lineOffset = Document.GetLineOffset(TopOffset);
        if (IsHex || lineOffset == TopOffset || Document.ReadLines(lineOffset, 1) is not [var line])
        {
            return (lineOffset, 0);
        }

        return (lineOffset, Document.GetColumn(line, TopOffset));
    }

    private async Task<bool> FindAsync(ViewerSearchOptions options, long lineOffset, int column, int rows,
        int columns, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var match = await Document.FindAsync(options, lineOffset, column, progress, cancellationToken);
        if (match == null)
        {
            return false;
        }

        Match = match;
        ShowMatch(match, rows, columns);
        return true;
    }

    /// <summary>Scrolls the text found into view, a third down from the top when it isn't shown.</summary>
    private void ShowMatch(ViewerMatch match, int rows, int columns)
    {
        if (IsHex)
        {
            var rowBytes = (long)Math.Max(1, rows) * ViewerDocument.BytesPerRow;
            if (match.ByteOffset < TopOffset || match.ByteOffset >= TopOffset + rowBytes)
            {
                TopOffset = Math.Min(AlignRow(Math.Max(0, match.ByteOffset - rowBytes / 3)), GetLastHexTop(rows));
            }

            return;
        }

        var wrapColumns = WrapColumns;
        if (GetLines(rows).All(line =>
                match.ByteOffset < line.Offset || match.ByteOffset >= line.Offset + Math.Max(1, line.Length)))
        {
            var matchRow = Document.GetLineOffset(match.ByteOffset, wrapColumns);
            TopOffset = Math.Min(Document.MoveLines(matchRow, -(Math.Max(1, rows) / 3), wrapColumns),
                Math.Max(matchRow, Document.GetLastPageOffset(rows, wrapColumns)));
        }

        if (wrapColumns > 0)
        {
            return;
        }

        columns = Math.Max(1, columns);
        if (match.Column < LeftColumn || match.Column + match.Length > LeftColumn + columns)
        {
            LeftColumn = Math.Max(0, Math.Min(match.Column, match.Column + match.Length - columns + columns / 4));
        }
    }

    /// <summary>Searching shows the text found in the hex view instead of the image view.</summary>
    private void ShowHexForSearch()
    {
        if (IsImage)
        {
            ToggleHex();
        }
    }

    private long GetLastHexTop(int rows) =>
        Math.Max(0, Document.LastHexRowOffset - (long)(Math.Max(1, rows) - 1) * ViewerDocument.BytesPerRow);

    private static long AlignRow(long offset) => offset / ViewerDocument.BytesPerRow * ViewerDocument.BytesPerRow;
}

/// <summary>
///     Where an image is drawn in the view: the factor it is scaled by, the position of its upper left corner (negative
///     when it starts outside the view) and its size, in pixels of the view; <see cref="IsFitted" /> when fitted to the
///     view.
/// </summary>
public sealed record ViewerImageLayout(double Scale, double X, double Y, double Width, double Height, bool IsFitted);

/// <summary>A row of the hex view: its offset, its bytes and the row formatted by <see cref="ViewerDocument.FormatHexRow" />.</summary>
public sealed record ViewerHexRow(long Offset, byte[] Bytes, string Text);

/// <summary>
///     Bytes selected in the viewer, from <see cref="Start" /> to before <see cref="End" />; in the hex view selected in
///     the hex values (<see cref="IsHexValues" />, copied as hex values) or the text.
/// </summary>
public sealed record ViewerSelection(long Start, long End, bool IsHexValues);
