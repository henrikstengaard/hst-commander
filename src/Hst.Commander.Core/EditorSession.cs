namespace Hst.Commander.Core;

/// <summary>
///     A file opened in the built-in editor (F4), holding what both front-ends share: the document, whether and why it's
///     read-only, saving, the last search and the status text. The front-ends edit the text with their own text
///     control. Saving writes the file through the volume it was opened from (a volume of its own for files inside
///     images, see <see cref="NavigationContext.OpenEditorAsync" />), which is disposed with the session.
/// </summary>
public sealed class EditorSession : IDisposable
{
    private readonly Func<byte[], CancellationToken, Task>? _write;
    private readonly IDisposable? _resource;

    internal EditorSession(string name, EditorDocument document, Func<byte[], CancellationToken, Task>? write,
        string? readOnlyReason, IDisposable? resource)
    {
        Name = name;
        Document = document;
        _write = write;
        ReadOnlyReason = write == null ? readOnlyReason ?? "The file can't be saved" : null;
        _resource = resource;
        SavedText = document.Text;
    }

    /// <summary>Creates a session over a document, e.g. for tests; null <paramref name="write" /> is read-only.</summary>
    public static EditorSession Create(string name, EditorDocument document,
        Func<byte[], CancellationToken, Task>? write, string? readOnlyReason = null) =>
        new(name, document, write, readOnlyReason, null);

    public string Name { get; }

    public EditorDocument Document { get; }

    /// <summary>Why the file can't be saved, e.g. in a read-only image; null when it can.</summary>
    public string? ReadOnlyReason { get; }

    public bool IsReadOnly => ReadOnlyReason != null;

    /// <summary>The text as last read or saved, with LF line breaks.</summary>
    public string SavedText { get; private set; }

    /// <summary>True after the file has been saved, so the panes are refreshed when the editor is closed.</summary>
    public bool HasSaved { get; private set; }

    /// <summary>The last search or replace, used by find next (F5) and previous (F6) and shown again in the dialogs.</summary>
    public EditorSearchOptions? LastSearch { get; set; }

    /// <summary>True when the text differs from the text last read or saved.</summary>
    public bool IsModified(string text) => !EditorDocument.NormalizeLineBreaks(text).Equals(SavedText, StringComparison.Ordinal);

    /// <summary>
    ///     Saves the text with the encoding and line breaks of the file. Throws <see cref="NotSupportedException" /> when
    ///     read-only and <see cref="IOException" /> when a character can't be encoded or writing fails.
    /// </summary>
    public async Task SaveAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (_write == null)
        {
            throw new NotSupportedException(ReadOnlyReason);
        }

        var bytes = Document.Encode(text);
        await _write(bytes, cancellationToken);
        SavedText = EditorDocument.NormalizeLineBreaks(text);
        HasSaved = true;
    }

    /// <summary>
    ///     Status line text of the one-based line and column of the cursor, e.g. "Line 12, Col 8   UTF-8, LF   Modified",
    ///     with the word wrap when on and why the file is read-only.
    /// </summary>
    public string GetStatusText(int line, int column, bool modified, bool wordWrap)
    {
        var parts = new List<string> { $"Line {line:N0}, Col {column:N0}", Document.Description };
        if (wordWrap)
        {
            parts.Add("Wrap");
        }

        if (ReadOnlyReason != null)
        {
            parts.Add($"Read-only: {ReadOnlyReason}");
        }
        else if (modified)
        {
            parts.Add("Modified");
        }

        return string.Join("   ", parts);
    }

    /// <summary>One-based line and column of an index in text with LF line breaks.</summary>
    public static (int Line, int Column) GetLineColumn(string text, int index)
    {
        ArgumentNullException.ThrowIfNull(text);

        index = Math.Clamp(index, 0, text.Length);
        var line = 1;
        var lineStart = 0;
        for (var i = text.IndexOf('\n'); i >= 0 && i < index; i = text.IndexOf('\n', i + 1))
        {
            line++;
            lineStart = i + 1;
        }

        return (line, index - lineStart + 1);
    }

    /// <summary>Index of the start of a one-based line (go to line, Ctrl+G), the last line when there are fewer.</summary>
    public static int GetLineIndex(string text, int line)
    {
        ArgumentNullException.ThrowIfNull(text);

        var index = 0;
        for (var current = 1; current < line; current++)
        {
            var lineBreak = text.IndexOf('\n', index);
            if (lineBreak < 0)
            {
                break;
            }

            index = lineBreak + 1;
        }

        return index;
    }

    public void Dispose() => _resource?.Dispose();
}
