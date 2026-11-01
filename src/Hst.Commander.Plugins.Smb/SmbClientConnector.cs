using Hst.Commander.Abstractions;
using SMBLibrary;
using SMBLibrary.Client;

namespace Hst.Commander.Plugins.Smb;

/// <summary>Connects to SMB servers with the SMB 2 and 3 client of SMBLibrary, on port 445.</summary>
public sealed class SmbClientConnector : ISmbConnector
{
    /// <summary>Most bytes read or written with one request, so progress is reported often.</summary>
    private const int MaxTransferSize = 1024 * 1024;

    public ISmbConnection Connect(string server, VolumeCredentials? credentials)
    {
        ArgumentNullException.ThrowIfNull(server);

        var client = new SMB2Client();
        bool connected;
        try
        {
            connected = client.Connect(server, SMBTransportType.DirectTCPTransport);
        }
        catch (Exception exception)
        {
            // e.g. a server name that can't be resolved
            throw new IOException($"Can't connect to SMB server '{server}': {exception.Message}", exception);
        }

        if (!connected)
        {
            throw new IOException($"Can't connect to SMB server '{server}'");
        }

        var (domain, userName) = SplitUserName(credentials?.UserName ?? string.Empty);
        NTStatus status;
        try
        {
            status = client.Login(domain, userName, credentials?.Password ?? string.Empty);
        }
        catch (Exception exception)
        {
            client.Disconnect();
            throw new IOException($"Logging in to SMB server '{server}' failed: {exception.Message}", exception);
        }

        if (status == NTStatus.STATUS_SUCCESS)
        {
            return new SmbClientConnection(client);
        }

        client.Disconnect();
        if (SmbStatus.IsLogonFailure(status))
        {
            throw new CredentialsRequiredException(credentials == null
                ? $"SMB server '{server}' needs a user name and password"
                : $"Logging in to SMB server '{server}' as '{credentials.UserName}' failed, check the user name and password");
        }

        throw new IOException($"Logging in to SMB server '{server}' failed: {status}");
    }

    /// <summary>Domain and user name of "DOMAIN\user", an empty domain otherwise ("user" or "user@domain").</summary>
    public static (string Domain, string UserName) SplitUserName(string userName)
    {
        ArgumentNullException.ThrowIfNull(userName);

        var backslash = userName.IndexOf('\\');
        return backslash >= 0
            ? (userName[..backslash], userName[(backslash + 1)..])
            : (string.Empty, userName);
    }

    private sealed class SmbClientConnection(SMB2Client client) : ISmbConnection
    {
        private readonly List<ISMBFileStore> _shares = [];

        public bool IsConnected => client.IsConnected;

        public int MaxReadSize => (int)Math.Min(client.MaxReadSize, MaxTransferSize);

        public int MaxWriteSize => (int)Math.Min(client.MaxWriteSize, MaxTransferSize);

        public IReadOnlyList<string> ListShares()
        {
            var shares = client.ListShares(out var status);
            SmbStatus.ThrowIfFailed(status, "Listing shares");
            return shares;
        }

        public INTFileStore OpenShare(string share)
        {
            var fileStore = client.TreeConnect(share, out var status);
            SmbStatus.ThrowIfFailed(status, $"Opening share '{share}'");
            _shares.Add(fileStore);
            return fileStore;
        }

        public void Dispose()
        {
            if (!client.IsConnected)
            {
                return;
            }

            try
            {
                foreach (var share in _shares)
                {
                    share.Disconnect();
                }

                client.Logoff();
            }
            catch (Exception)
            {
                // the server may have closed the connection meanwhile
            }

            client.Disconnect();
        }
    }
}
