using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Core;

/// <summary>What the content of files is searched for (see <see cref="SearchOptions" />).</summary>
public enum SearchContentType
{
    /// <summary>Entries are found by their names only.</summary>
    None,

    /// <summary>Text, as UTF-8 and Latin-1 bytes.</summary>
    Text,

    /// <summary>A regular expression, matched against the bytes read as Latin-1 characters.</summary>
    Regex,

    /// <summary>Hex values, e.g. "DE AD BE EF".</summary>
    Hex
}

/// <summary>How deep subdirectories are searched (see <see cref="SearchOptions" />).</summary>
public enum SearchDepth
{
    /// <summary>All subdirectories.</summary>
    All,

    /// <summary>The start directory only.</summary>
    Current,

    /// <summary>A number of subdirectory levels below the start directory (<see cref="SearchOptions.Levels" />).</summary>
    Levels
}

/// <summary>
///     Options entered in the search dialog (Ctrl+S), see <see cref="NavigationContext.SearchAsync" />.
/// </summary>
/// <param name="StartPath">Location to start in, resolved like <see cref="NavigationContext.ChangeLocationAsync(string, CancellationToken)" />.</param>
/// <param name="NamePattern">Names to find: wildcards or a regular expression (see <see cref="NameFilter" />).</param>
/// <param name="NameIsRegex">True to use the name pattern as a regular expression, also without regex characters.</param>
/// <param name="CaseSensitive">True to match names and text case-sensitively.</param>
/// <param name="ContentType">What file contents are searched for, <see cref="SearchContentType.None" /> for names only.</param>
/// <param name="Content">Text, regular expression or hex values to search file contents for.</param>
/// <param name="Depth">How deep subdirectories are searched.</param>
/// <param name="Levels">Subdirectory levels searched with <see cref="SearchDepth.Levels" />.</param>
/// <param name="SearchInImages">True to search inside images and archives, e.g. zip archives and adf images.</param>
public sealed record SearchOptions(
    string StartPath,
    string NamePattern,
    bool NameIsRegex = false,
    bool CaseSensitive = false,
    SearchContentType ContentType = SearchContentType.None,
    string Content = "",
    SearchDepth Depth = SearchDepth.All,
    int Levels = 1,
    bool SearchInImages = false)
{
    /// <summary>Options the search dialog starts with the first time.</summary>
    public static SearchOptions Default { get; } = new(string.Empty, EntryFilter.All);

    /// <summary>Subdirectory levels searched below the start directory, null for all.</summary>
    public int? MaxLevels => Depth switch
    {
        SearchDepth.Current => 0,
        SearchDepth.Levels => Math.Max(0, Levels),
        _ => null
    };

    /// <summary>True when file contents are searched.</summary>
    public bool SearchesContent => ContentType != SearchContentType.None && Content.Length > 0;
}

/// <summary>
///     Entry found by a search: the location of the directory it's in (<see cref="DirectoryPath" />, a path for
///     <see cref="NavigationContext.ChangeLocationAsync(string, CancellationToken)" />) and the volume provider it was
///     found in, to go to it with <see cref="NavigationContext.GoToSearchResultAsync" />.
/// </summary>
public sealed record SearchResult(
    string DirectoryPath,
    IEntry Entry,
    IVolumeProvider VolumeProvider,
    LocationConnection? Connection)
{
    public string Name => Entry.Name;

    /// <summary>Size shown in the results, like the panes: "&lt;DIR&gt;" for directories.</summary>
    public string SizeText => SizeFormatter.FormatEntrySize(Entry);

    /// <summary>Modified date shown in the results, like the panes.</summary>
    public string ModifiedText => Entry.ModifiedDate?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

    /// <summary>Path of the entry shown in the result list, e.g. "c:\dir\file.txt".</summary>
    public string FullPath
    {
        get
        {
            var separator = DirectoryPath.Contains("://", StringComparison.Ordinal) ? '/' : Path.DirectorySeparatorChar;
            return DirectoryPath.Length == 0
                ? Name
                : $"{DirectoryPath.TrimEnd('\\', '/')}{separator}{Name}";
        }
    }
}

/// <summary>Column of the search results, see <see cref="SearchResultSort" />.</summary>
public enum SearchResultColumn
{
    Path,
    Size,
    Modified
}

/// <summary>
///     Order of the search results by a column, ascending or descending, shared by the front-ends: clicking a column
///     header (<see cref="Toggle" />) sorts by it ascending, clicking it again descending. Results found while
///     searching are inserted in order (<see cref="IndexToInsert" />). Equal sizes and dates are ordered by path.
/// </summary>
public sealed record SearchResultSort(SearchResultColumn Column = SearchResultColumn.Path, bool Descending = false)
    : IComparer<SearchResult>
{
    /// <summary>Arrow after the header of the sorted column when ascending.</summary>
    public const string AscendingArrow = "▲";

    /// <summary>Arrow after the header of the sorted column when descending.</summary>
    public const string DescendingArrow = "▼";

    public static SearchResultSort Default { get; } = new();

    /// <summary>The columns in the order they're shown.</summary>
    public static IReadOnlyList<SearchResultColumn> Columns { get; } =
        [SearchResultColumn.Path, SearchResultColumn.Size, SearchResultColumn.Modified];

    /// <summary>Sorting after clicking a column header: the same column reversed, another column ascending.</summary>
    public SearchResultSort Toggle(SearchResultColumn column) =>
        column == Column ? this with { Descending = !Descending } : new SearchResultSort(column);

    /// <summary>Header of a column, with an arrow when the results are sorted by it, e.g. "Size ▼".</summary>
    public string GetHeader(SearchResultColumn column)
    {
        var name = column switch
        {
            SearchResultColumn.Size => "Size",
            SearchResultColumn.Modified => "Modified",
            _ => "Path"
        };
        return column == Column ? $"{name} {(Descending ? DescendingArrow : AscendingArrow)}" : name;
    }

    public int Compare(SearchResult? x, SearchResult? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x == null || y == null)
        {
            return x == null ? -1 : 1;
        }

        var result = Column switch
        {
            SearchResultColumn.Size => Nullable.Compare(GetSize(x), GetSize(y)),
            SearchResultColumn.Modified => Nullable.Compare(x.Entry.ModifiedDate, y.Entry.ModifiedDate),
            _ => 0
        };
        if (result == 0)
        {
            result = ComparePaths(x, y);
        }

        return Descending ? -result : result;
    }

    /// <summary>The results sorted in this order.</summary>
    public IReadOnlyList<SearchResult> Sort(IEnumerable<SearchResult> results) => results.Order(this).ToList();

    /// <summary>Index to insert a result at to keep sorted results in order, after equal results.</summary>
    public int IndexToInsert(IReadOnlyList<SearchResult> results, SearchResult result)
    {
        ArgumentNullException.ThrowIfNull(results);

        var low = 0;
        var high = results.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (Compare(results[middle], result) <= 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    // directories have no size and come before files when ascending
    private static long? GetSize(SearchResult result) =>
        result.Entry.Type == EntryType.Container ? null : result.Entry.Size ?? 0;

    private static int ComparePaths(SearchResult x, SearchResult y)
    {
        var result = string.Compare(x.FullPath, y.FullPath, StringComparison.OrdinalIgnoreCase);
        return result != 0 ? result : string.Compare(x.FullPath, y.FullPath, StringComparison.Ordinal);
    }
}

/// <summary>
///     Progress of a search: the directory being searched, the directories and files searched, the entries found and
///     the directories and files that couldn't be read.
/// </summary>
public sealed record SearchProgress(
    string CurrentPath,
    int Directories,
    int Files,
    int Found,
    int Errors,
    bool IsComplete)
{
    /// <summary>Status text, e.g. "Searching c:\dir... 3 found in 10 directories and 120 files".</summary>
    public string Text
    {
        get
        {
            var counts = $"{Found} found in {Directories} director{(Directories == 1 ? "y" : "ies")} and {Files} " +
                         $"file{(Files == 1 ? string.Empty : "s")}";
            if (Errors > 0)
            {
                counts += $", {Errors} not readable";
            }

            return IsComplete ? $"Done: {counts}" : $"Searching {CurrentPath}... {counts}";
        }
    }
}

/// <summary>
///     Matches the contents of a file (a stream) with text, a regular expression or hex values (see
///     <see cref="SearchContentType" />). The stream is read in chunks keeping the end of the previous chunk, so
///     matches across chunks are found; a regular expression match longer than <see cref="RegexOverlap" /> characters
///     may be missed.
/// </summary>
public sealed class ContentMatcher
{
    /// <summary>Bytes read at a time.</summary>
    public const int ChunkSize = 64 * 1024;

    /// <summary>Characters at the end of a chunk kept for matching a regular expression with the next chunk.</summary>
    public const int RegexOverlap = 4096;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly byte[][] _patterns = [];
    private readonly bool _ignoreCase;
    private readonly Regex? _regex;

    private ContentMatcher(byte[][] patterns, bool ignoreCase)
    {
        _patterns = patterns;
        _ignoreCase = ignoreCase;
    }

    private ContentMatcher(Regex regex) => _regex = regex;

    /// <summary>
    ///     Matcher of contents, null when not searching contents (<see cref="SearchContentType.None" /> or empty).
    /// </summary>
    /// <exception cref="IOException">Invalid hex values or regular expression.</exception>
    public static ContentMatcher? Create(SearchContentType contentType, string content, bool caseSensitive)
    {
        if (contentType == SearchContentType.None || string.IsNullOrEmpty(content))
        {
            return null;
        }

        switch (contentType)
        {
            case SearchContentType.Hex:
                return new ContentMatcher([ParseHex(content)], false);
            case SearchContentType.Regex:
                var options = RegexOptions.CultureInvariant | RegexOptions.Multiline;
                try
                {
                    return new ContentMatcher(new Regex(content,
                        caseSensitive ? options : options | RegexOptions.IgnoreCase, RegexTimeout));
                }
                catch (ArgumentException exception)
                {
                    throw new IOException($"Invalid regular expression '{content}': {exception.Message}", exception);
                }
            default:
                var ignoreCase = !caseSensitive;
                List<byte[]> patterns = [Encoding.UTF8.GetBytes(content)];
                // Latin-1 (e.g. Amiga or old Windows files) differs from UTF-8 for characters above 127
                if (content.Any(c => c > 127) && content.All(c => c <= 255))
                {
                    patterns.Add(Encoding.Latin1.GetBytes(content));
                }

                return new ContentMatcher(
                    patterns.Select(pattern => ignoreCase ? pattern.Select(FoldCase).ToArray() : pattern).ToArray(),
                    ignoreCase);
        }
    }

    /// <summary>
    ///     Bytes of hex values, separated or not by spaces, commas or dashes and optionally prefixed with "0x" or "$",
    ///     e.g. "DE AD BE EF", "deadbeef" or "0xDE, 0xAD".
    /// </summary>
    /// <exception cref="IOException">Invalid hex values.</exception>
    public static byte[] ParseHex(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var bytes = new List<byte>();
        foreach (var token in text.Split([' ', ',', '-', '\t', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var digits = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token[2..]
                : token.StartsWith('$') ? token[1..]
                : token;
            if (digits.Length == 0 || digits.Length % 2 != 0)
            {
                throw new IOException($"Invalid hex value '{token}': use two digits per byte, e.g. 'DE AD BE EF'");
            }

            for (var index = 0; index < digits.Length; index += 2)
            {
                if (!byte.TryParse(digits.AsSpan(index, 2), NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture, out var value))
                {
                    throw new IOException($"Invalid hex value '{token}': use the digits 0-9 and A-F");
                }

                bytes.Add(value);
            }
        }

        return bytes.Count > 0 ? bytes.ToArray() : throw new IOException("No hex values to search for");
    }

    /// <summary>True when the contents of the stream match, read until the first match.</summary>
    public async Task<bool> IsMatchAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var overlap = _regex != null ? RegexOverlap : _patterns.Max(pattern => pattern.Length) - 1;
        var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize + overlap);
        try
        {
            var kept = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(kept, ChunkSize), cancellationToken);
                if (read == 0)
                {
                    return false;
                }

                var length = kept + read;
                if (IsMatch(buffer, kept, length))
                {
                    return true;
                }

                kept = Math.Min(overlap, length);
                Buffer.BlockCopy(buffer, length - kept, buffer, 0, kept);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>True when the bytes match; bytes before <paramref name="start" /> were kept from the last chunk.</summary>
    private bool IsMatch(byte[] buffer, int start, int length)
    {
        if (_regex != null)
        {
            try
            {
                return _regex.IsMatch(Encoding.Latin1.GetString(buffer, 0, length));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        if (_ignoreCase)
        {
            // the kept bytes were folded with the last chunk
            for (var index = start; index < length; index++)
            {
                buffer[index] = FoldCase(buffer[index]);
            }
        }

        var bytes = buffer.AsSpan(0, length);
        foreach (var pattern in _patterns)
        {
            if (bytes.IndexOf(pattern) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static byte FoldCase(byte value) => value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value;
}

/// <summary>Counts searched directories and files and reports progress at most every 100 ms.</summary>
internal sealed class SearchCounter(IProgress<SearchProgress>? progress)
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(100);

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private TimeSpan _lastReport = TimeSpan.Zero;
    private string _currentPath = string.Empty;
    private int _directories;
    private int _files;
    private int _found;
    private int _errors;

    public void AddDirectory(string path)
    {
        _currentPath = path;
        _directories++;
        Report();
    }

    public void AddFile()
    {
        _files++;
        Report();
    }

    public void AddFound()
    {
        _found++;
        Report();
    }

    public void AddError()
    {
        _errors++;
        Report();
    }

    public void Complete() => progress?.Report(Create(true));

    private void Report()
    {
        var elapsed = _stopwatch.Elapsed;
        if (progress == null || elapsed - _lastReport < ReportInterval)
        {
            return;
        }

        _lastReport = elapsed;
        progress.Report(Create(false));
    }

    private SearchProgress Create(bool isComplete) =>
        new(_currentPath, _directories, _files, _found, _errors, isComplete);
}
