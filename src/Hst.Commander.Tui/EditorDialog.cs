using Hst.Commander.Core;
using Terminal.Gui;
using TuiAttribute = Terminal.Gui.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal full screen built-in editor (F4) of a text file. F2 or Ctrl+S saves, F7 or Ctrl+F searches for text or a
///     regular expression, F4 replaces (one at a time or all), F5 finds the next match and F6 the previous one, F8
///     toggles word wrap and Ctrl+G goes to a line. Editing keys are Terminal.Gui's text view's, with Ctrl+V pasting and
///     Ctrl+Y redoing like desktop editors (Ctrl+Z undoes, Ctrl+C copies, Ctrl+X cuts, Ctrl+A selects all). Esc or F10
///     closes the editor, asking to save changes.
/// </summary>
public sealed class EditorDialog : Dialog
{
    private readonly EditorSession _session;
    private readonly TuiPalette _palette;
    private readonly Label _statusLabel;
    private readonly EditorTextView _textView;
    private readonly FunctionKeyBarView _keyBar;
    private (int Line, int Column) _cursor = (1, 1);
    private bool _modified;
    private string? _message;

    public EditorDialog(EditorSession session, TuiPalette palette)
    {
        _session = session;
        _palette = palette;
        Title = $"Edit - {session.Name}";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        ColorScheme = palette.DialogColorScheme;

        var text = new TuiAttribute(palette.Item, palette.PaneBackground);
        _statusLabel = new Label { X = 0, Y = 0, Width = Dim.Fill(), Height = 1 };
        _textView = new EditorTextView
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Text = session.Document.Text,
            ReadOnly = session.IsReadOnly,
            ColorScheme = new ColorScheme
            {
                Normal = text,
                Focus = text,
                HotNormal = text,
                HotFocus = text,
                Disabled = text
            }
        };
        _keyBar = new FunctionKeyBarView
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            KeyAttribute = palette.FunctionKey,
            TitleAttribute = palette.Menu
        };

        _textView.KeyDown += (_, key) => key.Handled = HandleKey(key);
        // raised when the text view is initialized too, so the text is compared with the text saved, which also
        // shows it unmodified after undoing all changes
        _textView.ContentsChanged += (_, _) =>
        {
            _modified = _session.IsModified(_textView.EditedText);
            UpdateStatus();
        };
        _textView.UnwrappedCursorPosition += (_, position) =>
        {
            _cursor = (position.Y + 1, position.X + 1);
            UpdateStatus();
        };
        // esc closes the dialog without asking, so it's handled like f10
        Closing += (_, e) => e.Cancel = !ConfirmClose();

        Add(_statusLabel, _textView, _keyBar);
        UpdateKeyBar();
        Initialized += (_, _) =>
        {
            _textView.SetFocus();
            UpdateStatus();
        };
    }

    /// <summary>True when the file was saved, so the panes are refreshed.</summary>
    public bool HasSaved => _session.HasSaved;

    private bool HandleKey(Key key)
    {
        if (key.KeyCode == KeyCode.F2 || key == Key.S.WithCtrl)
        {
            _ = SaveAsync();
            return true;
        }

        if (key.KeyCode == KeyCode.F4)
        {
            Replace();
            return true;
        }

        if (key.KeyCode == KeyCode.F5)
        {
            FindNext(false);
            return true;
        }

        if (key.KeyCode == KeyCode.F6)
        {
            FindNext(true);
            return true;
        }

        if (key.KeyCode == KeyCode.F7 || key == Key.F.WithCtrl)
        {
            Search();
            return true;
        }

        if (key.KeyCode == KeyCode.F8)
        {
            ToggleWordWrap();
            return true;
        }

        if (key == Key.G.WithCtrl)
        {
            GoToLine();
            return true;
        }

        if (key == Key.V.WithCtrl)
        {
            _textView.Paste();
            return true;
        }

        if (key == Key.Y.WithCtrl)
        {
            _textView.Redo();
            return true;
        }

        // the text view opens a file with ctrl+l
        if (key == Key.L.WithCtrl)
        {
            return true;
        }

        if (key.KeyCode is KeyCode.Esc or KeyCode.F10)
        {
            Application.RequestStop(this);
            return true;
        }

        return false;
    }

    /// <summary>Asks to save changes before closing: save, discard or cancel (keep editing).</summary>
    private bool ConfirmClose()
    {
        if (_session.IsReadOnly || !_modified || !_session.IsModified(_textView.EditedText))
        {
            return true;
        }

        switch (MessageBox.Query("Edit", $"Save the changes to '{_session.Name}'?", "Save", "Discard", "Cancel"))
        {
            case 0:
                // saved synchronously, so the dialog only closes when saving worked
                return Task.Run(() => TrySaveAsync()).GetAwaiter().GetResult();
            case 1:
                return true;
            default:
                _textView.SetFocus();
                return false;
        }
    }

    private async Task SaveAsync()
    {
        if (await TrySaveAsync())
        {
            _message = $"Saved '{_session.Name}'";
        }

        UpdateStatus();
    }

    /// <summary>Saves the text, showing errors (e.g. a character the file's encoding can't hold) and returning false.</summary>
    private async Task<bool> TrySaveAsync()
    {
        if (_session.IsReadOnly)
        {
            Application.Invoke(() => MessageBox.ErrorQuery("Error", _session.ReadOnlyReason!, "OK"));
            return false;
        }

        try
        {
            await _session.SaveAsync(_textView.EditedText);
            _modified = false;
            return true;
        }
        catch (Exception exception)
        {
            Application.Invoke(() => MessageBox.ErrorQuery("Error", exception.Message, "OK"));
            return false;
        }
    }

    private void Search()
    {
        if (ShowSearchDialog(false).Options is { } options)
        {
            Find(options with { Replacement = _session.LastSearch?.Replacement }, false);
        }
    }

    private void Replace()
    {
        if (_session.IsReadOnly)
        {
            MessageBox.ErrorQuery("Error", _session.ReadOnlyReason!, "OK");
            return;
        }

        var (options, replaceAll) = ShowSearchDialog(true);
        if (options == null)
        {
            return;
        }

        _session.LastSearch = options;
        try
        {
            if (replaceAll)
            {
                var (text, count) = EditorSearch.ReplaceAll(_textView.EditedText, options);
                if (count > 0)
                {
                    _textView.ReplaceAll(text);
                }

                _message = count == 0 ? $"'{options.Text}' not found" : $"Replaced {count:N0} matches";
                UpdateStatus();
                return;
            }

            Find(options, true);
        }
        catch (Exception exception)
        {
            MessageBox.ErrorQuery("Error", exception.Message, "OK");
        }
    }

    private (EditorSearchOptions? Options, bool ReplaceAll) ShowSearchDialog(bool replace)
    {
        var dialog = new EditorSearchDialog(_session.LastSearch, replace, _palette);
        Application.Run(dialog);
        var result = (dialog.Result, dialog.ReplaceAll);
        dialog.Dispose();
        _textView.SetFocus();
        return result;
    }

    private void FindNext(bool backwards)
    {
        if (_session.LastSearch is not { } search)
        {
            Search();
            return;
        }

        Find(search with { Backwards = backwards }, false);
    }

    /// <summary>
    ///     Finds the next match from the cursor (backwards from the start of the selection). Replacing replaces the
    ///     selected text first when it's a match, so pressing F4 again replaces the next one.
    /// </summary>
    private void Find(EditorSearchOptions options, bool replace)
    {
        _session.LastSearch = options;
        try
        {
            var text = _textView.EditedText;
            var (index, length) = _textView.Selection;
            var start = options.Backwards ? index : index + length;
            if (replace && EditorSearch.IsMatch(text, options, index, length))
            {
                var replacement = EditorSearch.GetReplacement(text, options, new EditorMatch(index, length));
                _textView.Select(index, length);
                _textView.ReplaceSelection(replacement);
                text = _textView.EditedText;
                start = options.Backwards ? index : index + replacement.Length;
            }

            if (EditorSearch.Find(text, options, start) is not { } match)
            {
                _message = $"'{options.Text}' not found";
            }
            else
            {
                _textView.Select(match.Index, match.Length);
                if (match.Wrapped)
                {
                    _message = options.Backwards ? "Continued from the end" : "Continued from the start";
                }
            }
        }
        catch (Exception exception)
        {
            MessageBox.ErrorQuery("Error", exception.Message, "OK");
        }

        UpdateStatus();
    }

    private void GoToLine()
    {
        var dialog = new GoToLineDialog(_cursor.Line, _palette);
        Application.Run(dialog);
        var line = dialog.Result;
        dialog.Dispose();
        _textView.SetFocus();
        if (line is { } number)
        {
            _textView.Select(EditorSession.GetLineIndex(_textView.EditedText, number), 0);
        }
    }

    private void ToggleWordWrap()
    {
        // turning word wrap on or off moves the cursor to the start, so it's moved back
        var (index, length) = _textView.Selection;
        _textView.WordWrap = !_textView.WordWrap;
        _textView.Select(index, length);
        UpdateKeyBar();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        _statusLabel.Text = _message ??
                            _session.GetStatusText(_cursor.Line, _cursor.Column, _modified, _textView.WordWrap);
        // a message is shown until the next key
        _message = null;
    }

    private void UpdateKeyBar()
    {
        _keyBar.Items =
        [
            new FunctionKeyItem("F2", "Save", () => _ = SaveAsync()),
            new FunctionKeyItem("F4", "Replace", Replace),
            new FunctionKeyItem("F5", "Next", () => FindNext(false)),
            new FunctionKeyItem("F6", "Prev", () => FindNext(true)),
            new FunctionKeyItem("F7", "Search", Search),
            new FunctionKeyItem("F8", _textView.WordWrap ? "Unwrap" : "Wrap", ToggleWordWrap),
            new FunctionKeyItem("F10", "Quit", () => Application.RequestStop(this)),
            new FunctionKeyItem("C-G", "Line", GoToLine)
        ];
        _keyBar.NeedsDraw = true;
    }
}

/// <summary>
///     Modal dialog asking for the line to go to in the editor (Ctrl+G). <see cref="Result" /> holds the one-based line on
///     OK or null on cancel.
/// </summary>
public sealed class GoToLineDialog : Dialog
{
    private readonly TextField _lineField;

    public GoToLineDialog(int line, TuiPalette palette)
    {
        Title = "Go to line";
        Width = 36;
        Height = 8;
        ColorScheme = palette.DialogColorScheme;

        var lineLabel = new Label { X = 1, Y = 1, Text = "Line number:" };
        _lineField = new TextField { X = 1, Y = 2, Width = Dim.Fill(1), Text = line.ToString() };

        var okButton = new Button { Text = "OK", IsDefault = true };
        okButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            if (!int.TryParse(_lineField.Text.Trim(), out var number) || number < 1)
            {
                MessageBox.ErrorQuery("Error", "Enter a line number of 1 or more", "OK");
                return;
            }

            Result = number;
            Application.RequestStop(this);
        };
        var cancelButton = new Button { Text = "Cancel" };
        cancelButton.Accepting += (_, e) =>
        {
            e.Cancel = true;
            Application.RequestStop(this);
        };

        Add(lineLabel, _lineField);
        AddButton(okButton);
        AddButton(cancelButton);
        Initialized += (_, _) =>
        {
            _lineField.SetFocus();
            _lineField.SelectAll();
        };
    }

    /// <summary>The line to go to when OK was pressed, otherwise null.</summary>
    public int? Result { get; private set; }
}
