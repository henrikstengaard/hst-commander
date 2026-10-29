using System.Text.RegularExpressions;

namespace Hst.Commander.Core;

/// <summary>
///     What to search for in the editor (F7) and what to replace it with (F4): the text or regular expression, case
///     sensitive, whole words and backwards. A regular expression's replacement can use groups, e.g. "$1".
/// </summary>
public sealed record EditorSearchOptions(
    string Text,
    string? Replacement = null,
    bool IsRegex = false,
    bool CaseSensitive = false,
    bool WholeWords = false,
    bool Backwards = false);

/// <summary>Text found in the editor: its index and length in the text, and whether searching wrapped around.</summary>
public sealed record EditorMatch(int Index, int Length, bool Wrapped = false);

/// <summary>
///     Searching and replacing in the text of the editor, shared by both front-ends. Searching continues from the
///     cursor and wraps around at the end (or the start, backwards). Text and regular expressions match across lines
///     (the text has LF line breaks); empty matches are ignored. Empty text or an invalid regular expression throws
///     <see cref="ArgumentException" />.
/// </summary>
public static class EditorSearch
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     Finds the next match from <paramref name="start" /> (forwards a match starting at it, backwards a match ending
    ///     before it), wrapping around. Null when nothing matches.
    /// </summary>
    public static EditorMatch? Find(string text, EditorSearchOptions options, int start)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);

        start = Math.Clamp(start, 0, text.Length);
        var matches = Matches(text, options);
        if (options.Backwards)
        {
            var before = matches.LastOrDefault(match => match.Index + match.Length <= start);
            if (before != null)
            {
                return before;
            }

            var last = matches.LastOrDefault();
            return last == null ? null : last with { Wrapped = true };
        }

        // matches are found in order, so the first after the start is found without matching the rest of the text
        foreach (var match in matches)
        {
            if (match.Index >= start)
            {
                return match;
            }
        }

        var first = Matches(text, options).FirstOrDefault();
        return first == null ? null : first with { Wrapped = true };
    }

    /// <summary>
    ///     True when the selected text is a match, so replacing (F4) replaces it before finding the next one instead of
    ///     only finding it.
    /// </summary>
    public static bool IsMatch(string text, EditorSearchOptions options, int index, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);

        return length > 0 && Matches(text, options)
            .SkipWhile(match => match.Index < index)
            .TakeWhile(match => match.Index == index)
            .Any(match => match.Length == length);
    }

    /// <summary>The replacement of a match, with the groups of a regular expression substituted.</summary>
    public static string GetReplacement(string text, EditorSearchOptions options, EditorMatch match)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(match);

        var replacement = options.Replacement ?? string.Empty;
        if (!options.IsRegex)
        {
            return replacement;
        }

        var regexMatch = CreateRegex(options).Match(text, match.Index);
        return regexMatch.Success && regexMatch.Index == match.Index
            ? regexMatch.Result(replacement)
            : replacement;
    }

    /// <summary>Replaces every match, returning the new text and the number of matches replaced.</summary>
    public static (string Text, int Count) ReplaceAll(string text, EditorSearchOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);

        var count = 0;
        var result = CreateRegex(options).Replace(text, match =>
        {
            if (match.Length == 0)
            {
                return match.Value;
            }

            count++;
            return options.IsRegex ? match.Result(options.Replacement ?? string.Empty) : options.Replacement ?? string.Empty;
        });
        return (result, count);
    }

    private static IEnumerable<EditorMatch> Matches(string text, EditorSearchOptions options)
    {
        var regex = CreateRegex(options);
        return MatchesOf(regex, text);
    }

    private static IEnumerable<EditorMatch> MatchesOf(Regex regex, string text)
    {
        for (var match = regex.Match(text); match.Success; match = match.NextMatch())
        {
            if (match.Length > 0)
            {
                yield return new EditorMatch(match.Index, match.Length);
            }
        }
    }

    /// <summary>
    ///     Plain text is searched as an escaped regular expression too, so whole words and case work the same way for
    ///     both.
    /// </summary>
    private static Regex CreateRegex(EditorSearchOptions options)
    {
        if (string.IsNullOrEmpty(options.Text))
        {
            throw new ArgumentException("Enter the text to search for");
        }

        var pattern = options.IsRegex ? options.Text : Regex.Escape(options.Text);
        if (options.WholeWords)
        {
            pattern = $@"(?<!\w)(?:{pattern})(?!\w)";
        }

        var regexOptions = RegexOptions.CultureInvariant | RegexOptions.Multiline;
        if (!options.CaseSensitive)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        try
        {
            return new Regex(pattern, regexOptions, RegexTimeout);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException($"Invalid regular expression: {exception.Message}", exception);
        }
    }
}
