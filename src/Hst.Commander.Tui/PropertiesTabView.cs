using Hst.Commander.Abstractions;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     Contents of a tab of a plugin in the <see cref="PropertiesDialog" /> (see <see cref="PropertiesTab" />), e.g. the
///     comment of an Amiga entry. Without a text it shows <see cref="PropertiesTab.NoTextMessage" /> and an add button
///     showing an empty text view; with a text it shows the text in a text view and a delete button, which hides the
///     text view again; the buttons are right below the message and the text view. The text view has a fixed height
///     with a scroll bar when the text is longer: the rows the maximum length needs for a single-line text (e.g. a comment, word wrapped and
///     limited to <see cref="PropertiesTab.MaxLength" />), more for multi-line texts (e.g. tooltypes, not wrapped). The buttons are enabled and the text editable when the text
///     can be changed. Nothing is saved here: the dialog reads <see cref="EditedText" /> when Save is pressed.
/// </summary>
internal sealed class PropertiesTabView : View
{
    /// <summary>Rows of the text view of multi-line texts; longer texts scroll.</summary>
    private const int MultilineTextRows = 8;

    /// <summary>Most rows of the text view of single-line texts; longer texts scroll.</summary>
    private const int MaxSingleLineTextRows = 3;

    private readonly PropertiesTab _tab;
    private readonly Label _noTextLabel;
    private readonly TextView _textView;
    private readonly ScrollBar _scrollBar;
    private readonly Button _addButton;
    private readonly Button _deleteButton;
    private string _validText;
    private bool _hasText;
    private bool _updating;

    public PropertiesTabView(PropertiesTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        _tab = tab;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        _noTextLabel = new Label { X = 0, Y = 0, Text = tab.NoTextMessage };
        _validText = tab.Text ?? string.Empty;
        _textView = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Func(GetTextRows),
            Text = _validText,
            ReadOnly = !tab.CanChange,
            AllowsTab = false,
            // Enter saves a single-line text like other dialogs, but adds a line to multi-line texts
            AllowsReturn = tab.IsMultiline,
            WordWrap = !tab.IsMultiline
        };
        _textView.KeyDown += (_, key) => key.Handled = IsTooLong(key);
        _textView.ContentsChanged += (_, _) => LimitText();
        _textView.DrawComplete += (_, _) => UpdateScrollBar();

        _scrollBar = new ScrollBar
        {
            X = Pos.AnchorEnd(1),
            Y = 0,
            Height = Dim.Func(GetTextRows),
            Orientation = Orientation.Vertical,
            AutoShow = true
        };
        _scrollBar.PositionChanged += (_, e) =>
        {
            if (_updating)
            {
                return;
            }

            _textView.TopRow = e.CurrentValue;
            _textView.SetNeedsDraw();
        };

        // the buttons are right below the message and the text view
        _addButton = new Button { X = 0, Y = 1, Text = tab.AddText, Enabled = tab.CanChange };
        _deleteButton = new Button { X = 0, Y = Pos.Bottom(_textView), Text = tab.DeleteText, Enabled = tab.CanChange };
        _addButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            SetText(string.Empty);
            ShowText(true);
            _textView.SetFocus();
        };
        _deleteButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            ShowText(false);
            _addButton.SetFocus();
        };

        Add(_noTextLabel, _textView, _scrollBar, _addButton, _deleteButton);
        ShowText(tab.Text != null);
    }

    /// <summary>
    ///     Rows of the text view: those of multi-line texts, or for a single-line text as many as its maximum length
    ///     needs wrapped in the width of the text view (at most <see cref="MaxSingleLineTextRows" />), so there are no
    ///     empty rows between the text and the delete button, as the text view has no border.
    /// </summary>
    private int GetTextRows()
    {
        if (_tab.IsMultiline)
        {
            return MultilineTextRows;
        }

        if (_tab.MaxLength <= 0)
        {
            return MaxSingleLineTextRows;
        }

        // the text view's width without the scroll bar column, with a column for the cursor after the text
        var width = Math.Max(Viewport.Width - 1, 1);
        return Math.Clamp((_tab.MaxLength + width) / width, 1, MaxSingleLineTextRows);
    }

    /// <summary>Rows needed by a tab, the same for all tabs: the largest text view and the buttons.</summary>
    public static int Rows => MultilineTextRows + 1;

    /// <summary>The text with "\n" line breaks, null when there is none (not added or deleted).</summary>
    public string? EditedText => _hasText ? _textView.Text.ReplaceLineEndings("\n") : null;

    /// <summary>True when a typed character would make a single-line text longer than its maximum length.</summary>
    private bool IsTooLong(Key key) =>
        !_tab.IsMultiline && _tab.MaxLength > 0 && key.AsRune is { Value: >= ' ' } &&
        !key.IsCtrl && !key.IsAlt && _textView.SelectedLength == 0 &&
        _textView.Text.Length >= _tab.MaxLength;

    /// <summary>
    ///     Keeps a single-line text on one line and within its maximum length, e.g. when pasted, by going back to the
    ///     last valid text.
    /// </summary>
    private void LimitText()
    {
        if (_updating || _tab.IsMultiline)
        {
            return;
        }

        var text = _textView.Text;
        if (text.Contains('\n') || text.Contains('\r') || (_tab.MaxLength > 0 && text.Length > _tab.MaxLength))
        {
            var cursorPosition = _textView.CursorPosition;
            var singleLine = text.ReplaceLineEndings(" ");
            SetText(_tab.MaxLength > 0 && singleLine.Length > _tab.MaxLength ? _validText : singleLine);
            _textView.CursorPosition = cursorPosition;
            return;
        }

        _validText = text;
    }

    private void SetText(string text)
    {
        _updating = true;
        try
        {
            _textView.Text = text;
            _validText = text;
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>Shows the scroll bar when the text has more rows than shown, at the first row shown.</summary>
    private void UpdateScrollBar()
    {
        _updating = true;
        try
        {
            _scrollBar.ScrollableContentSize = Math.Max(_textView.Lines, 1);
            _scrollBar.VisibleContentSize = GetTextRows();
            _scrollBar.Position = _textView.TopRow;
        }
        finally
        {
            _updating = false;
        }
    }

    private void ShowText(bool hasText)
    {
        _hasText = hasText;
        _noTextLabel.Visible = !hasText;
        _addButton.Visible = !hasText;
        _textView.Visible = hasText;
        _deleteButton.Visible = hasText;
        if (!hasText)
        {
            // the scroll bar shows itself while its content is larger than shown
            _updating = true;
            _scrollBar.ScrollableContentSize = 0;
            _updating = false;
        }
    }
}
