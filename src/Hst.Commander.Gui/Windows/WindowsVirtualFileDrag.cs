using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;
using Hst.Commander.Core;
using ComIStream = System.Runtime.InteropServices.ComTypes.IStream;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace Hst.Commander.Gui.Windows;

/// <summary>
///     Drags entries of an image or another volume that isn't a local directory to other applications on Windows as
///     virtual files: the files are described with "FileGroupDescriptorW" and each file's contents are a stream of
///     "FileContents", which the application dropped on (e.g. Explorer) reads when they are dropped, so nothing is
///     copied first. The data object supports asynchronous operation (<c>IDataObjectAsyncCapability</c>), so Explorer
///     copies on a thread of its own with its own progress dialog while Hst Commander stays usable. Avalonia's drag
///     source only offers data in memory, so this runs its own OLE drag with <c>SHDoDragDrop</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsVirtualFileDrag
{
    /// <summary>
    ///     Drags the entries of a drag source, returning when the drag has ended (the files may still be read
    ///     afterwards, the drag source is disposed when they have been). Must be called on the UI thread while the
    ///     mouse button is pressed. Errors reading the entries are reported to <paramref name="failed" /> on any
    ///     thread.
    /// </summary>
    public static void DoDragDrop(IntPtr hwnd, Task<VolumeDragSource> source, Action<Exception> failed)
    {
        // a drop target that stopped reading the files of the last drag (e.g. cancelled) doesn't say so
        s_lastDataObject?.Dispose();
        var dataObject = s_lastDataObject = new VirtualFileDataObject(source, failed);
        try
        {
            SHDoDragDrop(hwnd, dataObject, IntPtr.Zero, DropEffectCopy | DropEffectMove, out _);
        }
        finally
        {
            dataObject.DragEnded();
        }
    }

    /// <summary>Data object of the last drag, whose files may still be read.</summary>
    private static VirtualFileDataObject? s_lastDataObject;

    private const int DropEffectCopy = 1;
    private const int DropEffectMove = 2;

    [DllImport("shell32.dll")]
    private static extern int SHDoDragDrop(IntPtr hwnd, IOleDataObject dataObject, IntPtr dropSource, int okEffects,
        out int effect);

    /// <summary>OLE <c>IDataObject</c> with HRESULTs returned, so formats not offered don't throw.</summary>
    [ComImport]
    [Guid("0000010e-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOleDataObject
    {
        [PreserveSig]
        int GetData(ref FORMATETC format, out STGMEDIUM medium);

        [PreserveSig]
        int GetDataHere(ref FORMATETC format, ref STGMEDIUM medium);

        [PreserveSig]
        int QueryGetData(ref FORMATETC format);

        [PreserveSig]
        int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut);

        [PreserveSig]
        int SetData(ref FORMATETC format, ref STGMEDIUM medium, [MarshalAs(UnmanagedType.Bool)] bool release);

        [PreserveSig]
        int EnumFormatEtc(DATADIR direction, out IEnumFORMATETC? enumerator);

        [PreserveSig]
        int DAdvise(ref FORMATETC format, ADVF advf, IntPtr adviseSink, out int connection);

        [PreserveSig]
        int DUnadvise(int connection);

        [PreserveSig]
        int EnumDAdvise(out IntPtr enumerator);
    }

    /// <summary>Lets the drop target read the data on a thread of its own after the drag has ended.</summary>
    [ComImport]
    [Guid("3D8B0590-F691-11d2-8EA9-006097DF5BD4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDataObjectAsyncCapability
    {
        [PreserveSig]
        int SetAsyncMode(int doOpAsync);

        [PreserveSig]
        int GetAsyncMode(out int isOpAsync);

        [PreserveSig]
        int StartOperation(IntPtr bindContext);

        [PreserveSig]
        int InOperation(out int inAsyncOp);

        [PreserveSig]
        int EndOperation(int result, IntPtr bindContext, int effects);
    }

    private const int S_OK = 0;
    private const int E_NOTIMPL = unchecked((int)0x80004001);
    private const int E_UNEXPECTED = unchecked((int)0x8000FFFF);
    private const int DV_E_FORMATETC = unchecked((int)0x80040064);
    private const int DV_E_TYMED = unchecked((int)0x80040069);
    private const int DV_E_LINDEX = unchecked((int)0x80040068);
    private const int OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003);
    private const int DATA_S_SAMEFORMATETC = 0x00040130;
    private const int STG_E_INVALIDFUNCTION = unchecked((int)0x80030001);

    private static readonly short FileGroupDescriptorFormat = RegisterFormat("FileGroupDescriptorW");
    private static readonly short FileContentsFormat = RegisterFormat("FileContents");
    private static readonly short PreferredDropEffectFormat = RegisterFormat("Preferred DropEffect");

    private static short RegisterFormat(string name) => unchecked((short)RegisterClipboardFormat(name));

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormat(string format);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr memory);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    private const uint GHND = 0x0042;

    private static IntPtr CreateHGlobal(byte[] bytes)
    {
        var memory = GlobalAlloc(GHND, (UIntPtr)bytes.Length);
        if (memory == IntPtr.Zero)
        {
            throw new OutOfMemoryException();
        }

        Marshal.Copy(bytes, 0, GlobalLock(memory), bytes.Length);
        GlobalUnlock(memory);
        return memory;
    }

    private static byte[] ReadHGlobal(IntPtr memory)
    {
        var bytes = new byte[(int)GlobalSize(memory)];
        Marshal.Copy(GlobalLock(memory), bytes, 0, bytes.Length);
        GlobalUnlock(memory);
        return bytes;
    }

    /// <summary>
    ///     The dragged files: "FileGroupDescriptorW", a stream per file in "FileContents", "Preferred DropEffect" copy
    ///     and the formats the shell stores with <c>SetData</c>, e.g. the drag image.
    /// </summary>
    private sealed class VirtualFileDataObject(Task<VolumeDragSource> source, Action<Exception> failed)
        : IOleDataObject, IDataObjectAsyncCapability
    {
        private readonly object _lock = new();
        private readonly Dictionary<short, byte[]> _storedData = [];
        private readonly HashSet<VolumeFileStream> _streams = [];
        private bool _isAsync = true;
        private bool _inOperation;
        private bool _dragEnded;
        private bool _isDisposed;
        private bool _hasFailed;

        private IReadOnlyList<DragFile> Files =>
            Task.Run(async () => await (await source).ListFilesAsync()).GetAwaiter().GetResult();

        public int GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            medium = default;
            var result = QueryGetData(ref format);
            if (result != S_OK)
            {
                return result;
            }

            try
            {
                if (format.cfFormat == FileContentsFormat)
                {
                    var files = Files;
                    if (format.lindex < 0 || format.lindex >= files.Count || files[format.lindex].IsDirectory)
                    {
                        return DV_E_LINDEX;
                    }

                    var stream = new VolumeFileStream(source, files[format.lindex], ReportFailure, StreamRead);
                    lock (_lock)
                    {
                        _streams.Add(stream);
                    }

                    medium.tymed = TYMED.TYMED_ISTREAM;
                    medium.unionmember = Marshal.GetComInterfaceForObject<VolumeFileStream, ComIStream>(stream);
                    return S_OK;
                }

                byte[] bytes;
                if (format.cfFormat == FileGroupDescriptorFormat)
                {
                    bytes = CreateFileGroupDescriptor(Files);
                }
                else if (format.cfFormat == PreferredDropEffectFormat)
                {
                    bytes = BitConverter.GetBytes(DropEffectCopy);
                }
                else
                {
                    lock (_lock)
                    {
                        bytes = _storedData[format.cfFormat];
                    }
                }

                medium.tymed = TYMED.TYMED_HGLOBAL;
                medium.unionmember = CreateHGlobal(bytes);
                return S_OK;
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
                return E_UNEXPECTED;
            }
        }

        public int GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => E_NOTIMPL;

        public int QueryGetData(ref FORMATETC format)
        {
            if (format.dwAspect != DVASPECT.DVASPECT_CONTENT)
            {
                return DV_E_FORMATETC;
            }

            if (format.cfFormat == FileContentsFormat)
            {
                return (format.tymed & TYMED.TYMED_ISTREAM) != 0 ? S_OK : DV_E_TYMED;
            }

            bool isOffered;
            lock (_lock)
            {
                isOffered = format.cfFormat == FileGroupDescriptorFormat ||
                            format.cfFormat == PreferredDropEffectFormat ||
                            _storedData.ContainsKey(format.cfFormat);
            }

            if (!isOffered)
            {
                return DV_E_FORMATETC;
            }

            return (format.tymed & TYMED.TYMED_HGLOBAL) != 0 ? S_OK : DV_E_TYMED;
        }

        public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
        {
            formatOut = formatIn;
            formatOut.ptd = IntPtr.Zero;
            return DATA_S_SAMEFORMATETC;
        }

        /// <summary>Stores data the shell keeps with the data object, e.g. the drag image and the drop effect.</summary>
        public int SetData(ref FORMATETC format, ref STGMEDIUM medium, bool release)
        {
            if (medium.tymed != TYMED.TYMED_HGLOBAL || format.cfFormat == FileGroupDescriptorFormat ||
                format.cfFormat == FileContentsFormat)
            {
                return E_NOTIMPL;
            }

            var bytes = ReadHGlobal(medium.unionmember);
            lock (_lock)
            {
                _storedData[format.cfFormat] = bytes;
            }

            if (release)
            {
                ReleaseStgMedium(ref medium);
            }

            return S_OK;
        }

        public int EnumFormatEtc(DATADIR direction, out IEnumFORMATETC? enumerator)
        {
            if (direction != DATADIR.DATADIR_GET)
            {
                enumerator = null;
                return E_NOTIMPL;
            }

            var formats = new List<FORMATETC>
            {
                CreateFormat(FileGroupDescriptorFormat, TYMED.TYMED_HGLOBAL),
                CreateFormat(FileContentsFormat, TYMED.TYMED_ISTREAM),
                CreateFormat(PreferredDropEffectFormat, TYMED.TYMED_HGLOBAL)
            };
            lock (_lock)
            {
                formats.AddRange(_storedData.Keys.Select(id => CreateFormat(id, TYMED.TYMED_HGLOBAL)));
            }

            enumerator = new FormatEnumerator(formats.ToArray());
            return S_OK;
        }

        private static FORMATETC CreateFormat(short id, TYMED tymed) => new()
        {
            cfFormat = id,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = tymed
        };

        public int DAdvise(ref FORMATETC format, ADVF advf, IntPtr adviseSink, out int connection)
        {
            connection = 0;
            return OLE_E_ADVISENOTSUPPORTED;
        }

        public int DUnadvise(int connection) => OLE_E_ADVISENOTSUPPORTED;

        public int EnumDAdvise(out IntPtr enumerator)
        {
            enumerator = IntPtr.Zero;
            return OLE_E_ADVISENOTSUPPORTED;
        }

        public int SetAsyncMode(int doOpAsync)
        {
            _isAsync = doOpAsync != 0;
            return S_OK;
        }

        public int GetAsyncMode(out int isOpAsync)
        {
            isOpAsync = _isAsync ? 1 : 0;
            return S_OK;
        }

        public int StartOperation(IntPtr bindContext)
        {
            lock (_lock)
            {
                _inOperation = true;
            }

            return S_OK;
        }

        public int InOperation(out int inAsyncOp)
        {
            lock (_lock)
            {
                inAsyncOp = _inOperation ? 1 : 0;
            }

            return S_OK;
        }

        /// <summary>The drop target has read the files on its own thread, so the drag source is closed.</summary>
        public int EndOperation(int result, IntPtr bindContext, int effects)
        {
            lock (_lock)
            {
                _inOperation = false;
            }

            DisposeWhenDone();
            return S_OK;
        }

        /// <summary>
        ///     The drag has ended: the drag source is closed when the drop target isn't reading the files, i.e. it
        ///     doesn't read them on its own thread (<see cref="StartOperation" /> until <see cref="EndOperation" />)
        ///     and every stream given to it has been read to its end.
        /// </summary>
        public void DragEnded()
        {
            lock (_lock)
            {
                _dragEnded = true;
            }

            DisposeWhenDone();
        }

        /// <summary>A stream has been read to its end.</summary>
        private void StreamRead(VolumeFileStream stream)
        {
            lock (_lock)
            {
                _streams.Remove(stream);
            }

            DisposeWhenDone();
        }

        private void DisposeWhenDone()
        {
            lock (_lock)
            {
                if (!_dragEnded || _inOperation || _streams.Count > 0)
                {
                    return;
                }
            }

            Dispose();
        }

        /// <summary>Closes the streams and the drag source, e.g. when the next drag starts.</summary>
        public void Dispose()
        {
            List<VolumeFileStream> streams;
            lock (_lock)
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                streams = _streams.ToList();
                _streams.Clear();
            }

            foreach (var stream in streams)
            {
                stream.Close();
            }

            source.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    task.Result.Dispose();
                }
            }, TaskScheduler.Default);
        }

        /// <summary>Reports the first error, e.g. the image couldn't be opened again or a file couldn't be read.</summary>
        private void ReportFailure(Exception exception)
        {
            lock (_lock)
            {
                if (_hasFailed)
                {
                    return;
                }

                _hasFailed = true;
            }

            failed(exception is AggregateException { InnerException: { } inner } ? inner : exception);
        }
    }

    /// <summary>
    ///     FILEGROUPDESCRIPTORW: the number of files and a FILEDESCRIPTORW of 592 bytes for each, with attributes,
    ///     size, modified date and the path below the drop location.
    /// </summary>
    private static byte[] CreateFileGroupDescriptor(IReadOnlyList<DragFile> files)
    {
        const int FdAttributes = 0x4;
        const int FdWritesTime = 0x20;
        const int FdFileSize = 0x40;
        const int FdProgressUi = 0x4000;
        const int FdUnicode = unchecked((int)0x80000000);
        const int FileAttributeDirectory = 0x10;
        const int FileAttributeNormal = 0x80;
        const int MaxPath = 260;

        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory, Encoding.Unicode);
        writer.Write(files.Count);
        foreach (var file in files)
        {
            var flags = FdAttributes | FdFileSize | FdProgressUi | FdUnicode |
                        (file.ModifiedDate != null ? FdWritesTime : 0);
            writer.Write(flags);
            writer.Write(new byte[16 + 8 + 8]); // clsid, sizel, pointl
            writer.Write(file.IsDirectory ? FileAttributeDirectory : FileAttributeNormal);
            writer.Write(0L); // creation time
            writer.Write(0L); // last access time
            writer.Write(file.ModifiedDate is { } modified ? ToFileTime(modified) : 0L);
            writer.Write((int)(file.Size >> 32));
            writer.Write((int)(file.Size & 0xFFFFFFFF));
            var name = new char[MaxPath];
            file.RelativePath.AsSpan(0, Math.Min(file.RelativePath.Length, MaxPath - 1)).CopyTo(name);
            writer.Write(name);
        }

        writer.Flush();
        return memory.ToArray();
    }

    private static long ToFileTime(DateTime date)
    {
        try
        {
            return (date.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(date, DateTimeKind.Local) : date)
                .ToFileTimeUtc();
        }
        catch (ArgumentOutOfRangeException)
        {
            return 0;
        }
    }

    private sealed class FormatEnumerator(FORMATETC[] formats, int index = 0) : IEnumFORMATETC
    {
        private int _index = index;

        public int Next(int celt, FORMATETC[] rgelt, int[]? pceltFetched)
        {
            var fetched = 0;
            while (fetched < celt && _index < formats.Length)
            {
                rgelt[fetched++] = formats[_index++];
            }

            if (pceltFetched is { Length: > 0 })
            {
                pceltFetched[0] = fetched;
            }

            return fetched == celt ? S_OK : 1;
        }

        public int Skip(int celt)
        {
            _index = Math.Min(formats.Length, _index + celt);
            return _index < formats.Length ? S_OK : 1;
        }

        public int Reset()
        {
            _index = 0;
            return S_OK;
        }

        public void Clone(out IEnumFORMATETC newEnum) => newEnum = new FormatEnumerator(formats, _index);
    }

    /// <summary>
    ///     A COM stream reading a dragged file. The file is opened on the first read and closed when it has been read
    ///     to its end, reading again there reads no bytes (the drop target reads until it gets none). Seeking only
    ///     moves the position, which the next read goes to, opening the file again when it was closed (the drop
    ///     target may seek after reading the end). Reads run on the thread pool, as the drop target may read on the
    ///     UI thread.
    /// </summary>
    private sealed class VolumeFileStream(
        Task<VolumeDragSource> source,
        DragFile file,
        Action<Exception> failed,
        Action<VolumeFileStream> read) : ComIStream
    {
        private readonly object _lock = new();
        private Stream? _stream;
        private long _streamPosition;
        private long _position;
        private long? _endPosition;
        private bool _isClosed;
        private bool _isRead;

        /// <summary>The file at the position of the stream, opened or moved to it.</summary>
        private Stream GetStream()
        {
            ObjectDisposedException.ThrowIf(_isClosed, this);
            if (_stream != null && _streamPosition != _position && !_stream.CanSeek && _position < _streamPosition)
            {
                // can't go back, so it's read from the start again
                _stream.Dispose();
                _stream = null;
            }

            if (_stream == null)
            {
                _stream = Task.Run(async () => await (await source).OpenReadAsync(file)).GetAwaiter().GetResult();
                _streamPosition = 0;
            }

            var stream = _stream;
            if (_streamPosition != _position)
            {
                if (stream.CanSeek)
                {
                    stream.Position = _position;
                }
                else
                {
                    var buffer = new byte[81920];
                    while (_streamPosition < _position)
                    {
                        var count = (int)Math.Min(buffer.Length, _position - _streamPosition);
                        var skipped = Task.Run(() => stream.Read(buffer, 0, count)).GetAwaiter().GetResult();
                        if (skipped == 0)
                        {
                            break;
                        }

                        _streamPosition += skipped;
                    }
                }

                _streamPosition = _position;
            }

            return stream;
        }

        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            var total = 0;
            var isEnd = false;
            lock (_lock)
            {
                try
                {
                    if (_endPosition is not { } end || _position < end)
                    {
                        var stream = GetStream();
                        while (total < cb)
                        {
                            var offset = total;
                            var count = Task.Run(() => stream.Read(pv, offset, cb - offset)).GetAwaiter()
                                .GetResult();
                            if (count == 0)
                            {
                                break;
                            }

                            total += count;
                        }

                        _position += total;
                        _streamPosition = _position;
                        if (total < cb)
                        {
                            // read to the end, the file is closed until the drop target goes back
                            _endPosition = _position;
                            _stream?.Dispose();
                            _stream = null;
                        }
                    }

                    isEnd = total < cb && !_isRead;
                    _isRead |= isEnd;
                }
                catch (Exception exception)
                {
                    failed(exception);
                    throw;
                }
            }

            if (pcbRead != IntPtr.Zero)
            {
                Marshal.WriteInt32(pcbRead, total);
            }

            if (isEnd)
            {
                read(this);
            }
        }

        public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            long position;
            lock (_lock)
            {
                position = dwOrigin switch
                {
                    0 => dlibMove,
                    1 => _position + dlibMove,
                    _ => (_endPosition ?? file.Size) + dlibMove
                };
                if (position < 0)
                {
                    throw new COMException("The position is before the start", STG_E_INVALIDFUNCTION);
                }

                _position = position;
            }

            if (plibNewPosition != IntPtr.Zero)
            {
                Marshal.WriteInt64(plibNewPosition, position);
            }
        }

        public void Stat(out STATSTG pstatstg, int grfStatFlag)
        {
            pstatstg = new STATSTG
            {
                type = 2, // STGTY_STREAM
                cbSize = file.Size,
                mtime = ToComFileTime(file.ModifiedDate),
                pwcsName = (grfStatFlag & 1) != 0 ? null! : file.RelativePath // STATFLAG_NONAME
            };
        }

        private static FILETIME ToComFileTime(DateTime? date)
        {
            var value = date is { } modified ? ToFileTime(modified) : 0;
            return new FILETIME { dwLowDateTime = (int)(value & 0xFFFFFFFF), dwHighDateTime = (int)(value >> 32) };
        }

        /// <summary>Closes the file when the drag source is closed, reading afterwards fails.</summary>
        public void Close()
        {
            lock (_lock)
            {
                _isClosed = true;
                _stream?.Dispose();
                _stream = null;
            }
        }

        public void Write(byte[] pv, int cb, IntPtr pcbWritten) =>
            throw new COMException("The stream is read-only", STG_E_INVALIDFUNCTION);

        public void SetSize(long libNewSize) => throw new COMException(null, E_NOTIMPL);

        public void CopyTo(ComIStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) =>
            throw new COMException(null, E_NOTIMPL);

        public void Commit(int grfCommitFlags)
        {
        }

        public void Revert() => throw new COMException(null, E_NOTIMPL);

        public void LockRegion(long libOffset, long cb, int dwLockType) => throw new COMException(null, E_NOTIMPL);

        public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new COMException(null, E_NOTIMPL);

        public void Clone(out ComIStream ppstm) => throw new COMException(null, E_NOTIMPL);
    }
}
