using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using FluentFTP;
using FluentFTP.Exceptions;
using Hst.Commander.Abstractions;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Hst.Commander.Plugins.Ftp;

/// <summary>
/// Connects to FTP and FTPS servers with FluentFTP and to SFTP servers with SSH.NET, checking SSH host keys and FTPS
/// certificates that the operating system doesn't trust with <see cref="KnownHosts" />.
/// </summary>
/// <param name="knownHosts">Keys of the servers connected to, <see cref="KnownHosts.Default" /> by default.</param>
public sealed class RemoteConnector(KnownHosts? knownHosts = null) : IRemoteConnector
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly KnownHosts _knownHosts = knownHosts ?? KnownHosts.Default;

    public Task<IRemoteClient> Connect(RemoteServer server, VolumeCredentials? credentials,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);

        return server.Protocol == RemoteProtocol.Sftp
            ? ConnectSftp(server, credentials, cancellationToken)
            : ConnectFtp(server, credentials, cancellationToken);
    }

    private async Task<IRemoteClient> ConnectFtp(RemoteServer server, VolumeCredentials? credentials,
        CancellationToken cancellationToken) =>
        new FtpRemoteClient(await OpenFtp(server, credentials, cancellationToken),
            token => OpenFtp(server, credentials, token));

    /// <summary>Connects and logs in an FTP client, as anonymous without credentials.</summary>
    private async Task<AsyncFtpClient> OpenFtp(RemoteServer server, VolumeCredentials? credentials,
        CancellationToken cancellationToken)
    {
        var config = new FtpConfig
        {
            EncryptionMode = server.Protocol == RemoteProtocol.Ftps ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None,
            ConnectTimeout = (int)Timeout.TotalMilliseconds,
            ReadTimeout = (int)Timeout.TotalMilliseconds,
            DataConnectionConnectTimeout = (int)Timeout.TotalMilliseconds,
            DataConnectionReadTimeout = (int)Timeout.TotalMilliseconds
        };
        var client = credentials is { UserName.Length: > 0 }
            ? new AsyncFtpClient(server.Host, credentials.UserName, credentials.Password, server.Port, config)
            : new AsyncFtpClient(server.Host, "anonymous", "anonymous@", server.Port, config);

        string? keyFailure = null;
        client.ValidateCertificate += (_, e) =>
        {
            if (e.PolicyErrors == SslPolicyErrors.None)
            {
                e.Accept = true;
                return;
            }

            // e.g. a self-signed certificate, trusted the first time
            var fingerprint = Convert.ToBase64String(SHA256.HashData(e.Certificate.GetRawCertData()));
            keyFailure = _knownHosts.Check(server.PathRoot, KnownHosts.CertificateKeyType, fingerprint);
            e.Accept = keyFailure == null;
        };

        try
        {
            await client.Connect(cancellationToken);
            return client;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            client.Dispose();
            if (keyFailure != null)
            {
                throw new IOException(keyFailure, exception);
            }

            if (exception is FtpAuthenticationException)
            {
                throw new CredentialsRequiredException(credentials is not { UserName.Length: > 0 }
                    ? $"FTP server {server} needs a user name and password"
                    : $"Logging in to {server} as '{credentials.UserName}' failed, check the user name and password",
                    exception);
            }

            if (exception is FtpSecurityNotAvailableException)
            {
                throw new IOException($"FTP server {server} doesn't support TLS, connect with FTP instead",
                    exception);
            }

            throw new IOException($"Can't connect to {server}: {exception.Message}", exception);
        }
    }

    private async Task<IRemoteClient> ConnectSftp(RemoteServer server, VolumeCredentials? credentials,
        CancellationToken cancellationToken)
    {
        if (credentials is not { UserName.Length: > 0 })
        {
            throw new CredentialsRequiredException($"SFTP server {server} needs a user name and password");
        }

        var connectionInfo = new ConnectionInfo(server.Host, server.Port, credentials.UserName,
            new PasswordAuthenticationMethod(credentials.UserName, credentials.Password),
            new KeyboardInteractiveAuthenticationMethod(credentials.UserName))
        {
            Timeout = Timeout
        };

        // servers asking for the password with keyboard-interactive authentication get the password
        foreach (var method in connectionInfo.AuthenticationMethods.OfType<KeyboardInteractiveAuthenticationMethod>())
        {
            method.AuthenticationPrompt += (_, e) =>
            {
                foreach (var prompt in e.Prompts)
                {
                    prompt.Response = credentials.Password;
                }
            };
        }

        var client = new SftpClient(connectionInfo) { OperationTimeout = Timeout };
        string? keyFailure = null;
        client.HostKeyReceived += (_, e) =>
        {
            keyFailure = _knownHosts.Check(server.PathRoot, e.HostKeyName, e.FingerPrintSHA256);
            e.CanTrust = keyFailure == null;
        };

        try
        {
            await client.ConnectAsync(cancellationToken);
            return new SftpRemoteClient(client);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            client.Dispose();
            if (keyFailure != null)
            {
                throw new IOException(keyFailure, exception);
            }

            throw exception switch
            {
                SshAuthenticationException => new CredentialsRequiredException(
                    $"Logging in to {server} as '{credentials.UserName}' failed, check the user name and password",
                    exception),
                _ => new IOException($"Can't connect to {server}: {exception.Message}", exception)
            };
        }
    }

    /// <summary>Message of a failed FTP command with the server's reply.</summary>
    internal static Exception CreateFtpException(Exception exception, string what)
    {
        return exception switch
        {
            OperationCanceledException or IOException or UnauthorizedAccessException or NotSupportedException =>
                exception,
            FtpCommandException { CompletionCode: "550" } command when IsPermissionDenied(command.Message) =>
                new UnauthorizedAccessException($"{what} failed: {command.Message}", exception),
            FtpCommandException { CompletionCode: "550" } command => new FileNotFoundException(
                $"{what} failed: {command.Message}", exception),
            FtpCommandException { CompletionCode: "553" or "552" or "532" } command => new IOException(
                $"{what} failed: {command.Message}", exception),
            WebException or SocketException or TimeoutException or FtpException => new IOException(
                $"{what} failed: {exception.Message}", exception),
            _ => exception
        };
    }

    private static bool IsPermissionDenied(string message) =>
        message.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("denied", StringComparison.OrdinalIgnoreCase);
}
