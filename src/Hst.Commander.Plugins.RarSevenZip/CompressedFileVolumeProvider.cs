using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.RarSevenZip;

/// <summary>
/// File volume provider opening a compressed file (gzip or xz), detected by the signature of its format: a compressed
/// tar archive (e.g. .tar.gz or .tgz) opens as the tar archive, decompressed to a temporary file deleted with the volume
/// (tar archives are read at the offsets of their files); any other file as a volume with the decompressed file,
/// decompressed as it's read, e.g. "disk.img" in "disk.img.gz", which can be copied, viewed and opened as an image.
/// </summary>
public abstract class CompressedFileVolumeProvider : IFileVolumeProvider
{
    private const int CopyBufferSize = 1024 * 1024;

    public abstract string Name { get; }

    public abstract string Description { get; }

    /// <summary>Signature at the start of the compressed files.</summary>
    protected abstract byte[] Signature { get; }

    /// <summary>
    /// Extensions of the compressed files with the extension of the decompressed file, e.g. (".tgz", ".tar") and
    /// (".gz", ""), naming the decompressed file after the compressed one when it isn't stored.
    /// </summary>
    protected abstract IReadOnlyList<(string Extension, string DecompressedExtension)> Extensions { get; }

    /// <summary>Stream decompressing a compressed file as it's read, disposing <paramref name="stream" /> with it.</summary>
    protected abstract Stream Decompress(Stream stream);

    /// <summary>
    /// Name, modified date and size of the decompressed file as far as the format stores them, read from the seekable
    /// stream of the compressed file; the size is null when it isn't known exactly.
    /// </summary>
    protected abstract CompressedFileInfo ReadInfo(Stream stream);

    public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var signature = new byte[Signature.Length];
        var length = await stream.ReadAtLeastAsync(signature, signature.Length, false, cancellationToken);
        return length == signature.Length && signature.AsSpan().SequenceEqual(Signature);
    }

    /// <summary>Creates the volume without the name of the file, whose decompressed file is named "file" unless stored.</summary>
    public Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default) =>
        CreateVolume("file", stream, cancellationToken);

    public Task<IVolume> CreateVolume(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            stream.Position = 0;
            var info = ReadInfo(stream);
            stream.Position = 0;
            return Task.FromResult(IsTar(stream)
                ? OpenTar(stream, cancellationToken)
                : new CompressedFileVolume(Name, info.Name ?? GetDecompressedName(name), info, stream, this));
        }
        catch (Exception exception) when (ArchiveEntryStream.IsDamage(exception))
        {
            throw new InvalidDataException($"The {Name} file can't be decompressed: {exception.Message}", exception);
        }
    }

    /// <summary>Name of the decompressed file after the name of the compressed file, e.g. "disk.img" for "disk.img.gz".</summary>
    internal string GetDecompressedName(string name)
    {
        foreach (var (extension, decompressedExtension) in Extensions)
        {
            if (name.Length > extension.Length && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return name[..^extension.Length] + decompressedExtension;
            }
        }

        return name;
    }

    /// <summary>True when the decompressed file starts with a tar header.</summary>
    private bool IsTar(Stream stream)
    {
        var block = new byte[TarHeader.Size];
        int length;
        using (var decompressed = Decompress(new SharedStream(stream)))
        {
            length = decompressed.ReadAtLeast(block, block.Length, false);
        }

        stream.Position = 0;
        return TarHeader.IsTarHeader(block.AsSpan(0, length));
    }

    /// <summary>Decompresses a compressed tar archive to a temporary file deleted with the volume and opens it.</summary>
    private IVolume OpenTar(Stream stream, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid():N}.tar");
        var tarStream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, CopyBufferSize,
            FileOptions.DeleteOnClose);
        try
        {
            using (var decompressed = Decompress(new SharedStream(stream)))
            {
                // synchronously, decompressing is work for the processor and SharpCompress is slower asynchronously
                var buffer = new byte[CopyBufferSize];
                int length;
                while ((length = decompressed.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    tarStream.Write(buffer, 0, length);
                }
            }

            tarStream.Position = 0;
            return TarFileVolumeProvider.OpenTar(Name, tarStream, false);
        }
        catch
        {
            tarStream.Dispose();
            throw;
        }
    }

    /// <summary>Volume with the decompressed file of a compressed file, decompressed as it's read.</summary>
    private sealed class CompressedFileVolume : ArchiveVolume<CompressedFileInfo>
    {
        private readonly Stream _stream;
        private readonly CompressedFileVolumeProvider _provider;
        private readonly long? _size;

        public CompressedFileVolume(string name, string fileName, CompressedFileInfo info, Stream stream,
            CompressedFileVolumeProvider provider) : base(name)
        {
            _stream = stream;
            _provider = provider;
            _size = info.Size;
            // a name stored in the file may have a path, only its name is used
            AddEntry(Path.GetFileName(fileName.Replace('\\', '/')) is { Length: > 0 } entryName ? entryName : "file",
                info.Size ?? info.StoredSize ?? 0, info.ModifiedDate, info);
        }

        protected override Task<Stream> OpenArchiveEntry(string name, CompressedFileInfo archiveEntry,
            CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new ArchiveEntryStream(_provider.Decompress(new SharedStream(_stream)), name,
                _size, 0));
    }
}

/// <summary>
/// Name, modified date and size of the decompressed file of a compressed file, as far as its format stores them.
/// </summary>
/// <param name="Name">Name of the decompressed file, null when not stored.</param>
/// <param name="ModifiedDate">Modified date in UTC, null when not stored.</param>
/// <param name="Size">Exact size, null when not known exactly.</param>
/// <param name="StoredSize">Size shown when the exact size isn't known, e.g. gzip's size modulo 4 GB.</param>
public sealed record CompressedFileInfo(string? Name, DateTime? ModifiedDate, long? Size, long? StoredSize = null);
