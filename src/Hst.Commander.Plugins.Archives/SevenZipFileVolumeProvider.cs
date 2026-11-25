using Hst.Commander.Abstractions;
using SharpCompress.Archives.SevenZip;
using ArchiveOperationException = SharpCompress.Common.ArchiveOperationException;
using IncompleteArchiveException = SharpCompress.Common.IncompleteArchiveException;
using SharpCompress.Readers;

namespace Hst.Commander.Plugins.Archives;

/// <summary>
/// File volume provider opening 7z archives, detected by their magic signature "7z" BC AF 27 1C.
/// </summary>
public class SevenZipFileVolumeProvider : IFileVolumeProvider
{
    private static readonly byte[] Signature = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];

    public string Name => "7z";

    public string Description => "7z archives";

    public async Task<bool> IsMatch(string name, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var signature = new byte[Signature.Length];
        var length = await stream.ReadAtLeastAsync(signature, signature.Length, false, cancellationToken);
        return length == signature.Length && signature.AsSpan().SequenceEqual(Signature);
    }

    public Task<IVolume> CreateVolume(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        stream.Position = 0;
        try
        {
            var archive = SevenZipArchive.OpenArchive(stream, new ReaderOptions { LeaveStreamOpen = true });
            try
            {
                return Task.FromResult<IVolume>(new SharpCompressVolume(Name, archive));
            }
            catch
            {
                archive.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (IsIncomplete(exception))
        {
            throw new InvalidDataException(IncompleteMessage, exception);
        }
    }

    /// <summary>Message of a 7z archive whose list of files is beyond the end of the file.</summary>
    public const string IncompleteMessage =
        "The 7z archive is incomplete: its list of files is beyond the end of the file. It may be a part of a split " +
        "archive whose other parts are missing (e.g. name.7z.002 next to name.7z.001), or a download that didn't finish.";

    /// <summary>
    ///     True for SharpCompress' errors of a 7z archive whose header points beyond the end of the stream, e.g. the
    ///     first part of a split archive opened alone or a truncated archive.
    /// </summary>
    private static bool IsIncomplete(Exception exception) =>
        exception is IncompleteArchiveException ||
        (exception is ArchiveOperationException && exception.Message.Contains("nextHeaderOffset", StringComparison.Ordinal));
}
