using FluentFTP;
using FluentFTP.Exceptions;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Ftp;

/// <summary>
/// Connection to an FTP or FTPS server with FluentFTP. As FTP transfers one file at a time per connection and the file's
/// transfer must finish before other commands, each opened file has a connection of its own
/// (<paramref name="openTransfer" />), closed with the stream, so the volume can list and change directories meanwhile.
/// </summary>
/// <param name="client">Logged in client for listing and changing files.</param>
/// <param name="openTransfer">Connects and logs in a new client for transferring a file.</param>
internal sealed class FtpRemoteClient(
    AsyncFtpClient client,
    Func<CancellationToken, Task<AsyncFtpClient>> openTransfer) : IRemoteClient
{
    public bool IsConnected => client.IsConnected;

    public Task<string> GetHomeDirectory(CancellationToken cancellationToken) =>
        Run(async () =>
        {
            var directory = await client.GetWorkingDirectory(cancellationToken);
            return directory.StartsWith('/') ? directory : "/";
        }, "Reading the home directory");

    public Task<IReadOnlyList<RemoteEntry>> List(string path, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            var items = await client.GetListing(path, cancellationToken);
            var entries = new List<RemoteEntry>();
            foreach (var item in items)
            {
                if (item.Name is "." or ".." or "")
                {
                    continue;
                }

                entries.Add(await CreateEntry(item, cancellationToken));
            }

            return (IReadOnlyList<RemoteEntry>)entries;
        }, $"Listing '{path}'");

    private async Task<RemoteEntry> CreateEntry(FtpListItem item, CancellationToken cancellationToken)
    {
        var isDirectory = item.Type switch
        {
            FtpObjectType.Directory => true,
            FtpObjectType.Link => await client.DirectoryExists(item.FullName, cancellationToken),
            _ => false
        };
        return new RemoteEntry(item.Name, isDirectory, isDirectory ? null : item.Size,
            item.Modified == DateTime.MinValue ? null : DateTime.SpecifyKind(item.Modified, DateTimeKind.Utc));
    }

    public Task<RemoteEntry?> GetEntry(string path, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            var name = RemoteVolume.GetName(path);
            if (await client.DirectoryExists(path, cancellationToken))
            {
                return new RemoteEntry(name, true, null, null);
            }

            // the listing of the parent has the size and date, also from servers without MLST
            var parent = RemoteVolume.GetParent(path);
            var items = await client.GetListing(parent, cancellationToken);
            var item = items.FirstOrDefault(item => item.Name == name);
            return item == null ? null : await CreateEntry(item, cancellationToken);
        }, $"Reading '{path}'");

    public Task<bool> DirectoryExists(string path, CancellationToken cancellationToken) =>
        Run(() => client.DirectoryExists(path, cancellationToken), $"Opening '{path}'");

    public Task<Stream> OpenRead(string path, CancellationToken cancellationToken) =>
        OpenTransfer(path, false, cancellationToken);

    public Task<Stream> OpenWrite(string path, CancellationToken cancellationToken) =>
        OpenTransfer(path, true, cancellationToken);

    private async Task<Stream> OpenTransfer(string path, bool write, CancellationToken cancellationToken)
    {
        var transferClient = await openTransfer(cancellationToken);
        try
        {
            var stream = await Run(() => write
                    ? transferClient.OpenWrite(path, FtpDataType.Binary, false, cancellationToken)
                    : transferClient.OpenRead(path, FtpDataType.Binary, 0, true, cancellationToken),
                write ? $"Creating '{path}'" : $"Opening '{path}'");
            return new FtpTransferStream(stream, transferClient, path, write);
        }
        catch
        {
            transferClient.Dispose();
            throw;
        }
    }

    public Task CreateDirectory(string path, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            if (!await client.DirectoryExists(path, cancellationToken))
            {
                await ExecuteChecked($"MKD {path}", cancellationToken);
            }

            return true;
        }, $"Creating directory '{path}'");

    public Task DeleteFile(string path, CancellationToken cancellationToken) =>
        Run(() => ExecuteChecked($"DELE {path}", cancellationToken), $"Deleting '{path}'");

    /// <summary>Deletes an empty directory with RMD, as FluentFTP's DeleteDirectory deletes its contents too.</summary>
    public Task DeleteDirectory(string path, CancellationToken cancellationToken) =>
        Run(() => ExecuteChecked($"RMD {path}", cancellationToken), $"Deleting '{path}'");

    public Task Rename(string path, string newPath, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            await client.Rename(path, newPath, cancellationToken);
            return true;
        }, $"Renaming '{path}'");

    public Task SetModifiedDate(string path, DateTime modifiedDate, CancellationToken cancellationToken) =>
        Run(async () =>
        {
            if (!client.HasFeature(FtpCapability.MFMT))
            {
                throw new NotSupportedException("The server can't set modified dates");
            }

            var reply = await client.Execute($"MFMT {modifiedDate.ToUniversalTime():yyyyMMddHHmmss} {path}",
                cancellationToken);
            if (!reply.Success)
            {
                // e.g. directories on some servers; dates are kept when the server can
                throw new NotSupportedException($"The server can't set the modified date of '{path}': {reply.Message}");
            }

            return true;
        }, $"Setting the date of '{path}'");

    public Task<VolumeSpace?> GetSpace(string path, CancellationToken cancellationToken) =>
        Task.FromResult<VolumeSpace?>(null);

    /// <summary>Runs a command, throwing the server's reply when it fails.</summary>
    private async Task<bool> ExecuteChecked(string command, CancellationToken cancellationToken)
    {
        var reply = await client.Execute(command, cancellationToken);
        if (!reply.Success)
        {
            throw new FtpCommandException(reply);
        }

        return true;
    }

    private static async Task<T> Run<T>(Func<Task<T>> action, string what)
    {
        try
        {
            return await action();
        }
        catch (Exception exception) when (exception is FtpException or System.Net.Sockets.SocketException
                                              or TimeoutException or System.Net.WebException)
        {
            throw RemoteConnector.CreateFtpException(exception, what);
        }
    }

    public void Dispose() => client.Dispose();

    /// <summary>
    /// Stream of a file transferred on a connection of its own, disconnected when the stream is disposed. Disposing a
    /// written file reads the server's reply, so a failed upload (e.g. a full disk) throws <see cref="IOException" />.
    /// </summary>
    private sealed class FtpTransferStream(Stream stream, AsyncFtpClient client, string path, bool write) : Stream
    {
        private bool _disposed;

        public override bool CanRead => !write && stream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => write && stream.CanWrite;
        public override long Length => stream.Length;

        public override long Position
        {
            get => stream.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => stream.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => stream.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) => stream.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            stream.ReadAsync(buffer, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            stream.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) => stream.WriteAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                await stream.DisposeAsync();
                if (write)
                {
                    // the server replies when the file is stored
                    var reply = await client.GetReply(CancellationToken.None);
                    if (!reply.Success)
                    {
                        throw new IOException($"Writing '{path}' failed: {reply.Message}");
                    }
                }
            }
            catch (Exception exception) when (exception is FtpException or System.Net.Sockets.SocketException
                                                  or TimeoutException)
            {
                throw new IOException($"Writing '{path}' failed: {exception.Message}", exception);
            }
            finally
            {
                client.Dispose();
                GC.SuppressFinalize(this);
            }
        }
    }
}
