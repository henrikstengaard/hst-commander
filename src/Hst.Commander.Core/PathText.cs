namespace Hst.Commander.Core;

/// <summary>
///     Shortens paths and messages holding a path to a number of characters, keeping the end of the path, where the
///     current directory is: "..." followed by the end of the path from a separator, like the path panel above a pane.
/// </summary>
public static class PathText
{
    /// <summary>Text put in place of the part cut off.</summary>
    public const string Ellipsis = "...";

    private static readonly char[] Separators = ['\\', '/'];

    /// <summary>
    ///     A path shortened to at most <paramref name="maxLength" /> characters: the path itself when it fits, otherwise
    ///     "..." followed by its end from the first separator in it, e.g. "...\games\whdload" for
    ///     "C:\Users\name\amiga\games\whdload". The end is cut mid-name only when it has no separator.
    /// </summary>
    public static string Shorten(string path, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Length <= maxLength)
        {
            return path;
        }

        if (maxLength <= 0)
        {
            return string.Empty;
        }

        if (maxLength <= Ellipsis.Length)
        {
            return path[^maxLength..];
        }

        return Ellipsis + TrimToSeparator(path[^(maxLength - Ellipsis.Length)..]);
    }

    /// <summary>
    ///     A message holding a path shortened to at most <paramref name="maxLength" /> characters, e.g. "Reading
    ///     ...\games\whdload..." for "Reading C:\Users\name\amiga\games\whdload...": the words before the path (the
    ///     first word with a separator) are kept and the path is shortened with <see cref="Shorten" />. A message without
    ///     a path, or whose words before the path don't leave room for it, is cut at the end with "...".
    /// </summary>
    public static string ShortenMessage(string message, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Length <= maxLength)
        {
            return message;
        }

        if (maxLength <= Ellipsis.Length)
        {
            return message[..Math.Max(maxLength, 0)];
        }

        var pathStart = GetPathStart(message);
        if (pathStart >= 0 && maxLength - pathStart > Ellipsis.Length)
        {
            return message[..pathStart] + Shorten(message[pathStart..], maxLength - pathStart);
        }

        return message[..(maxLength - Ellipsis.Length)] + Ellipsis;
    }

    /// <summary>Index of the first word with a separator, -1 when there is none.</summary>
    private static int GetPathStart(string message)
    {
        var separatorIndex = message.IndexOfAny(Separators);
        return separatorIndex < 0 ? -1 : message.LastIndexOf(' ', separatorIndex) + 1;
    }

    private static string TrimToSeparator(string tail)
    {
        var separatorIndex = tail.IndexOfAny(Separators);
        return separatorIndex > 0 ? tail[separatorIndex..] : tail;
    }
}
