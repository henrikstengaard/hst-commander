using System.Net.Sockets;
using Hst.Commander.Abstractions;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Hst.Commander.Plugins.Ftp;

/// <summary>
/// Connection to an SFTP server with SSH.NET. Opened files share the connection, as SFTP transfers several files and
/// runs other requests at once over one connection.
/// </summary>
internal sealed class SftpRemoteClient(SftpClient client) : IRemoteClient
{
    // the working directory after logging in, before checking directories changes it
    private readonly string _homeDirectory =
        client.WorkingDirectory is { Length: > 0 } directory && directory.StartsWith('/') ? directory : "/";

    public bool IsConnected => client.IsConnected;

    public Task<string> GetHomeDirectory(CancellationToken cancellationToken) => Task.FromResult(_homeDirectory);

    public Task<IReadOnlyList<RemoteEntry>> List(string path, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            var entries = new List<RemoteEntry>();
            await foreach (var file in client.ListDirectoryAsync(path, cancellationToken))
            {
                if (file.Name is "." or ".." or "")
                {
                    continue;
                }

                entries.Add(await CreateEntry(file, cancellationToken));
            }

            return (IReadOnlyList<RemoteEntry>)entries;
        }, $"Listing '{path}'");

    private async Task<RemoteEntry> CreateEntry(ISftpFile file, CancellationToken cancellationToken)
    {
        var isDirectory = file.IsDirectory ||
                          (file.IsSymbolicLink && await CanOpenDirectory(file.FullName, cancellationToken));
        return new RemoteEntry(file.Name, isDirectory, isDirectory ? null : file.Length,
            DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc));
    }

    /// <summary>True when a directory, or a link to one, exists at a path.</summary>
    private async Task<bool> IsDirectory(string path, CancellationToken cancellationToken)
    {
        try
        {
            // the attributes of a link are those of the link, not its target
            var attributes = await client.GetAttributesAsync(path, cancellationToken);
            return attributes.IsDirectory ||
                   (attributes.IsSymbolicLink && await CanOpenDirectory(path, cancellationToken));
        }
        catch (SftpPathNotFoundException)
        {
            return false;
        }
    }

    /// <summary>True when a path can be opened as a directory, e.g. a link to a directory.</summary>
    private async Task<bool> CanOpenDirectory(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var files = client.ListDirectoryAsync(path, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            await files.MoveNextAsync();
            return true;
        }
        catch (SshException)
        {
            return false;
        }
    }

    public Task<RemoteEntry?> GetEntry(string path, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            try
            {
                var file = await client.GetAsync(path, cancellationToken);
                var entry = await CreateEntry(file, cancellationToken);
                return (RemoteEntry?)(entry with { Name = RemoteVolume.GetName(path) });
            }
            catch (SftpPathNotFoundException)
            {
                return null;
            }
        }, $"Reading '{path}'");

    public Task<bool> DirectoryExists(string path, CancellationToken cancellationToken) =>
        Run(() => IsDirectory(path, cancellationToken), $"Opening '{path}'");

    public Task<Stream> OpenRead(string path, CancellationToken cancellationToken) =>
        Run<Stream>(async () => await client.OpenAsync(path, FileMode.Open, FileAccess.Read, cancellationToken),
            $"Opening '{path}'");

    public Task<Stream> OpenWrite(string path, CancellationToken cancellationToken) =>
        Run<Stream>(async () => await client.OpenAsync(path, FileMode.Create, FileAccess.Write, cancellationToken),
            $"Creating '{path}'");

    public Task CreateDirectory(string path, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            if (!await IsDirectory(path, cancellationToken))
            {
                await client.CreateDirectoryAsync(path, cancellationToken);
            }

            return true;
        }, $"Creating directory '{path}'");

    public Task DeleteFile(string path, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            await client.DeleteFileAsync(path, cancellationToken);
            return true;
        }, $"Deleting '{path}'");

    public Task DeleteDirectory(string path, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            await client.DeleteDirectoryAsync(path, cancellationToken);
            return true;
        }, $"Deleting '{path}'");

    public Task Rename(string path, string newPath, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            await client.RenameFileAsync(path, newPath, cancellationToken);
            return true;
        }, $"Renaming '{path}'");

    public Task SetModifiedDate(string path, DateTime modifiedDate, CancellationToken cancellationToken) =>
        Run(() => Task.Run(() =>
        {
            client.SetLastWriteTimeUtc(path, modifiedDate.ToUniversalTime());
            return true;
        }, cancellationToken), $"Setting the date of '{path}'");

    public async Task<VolumeSpace?> GetSpace(string path, CancellationToken cancellationToken)
    {
        try
        {
            // the statvfs@openssh.com extension, not supported by all servers
            var status = await client.GetStatusAsync(path, cancellationToken);
            var total = (long)(status.TotalBlocks * status.BlockSize);
            var free = (long)(status.AvailableBlocks * status.BlockSize);
            return total > 0 ? new VolumeSpace(free, total) : null;
        }
        catch (Exception exception) when (exception is SshException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private static async Task<T> Run<T>(Func<Task<T>> action, string what)
    {
        try
        {
            return await action();
        }
        catch (SftpPathNotFoundException exception)
        {
            throw new FileNotFoundException($"{what} failed: {exception.Message}", exception);
        }
        catch (SftpPermissionDeniedException exception)
        {
            throw new UnauthorizedAccessException($"{what} failed: {exception.Message}", exception);
        }
        catch (Exception exception) when (exception is SshException or SocketException or TimeoutException
                                              or ObjectDisposedException)
        {
            throw new IOException($"{what} failed: {exception.Message}", exception);
        }
    }

    public void Dispose()
    {
        try
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
        catch (Exception exception) when (exception is SshException or SocketException or ObjectDisposedException)
        {
            // the server may have closed the connection meanwhile
        }

        client.Dispose();
    }
}
