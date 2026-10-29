using System.Security.Cryptography;

namespace Hst.Commander.Core;

/// <summary>
///     A file opened in an external editor (see <see cref="ExternalEditor" />). Local files are edited in place. A file
///     inside an image or another volume is copied to a temporary directory, edited there and written back by
///     <see cref="SaveChangesAsync" /> when it has changed; disposing deletes the temporary copy.
/// </summary>
public sealed class ExternalEditFile : IDisposable
{
    private readonly Func<string, CancellationToken, Task>? _writeBack;
    private readonly string? _temporaryDirectory;
    private byte[]? _savedHash;

    private ExternalEditFile(string name, string path, string? temporaryDirectory,
        Func<string, CancellationToken, Task>? writeBack)
    {
        Name = name;
        Path = path;
        _temporaryDirectory = temporaryDirectory;
        _writeBack = writeBack;
    }

    public string Name { get; }

    /// <summary>Path of the file the editor opens: the local file or the temporary copy.</summary>
    public string Path { get; }

    /// <summary>True for a temporary copy of a file inside a volume, whose changes are written back.</summary>
    public bool IsTemporary => _temporaryDirectory != null;

    /// <summary>A local file, edited in place.</summary>
    public static ExternalEditFile ForLocalFile(string path) =>
        new(System.IO.Path.GetFileName(path), path, null, null);

    /// <summary>
    ///     Copies a file to a temporary directory, keeping its name so the editor recognizes its type.
    ///     <paramref name="writeBack" /> writes the copy at the path back.
    /// </summary>
    internal static async Task<ExternalEditFile> CreateTemporaryAsync(string name, Stream source,
        Func<string, CancellationToken, Task> writeBack, CancellationToken cancellationToken)
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hst-commander-edit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = System.IO.Path.Combine(directory, name);
            await using (var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            var file = new ExternalEditFile(name, path, directory, writeBack);
            file._savedHash = await HashAsync(path, cancellationToken);
            return file;
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }
    }

    /// <summary>
    ///     Writes the temporary copy back when it has changed since it was copied or last written back. Returns true
    ///     when written; false for local files and unchanged copies.
    /// </summary>
    public async Task<bool> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (_writeBack == null || !File.Exists(Path))
        {
            return false;
        }

        var hash = await HashAsync(Path, cancellationToken);
        if (_savedHash != null && hash.AsSpan().SequenceEqual(_savedHash))
        {
            return false;
        }

        await _writeBack(Path, cancellationToken);
        _savedHash = hash;
        return true;
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken cancellationToken)
    {
        // shared with the editor, which may still have the file open
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return await SHA256.HashDataAsync(stream, cancellationToken);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        if (_temporaryDirectory != null)
        {
            TryDeleteDirectory(_temporaryDirectory);
        }
    }
}
