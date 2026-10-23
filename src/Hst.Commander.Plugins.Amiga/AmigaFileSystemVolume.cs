using System.Runtime.CompilerServices;
using Hst.Amiga.FileSystems;
using Hst.Amiga.FileSystems.Exceptions;
using Hst.Commander.Abstractions;
using AmigaEntry = Hst.Amiga.FileSystems.Entry;
using AmigaEntryType = Hst.Amiga.FileSystems.EntryType;
using AmigaFileMode = Hst.Amiga.FileSystems.FileMode;
using EntryType = Hst.Commander.Abstractions.EntryType;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// Volume browsing an Amiga file system (Fast File System or PFS3) of an adf or a partition of a rigid disk block,
/// writable when mounted from a writable stream. Changes are flushed after each operation. Amiga dates are in local
/// time and names are case-insensitive. Errors of the file system are thrown as <see cref="IOException" />.
/// </summary>
public class AmigaFileSystemVolume : IVolume, IDisposable
{
    private readonly IFileSystemVolume _fileSystemVolume;
    private readonly bool _isWritable;
    private readonly int _maxNameLength;
    private List<string> _currentPathComponents = [];

    /// <param name="name">Name of the volume.</param>
    /// <param name="fileSystemVolume">Mounted file system at its root, disposed with the volume.</param>
    /// <param name="isWritable">True when the file system can be changed.</param>
    /// <param name="maxNameLength">Maximum length of names in the file system, longer names are not created.</param>
    public AmigaFileSystemVolume(string name, IFileSystemVolume fileSystemVolume, bool isWritable, int maxNameLength)
    {
        ArgumentNullException.ThrowIfNull(fileSystemVolume);

        Name = name;
        _fileSystemVolume = fileSystemVolume;
        _isWritable = isWritable;
        _maxNameLength = maxNameLength;
    }

    public string Name { get; }

    public IEnumerable<string> CurrentPathComponents => _currentPathComponents.ToList();

    /// <summary>True when the current directory is the root of the file system.</summary>
    public bool IsAtRoot => _currentPathComponents.Count == 0;

    /// <summary>Size of the file system in bytes.</summary>
    public long Size => _fileSystemVolume.Size;

    /// <summary>Free space of the file system in bytes.</summary>
    public long Free => _fileSystemVolume.Free;

    /// <summary>Volume name of the file system, e.g. "Workbench".</summary>
    public string VolumeName => _fileSystemVolume.Name;

    public async IAsyncEnumerable<IEntry> ListEntries(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var entries = (await Run(() => _fileSystemVolume.ListEntries()))
            .Select(CreateEntry)
            .OrderBy(entry => entry.Type == EntryType.Container ? 0 : 1)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    public async Task ChangeDirectory(IEnumerable<string> pathComponents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathComponents);

        var previousPathComponents = _currentPathComponents.ToList();
        try
        {
            foreach (var pathComponent in pathComponents)
            {
                if (pathComponent.Equals(Paths.Root, StringComparison.OrdinalIgnoreCase))
                {
                    await ChangeToPath([]);
                }
                else if (pathComponent.Equals(Paths.Parent, StringComparison.OrdinalIgnoreCase))
                {
                    await ChangeToPath(_currentPathComponents.Take(Math.Max(_currentPathComponents.Count - 1, 0))
                        .ToList());
                }
                else
                {
                    var entry = await FindEntry(pathComponent);
                    if (entry == null || CreateEntry(entry).Type != EntryType.Container)
                    {
                        throw new DirectoryNotFoundException($"Directory '{pathComponent}' not found");
                    }

                    await Run(() => _fileSystemVolume.ChangeDirectory(entry.Name));
                    _currentPathComponents = [.._currentPathComponents, entry.Name];
                }
            }
        }
        catch
        {
            await ChangeToPath(previousPathComponents);
            throw;
        }
    }

    /// <summary>
    /// Changes to a directory from the root, one directory at a time, as not all file systems change to a path with
    /// several directories.
    /// </summary>
    private async Task ChangeToPath(List<string> pathComponents)
    {
        await Run(() => _fileSystemVolume.ChangeDirectory("/"));
        _currentPathComponents = [];
        foreach (var pathComponent in pathComponents)
        {
            await Run(() => _fileSystemVolume.ChangeDirectory(pathComponent));
            _currentPathComponents.Add(pathComponent);
        }
    }

    public async Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default)
    {
        var amigaEntry = await FindEntry(name) ?? throw new IOException($"Entry '{name}' not found");
        var entry = CreateEntry(amigaEntry);
        if (entry.Type == EntryType.Container)
        {
            await ChangeDirectory([amigaEntry.Name], cancellationToken);
        }

        return entry;
    }

    public async Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default)
    {
        var entry = await FindEntry(name);
        if (entry == null || CreateEntry(entry).Type != EntryType.Item)
        {
            throw new FileNotFoundException($"File '{name}' not found");
        }

        return await Run(() => _fileSystemVolume.OpenFile(entry.Name, AmigaFileMode.Read, false, true));
    }

    public async Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfReadOnly();
        ValidateName(name);

        if (await FindEntry(name) is { } entry && CreateEntry(entry).Type == EntryType.Container)
        {
            throw new IOException($"'{entry.Name}' is a directory");
        }

        var stream = await Run(() => _fileSystemVolume.OpenFile(name, AmigaFileMode.Write, true, true));
        return new FlushingStream(stream, _fileSystemVolume);
    }

    public async Task CreateContainer(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfReadOnly();
        ValidateName(name);

        if (await FindEntry(name) is { } entry)
        {
            if (CreateEntry(entry).Type == EntryType.Container)
            {
                return;
            }

            throw new IOException($"'{entry.Name}' already exists");
        }

        await Run(() => _fileSystemVolume.CreateDirectory(name));
        await Flush();
    }

    public async Task Delete(string name, CancellationToken cancellationToken = default)
    {
        ThrowIfReadOnly();

        var entry = await FindEntry(name) ?? throw new FileNotFoundException($"Entry '{name}' not found");
        await Run(() => _fileSystemVolume.Delete(entry.Name, true));
        await Flush();
    }

    public async Task Rename(string name, string newName, CancellationToken cancellationToken = default)
    {
        ThrowIfReadOnly();
        ValidateName(newName);

        var entry = await FindEntry(name) ?? throw new FileNotFoundException($"Entry '{name}' not found");
        if (entry.Name.Equals(newName, StringComparison.Ordinal))
        {
            return;
        }

        // names are case-insensitive, so only the case is changed by renaming to a temporary name first
        if (entry.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
        {
            var temporaryName = await GetTemporaryName();
            await Run(() => _fileSystemVolume.Rename(entry.Name, temporaryName));
            await Run(() => _fileSystemVolume.Rename(temporaryName, newName));
        }
        else
        {
            if (await FindEntry(newName) is { } existingEntry)
            {
                throw new IOException($"'{existingEntry.Name}' already exists");
            }

            await Run(() => _fileSystemVolume.Rename(entry.Name, newName));
        }

        await Flush();
    }

    private async Task<string> GetTemporaryName()
    {
        for (var number = 0;; number++)
        {
            var temporaryName = $"~hst-commander-{number}";
            if (await FindEntry(temporaryName) == null)
            {
                return temporaryName[..Math.Min(temporaryName.Length, _maxNameLength)];
            }
        }
    }

    public async Task SetModifiedDate(string name, DateTime modifiedDate,
        CancellationToken cancellationToken = default)
    {
        ThrowIfReadOnly();

        var entry = await FindEntry(name) ?? throw new FileNotFoundException($"Entry '{name}' not found");
        await Run(() => _fileSystemVolume.SetDate(entry.Name, ToAmigaDate(modifiedDate)));
        await Flush();
    }

    public Task<bool> SupportsAmigaMetadata(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public async Task SetAmigaMetadata(string name, AmigaMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ThrowIfReadOnly();

        var entry = await FindEntry(name) ?? throw new FileNotFoundException($"Entry '{name}' not found");
        var comment = metadata.Comment.Length > MaxCommentLength
            ? metadata.Comment[..MaxCommentLength]
            : metadata.Comment;
        await Run(() => _fileSystemVolume.SetProtectionBits(entry.Name,
            ProtectionBitsConverter.ToProtectionBits(metadata.ProtectionBits)));
        await Run(() => _fileSystemVolume.SetComment(entry.Name, comment));
        await Flush();
    }

    /// <summary>Maximum length of comments in Amiga file systems.</summary>
    private const int MaxCommentLength = 79;

    /// <summary>
    /// Amiga date of a date, in local time without a time zone. Dates before the Amiga epoch, 1 January 1978, can't
    /// be stored and are set to the epoch.
    /// </summary>
    private static DateTime ToAmigaDate(DateTime date)
    {
        var utcDate = date.Kind == DateTimeKind.Local
            ? date.ToUniversalTime()
            : DateTime.SpecifyKind(date, DateTimeKind.Utc);
        var localDate = DateTime.SpecifyKind(utcDate.ToLocalTime(), DateTimeKind.Unspecified);
        return localDate < AmigaEpoch ? AmigaEpoch : localDate;
    }

    private static readonly DateTime AmigaEpoch = new(1978, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>Modified date of an Amiga date in local time, as UTC.</summary>
    private static DateTime FromAmigaDate(DateTime date) =>
        DateTime.SpecifyKind(date, DateTimeKind.Local).ToUniversalTime();

    private static IEntry CreateEntry(AmigaEntry entry)
    {
        var isDirectory = entry.Type switch
        {
            AmigaEntryType.Dir or AmigaEntryType.DirLink => true,
            AmigaEntryType.SoftLink => entry.LinkEntry?.Type is AmigaEntryType.Dir or AmigaEntryType.DirLink,
            _ => false
        };

        var metadata = new AmigaMetadata(ProtectionBitsConverter.ToProtectionValue(entry.ProtectionBits),
            entry.Comment ?? string.Empty);
        return isDirectory
            ? new AmigaVolumeEntry(entry.Name, EntryType.Container, null, FromAmigaDate(entry.Date), metadata)
            : new AmigaVolumeEntry(entry.Name, EntryType.Item,
                entry.Type == AmigaEntryType.SoftLink && entry.LinkEntry != null ? entry.LinkEntry.Size : entry.Size,
                FromAmigaDate(entry.Date), metadata);
    }

    /// <summary>Finds an entry in the current directory, case-insensitive like Amiga file systems.</summary>
    private async Task<AmigaEntry?> FindEntry(string name)
    {
        if (name.IndexOfAny(['/', ':']) >= 0)
        {
            return null;
        }

        var entries = await Run(() => _fileSystemVolume.ListEntries());
        return entries.FirstOrDefault(entry => entry.Name.Equals(name, StringComparison.Ordinal)) ??
               entries.FirstOrDefault(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private void ValidateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0 || name.IndexOfAny(['/', ':']) >= 0)
        {
            throw new IOException($"Invalid Amiga name '{name}', names can't contain '/' or ':'");
        }

        if (name.Length > _maxNameLength)
        {
            throw new IOException(
                $"Name '{name}' is longer than {_maxNameLength} characters, the maximum of the file system");
        }
    }

    private void ThrowIfReadOnly()
    {
        if (!_isWritable)
        {
            throw new NotSupportedException("The Amiga file system is read-only");
        }
    }

    private Task Flush() => Run(() => _fileSystemVolume.Flush());

    /// <summary>Runs a file system operation, throwing its errors as <see cref="IOException" />.</summary>
    private static async Task Run(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception exception) when (IsFileSystemError(exception))
        {
            throw new IOException(exception.Message, exception);
        }
    }

    /// <summary>Runs a file system operation, throwing its errors as <see cref="IOException" />.</summary>
    private static async Task<T> Run<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (Exception exception) when (IsFileSystemError(exception))
        {
            throw new IOException(exception.Message, exception);
        }
    }

    private static bool IsFileSystemError(Exception exception) =>
        exception is FileSystemException or ArgumentException;

    public void Dispose()
    {
        // PFS3 flushes when disposed, waiting for async writes, so it runs on the thread pool: on a UI thread the
        // writes would wait for the blocked thread
        RunBlocking(() =>
        {
            _fileSystemVolume.Dispose();
            return Task.CompletedTask;
        });
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Waits for an operation run on the thread pool, so it doesn't wait for a blocked thread with a synchronization
    /// context, e.g. a UI thread.
    /// </summary>
    private static void RunBlocking(Func<Task> operation) => Task.Run(operation).GetAwaiter().GetResult();

    private sealed record AmigaVolumeEntry(
        string Name,
        EntryType Type,
        long? Size,
        DateTime? ModifiedDate,
        AmigaMetadata AmigaMetadata) : IEntry
    {
        AmigaMetadata? IEntry.AmigaMetadata => AmigaMetadata;
    }

    /// <summary>Stream writing a file, flushing the file system when closed, e.g. caches of PFS3.</summary>
    private sealed class FlushingStream(Stream stream, IFileSystemVolume fileSystemVolume) : Stream
    {
        private bool _disposed;

        public override bool CanRead => stream.CanRead;
        public override bool CanSeek => stream.CanSeek;
        public override bool CanWrite => stream.CanWrite;
        public override long Length => stream.Length;

        public override long Position
        {
            get => stream.Position;
            set => stream.Position = value;
        }

        public override void Flush() => stream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) => stream.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);
        public override void SetLength(long value) => stream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);

        public override Task WriteAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) => stream.WriteAsync(buffer, offset, count, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                RunBlocking(async () =>
                {
                    await stream.DisposeAsync();
                    await fileSystemVolume.Flush();
                });
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                await stream.DisposeAsync();
                await fileSystemVolume.Flush();
            }

            await base.DisposeAsync();
        }
    }
}
