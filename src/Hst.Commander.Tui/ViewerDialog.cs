using Hst.Commander.Core;
using Terminal.Gui;
using TuiAttribute = Terminal.Gui.Attribute;

namespace Hst.Commander.Tui;

/// <summary>
///     Modal full screen viewer (F3) of a file in the text or hex view (F4), reading only the part of the file shown, so
///     files of any size can be viewed. Images aren't shown in a terminal, they are viewed as binary files in the hex
///     view. F7, Ctrl+F or / searches for text or a regular expression, F5 finds the
///     next match and F6 the previous one; searching runs in the background and Esc cancels it. A match is
///     highlighted, "not found" is shown in the status line. Text is selected by dragging with the mouse (Shift+click
///     extends, Ctrl+A selects all, a click clears it) and copied to the clipboard with Ctrl+C or Ctrl+Insert. Esc or F10
///     closes the viewer.
/// </summary>
public sealed class ViewerDialog : Dialog
{
    private readonly ViewerSession _session;
    private readonly TuiPalette _palette;
    private readonly Label _statusLabel;
    private readonly ViewerView _view;
    private readonly FunctionKeyBarView _keyBar;
    private CancellationTokenSource? _searchCancellation;
    private string? _message;
    private bool _closed;

    public ViewerDialog(string name, ViewerSession session, TuiPalette palette)
    {
        _session = session;
        _palette = palette;
        Title = $"View - {name}";
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        ColorScheme = palette.DialogColorScheme;

        _statusLabel = new Label { X = 0, Y = 0, Width = Dim.Fill(), Height = 1 };
        _view = new ViewerView(session)
        {
            X = 0,
            Y = 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            CanFocus = true,
            TextAttribute = new TuiAttribute(palette.Item, palette.PaneBackground),
            OffsetAttribute = new TuiAttribute(palette.Header, palette.PaneBackground),
            MatchAttribute = palette.Cursor,
            SelectionAttribute = palette.DialogFocus
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

        _view.KeyDown += (_, key) => key.Handled = HandleKey(key);
        _view.ViewChanged += (_, _) => UpdateStatus();
        _view.FrameChanged += (_, _) => UpdateStatus();
        Closing += (_, _) =>
        {
            _closed = true;
            _searchCancellation?.Cancel();
        };

        Add(_statusLabel, _view, _keyBar);
        UpdateKeyBar();
        Initialized += (_, _) =>
        {
            _view.SetFocus();
            UpdateStatus();
        };
    }

    private bool HandleKey(Key key)
    {
        if (key.KeyCode == KeyCode.Esc && _searchCancellation != null)
        {
            _searchCancellation.Cancel();
            return true;
        }

        if (key == Key.C.WithCtrl || key == Key.InsertChar.WithCtrl)
        {
            Copy();
            return true;
        }

        if (key == Key.A.WithCtrl)
        {
            _session.SelectAll();
            UpdateView();
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

        if (key.KeyCode == KeyCode.F7 || key == Key.F.WithCtrl || key.AsRune.Value == '/')
        {
            Search();
            return true;
        }

        if (key.KeyCode is KeyCode.Esc or KeyCode.F10)
        {
            CloseViewer();
            return true;
        }

        var rows = _view.Rows;
        switch (key.KeyCode)
        {
            case KeyCode.F4:
                ToggleHex();
                return true;
            case KeyCode.CursorUp:
                _session.Scroll(-1, rows);
                break;
            case KeyCode.CursorDown:
                _session.Scroll(1, rows);
                break;
            case KeyCode.PageUp:
                _session.Scroll(-rows, rows);
                break;
            case KeyCode.PageDown:
            case KeyCode.Space:
                _session.Scroll(rows, rows);
                break;
            case KeyCode.Home:
            case KeyCode.Home | KeyCode.CtrlMask:
                _session.ScrollToStart();
                break;
            case KeyCode.End:
            case KeyCode.End | KeyCode.CtrlMask:
                _session.ScrollToEnd(rows);
                break;
            case KeyCode.CursorLeft:
                _session.ScrollColumns(-1, rows, _view.Columns);
                break;
            case KeyCode.CursorRight:
                _session.ScrollColumns(1, rows, _view.Columns);
                break;
            default:
                return false;
        }

        UpdateView();
        return true;
    }

    /// <summary>Copies the selected text to the clipboard, showing what was copied in the status line.</summary>
    private void Copy()
    {
        try
        {
            if (_session.GetSelectedText() is not { } text)
            {
                _message = "Select text to copy by dragging with the mouse or Ctrl+A";
            }
            else
            {
                _message = Clipboard.TrySetClipboardData(text)
                    ? $"Copied {text.Length:N0} characters"
                    : "Copying to the clipboard isn't supported by this terminal";
            }
        }
        catch (IOException exception)
        {
            _message = exception.Message;
        }

        UpdateStatus();
    }

    private void CloseViewer() => Application.RequestStop(this);

    private void ToggleHex()
    {
        _session.ToggleHex();
        UpdateKeyBar();
        UpdateView();
    }

    private void Search()
    {
        if (_searchCancellation != null)
        {
            return;
        }

        var dialog = new ViewerSearchDialog(_session.LastSearch, _palette);
        Application.Run(dialog);
        var options = dialog.Result;
        dialog.Dispose();
        _view.SetFocus();
        if (options != null)
        {
            _ = RunSearchAsync(options.Text,
                (progress, cancellationToken) =>
                    _session.FindAsync(options, _view.Rows, _view.Columns, progress, cancellationToken));
        }
    }

    private void FindNext(bool backwards)
    {
        if (_searchCancellation != null)
        {
            return;
        }

        if (_session.LastSearch is not { } search)
        {
            Search();
            return;
        }

        _ = RunSearchAsync(search.Text,
            (progress, cancellationToken) =>
                _session.FindNextAsync(backwards, _view.Rows, _view.Columns, progress, cancellationToken));
    }

    /// <summary>Searches in the background, showing the progress in the status line until found or cancelled.</summary>
    private async Task RunSearchAsync(string text,
        Func<IProgress<double>, CancellationToken, Task<bool>> find)
    {
        using var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        _statusLabel.Text = "Searching... (Esc cancels)";
        var progress = new UiProgress<double>(fraction => Application.Invoke(() =>
        {
            if (!_closed && !cancellation.IsCancellationRequested)
            {
                _statusLabel.Text = $"Searching... {fraction:P0} (Esc cancels)";
            }
        }));
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
                MessageBox.ErrorQuery("Error", exception.Message, "OK");
            }
        }
        finally
        {
            _searchCancellation = null;
        }

        if (!_closed)
        {
            UpdateKeyBar();
            UpdateView();
            _view.SetFocus();
        }
    }

    private void UpdateView()
    {
        _view.NeedsDraw = true;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_searchCancellation != null)
        {
            return;
        }

        // a search message is shown until the next key
        _statusLabel.Text = _message ?? _session.GetStatusText(_view.Rows);
        _message = null;
    }

    private void UpdateKeyBar()
    {
        _keyBar.Items =
        [
            new FunctionKeyItem("F4", _session.IsHex ? "Text" : "Hex", ToggleHex),
            new FunctionKeyItem("F5", "Next", () => FindNext(false)),
            new FunctionKeyItem("F6", "Prev", () => FindNext(true)),
            new FunctionKeyItem("F7", "Search", Search),
            new FunctionKeyItem("F10", "Quit", CloseViewer),
            new FunctionKeyItem("C-C", "Copy", Copy)
        ];
        _keyBar.NeedsDraw = true;
    }

    /// <summary>Progress calling the handler on the reporting thread; the handler invokes the UI thread.</summary>
    private sealed class UiProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
