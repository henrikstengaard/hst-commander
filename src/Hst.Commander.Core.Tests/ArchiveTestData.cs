namespace Hst.Commander.Core.Tests;

/// <summary>
/// Archives in TestData/Archives: lha and lzx archives from the Hst.Compression tests, a 7z archive made with 7-Zip and
/// rar 4 and 5 archives with stored entries written by hand. solid.7z (dir1/a.txt, dir1/b.txt, dir1/sub/c.txt and
/// other/d.txt in one solid block, containing "file a" to "file d") and deflate.7z are made with 7-Zip.
/// </summary>
public static class ArchiveTestData
{
    /// <summary>
    /// Name of dirs-files.lha, .lzx, .rar (rar 5), .7z and dirs-files-rar4.rar: archives with the directory entries dir1\dir3\ and dir2\ and the file
    /// dir1\file1.txt (1 byte), so dir1 only exists in the paths of entries.
    /// </summary>
    public const string DirsFiles = "dirs-files";

    public static string GetPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", fileName);

    public static byte[] Read(string fileName) => File.ReadAllBytes(GetPath(fileName));
}
