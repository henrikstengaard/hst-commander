using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Smb;

/// <summary>
/// SMB path entered by the user: a server, optionally a share and directories in it, e.g. "\\server\share\dir" (a
/// Windows UNC path) or "smb://server/share/dir" (a URL, which can hold a user name and password like
/// "smb://user:password@server/share" and escape characters like "%20"). Backslashes and slashes both separate the
/// parts, so "//server/share" and "\\server/share" are valid too, and the leading separators can be left out.
/// </summary>
/// <param name="Server">Name or IP address of the server, e.g. "nas" or "192.168.1.10".</param>
/// <param name="Share">Name of the share, null for the list of shares of the server.</param>
/// <param name="Directories">Directories in the share, empty for its root.</param>
/// <param name="Credentials">User name and password of a URL, null when it has none.</param>
public sealed record SmbPath(
    string Server,
    string? Share,
    IReadOnlyList<string> Directories,
    VolumeCredentials? Credentials = null)
{
    private const string UrlScheme = "smb://";

    /// <summary>Port of SMB, the only one SMBLibrary connects to.</summary>
    public const int Port = 445;

    /// <summary>Examples of SMB paths shown when asking for one.</summary>
    public const string Example = @"\\server\share or smb://server/share";

    /// <summary>Path components from the root of a server volume (<see cref="SmbVolume" />): the share and directories.</summary>
    public IReadOnlyList<string> PathComponents => Share == null ? [] : [Share, ..Directories];

    /// <summary>
    /// Root of the paths shown for a server, e.g. "\\server" on Windows and "//server" on Linux and macOS, followed by
    /// the share and directories separated by the directory separator of the operating system.
    /// </summary>
    public static string GetPathRoot(string server) =>
        $"{Path.DirectorySeparatorChar}{Path.DirectorySeparatorChar}{server}";

    /// <summary>
    /// Parses an SMB path. Throws <see cref="IOException" /> when it has no server, has a port other than 445 or is an
    /// invalid URL.
    /// </summary>
    public static SmbPath Parse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var text = path.Trim();
        VolumeCredentials? credentials = null;
        var isUrl = text.StartsWith(UrlScheme, StringComparison.OrdinalIgnoreCase);
        if (isUrl)
        {
            text = text[UrlScheme.Length..];
        }

        var parts = text.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => isUrl ? Unescape(part) : part)
            .Where(part => part != ".")
            .ToList();
        if (parts.Count == 0)
        {
            throw new IOException($"Enter an SMB path like {Example}");
        }

        var server = parts[0];
        if (isUrl)
        {
            // user info of a url, "user[:password]@server"
            var at = server.LastIndexOf('@');
            if (at >= 0)
            {
                var userInfo = server[..at];
                server = server[(at + 1)..];
                var colon = userInfo.IndexOf(':');
                credentials = colon >= 0
                    ? new VolumeCredentials(userInfo[..colon], userInfo[(colon + 1)..])
                    : new VolumeCredentials(userInfo, string.Empty);
            }
        }

        server = ParseServer(server, path);
        if (parts.Skip(1).Any(part => part == Paths.Parent))
        {
            throw new IOException($"SMB path '{path}' can't contain '..'");
        }

        return new SmbPath(server, parts.Count > 1 ? parts[1] : null, parts.Skip(2).ToList(), credentials);
    }

    /// <summary>Server name without a port ("server:445") and the brackets of an IPv6 address ("[::1]").</summary>
    private static string ParseServer(string server, string path)
    {
        string? port = null;
        if (server.StartsWith('['))
        {
            var end = server.IndexOf(']');
            if (end < 0)
            {
                throw new IOException($"SMB path '{path}' has an invalid server");
            }

            var rest = server[(end + 1)..];
            port = rest.StartsWith(':') ? rest[1..] : null;
            server = server[1..end];
        }
        else if (server.Count(c => c == ':') == 1)
        {
            // a single colon is a port, more are an IPv6 address
            var colon = server.IndexOf(':');
            port = server[(colon + 1)..];
            server = server[..colon];
        }

        if (port != null && port != Port.ToString())
        {
            throw new IOException($"SMB path '{path}' has port {port}, only port {Port} is supported");
        }

        return server.Length > 0 ? server : throw new IOException($"SMB path '{path}' has no server");
    }

    private static string Unescape(string part)
    {
        try
        {
            return Uri.UnescapeDataString(part);
        }
        catch (UriFormatException exception)
        {
            throw new IOException($"SMB path part '{part}' is invalid", exception);
        }
    }

    /// <summary>The path as a UNC path with the separator of the operating system, e.g. "\\server\share\dir".</summary>
    public override string ToString() =>
        string.Join(Path.DirectorySeparatorChar, [GetPathRoot(Server), ..PathComponents]);
}
