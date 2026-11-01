using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Smb;
using SMBLibrary;
using FileAttributes = SMBLibrary.FileAttributes;

namespace Hst.Commander.Core.Tests;

/// <summary>
/// Test SMB server for <see cref="SmbVolume" />: shares are local directories served by <see cref="DirectoryFileStore" />.
/// Logging in needs <see cref="Credentials" /> when set (a guest login otherwise), and shares in
/// <see cref="GuestDeniedShares" /> deny access to guests.
/// </summary>
public sealed class TestSmbServer : ISmbConnector
{
    private readonly Dictionary<string, DirectoryFileStore> _shares = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Credentials needed to log in, null to log in as guest.</summary>
    public VolumeCredentials? Credentials { get; set; }

    /// <summary>Shares guests can't open.</summary>
    public HashSet<string> GuestDeniedShares { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Connections made, the last one last.</summary>
    public List<TestSmbConnection> Connections { get; } = [];

    public void AddShare(string name, string path) => _shares[name] = new DirectoryFileStore(path);

    public ISmbConnection Connect(string server, VolumeCredentials? credentials)
    {
        if (Credentials != null && credentials != Credentials)
        {
            throw new CredentialsRequiredException($"SMB server '{server}' needs a user name and password");
        }

        var connection = new TestSmbConnection(this, credentials == null || credentials.UserName.Length == 0);
        Connections.Add(connection);
        return connection;
    }

    public sealed class TestSmbConnection(TestSmbServer server, bool isGuest) : ISmbConnection
    {
        public bool IsConnected { get; set; } = true;

        public bool IsDisposed { get; private set; }

        public int MaxReadSize { get; set; } = 7;

        public int MaxWriteSize { get; set; } = 5;

        public IReadOnlyList<string> ListShares() => server._shares.Keys.ToList();

        public INTFileStore OpenShare(string share)
        {
            if (!server._shares.TryGetValue(share, out var fileStore))
            {
                throw new DirectoryNotFoundException($"Share '{share}' not found");
            }

            return isGuest && server.GuestDeniedShares.Contains(share)
                ? throw new UnauthorizedAccessException($"Access to share '{share}' denied")
                : fileStore;
        }

        public void Dispose() => IsDisposed = true;
    }
}

/// <summary>
/// SMB file store serving a local directory with the requests <see cref="SmbVolume" /> uses. Attributes other than
/// directories are kept in memory, so they work on every operating system.
/// </summary>
public sealed class DirectoryFileStore(string rootPath) : INTFileStore
{
    private readonly Dictionary<string, FileAttributes> _attributes = new(StringComparer.Ordinal);

    private sealed class Handle(string path)
    {
        public string Path { get; set; } = path;
        public bool DeletePending { get; set; }
    }

    private string GetFullPath(string path) => path.Length == 0
        ? rootPath
        : Path.Combine(rootPath, path.Replace('\\', Path.DirectorySeparatorChar));

    public NTStatus CreateFile(out object handle, out FileStatus fileStatus, string path, AccessMask desiredAccess,
        FileAttributes fileAttributes, ShareAccess shareAccess, CreateDisposition createDisposition,
        CreateOptions createOptions, SecurityContext securityContext)
    {
        handle = null!;
        fileStatus = FileStatus.FILE_DOES_NOT_EXIST;
        var fullPath = GetFullPath(path);
        if (!Directory.Exists(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(fullPath)) ?? rootPath))
        {
            return NTStatus.STATUS_OBJECT_PATH_NOT_FOUND;
        }

        var isDirectory = Directory.Exists(fullPath);
        var exists = isDirectory || File.Exists(fullPath);
        var wantsDirectory = createOptions.HasFlag(CreateOptions.FILE_DIRECTORY_FILE);
        if (exists && wantsDirectory && !isDirectory)
        {
            return NTStatus.STATUS_NOT_A_DIRECTORY;
        }

        if (exists && createOptions.HasFlag(CreateOptions.FILE_NON_DIRECTORY_FILE) && isDirectory)
        {
            return NTStatus.STATUS_FILE_IS_A_DIRECTORY;
        }

        switch (createDisposition)
        {
            case CreateDisposition.FILE_OPEN when !exists:
            case CreateDisposition.FILE_OVERWRITE when !exists:
                return NTStatus.STATUS_OBJECT_NAME_NOT_FOUND;
            case CreateDisposition.FILE_CREATE when exists:
                return NTStatus.STATUS_OBJECT_NAME_COLLISION;
        }

        if (!exists)
        {
            if (wantsDirectory)
            {
                Directory.CreateDirectory(fullPath);
            }
            else
            {
                File.WriteAllBytes(fullPath, []);
            }

            fileStatus = FileStatus.FILE_CREATED;
        }
        else if (!isDirectory && createDisposition is CreateDisposition.FILE_OVERWRITE
                     or CreateDisposition.FILE_OVERWRITE_IF or CreateDisposition.FILE_SUPERSEDE)
        {
            File.WriteAllBytes(fullPath, []);
            fileStatus = FileStatus.FILE_OVERWRITTEN;
        }
        else
        {
            fileStatus = FileStatus.FILE_OPENED;
        }

        handle = new Handle(fullPath);
        return NTStatus.STATUS_SUCCESS;
    }

    public NTStatus CloseFile(object handle)
    {
        var fileHandle = (Handle)handle;
        if (fileHandle.DeletePending)
        {
            if (Directory.Exists(fileHandle.Path))
            {
                Directory.Delete(fileHandle.Path);
            }
            else
            {
                File.Delete(fileHandle.Path);
            }
        }

        return NTStatus.STATUS_SUCCESS;
    }

    public NTStatus ReadFile(out byte[] data, object handle, long offset, int maxCount)
    {
        using var stream = File.OpenRead(((Handle)handle).Path);
        if (offset >= stream.Length)
        {
            data = [];
            return NTStatus.STATUS_END_OF_FILE;
        }

        stream.Position = offset;
        var buffer = new byte[Math.Min(maxCount, stream.Length - offset)];
        stream.ReadExactly(buffer);
        data = buffer;
        return NTStatus.STATUS_SUCCESS;
    }

    public NTStatus WriteFile(out int numberOfBytesWritten, object handle, long offset, byte[] data)
    {
        using var stream = new FileStream(((Handle)handle).Path, FileMode.Open, FileAccess.Write);
        stream.Position = offset;
        stream.Write(data);
        numberOfBytesWritten = data.Length;
        return NTStatus.STATUS_SUCCESS;
    }

    public NTStatus FlushFileBuffers(object handle) => NTStatus.STATUS_SUCCESS;

    public NTStatus LockFile(object handle, long byteOffset, long length, bool exclusiveLock) =>
        NTStatus.STATUS_NOT_SUPPORTED;

    public NTStatus UnlockFile(object handle, long byteOffset, long length) => NTStatus.STATUS_NOT_SUPPORTED;

    public NTStatus QueryDirectory(out List<QueryDirectoryFileInformation> result, object handle, string fileName,
        FileInformationClass informationClass)
    {
        var path = ((Handle)handle).Path;
        result =
        [
            new FileDirectoryInformation { FileName = ".", FileAttributes = FileAttributes.Directory },
            new FileDirectoryInformation { FileName = "..", FileAttributes = FileAttributes.Directory },
            ..new DirectoryInfo(path).EnumerateFileSystemInfos().Select(info => new FileDirectoryInformation
            {
                FileName = info.Name,
                FileAttributes = GetAttributes(info.FullName),
                EndOfFile = info is FileInfo file ? file.Length : 0,
                LastWriteTime = info.LastWriteTimeUtc
            })
        ];

        // like SMB2, the last page ends with no more files
        return NTStatus.STATUS_NO_MORE_FILES;
    }

    private FileAttributes GetAttributes(string fullPath) => Directory.Exists(fullPath)
        ? FileAttributes.Directory | _attributes.GetValueOrDefault(fullPath)
        : _attributes.GetValueOrDefault(fullPath, FileAttributes.Archive);

    public NTStatus GetFileInformation(out FileInformation result, object handle,
        FileInformationClass informationClass)
    {
        var path = ((Handle)handle).Path;
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        var length = info is FileInfo file ? file.Length : 0;
        result = informationClass switch
        {
            FileInformationClass.FileNetworkOpenInformation => new FileNetworkOpenInformation
            {
                FileAttributes = GetAttributes(path),
                EndOfFile = length,
                LastWriteTime = info.LastWriteTimeUtc
            },
            FileInformationClass.FileStandardInformation => new FileStandardInformation
            {
                EndOfFile = length,
                Directory = info is DirectoryInfo
            },
            FileInformationClass.FileBasicInformation => new FileBasicInformation
            {
                FileAttributes = GetAttributes(path),
                LastWriteTime = new SetFileTime(info.LastWriteTimeUtc)
            },
            _ => null!
        };
        return result == null ? NTStatus.STATUS_NOT_SUPPORTED : NTStatus.STATUS_SUCCESS;
    }

    public NTStatus SetFileInformation(object handle, FileInformation information)
    {
        var fileHandle = (Handle)handle;
        switch (information)
        {
            case FileDispositionInformation disposition:
                if (Directory.Exists(fileHandle.Path) && Directory.EnumerateFileSystemEntries(fileHandle.Path).Any())
                {
                    return NTStatus.STATUS_DIRECTORY_NOT_EMPTY;
                }

                fileHandle.DeletePending = disposition.DeletePending;
                return NTStatus.STATUS_SUCCESS;
            case FileRenameInformationType2 rename:
                var newPath = GetFullPath(rename.FileName);
                if (Directory.Exists(fileHandle.Path))
                {
                    Directory.Move(fileHandle.Path, newPath);
                }
                else
                {
                    File.Move(fileHandle.Path, newPath, rename.ReplaceIfExists);
                }

                fileHandle.Path = newPath;
                return NTStatus.STATUS_SUCCESS;
            case FileBasicInformation basic:
                if (basic.LastWriteTime.Time is { } lastWriteTime)
                {
                    if (Directory.Exists(fileHandle.Path))
                    {
                        Directory.SetLastWriteTimeUtc(fileHandle.Path, lastWriteTime);
                    }
                    else
                    {
                        File.SetLastWriteTimeUtc(fileHandle.Path, lastWriteTime);
                    }
                }

                if (basic.FileAttributes != 0)
                {
                    _attributes[fileHandle.Path] = basic.FileAttributes & ~FileAttributes.Directory;
                }

                return NTStatus.STATUS_SUCCESS;
            case FileEndOfFileInformation endOfFile:
                using (var stream = new FileStream(fileHandle.Path, FileMode.Open, FileAccess.Write))
                {
                    stream.SetLength(endOfFile.EndOfFile);
                }

                return NTStatus.STATUS_SUCCESS;
            default:
                return NTStatus.STATUS_NOT_SUPPORTED;
        }
    }

    public NTStatus GetFileSystemInformation(out FileSystemInformation result,
        FileSystemInformationClass informationClass)
    {
        result = new FileFsFullSizeInformation
        {
            TotalAllocationUnits = 1000,
            CallerAvailableAllocationUnits = 250,
            ActualAvailableAllocationUnits = 250,
            SectorsPerAllocationUnit = 8,
            BytesPerSector = 512
        };
        return NTStatus.STATUS_SUCCESS;
    }

    public NTStatus SetFileSystemInformation(FileSystemInformation information) => NTStatus.STATUS_NOT_SUPPORTED;

    public NTStatus GetSecurityInformation(out SecurityDescriptor result, object handle,
        SecurityInformation securityInformation)
    {
        result = null!;
        return NTStatus.STATUS_NOT_SUPPORTED;
    }

    public NTStatus SetSecurityInformation(object handle, SecurityInformation securityInformation,
        SecurityDescriptor securityDescriptor) => NTStatus.STATUS_NOT_SUPPORTED;

    public NTStatus NotifyChange(out object ioRequest, object handle, NotifyChangeFilter completionFilter,
        bool watchTree, int outputBufferSize, OnNotifyChangeCompleted onNotifyChangeCompleted, object context)
    {
        ioRequest = null!;
        return NTStatus.STATUS_NOT_SUPPORTED;
    }

    public NTStatus Cancel(object ioRequest) => NTStatus.STATUS_NOT_SUPPORTED;

    public NTStatus DeviceIOControl(object handle, uint ctlCode, byte[] input, out byte[] output,
        int maxOutputLength)
    {
        output = [];
        return NTStatus.STATUS_NOT_SUPPORTED;
    }
}
