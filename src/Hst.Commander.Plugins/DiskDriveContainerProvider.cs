namespace Hst.Commander.Plugins;

public class DiskDriveContainerProvider : IContainerProvider
{
    public string CombinePath(string path1, string path2)
    {
        return
            path1.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Length == 1 &&
            path2 == ".."
                ? string.Empty
                : Path.Combine(path1, path2);
    }

    public string GetParentPath(string path)
    {
        return path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Length == 1
            ? string.Empty
            : Path.GetDirectoryName(path) ?? string.Empty;
    }

    public Task<IEnumerable<IEntry>> GetEntries(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return Task.FromResult(DriveInfo.GetDrives().Select(CreateEntry));
        }

        var directoryInfo = new DirectoryInfo(path);

        var directories = directoryInfo.GetDirectories().Select(CreateEntry).OrderBy(x => x.Name);
        var files = directoryInfo.GetFiles().Select(CreateEntry).OrderBy(x => x.Name);

        return Task.FromResult(directories.Concat(files));
    }

    private IEntry CreateEntry(DriveInfo driveInfo)
    {
        return new Entry(driveInfo.Name, EntryType.Container);
    }

    private IEntry CreateEntry(DirectoryInfo directoryInfo)
    {
        return new Entry(directoryInfo.Name, EntryType.Container, 0, directoryInfo.LastWriteTimeUtc);
    }

    private IEntry CreateEntry(FileInfo fileInfo)
    {
        return new Entry(fileInfo.Name, EntryType.Item, fileInfo.Length, fileInfo.LastWriteTimeUtc);
    }

    public Task NewContainer(string path)
    {
        // Directory.CreateDirectory(path);
        throw new NotImplementedException();
    }
}