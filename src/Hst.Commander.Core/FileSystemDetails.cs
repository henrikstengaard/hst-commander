using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Extensions.Volume.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Details of local files and directories shown in the properties dialog: type names, attributes, permissions and
///     the size on disk. Windows details come from the shell and file system, other systems get generic ones.
/// </summary>
public static class FileSystemDetails
{
    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        RecurseSubdirectories = false
    };

    /// <summary>
    ///     Name of the type of a file or directory like Windows Explorer shows it, e.g. "Text Document (.txt)" or
    ///     "File folder". Uses the type names registered in Windows, elsewhere the extension, e.g. "TXT File (.txt)".
    /// </summary>
    public static string GetTypeName(string name, bool isDirectory)
    {
        var shellTypeName = OperatingSystem.IsWindows() ? GetShellTypeName(name, isDirectory) : null;
        if (isDirectory)
        {
            return shellTypeName ?? "File folder";
        }

        var extension = Path.GetExtension(name);
        if (extension.Length <= 1)
        {
            return shellTypeName ?? "File";
        }

        var typeName = shellTypeName ?? $"{extension[1..].ToUpperInvariant()} File";
        return $"{typeName} ({extension.ToLowerInvariant()})";
    }

    /// <summary>
    ///     Attributes set, e.g. "Read-only, Hidden, Archive", or "None". On other systems than Windows, .NET sets
    ///     read-only for files the user can't write and hidden for names starting with a dot.
    /// </summary>
    public static string FormatAttributes(FileAttributes attributes)
    {
        var names = new List<string>();
        foreach (var (attribute, name) in AttributeNames)
        {
            if (attributes.HasFlag(attribute))
            {
                names.Add(name);
            }
        }

        return names.Count == 0 ? "None" : string.Join(", ", names);
    }

    private static readonly (FileAttributes Attribute, string Name)[] AttributeNames =
    [
        (FileAttributes.ReadOnly, "Read-only"),
        (FileAttributes.Hidden, "Hidden"),
        (FileAttributes.System, "System"),
        (FileAttributes.Archive, "Archive"),
        (FileAttributes.Compressed, "Compressed"),
        (FileAttributes.Encrypted, "Encrypted"),
        (FileAttributes.Temporary, "Temporary"),
        (FileAttributes.Offline, "Offline"),
        (FileAttributes.SparseFile, "Sparse"),
        (FileAttributes.ReparsePoint, "Link")
    ];

    /// <summary>Unix permissions like "ls -l" with the octal mode, e.g. "rwxr-xr-x (755)".</summary>
    public static string FormatUnixMode(UnixFileMode mode)
    {
        var permissions = new[]
        {
            (UnixFileMode.UserRead, 'r'), (UnixFileMode.UserWrite, 'w'), (UnixFileMode.UserExecute, 'x'),
            (UnixFileMode.GroupRead, 'r'), (UnixFileMode.GroupWrite, 'w'), (UnixFileMode.GroupExecute, 'x'),
            (UnixFileMode.OtherRead, 'r'), (UnixFileMode.OtherWrite, 'w'), (UnixFileMode.OtherExecute, 'x')
        };
        var text = new string(permissions.Select(permission => mode.HasFlag(permission.Item1) ? permission.Item2 : '-')
            .ToArray());
        var octal = Convert.ToString((int)mode & 0xfff, 8).PadLeft(3, '0');
        return $"{text} ({octal})";
    }

    /// <summary>Name of the type of a drive, e.g. "Local Disk" or "CD Drive".</summary>
    public static string GetDriveTypeName(DriveType driveType) => driveType switch
    {
        DriveType.Fixed => "Local Disk",
        DriveType.Removable => "Removable Disk",
        DriveType.Network => "Network Drive",
        DriveType.CDRom => "CD Drive",
        DriveType.Ram => "RAM Disk",
        _ => "Drive"
    };

    /// <summary>
    ///     Counts the size and number of entries of entries in a local directory like
    ///     <see cref="VolumeContents.CountContentsAsync" />, reading the file system directly. On Windows the size on
    ///     disk is counted too. Inaccessible directories are skipped and links to directories are not followed.
    /// </summary>
    public static EntryContents CountContents(string directory, IReadOnlyCollection<IEntry> entries,
        IProgress<EntryContents>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(entries);

        var clusterSize = OperatingSystem.IsWindows() ? GetClusterSize(directory) : null;
        var counter = new VolumeContents.ContentsCounter(progress, clusterSize != null);
        var countEntries = entries.Count > 1;
        var directories = new Stack<DirectoryInfo>();
        foreach (var entry in entries.Where(entry => !entry.Name.Equals(Paths.Parent, StringComparison.Ordinal)))
        {
            var path = Path.Combine(directory, entry.Name);
            if (entry.Type == EntryType.Container)
            {
                if (countEntries)
                {
                    counter.AddFolder();
                }

                directories.Push(new DirectoryInfo(path));
                continue;
            }

            var fileInfo = new FileInfo(path);
            var size = fileInfo.Exists ? fileInfo.Length : entry.Size ?? 0;
            counter.AddFile(size, GetSizeOnDisk(path, size, clusterSize), countEntries);
        }

        while (directories.TryPop(out var directoryInfo))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<FileSystemInfo> childInfos;
            try
            {
                childInfos = directoryInfo.EnumerateFileSystemInfos("*", EnumerationOptions).ToList();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var childInfo in childInfos)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (childInfo is DirectoryInfo childDirectory)
                {
                    counter.AddFolder();
                    if (!childDirectory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        directories.Push(childDirectory);
                    }
                }
                else if (childInfo is FileInfo childFile)
                {
                    counter.AddFile(childFile.Length,
                        GetSizeOnDisk(childFile.FullName, childFile.Length, clusterSize));
                }
            }
        }

        return counter.Complete();
    }

    /// <summary>
    ///     Space a file uses on the disk: its compressed size rounded up to whole clusters. Null without a cluster size,
    ///     i.e. on other systems than Windows.
    /// </summary>
    private static long? GetSizeOnDisk(string path, long size, long? clusterSize)
    {
        if (clusterSize is not { } cluster || !OperatingSystem.IsWindows())
        {
            return null;
        }

        var compressedSize = GetCompressedFileSize(path) ?? size;
        return (compressedSize + cluster - 1) / cluster * cluster;
    }

    [SupportedOSPlatform("windows")]
    private static long? GetClusterSize(string directory)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(directory));
        return root != null && GetDiskFreeSpace(root, out var sectorsPerCluster, out var bytesPerSector, out _, out _)
            ? (long)sectorsPerCluster * bytesPerSector
            : null;
    }

    [SupportedOSPlatform("windows")]
    private static long? GetCompressedFileSize(string path)
    {
        var low = GetCompressedFileSizeW(path, out var high);
        if (low == InvalidFileSize && Marshal.GetLastPInvokeError() != 0)
        {
            return null;
        }

        return ((long)high << 32) | low;
    }

    [SupportedOSPlatform("windows")]
    private static string? GetShellTypeName(string name, bool isDirectory)
    {
        try
        {
            var fileInfo = new ShFileInfo();
            var attributes = isDirectory ? FileAttributeDirectory : FileAttributeNormal;
            return SHGetFileInfo(name, attributes, ref fileInfo, (uint)Marshal.SizeOf<ShFileInfo>(),
                       ShgfiTypeName | ShgfiUseFileAttributes) != 0 && !string.IsNullOrWhiteSpace(fileInfo.szTypeName)
                ? fileInfo.szTypeName
                : null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private const uint InvalidFileSize = 0xffffffff;
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const uint ShgfiTypeName = 0x400;
    private const uint ShgfiUseFileAttributes = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi,
        uint cbFileInfo, uint uFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetDiskFreeSpaceW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpace(string lpRootPathName, out uint lpSectorsPerCluster,
        out uint lpBytesPerSector, out uint lpNumberOfFreeClusters, out uint lpTotalNumberOfClusters);
}
