using SMBLibrary;

namespace Hst.Commander.Plugins.Smb;

/// <summary>Turns the status of failed SMB requests into the exceptions of the volume contract.</summary>
public static class SmbStatus
{
    /// <summary>True for the statuses of a user name or password that was rejected or is missing.</summary>
    public static bool IsLogonFailure(NTStatus status) => status is NTStatus.STATUS_LOGON_FAILURE
        or NTStatus.STATUS_WRONG_PASSWORD or NTStatus.STATUS_ACCESS_DENIED or NTStatus.STATUS_ACCOUNT_RESTRICTION
        or NTStatus.STATUS_LOGON_TYPE_NOT_GRANTED;

    /// <summary>Throws when a request failed: what is e.g. "Opening 'dir\file'".</summary>
    public static void ThrowIfFailed(NTStatus status, string what)
    {
        if (status != NTStatus.STATUS_SUCCESS)
        {
            throw CreateException(status, what);
        }
    }

    public static Exception CreateException(NTStatus status, string what) => status switch
    {
        NTStatus.STATUS_OBJECT_NAME_NOT_FOUND or NTStatus.STATUS_NO_SUCH_FILE =>
            new FileNotFoundException($"{what} failed: not found"),
        NTStatus.STATUS_OBJECT_PATH_NOT_FOUND or NTStatus.STATUS_NOT_A_DIRECTORY =>
            new DirectoryNotFoundException($"{what} failed: directory not found"),
        NTStatus.STATUS_BAD_NETWORK_NAME => new DirectoryNotFoundException($"{what} failed: share not found"),
        NTStatus.STATUS_ACCESS_DENIED or NTStatus.STATUS_MEDIA_WRITE_PROTECTED or NTStatus.STATUS_CANNOT_DELETE =>
            new UnauthorizedAccessException($"{what} failed: access denied"),
        NTStatus.STATUS_OBJECT_NAME_COLLISION or NTStatus.STATUS_OBJECT_NAME_EXISTS =>
            new IOException($"{what} failed: already exists"),
        NTStatus.STATUS_DIRECTORY_NOT_EMPTY => new IOException($"{what} failed: directory not empty"),
        NTStatus.STATUS_SHARING_VIOLATION => new IOException($"{what} failed: in use"),
        NTStatus.STATUS_DISK_FULL => new IOException($"{what} failed: disk full"),
        NTStatus.STATUS_FILE_IS_A_DIRECTORY => new IOException($"{what} failed: is a directory"),
        NTStatus.STATUS_OBJECT_NAME_INVALID or NTStatus.STATUS_OBJECT_PATH_INVALID
            or NTStatus.STATUS_OBJECT_PATH_SYNTAX_BAD => new IOException($"{what} failed: invalid name"),
        _ => new IOException($"{what} failed: {status}")
    };
}
