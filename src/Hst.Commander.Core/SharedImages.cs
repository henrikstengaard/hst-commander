using System.Runtime.CompilerServices;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Writable images (local files opened as volumes by a file volume provider that can write, e.g. Amiga disk files)
///     opened once per file and shared by both panes and file operations. Two volumes writing the same file would each
///     keep their own state of the file system, e.g. the free blocks, and damage it, so all use the same volume of the
///     file through views of their own (<see cref="SharedImageVolume" />) with their own current directory. The file is
///     closed when the last view is disposed.
/// </summary>
internal static class SharedImages
{
    private static readonly Lock OpenLock = new();
    private static readonly Dictionary<string, SharedImage> Images = new(PathComparer);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>
    ///     Opens a view of the image of a local file at its root, opening the file the first time: for reading and
    ///     writing when possible, otherwise only for reading (e.g. a read-only file), and then the volume is read-only.
    /// </summary>
    public static async Task<SharedImageVolume> OpenAsync(string path, IFileVolumeProvider fileVolumeProvider,
        CancellationToken cancellationToken)
    {
        path = Path.GetFullPath(path);
        lock (OpenLock)
        {
            if (Images.TryGetValue(path, out var openImage))
            {
                return openImage.OpenView();
            }
        }

        var stream = OpenFile(path);
        SharedImage image;
        try
        {
            var volume = await fileVolumeProvider.CreateVolume(stream, cancellationToken);
            image = new SharedImage(path, volume, stream);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }

        lock (OpenLock)
        {
            // the image may have been opened at the same time, e.g. for a copy on a background thread
            if (!Images.TryGetValue(path, out var openImage))
            {
                Images[path] = image;
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

    /// <summary>
    ///     Opens a file for reading and writing, shared with other readers and writers, e.g. a pane copying the file.
    ///     It's unbuffered, so changes are written to the file right away and seen by others reading it.
    /// </summary>
    private static FileStream OpenFile(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, 0);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 0);
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

            Images.Remove(image.Path);
        }

        image.Close();
    }

    /// <summary>An image shared by views. Operations are serialized with a lock, as volumes are not thread safe.</summary>
    internal sealed class SharedImage(string path, IVolume volume, Stream stream)
    {
        public string Path { get; } = path;

        public IVolume Volume { get; } = volume;

        public bool IsWritable => stream.CanWrite;

        public SemaphoreSlim Lock { get; } = new(1, 1);

        /// <summary>Number of open views, changed while holding the open lock.</summary>
        public int ViewCount { get; set; }

        public SharedImageVolume OpenView()
        {
            ViewCount++;
            return new SharedImageVolume(this);
        }

        public void Release() => SharedImages.Release(this);

        public void Close()
        {
            Lock.Wait();
            try
            {
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
            (Stream)new LockedStream(await volume.OpenRead(name, cancellationToken), _image.Lock), cancellationToken);

    public Task<Stream> OpenWrite(string name, CancellationToken cancellationToken = default) =>
        RunAsync(async volume =>
            (Stream)new LockedStream(await volume.OpenWrite(name, cancellationToken), _image.Lock), cancellationToken);

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
    ///     the lock (<see cref="SharedImages.SharedImage.Close" /> and the synchronous members of the streams).
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

                return await operation(volume);
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

    /// <summary>Stream of an entry in a shared image, holding the image's lock while reading and writing.</summary>
    private sealed class LockedStream(Stream stream, SemaphoreSlim imageLock) : Stream
    {
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
            imageLock.Wait();
            try
            {
                return operation();
            }
            finally
            {
                imageLock.Release();
            }
        }

        /// <summary>Runs an operation holding the lock on the thread pool, like <c>SharedImageVolume.RunAsync</c>.</summary>
        private Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) =>
            Task.Run(async () =>
            {
                await imageLock.WaitAsync(cancellationToken);
                try
                {
                    return await operation();
                }
                finally
                {
                    imageLock.Release();
                }
            }, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                Run(() =>
                {
                    stream.Dispose();
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
                    await stream.DisposeAsync();
                    return true;
                }, CancellationToken.None);
            }

            await base.DisposeAsync();
        }
    }
}
