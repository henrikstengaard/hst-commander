using System.IO.Compression;

namespace Hst.Commander.Core.Tests;

/// <summary>
/// Creates zip archives for tests.
/// </summary>
public static class ZipTestData
{
    /// <summary>
    /// Creates a zip archive with entries by name, e.g. "dir1/file1.txt". Names ending with "/" are directories.
    /// </summary>
    public static byte[] CreateZip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zipArchive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (var (name, content) in entries)
            {
                var zipArchiveEntry = zipArchive.CreateEntry(name);
                if (name.EndsWith('/'))
                {
                    continue;
                }

                using var entryStream = zipArchiveEntry.Open();
                using var writer = new StreamWriter(entryStream);
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Creates a zip archive with "file1.txt", "dir1/file2.txt" and the directory "dir2/".
    /// </summary>
    public static byte[] CreateZipWithDirectoriesAndFiles() => CreateZip(
        ("file1.txt", "zip-file1"),
        ("dir1/file2.txt", "zip-file2"),
        ("dir2/", string.Empty));
}
