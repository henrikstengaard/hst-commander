using System.Runtime.CompilerServices;
using Hst.Commander.Abstractions;
using Hst.Commander.Core.Volumes;

namespace Hst.Commander.Core;

/// <summary>
///     Writable images (items opened as volumes by a file volume provider that can write, e.g. local Amiga disk files
///     or physical disks, see <see cref="IVolume.GetWritableImageId" />) opened once per id and shared by both panes
///     and file operations. Two volumes writing the same file would each keep their own state of the file system, e.g.
///     the free blocks, and damage it, so all use the same volume of the file through views of their own
///     (<see cref="SharedImageVolume" />) with their own current directory. The file is closed when the last view is
///     disposed. A stream caching writes (<see cref="IWriteCache" />, e.g. a physical disk) is committed when the file
///     system is consistent: after an operation with no file open for writing, when the cache asks for it, or when
///     nothing has been done for <see cref="IdleFlushDelay" />.
/// </summary>
internal static class SharedImages
{
    /// <summary>Time without operations after which the cached writes of an image are written.</summary>
    public static readonly TimeSpan IdleFlushDelay = TimeSpan.FromSeconds(1);

    private static readonly Lock OpenLock = new();
    private static readonly Dictionary<string, SharedImage> Images = new(IdComparer);

    private static StringComparer IdComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>
    ///     Opens a view of the image of an item at its root, opening the item the first time with the volume it's in:
    ///     for reading and writing when possible (<see cref="IVolume.OpenReadWrite" />), otherwise only for reading
    ///     (e.g. a read-only file), and then the volume is read-only.
    /// </summary>
    /// <param name="id">Id of the item, <see cref="IVolume.GetWritableImageId" />.</param>
    /// <param name="name">Name of the item in the current directory of <paramref name="volume" />.</param>
    /// <param name="volume">Volume the item is in, only used when the image isn't open.</param>
    /// <param name="fileVolumeProvider">File volume provider creating the volume of the image.</param>
    /// <param name="cancellationToken"></param>
    public static async Task<SharedImageVolume> OpenAsync(string id, string name, IVolume volume,
        IFileVolumeProvider fileVolumeProvider, CancellationToken cancellationToken)
    {
        lock (OpenLock)
        {
            if (Images.TryGetValue(id, out var openImage))
            {
                return openImage.OpenView();
            }
        }

        var stream = await OpenStreamAsync(name, volume, cancellationToken);
        SharedImage image;
        try
        {
            var imageVolume = await fileVolumeProvider.CreateVolume(stream, cancellationToken);
            image = new SharedImage(id, imageVolume, stream);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }

        lock (OpenLock)
        {
            // the image may have been opened at the same time, e.g. for a copy on a background thread
            if (!Images.TryGetValue(id, out var openImage))
            {
                Images[id] = image;
                return image.OpenView();
            }

            image.Close();
            return openImage.OpenView();
        }
    }

    /// <summary>True when the image of a local file is open, e.g. to check that it's closed again in tests.</summary>
    public static bool IsOpen(string path)
    {
        lock (OpenLock)
        {
            return Images.ContainsKey(Path.GetFullPath(path));
        }
    }

    /// <summary>Opens an item for reading and writing, or only for reading when it can't be written.</summary>
    private static async Task<Stream> OpenStreamAsync(string name, IVolume volume,
        CancellationToken cancellationToken)
    {
        try
        {
            return await volume.OpenReadWrite(name, cancellationToken);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return await volume.OpenRead(name, cancellationToken);
        }
    }

    private static void Release(SharedImage image)
    {
        lock (OpenLock)
        {
            if (--image.ViewCount > 0)
            {
                return;
            }

            Images.Remove(image.Id);
        }

        image.Close();
    }

    /// <summary>An image shared by views. Operations are serialized with a lock, as volumes are not thread safe.</summary>
    /// <summary>Progress calling a handler on the thread reporting it.</summary>
    private sealed class ActionProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    internal sealed class SharedImage(string id, IVolume volume, Stream stream)
    {
        private Timer? _idleFlushTimer;
        private bool _closed;

        public string Id { get; } = id;

        public IVolume Volume { get; } = volume;

        public bool IsWritable => stream.CanWrite;

        public SemaphoreSlim Lock { get; } = new(1, 1);

        /// <summary>Number of open views, changed while holding the open lock.</summary>
        public int ViewCount { get; set; }

        /// <summary>Number of files open for writing, changed while holding <see cref="Lock" />.</summary>
        public int OpenWriteStreams { get; set; }

        public SharedImageVolume OpenView()
        {
            ViewCount++;
            return new SharedImageVolume(this);
        }

        public void Release() => SharedImages.Release(this);

        /// <summary>
        ///     Called holding <see cref="Lock" /> after an operation, when the file system is consistent unless a file is
        ///     open for writing: commits cached writes when the cache asks for it, otherwise they are committed when
        ///     nothing has been done for <see cref="IdleFlushDelay" />.
        /// </summary>
        /// <param name="progress">Reports the progress of a commit, e.g. to the progress dialog of a copy.</param>
        public void OperationCompleted(IProgress<DiskWriteProgress>? progress)
        {
            if (_closed || stream is not IWriteCache { HasCachedWrites: true } cache)
            {
                return;
            }

            if (cache.ShouldFlush && OpenWriteStreams == 0)
            {
                Commit(cache, progress);
                return;
            }

            _idleFlushTimer ??= new Timer(_ => _ = FlushWhenIdleAsync());
            _idleFlushTimer.Change(IdleFlushDelay, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        ///     Commits the cached writes when no file is open for writing, e.g. at the end of a copy, so they aren't
        ///     written after the progress dialog is closed. Runs on the thread pool, like the operations.
        /// </summary>
        public Task CommitAsync(IProgress<DiskWriteProgress>? progress) => Task.Run(async () =>
        {
            await Lock.WaitAsync();
            try
            {
                if (!_closed && OpenWriteStreams == 0 && stream is IWriteCache { HasCachedWrites: true } cache)
                {
                    Commit(cache, progress);
                }
            }
            finally
            {
                Lock.Release();
            }
        });

        private static void Commit(IWriteCache cache, IProgress<DiskWriteProgress>? progress)
        {
            if (progress == null)
            {
                cache.Commit();
                return;
            }

            var totalBytes = cache.CachedWriteBytes;
            progress.Report(new DiskWriteProgress(0, totalBytes));
            cache.Commit(new ActionProgress<long>(writtenBytes =>
                progress.Report(new DiskWriteProgress(writtenBytes, totalBytes))));
        }

        private async Task FlushWhenIdleAsync()
        {
            await Lock.WaitAsync();
            try
            {
                // a file open for writing flushes when it's closed
                if (!_closed && OpenWriteStreams == 0 && stream is IWriteCache { HasCachedWrites: true } cache)
                {
                    cache.Commit();
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // the writes stay cached and are written again by the next flush, which throws the error
            }
            finally
            {
                Lock.Release();
            }
        }

        public void Close()
        {
            Lock.Wait();
            try
            {
                _closed = true;
                _idleFlushTimer?.Dispose();
                (Volume as IDisposable)?.Dispose();
                stream.Dispose();
            }
            finally
            {
                Lock.Release();
            }
        }
    }
}

/// <summary>
///     View of a shared image with a current directory of its own (see <see cref="SharedImages" />). Each operation
///     changes the shared volume to the view's directory first while holding the image's lock, and so does each read
///     and write of the streams opened. Disposing the view releases the image.
/// </summary>
internal sealed class SharedImageVolume : IVolume, IDisposable
{
    private readonly SharedImages.SharedImage _image;
    private List<string> _currentPathComponents = [];
    private bool _disposed;

    public SharedImageVolume(SharedImages.SharedImage image)
    {
        _image = image;
    }

    /// <summary>True when the image can be changed, false when its file can only be read.</summary>
    public bool IsWritable => _image.IsWritable;

    public string Name => _image.Volume.Name;

    public IEnumerable<string> CurrentPathComponents => _currentPathComponents.ToList();

    /// <summary>
    ///     Reports the progress of committing cached writes after the operations of this view, e.g. to the progress
    ///     dialog of a copy; null when not shown.
    /// </summary>
    public IProgress<DiskWriteProgress>? DiskWriteProgress { get; set; }

    /// <summary>Commits the cached writes of the image (see <see cref="SharedImages.SharedImage.CommitAsync" />).</summary>
    public Task CommitAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _image.CommitAsync(DiskWriteProgress);
    }

    public async IAsyncEnumerable<IEntry> ListEntries(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var entries = await RunAsync(async volume =>
        {
            var list = new List<IEntry>();
            await foreach (var entry in volume.ListEntries(cancellationToken))
            {
                list.Add(entry);
            }

            return list;
        }, cancellationToken);

        foreach (var entry in entries)
        {
            yield return entry;
        }
    }

    public Task ChangeDirectory(IEnumerable<string> pathComponents, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pathComponents);

        var pathComponentsList = pathComponents.ToList();
        return RunAsync(async volume =>
        {
            await volume.ChangeDirectory(pathComponentsList, cancellationToken);
            _currentPathComponents = volume.CurrentPathComponents.ToList();
            return true;
        }, cancellationToken);
    }

    public Task<IEntry> OpenEntry(string name, CancellationToken cancellationToken = default) =>
        RunAsync(async volume =>
        {
            var entry = await volume.OpenEntry(name, cancellationToken);
            _currentPathComponents = volume.CurrentPathComponents.ToList();
            return entry;
        }, cancellationToken);

    public Task<Stream> OpenRead(string name, CancellationToken cancellationToken = default) =>
        RunAsync(async volume =>
            (Stream)new LockedStream(await volume.OpenRead(name, cancellationToken), _image, false), cancellationToken);

    public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default) =>
        RunAsync(async volume =>
        {
            var stream = new LockedStream(await volume.OpenWrite(name, cancellationToken), _image, true,
                DiskWriteProgress);
            _image.OpenWriteStreams++;
            return (Stream)stream;
        }, cancellationToken);

    public Task CreateContainer(string name, CancellationToken cancellationToken = default) =>
        RunAsync(async volume =>
        {
            await volume.CreateContainer(name, cancellationToken);
            return true;
        }, cancellationToken);

    public Task Delete(string name, CancellationToken cancellationToken = default) =>
        RunAsync(async volume =>
        {
            await volume.Delete(name, cancellationToken);
            return true;
        }, cancellationToken);

    public Task Rename(string name, string newName, CancellationToken cancellationToken = default) =>
        RunAsync(async volume =>
        {
            await volume.Rename(name, newName, cancellationToken);
            return true;
        }, cancellationToken);

    public Task SetModifiedDate(string name, DateTime modifiedDate, CancellationToken cancellationToken = default) =>
        RunAsync(async volume =>
        {
            await volume.SetModifiedDate(name, modifiedDate, cancellationToken);
            return true;
        }, cancellationToken);

    public Task<bool> SupportsAmigaMetadata(CancellationToken cancellationToken = default) =>
        RunAsync(volume => volume.SupportsAmigaMetadata(cancellationToken), cancellationToken);

    public Task SetAmigaMetadata(string name, AmigaMetadata metadata, CancellationToken cancellationToken = default) =>
        RunAsync(async volume =>
        {
            await volume.SetAmigaMetadata(name, metadata, cancellationToken);
            return true;
        }, cancellationToken);

    public Task<bool> CanSetAmigaMetadata(CancellationToken cancellationToken = default) =>
        RunAsync(volume => volume.CanSetAmigaMetadata(cancellationToken), cancellationToken);

    public Task<EntryAttributes> GetChangeableAttributes(CancellationToken cancellationToken = default) =>
        RunAsync(volume => volume.GetChangeableAttributes(cancellationToken), cancellationToken);

    public Task SetAttributes(string name, EntryAttributes attributes, CancellationToken cancellationToken = default) =>
        RunAsync(async volume =>
        {
            await volume.SetAttributes(name, attributes, cancellationToken);
            return true;
        }, cancellationToken);

    /// <summary>
    ///     Runs an operation on the shared volume in the view's current directory, holding the image's lock. It runs on
    ///     the thread pool, so the lock is never held by work waiting for a UI thread, which may be blocked waiting for
    ///     the lock (<see cref="SharedImages.SharedImage.Close" /> and the synchronous members of the streams). Cached
    ///     writes may be flushed after the operation (<see cref="SharedImages.SharedImage.OperationCompleted" />).
    /// </summary>
    private Task<T> RunAsync<T>(Func<IVolume, Task<T>> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Task.Run(async () =>
        {
            await _image.Lock.WaitAsync(cancellationToken);
            try
            {
                var volume = _image.Volume;
                if (!volume.CurrentPathComponents.SequenceEqual(_currentPathComponents, StringComparer.Ordinal))
                {
                    await volume.ChangeDirectory([Paths.Root, .._currentPathComponents], cancellationToken);
                }

                var result = await operation(volume);
                _image.OperationCompleted(DiskWriteProgress);
                return result;
            }
            finally
            {
                _image.Lock.Release();
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _image.Release();
    }

    /// <summary>
    ///     Stream of an entry in a shared image, holding the image's lock while reading and writing. A file written is
    ///     counted as open for writing until disposed (<see cref="SharedImages.SharedImage.OpenWriteStreams" />).
    /// </summary>
    /// <param name="stream">Stream of the entry.</param>
    /// <param name="image">Image the entry is in.</param>
    /// <param name="isWrite">True for a file opened for writing.</param>
    /// <param name="diskWriteProgress">Progress of committing cached writes when a file written is closed.</param>
    private sealed class LockedStream(Stream stream, SharedImages.SharedImage image, bool isWrite,
        IProgress<DiskWriteProgress>? diskWriteProgress = null) : Stream
    {
        private readonly SemaphoreSlim _imageLock = image.Lock;
        private bool _disposed;

        public override bool CanRead => stream.CanRead;
        public override bool CanSeek => stream.CanSeek;
        public override bool CanWrite => stream.CanWrite;
        public override long Length => Run(() => stream.Length);

        public override long Position
        {
            get => Run(() => stream.Position);
            set => Run(() => stream.Position = value);
        }

        public override void Flush() => Run(() =>
        {
            stream.Flush();
            return true;
        });

        public override int Read(byte[] buffer, int offset, int count) =>
            Run(() => stream.Read(buffer, offset, count));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) =>
            await RunAsync(() => stream.ReadAsync(buffer, offset, count, cancellationToken), cancellationToken);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            await RunAsync(() => stream.ReadAsync(buffer, cancellationToken).AsTask(), cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => Run(() => stream.Seek(offset, origin));

        public override void SetLength(long value) => Run(() =>
        {
            stream.SetLength(value);
            return true;
        });

        public override void Write(byte[] buffer, int offset, int count) => Run(() =>
        {
            stream.Write(buffer, offset, count);
            return true;
        });

        public override Task WriteAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) =>
            RunAsync(async () =>
            {
                await stream.WriteAsync(buffer, offset, count, cancellationToken);
                return true;
            }, cancellationToken);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            await RunAsync(async () =>
            {
                await stream.WriteAsync(buffer, cancellationToken);
                return true;
            }, cancellationToken);

        private T Run<T>(Func<T> operation)
        {
            _imageLock.Wait();
            try
            {
                return operation();
            }
            finally
            {
                _imageLock.Release();
            }
        }

        /// <summary>Runs an operation holding the lock on the thread pool, like <c>SharedImageVolume.RunAsync</c>.</summary>
        private Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) =>
            Task.Run(async () =>
            {
                await _imageLock.WaitAsync(cancellationToken);
                try
                {
                    return await operation();
                }
                finally
                {
                    _imageLock.Release();
                }
            }, cancellationToken);

        /// <summary>Called holding the lock when disposed: the file system is consistent again after a file is written.</summary>
        private void Closed()
        {
            if (isWrite)
            {
                image.OpenWriteStreams--;
                image.OperationCompleted(diskWriteProgress);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                Run(() =>
                {
                    try
                    {
                        stream.Dispose();
                    }
                    finally
                    {
                        Closed();
                    }

                    return true;
                });
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                await RunAsync(async () =>
                {
                    try
                    {
                        await stream.DisposeAsync();
                    }
                    finally
                    {
                        Closed();
                    }

                    return true;
                }, CancellationToken.None);
            }

            await base.DisposeAsync();
        }
    }
}
