using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Hst.Commander.Core;

namespace Hst.Commander.Gui;

/// <summary>
///     Modal built-in editor (F4) of a text file in a monospace font. F2 or Ctrl+S saves, F7 or Ctrl+F searches for text
///     or a regular expression, F4 or Ctrl+H replaces (one at a time or all), F5 finds the next match and F6 the previous
///     one, F8 toggles word wrap and Ctrl+G goes to a line. Editing keys are the text box's (Ctrl+Z undoes, Ctrl+Y
///     redoes, also replacing). Esc or F10 closes the editor, asking to save changes.
/// </summary>
public partial class EditorDialog : Window
{
    private readonly EditorSession? _session;
    private string? _message;
    private bool _closeConfirmed;

    public EditorDialog()
    {
        InitializeComponent();
    }

    public EditorDialog(EditorSession session) : this()
    {
        _session = session;
        Title = $"Edit - {session.Name}";
        TextBox.Text = session.Document.Text;
        TextBox.IsReadOnly = session.IsReadOnly;
        SetWordWrap(false);
        TextBox.CaretIndex = 0;
        TextBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty || e.Property == TextBox.CaretIndexProperty)
            {
                UpdateStatus();
            }
        };

        SaveButton.Click += async (_, _) => await SaveAsync();
        ReplaceButton.Click += async (_, _) => await ReplaceAsync();
        NextButton.Click += async (_, _) => await FindNextAsync(false);
        PreviousButton.Click += async (_, _) => await FindNextAsync(true);
        SearchButton.Click += async (_, _) => await SearchAsync();
        WrapButton.Click += (_, _) => ToggleWordWrap();
        CloseButton.Click += (_, _) => Close();
        GoToLineButton.Click += async (_, _) => await GoToLineAsync();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Closing += OnClosing;
        Opened += (_, _) =>
        {
            TextBox.Focus();
            UpdateStatus();
        };
    }

    private string Text => TextBox.Text ?? string.Empty;

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (_session == null || OwnedWindows.Count > 0)
        {
            return;
        }

        var control = e.KeyModifiers == KeyModifiers.Control;
        switch (e.Key)
        {
            case Key.F2:
            case Key.S when control:
                e.Handled = true;
                await SaveAsync();
                break;
            case Key.F4:
            case Key.H when control:
                e.Handled = true;
                await ReplaceAsync();
                break;
            case Key.F5:
                e.Handled = true;
                await FindNextAsync(false);
                break;
            case Key.F6:
                e.Handled = true;
                await FindNextAsync(true);
                break;
            case Key.F7:
            case Key.F when control:
                e.Handled = true;
                await SearchAsync();
                break;
            case Key.F8:
                e.Handled = true;
                ToggleWordWrap();
                break;
            case Key.G when control:
                e.Handled = true;
                await GoToLineAsync();
                break;
            case Key.Escape:
            case Key.F10:
                e.Handled = true;
                Close();
                break;
        }
    }

    /// <summary>
    ///     Asks to save changes before closing: closing is cancelled while the dialog is shown, and the window closed
    ///     again when saved or discarded.
    /// </summary>
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || _session == null || _session.IsReadOnly || !_session.IsModified(Text))
        {
            return;
        }

        e.Cancel = true;
        if (OwnedWindows.Count > 0)
        {
            return;
        }

        var save = await new SaveChangesDialog(_session.Name).ShowDialog<bool?>(this);
        if (save == false || (save == true && await TrySaveAsync()))
        {
            _closeConfirmed = true;
            Close();
            return;
        }

        TextBox.Focus();
    }

    private async Task SaveAsync()
    {
        if (await TrySaveAsync())
        {
            _message = $"Saved '{_session!.Name}'";
            UpdateStatus();
        }

        TextBox.Focus();
    }

    /// <summary>Saves the text, showing errors (e.g. a character the file's encoding can't hold) and returning false.</summary>
    private async Task<bool> TrySaveAsync()
    {
        if (_session!.IsReadOnly)
        {
            await new ErrorDialog(_session.ReadOnlyReason!).ShowDialog(this);
            return false;
        }

        try
        {
            await _session.SaveAsync(Text);
            UpdateStatus();
            return true;
        }
        catch (Exception exception)
        {
            await new ErrorDialog(exception.Message).ShowDialog(this);
            return false;
        }
    }

    private async Task SearchAsync()
    {
        var request = await new EditorSearchDialog(_session!.LastSearch, false).ShowDialog<EditorSearchRequest?>(this);
        TextBox.Focus();
        if (request != null)
        {
            await FindAsync(request.Options with { Replacement = _session.LastSearch?.Replacement }, false);
        }
    }

    private async Task ReplaceAsync()
    {
        if (_session!.IsReadOnly)
        {
            await new ErrorDialog(_session.ReadOnlyReason!).ShowDialog(this);
            return;
        }

        var request = await new EditorSearchDialog(_session.LastSearch, true).ShowDialog<EditorSearchRequest?>(this);
        TextBox.Focus();
        if (request == null)
        {
            return;
        }

        if (!request.ReplaceAll)
        {
            await FindAsync(request.Options, true);
            return;
        }

        _session.LastSearch = request.Options;
        try
        {
            var (text, count) = EditorSearch.ReplaceAll(Text, request.Options);
            if (count > 0)
            {
                // replaced as the selected text, so it can be undone
                var caretIndex = TextBox.CaretIndex;
                TextBox.SelectAll();
                TextBox.SelectedText = text;
                TextBox.CaretIndex = Math.Min(caretIndex, text.Length);
            }

            _message = count == 0 ? $"'{request.Options.Text}' not found" : $"Replaced {count:N0} matches";
            UpdateStatus();
        }
        catch (Exception exception)
        {
            await new ErrorDialog(exception.Message).ShowDialog(this);
        }
    }

    private async Task FindNextAsync(bool backwards)
    {
        if (_session!.LastSearch is not { } search)
        {
            await SearchAsync();
            return;
        }

        await FindAsync(search with { Backwards = backwards }, false);
    }

    /// <summary>
    ///     Finds the next match from the caret (backwards from the start of the selection). Replacing replaces the
    ///     selected text first when it's a match, so pressing F4 again replaces the next one.
    /// </summary>
    private async Task FindAsync(EditorSearchOptions options, bool replace)
    {
        _session!.LastSearch = options;
        try
        {
            var text = Text;
            var index = Math.Min(TextBox.SelectionStart, TextBox.SelectionEnd);
            var length = Math.Abs(TextBox.SelectionEnd - TextBox.SelectionStart);
            var start = options.Backwards ? index : index + length;
            if (replace && EditorSearch.IsMatch(text, options, index, length))
            {
                var replacement = EditorSearch.GetReplacement(text, options, new EditorMatch(index, length));
                Select(index, length);
                TextBox.SelectedText = replacement;
                text = Text;
                start = options.Backwards ? index : index + replacement.Length;
            }

            if (EditorSearch.Find(text, options, start) is not { } match)
            {
                _message = $"'{options.Text}' not found";
            }
            else
            {
                Select(match.Index, match.Length);
                if (match.Wrapped)
                {
                    _message = options.Backwards ? "Continued from the end" : "Continued from the start";
                }
            }
        }
        catch (Exception exception)
        {
            await new ErrorDialog(exception.Message).ShowDialog(this);
        }

        UpdateStatus();
    }

    private async Task GoToLineAsync()
    {
        var (line, _) = EditorSession.GetLineColumn(Text, TextBox.CaretIndex);
        var number = await new GoToLineDialog(line).ShowDialog<int?>(this);
        TextBox.Focus();
        if (number is { } lineNumber)
        {
            Select(EditorSession.GetLineIndex(Text, lineNumber), 0);
            TextBox.ScrollToLine(Math.Max(0, lineNumber - 1));
        }
    }

    /// <summary>Selects text with the caret at its end, scrolled into view.</summary>
    private void Select(int index, int length)
    {
        TextBox.CaretIndex = index + length;
        TextBox.SelectionStart = index;
        TextBox.SelectionEnd = index + length;
    }

    private void ToggleWordWrap() => SetWordWrap(TextBox.TextWrapping == TextWrapping.NoWrap);

    private void SetWordWrap(bool wordWrap)
    {
        TextBox.TextWrapping = wordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        WrapButtonText.Text = wordWrap ? "Unwrap" : "Wrap";
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_session == null)
        {
            return;
        }

        var text = Text;
        var (line, column) = EditorSession.GetLineColumn(text, TextBox.CaretIndex);
        StatusText.Text = _message ?? _session.GetStatusText(line, column, _session.IsModified(text),
            TextBox.TextWrapping != TextWrapping.NoWrap);
        // a message is shown until the next change
        _message = null;
    }
}
