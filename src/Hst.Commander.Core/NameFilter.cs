using System.Text.RegularExpressions;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Core;

/// <summary>
///     Filter matching entry names entered by the user, e.g. in the select group dialog. A filter is either wildcard
///     patterns like <see cref="EntryFilter" /> ("*.txt;*.md", whole names matched) or a regular expression, used when
///     the filter contains a character only meaningful in regular expressions (<see cref="RegexCharacters" />), e.g.
///     "^report\d+\.txt$" (any part of a name matched, anchor with ^ and $). Matching is case-insensitive unless
///     case-sensitive. An empty filter matches all names.
/// </summary>
public sealed class NameFilter
{
    /// <summary>Characters making a filter a regular expression instead of wildcard patterns.</summary>
    public const string RegexCharacters = @"^$\[](){}|+";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly EntryFilter? _wildcardFilter;
    private readonly Regex? _regex;

    /// <exception cref="IOException">The filter is an invalid regular expression.</exception>
    public NameFilter(string? filter, bool caseSensitive = false)
    {
        filter = (filter ?? string.Empty).Trim();
        if (!IsRegex(filter))
        {
            _wildcardFilter = new EntryFilter(filter, caseSensitive);
            return;
        }

        var options = RegexOptions.CultureInvariant | RegexOptions.Singleline;
        try
        {
            _regex = new Regex(filter, caseSensitive ? options : options | RegexOptions.IgnoreCase, RegexTimeout);
        }
        catch (ArgumentException exception)
        {
            throw new IOException($"Invalid regular expression '{filter}': {exception.Message}", exception);
        }
    }

    /// <summary>True when the filter is a regular expression instead of wildcard patterns.</summary>
    public static bool IsRegex(string filter) => filter.IndexOfAny(RegexCharacters.ToCharArray()) >= 0;

    public bool IsMatch(string name)
    {
        if (_regex == null)
        {
            return _wildcardFilter!.IsMatch(name);
        }

        try
        {
            return _regex.IsMatch(name);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}

/// <summary>
///     Options entered in the select group dialog (+): the filter (wildcards or a regular expression, see
///     <see cref="NameFilter" />), whether matching is case-sensitive and whether only files are selected.
/// </summary>
public sealed record SelectGroupOptions(string Filter, bool CaseSensitive = false, bool FilesOnly = false)
{
    /// <summary>Options the select group dialog starts with the first time.</summary>
    public static SelectGroupOptions Default { get; } = new(EntryFilter.All);
}
