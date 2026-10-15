using System.Text.RegularExpressions;

namespace Hst.Commander.Core.Volumes;

/// <summary>
/// Filter matching item names with wildcard patterns, e.g. "*" or "*.txt;*.md". "*" matches any number of characters
/// and "?" a single character. Multiple patterns are separated by ";". Matching is case-insensitive unless
/// <c>caseSensitive</c> is true. An empty filter matches all names.
/// </summary>
public class EntryFilter
{
    public const string All = "*";

    private readonly Regex[] _patterns;

    public EntryFilter(string? filter, bool caseSensitive = false)
    {
        _patterns = (filter ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(pattern => CreateRegex(pattern, caseSensitive))
            .ToArray();
    }

    public bool IsMatch(string name) => _patterns.Length == 0 || _patterns.Any(pattern => pattern.IsMatch(name));

    private static Regex CreateRegex(string pattern, bool caseSensitive)
    {
        // "*.*" matches names without extension too, like it does on Windows
        if (pattern == "*.*")
        {
            pattern = All;
        }

        var expression = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
        var options = RegexOptions.CultureInvariant | RegexOptions.Singleline;
        return new Regex(expression, caseSensitive ? options : options | RegexOptions.IgnoreCase);
    }
}
