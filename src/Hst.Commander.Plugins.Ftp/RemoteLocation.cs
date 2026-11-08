using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Ftp;

/// <summary>Protocol of an FTP or SFTP server.</summary>
public enum RemoteProtocol
{
    /// <summary>FTP, unencrypted, port 21.</summary>
    Ftp,

    /// <summary>FTP over TLS (explicit FTPS, AUTH TLS), port 21.</summary>
    Ftps,

    /// <summary>SFTP, the SSH file transfer protocol, port 22.</summary>
    Sftp
}

/// <summary>Server connected to: protocol, host and port.</summary>
/// <param name="Protocol">Protocol.</param>
/// <param name="Host">Name or IP address of the server, IPv6 addresses without brackets.</param>
/// <param name="Port">Port.</param>
public sealed record RemoteServer(RemoteProtocol Protocol, string Host, int Port)
{
    /// <summary>
    /// Root of the paths shown for the server, e.g. "sftp://server" or "ftp://server:2121" with a port other than the
    /// default one, followed by the path in the server.
    /// </summary>
    public string PathRoot
    {
        get
        {
            var host = Host.Contains(':') ? $"[{Host}]" : Host;
            var port = Port == RemoteLocation.GetDefaultPort(Protocol) ? string.Empty : $":{Port}";
            return $"{RemoteLocation.GetScheme(Protocol)}://{host}{port}";
        }
    }

    public override string ToString() => PathRoot;
}

/// <summary>
/// Location of an FTP or SFTP server entered by the user or stored with a bookmark: a URL like "ftp://server/pub",
/// "ftps://server/dir" or "sftp://server/home/user". Slashes and backslashes both separate directories, so a path
/// shown on Windows like "sftp://server\home\user" is valid too. A URL can hold a user name and password
/// ("sftp://user:password@server") and escape characters ("%20"). Without a path, the volume starts in the user's home
/// directory.
/// </summary>
/// <param name="Server">Server.</param>
/// <param name="Directories">Directories from the root of the server, null for the user's home directory.</param>
/// <param name="Credentials">User name and password of the URL, null when it has none.</param>
public sealed record RemoteLocation(
    RemoteServer Server,
    IReadOnlyList<string>? Directories,
    VolumeCredentials? Credentials = null)
{
    /// <summary>Examples of locations.</summary>
    public const string Example = "ftp://server/dir or sftp://server/dir";

    /// <summary>Protocols of the connection dialog, in the order shown.</summary>
    public static IReadOnlyList<ConnectionProtocol> Protocols { get; } =
    [
        new(nameof(RemoteProtocol.Ftp).ToUpperInvariant(), "FTP", 21),
        new(nameof(RemoteProtocol.Ftps).ToUpperInvariant(), "FTPS (FTP over TLS)", 21),
        new(nameof(RemoteProtocol.Sftp).ToUpperInvariant(), "SFTP (SSH)", 22)
    ];

    /// <summary>URL scheme of a protocol, e.g. "sftp".</summary>
    public static string GetScheme(RemoteProtocol protocol) => protocol.ToString().ToLowerInvariant();

    /// <summary>Default port of a protocol.</summary>
    public static int GetDefaultPort(RemoteProtocol protocol) => protocol == RemoteProtocol.Sftp ? 22 : 21;

    /// <summary>Protocol of a <see cref="ConnectionProtocol.Name" /> or URL scheme, e.g. "SFTP".</summary>
    public static RemoteProtocol GetProtocol(string name) =>
        Enum.TryParse<RemoteProtocol>(name, true, out var protocol) && Enum.IsDefined(protocol)
            ? protocol
            : throw new IOException($"Protocol '{name}' isn't supported, use FTP, FTPS or SFTP");

    /// <summary>
    /// Parses a location. Throws <see cref="IOException" /> when it has no supported scheme, no host, an invalid port or
    /// "..".
    /// </summary>
    public static RemoteLocation Parse(string location)
    {
        ArgumentNullException.ThrowIfNull(location);

        var text = location.Trim();
        var schemeEnd = text.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0)
        {
            throw new IOException($"Enter a location like {Example}");
        }

        var protocol = GetProtocol(text[..schemeEnd]);
        text = text[(schemeEnd + 3)..];
        var separator = text.IndexOfAny(['/', '\\']);
        var authority = separator >= 0 ? text[..separator] : text;
        var path = separator >= 0 ? text[separator..] : null;

        VolumeCredentials? credentials = null;
        var at = authority.LastIndexOf('@');
        if (at >= 0)
        {
            var userInfo = authority[..at];
            authority = authority[(at + 1)..];
            var colon = userInfo.IndexOf(':');
            credentials = colon >= 0
                ? new VolumeCredentials(Unescape(userInfo[..colon]), Unescape(userInfo[(colon + 1)..]))
                : new VolumeCredentials(Unescape(userInfo), string.Empty);
        }

        var (host, port) = ParseAuthority(authority, location, protocol);
        IReadOnlyList<string>? directories = null;
        if (path != null)
        {
            var parts = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Select(Unescape)
                .Where(part => part != ".")
                .ToList();
            if (parts.Any(part => part == Paths.Parent))
            {
                throw new IOException($"Location '{location}' can't contain '..'");
            }

            directories = parts;
        }

        return new RemoteLocation(new RemoteServer(protocol, host, port), directories, credentials);
    }

    /// <summary>
    /// Location of connection details, e.g. "sftp://server:2222/home/user". Throws <see cref="IOException" /> when the
    /// host is empty or the port is invalid.
    /// </summary>
    public static string Format(ConnectionDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var protocol = GetProtocol(details.Protocol);
        var host = details.Host.Trim();
        if (host.StartsWith('[') && host.EndsWith(']'))
        {
            host = host[1..^1];
        }

        if (host.Length == 0)
        {
            throw new IOException("Enter the host of the server");
        }

        if (host.IndexOfAny(['/', '\\', '@', ' ']) >= 0)
        {
            throw new IOException($"Host '{host}' is invalid");
        }

        var port = details.Port ?? GetDefaultPort(protocol);
        if (port is < 1 or > 65535)
        {
            throw new IOException($"Port {port} is invalid, enter a port from 1 to 65535");
        }

        var parts = details.Path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part != ".")
            .ToList();
        if (parts.Any(part => part == Paths.Parent))
        {
            throw new IOException($"Path '{details.Path}' can't contain '..'");
        }

        return new RemoteLocation(new RemoteServer(protocol, host, port), details.Path.Trim().Length == 0 ? null : parts)
            .ToString();
    }

    /// <summary>Connection details of the location, without its credentials.</summary>
    public ConnectionDetails ToConnectionDetails() =>
        new(Server.Protocol.ToString().ToUpperInvariant(), Server.Host,
            Server.Port == GetDefaultPort(Server.Protocol) ? null : Server.Port,
            Directories == null ? string.Empty : "/" + string.Join('/', Directories));

    private static (string Host, int Port) ParseAuthority(string authority, string location, RemoteProtocol protocol)
    {
        string host;
        string? port = null;
        if (authority.StartsWith('['))
        {
            var end = authority.IndexOf(']');
            if (end < 0)
            {
                throw new IOException($"Location '{location}' has an invalid host");
            }

            var rest = authority[(end + 1)..];
            port = rest.StartsWith(':') ? rest[1..] : null;
            host = authority[1..end];
        }
        else if (authority.Count(c => c == ':') == 1)
        {
            // a single colon is a port, more are an IPv6 address
            var colon = authority.IndexOf(':');
            port = authority[(colon + 1)..];
            host = authority[..colon];
        }
        else
        {
            host = authority;
        }

        if (host.Length == 0)
        {
            throw new IOException($"Location '{location}' has no host");
        }

        if (port == null)
        {
            return (host, GetDefaultPort(protocol));
        }

        return int.TryParse(port, out var number) && number is >= 1 and <= 65535
            ? (host, number)
            : throw new IOException($"Location '{location}' has an invalid port '{port}'");
    }

    private static string Unescape(string part)
    {
        try
        {
            return Uri.UnescapeDataString(part);
        }
        catch (UriFormatException exception)
        {
            throw new IOException($"Location part '{part}' is invalid", exception);
        }
    }

    /// <summary>The location as a URL without credentials, e.g. "sftp://server/home/user".</summary>
    public override string ToString() =>
        Directories == null
            ? Server.PathRoot
            : Server.PathRoot + "/" + string.Join('/', Directories.Select(Uri.EscapeDataString));
}
