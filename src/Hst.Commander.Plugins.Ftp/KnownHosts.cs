namespace Hst.Commander.Plugins.Ftp;

/// <summary>
/// Keys of the servers connected to, trusted the first time a server is connected to and checked each time after, so a
/// server pretending to be another one (e.g. a man-in-the-middle attack) is detected: SSH host keys of SFTP servers and
/// the certificates of FTPS servers that aren't trusted by the operating system (e.g. self-signed ones). Stored in a text
/// file (<see cref="Default" />, <c>~/.hst-commander/ftp-known-hosts.txt</c>) with a line per server, "server key-type
/// fingerprint", read again on each call. A server whose key changed on purpose is trusted again by removing its line.
/// </summary>
public sealed class KnownHosts(string filePath)
{
    /// <summary>Key type of the certificates of FTPS servers.</summary>
    public const string CertificateKeyType = "x509";

    private static readonly Lock FileLock = new();

    /// <summary>Known hosts in the settings directory of Hst Commander.</summary>
    public static KnownHosts Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hst-commander", "ftp-known-hosts.txt"));

    /// <summary>Path of the file.</summary>
    public string FilePath => filePath;

    /// <summary>
    /// Checks the key of a server: returns null when it is the known key, or when the server isn't known and the key is
    /// added. Returns the message for the user when the server is known with another key, e.g. a man-in-the-middle
    /// attack, so the connection must be refused.
    /// </summary>
    /// <param name="server">Server, e.g. "sftp://server:22".</param>
    /// <param name="keyType">Type of the key, e.g. "ssh-ed25519" or "x509".</param>
    /// <param name="fingerprint">Fingerprint of the key, e.g. its SHA-256 hash in base64.</param>
    public string? Check(string server, string keyType, string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(keyType);
        ArgumentNullException.ThrowIfNull(fingerprint);

        lock (FileLock)
        {
            var lines = Read();
            foreach (var line in lines)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3 || !parts[0].Equals(server, StringComparison.OrdinalIgnoreCase) ||
                    !parts[1].Equals(keyType, StringComparison.Ordinal))
                {
                    continue;
                }

                var key = keyType == CertificateKeyType ? "TLS certificate" : $"{keyType} host key";
                return parts[2].Equals(fingerprint, StringComparison.Ordinal)
                    ? null
                    : $"The {key} of {server} has changed, so it may not be the server connected to before " +
                      $"(someone may be intercepting the connection). Its fingerprint is {fingerprint}. If the key " +
                      $"was changed on purpose, remove the line of {server} from {filePath} to trust the new key.";
            }

            try
            {
                if (Path.GetDirectoryName(filePath) is { Length: > 0 } directory)
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllLines(filePath, [$"{server} {keyType} {fingerprint}"]);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // the key is trusted for this connection, and the server is checked again next time
            }

            return null;
        }
    }

    private List<string> Read()
    {
        try
        {
            return File.Exists(filePath) ? File.ReadAllLines(filePath).ToList() : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
