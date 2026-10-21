namespace Hst.Commander.Core.Tests;

/// <summary>
/// Lha and lzx archives in TestData/Archives, from the Hst.Compression tests.
/// </summary>
public static class ArchiveTestData
{
    /// <summary>
    /// Name of dirs-files.lha and dirs-files.lzx: archives with the directory entries dir1\dir3\ and dir2\ and the file
    /// dir1\file1.txt (1 byte), so dir1 only exists in the paths of entries.
    /// </summary>
    public const string DirsFiles = "dirs-files";

    public static string GetPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "Archives", fileName);

    public static byte[] Read(string fileName) => File.ReadAllBytes(GetPath(fileName));
}
