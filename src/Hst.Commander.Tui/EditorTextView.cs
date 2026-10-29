using System.Drawing;
using System.Reflection;
using System.Text;
using Hst.Commander.Core;
using Terminal.Gui;

namespace Hst.Commander.Tui;

/// <summary>
///     The text view of the editor, with the cursor and selection as indexes in its text with LF line breaks, the
///     positions <see cref="EditorSearch" /> and <see cref="EditorSession" /> use. Terminal.Gui's
///     <see cref="TextView" /> keeps rows and columns of the wrapped lines when word wrapped and doesn't expose the
///     mapping to the lines of the text, so its private methods for it are used (the package version is pinned); without
///     them the positions are of the wrapped lines.
/// </summary>
public sealed class EditorTextView : TextView
{
    private const string Caller = nameof(EditorTextView);
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly MethodInfo? SetWrapModelMethod = typeof(TextView).GetMethod("SetWrapModel", PrivateInstance);
    private static readonly MethodInfo? UpdateWrapModelMethod = typeof(TextView).GetMethod("UpdateWrapModel", PrivateInstance);
    private static readonly MethodInfo? GetUnwrappedPositionMethod =
        typeof(TextView).GetMethod("GetUnwrappedPosition", PrivateInstance);
    private static readonly MethodInfo? InsertAllTextMethod = typeof(TextView).GetMethod("InsertAllText", PrivateInstance);

    /// <summary>The text with LF line breaks; <see cref="TextView.Text" /> has the line breaks of the host.</summary>
    public string EditedText => EditorDocument.NormalizeLineBreaks(Text);

    /// <summary>Index of the cursor in <see cref="EditedText" />.</summary>
    public int CursorIndex => GetIndex(EditedText, GetUnwrappedPosition(CurrentRow, CurrentColumn));

    /// <summary>Index and length of the selected text in <see cref="EditedText" />, the cursor when nothing is selected.</summary>
    public (int Index, int Length) Selection
    {
        get
        {
            var text = EditedText;
            var cursor = GetIndex(text, GetUnwrappedPosition(CurrentRow, CurrentColumn));
            if (!IsSelecting || SelectedLength == 0)
            {
                return (cursor, 0);
            }

            var start = GetIndex(text, GetUnwrappedPosition(SelectionStartRow, SelectionStartColumn));
            return (Math.Min(start, cursor), Math.Abs(cursor - start));
        }
    }

    /// <summary>Selects text (moving the cursor to its end) and scrolls it into view; a zero length moves the cursor.</summary>
    public void Select(int index, int length)
    {
        var text = EditedText;
        var (startRow, startColumn) = GetPosition(text, index);
        var (endRow, endColumn) = GetPosition(text, index + length);
        RunUnwrapped(() =>
        {
            IsSelecting = false;
            CursorPosition = new Point(endColumn, endRow);
            if (length > 0)
            {
                SelectionStartRow = startRow;
                SelectionStartColumn = startColumn;
            }
        });
        // scrolls the cursor into view in the wrapped lines
        CursorPosition = CursorPosition;
        SetNeedsDraw();
    }

    /// <summary>Replaces the selected text (or inserts at the cursor), so it can be undone.</summary>
    public void ReplaceSelection(string replacement)
    {
        if (IsSelecting && SelectedLength > 0)
        {
            DeleteCharRight();
        }

        IsSelecting = false;
        InsertMultiline(replacement);
    }

    /// <summary>Replaces all text, so it can be undone, keeping the cursor near where it was.</summary>
    public void ReplaceAll(string text)
    {
        var cursor = CursorIndex;
        if (InsertAllTextMethod == null)
        {
            Text = text;
        }
        else
        {
            SelectAll();
            DeleteCharRight();
            IsSelecting = false;
            InsertMultiline(text);
        }

        Select(Math.Min(cursor, text.Length), 0);
    }

    private void InsertMultiline(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (InsertAllTextMethod != null)
        {
            InsertAllTextMethod.Invoke(this, [text, false]);
            OnContentsChanged();
        }
        else
        {
            InsertText(text);
        }
    }

    /// <summary>Runs an action with the rows and columns of the lines of the text, also when word wrapped.</summary>
    private void RunUnwrapped(Action action)
    {
        if (!WordWrap || SetWrapModelMethod == null || UpdateWrapModelMethod == null)
        {
            action();
            return;
        }

        SetWrapModelMethod.Invoke(this, [Caller]);
        try
        {
            action();
        }
        finally
        {
            UpdateWrapModelMethod.Invoke(this, [Caller]);
        }
    }

    private (int Row, int Column) GetUnwrappedPosition(int row, int column)
    {
        if (!WordWrap || GetUnwrappedPositionMethod?.Invoke(this, [row, column]) is not ValueTuple<int, int> position)
        {
            return (row, column);
        }

        return position;
    }

    /// <summary>Index in the text of a row and column, where columns count runes like the cells of a line.</summary>
    private static int GetIndex(string text, (int Row, int Column) position)
    {
        var lineStart = EditorSession.GetLineIndex(text, position.Row + 1);
        var lineEnd = text.IndexOf('\n', lineStart);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        var index = lineStart;
        for (var column = 0; column < position.Column && index < lineEnd; column++)
        {
            index += Rune.TryGetRuneAt(text, index, out var rune) ? rune.Utf16SequenceLength : 1;
        }

        return Math.Min(index, lineEnd);
    }

    private static (int Row, int Column) GetPosition(string text, int index)
    {
        var (line, _) = EditorSession.GetLineColumn(text, index);
        var lineStart = EditorSession.GetLineIndex(text, line);
        var column = 0;
        for (var i = lineStart; i < Math.Min(index, text.Length);)
        {
            i += Rune.TryGetRuneAt(text, i, out var rune) ? rune.Utf16SequenceLength : 1;
            column++;
        }

        return (line - 1, column);
    }
}
