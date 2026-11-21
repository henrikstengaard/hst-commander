namespace Hst.Commander.Core;

/// <summary>
///     Quick search of a pane (Ctrl+F): typed text moves the cursor to the first entry whose name starts with it,
///     case-insensitively. Text with no match is rejected. Front-ends pass the shown names of the pane's entries in pane
///     order, with null for entries never matched (e.g. "..").
/// </summary>
public sealed class QuickSearch
{
    /// <summary>The typed text.</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>True while the quick search box is shown.</summary>
    public bool IsActive { get; private set; }

    /// <summary>Shows the quick search box with empty text.</summary>
    public void Start()
    {
        Text = string.Empty;
        IsActive = true;
    }

    /// <summary>Hides the quick search box.</summary>
    public void Stop()
    {
        Text = string.Empty;
        IsActive = false;
    }

    /// <summary>
    ///     Adds typed text and returns the index of the first matching name, or null when nothing matches the new text,
    ///     which is then rejected and <see cref="Text" /> unchanged.
    /// </summary>
    public int? TryAppend(string text, IReadOnlyList<string?> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var matches = FindMatches(names, Text + text);
        if (matches.Count == 0)
        {
            return null;
        }

        Text += text;
        return matches[0];
    }

    /// <summary>
    ///     Removes the last character of the text and returns the index of the first name matching the rest, or null when
    ///     the text is empty afterwards (or was already).
    /// </summary>
    public int? RemoveLast(IReadOnlyList<string?> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (Text.Length == 0)
        {
            return null;
        }

        Text = Text[..^1];
        if (Text.Length == 0)
        {
            return null;
        }

        var matches = FindMatches(names, Text);
        return matches.Count > 0 ? matches[0] : null;
    }

    /// <summary>
    ///     Returns the index of the next (or previous) name matching the text after (or before) <paramref name="current" />,
    ///     wrapping around, or null when the text is empty or nothing matches.
    /// </summary>
    public int? Next(IReadOnlyList<string?> names, int current, bool forward)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (Text.Length == 0)
        {
            return null;
        }

        var matches = FindMatches(names, Text);
        if (matches.Count == 0)
        {
            return null;
        }

        if (forward)
        {
            foreach (var index in matches)
            {
                if (index > current)
                {
                    return index;
                }
            }

            return matches[0];
        }

        for (var i = matches.Count - 1; i >= 0; i--)
        {
            if (matches[i] < current)
            {
                return matches[i];
            }
        }

        return matches[^1];
    }

    /// <summary>Indices of the names starting with the text.</summary>
    private static List<int> FindMatches(IReadOnlyList<string?> names, string text)
    {
        var matches = new List<int>();
        for (var index = 0; index < names.Count; index++)
        {
            if (names[index] is { } name && name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(index);
            }
        }

        return matches;
    }
}
