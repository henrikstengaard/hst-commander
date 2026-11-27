using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Gui;

/// <summary>
///     Contents of a tab of a plugin in the <see cref="PropertiesDialog" /> (see <see cref="PropertiesTab" />), e.g. the
///     comment of an Amiga entry. Without a text it shows <see cref="PropertiesTab.NoTextMessage" /> and an add button
///     showing an empty text field; with a text it shows the text in a text field and a delete button right below it,
///     which hides the text field again. The text field has a fixed height and scrolls longer texts: a few lines for a
///     single-line text (e.g. a comment, wrapped), more for multi-line texts (e.g. tooltypes). The tab has the same
///     height in all cases, so the dialog keeps its size. The buttons are enabled and the text field editable when the
///     text can be changed. Nothing is saved here: the dialog reads <see cref="EditedText" /> when Save is clicked.
/// </summary>
internal sealed class PropertiesTabView : StackPanel
{
    /// <summary>Height of the text field of multi-line texts in pixels; longer texts scroll.</summary>
    private const double MultilineTextHeight = 160;

    /// <summary>Height of the text field of single-line texts in pixels, a few wrapped lines; longer texts scroll.</summary>
    private const double SingleLineTextHeight = 72;

    /// <summary>Space between the text and the buttons in pixels.</summary>
    private const double ButtonSpacing = 4;

    /// <summary>Height of the tab in pixels: the largest text field and the buttons.</summary>
    private const double TabHeight = MultilineTextHeight + ButtonSpacing + 32;

    private readonly double _textHeight;
    private readonly Panel _textArea;
    private readonly TextBlock _noTextBlock;
    private readonly TextBox _textBox;
    private readonly Button _addButton;
    private readonly Button _deleteButton;
    private bool _hasText;

    public PropertiesTabView(PropertiesTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);

        Spacing = ButtonSpacing;
        Height = TabHeight;
        VerticalAlignment = VerticalAlignment.Top;
        _textHeight = tab.IsMultiline ? MultilineTextHeight : SingleLineTextHeight;
        _noTextBlock = new TextBlock { Text = tab.NoTextMessage };
        // a single-line text (e.g. a comment) is wrapped, lines of multi-line texts (e.g. tooltypes) aren't
        _textBox = new TextBox
        {
            Text = tab.Text?.ReplaceLineEndings(Environment.NewLine) ?? string.Empty,
            IsReadOnly = !tab.CanChange,
            AcceptsReturn = tab.IsMultiline,
            TextWrapping = tab.IsMultiline ? Avalonia.Media.TextWrapping.NoWrap : Avalonia.Media.TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Top,
            // the maximum length of multi-line texts is of each line
            MaxLength = tab.IsMultiline ? 0 : tab.MaxLength
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_textBox, tab.IsMultiline
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_textBox, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        _addButton = new Button { Content = tab.AddText, IsEnabled = tab.CanChange };
        _deleteButton = new Button { Content = tab.DeleteText, IsEnabled = tab.CanChange };
        _addButton.Click += (_, _) =>
        {
            _textBox.Text = string.Empty;
            ShowText(true);
            Dispatcher.UIThread.Post(() => _textBox.Focus());
        };
        _deleteButton.Click += (_, _) =>
        {
            ShowText(false);
            DialogButtons.Focus(_addButton);
        };

        // the message without a text takes the place of the text, the buttons are right below them
        _textArea = new Panel { Children = { _noTextBlock, _textBox } };
        Children.Add(_textArea);
        Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _addButton, _deleteButton }
        });
        ShowText(tab.Text != null);
    }

    /// <summary>The text with "\n" line breaks, null when there is none (not added or deleted).</summary>
    public string? EditedText => _hasText ? (_textBox.Text ?? string.Empty).ReplaceLineEndings("\n") : null;

    private void ShowText(bool hasText)
    {
        _hasText = hasText;
        _textArea.Height = hasText ? _textHeight : double.NaN;
        _noTextBlock.IsVisible = !hasText;
        _addButton.IsVisible = !hasText;
        _textBox.IsVisible = hasText;
        _deleteButton.IsVisible = hasText;
    }
}
