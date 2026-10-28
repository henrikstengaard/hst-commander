using Hst.Commander.Plugins.PhysicalDisk;

namespace Hst.Commander.Core.Tests;

public class GivenDiskStream : IDisposable
{
    private const int SectorSize = 512;
    private const int DiskSize = 8 * SectorSize;

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}.img");
    private readonly byte[] _data = Enumerable.Range(0, DiskSize).Select(index => (byte)(index * 7)).ToArray();

    public GivenDiskStream() => File.WriteAllBytes(_path, _data);

    public void Dispose()
    {
        File.Delete(_path);
        GC.SuppressFinalize(this);
    }

    private DiskStream Open(bool canWrite) => new(
        File.OpenHandle(_path, FileMode.Open, canWrite ? FileAccess.ReadWrite : FileAccess.Read, FileShare.ReadWrite),
        DiskSize, SectorSize, canWrite);

    [Fact]
    public void When_ReadingAcrossSectorsAtUnalignedOffset_Then_BytesAreRead()
    {
        // arrange
        using var stream = Open(false);
        stream.Position = 500;
        var buffer = new byte[600];

        // act
        var read = stream.Read(buffer);

        // assert
        Assert.Equal(600, read);
        Assert.Equal(_data.AsSpan(500, 600).ToArray(), buffer);
        Assert.Equal(1100, stream.Position);
    }

    [Fact]
    public void When_ReadingPastTheEnd_Then_OnlyTheRestIsRead()
    {
        // arrange
        using var stream = Open(false);
        stream.Seek(-10, SeekOrigin.End);
        var buffer = new byte[100];

        // act
        var read = stream.Read(buffer);
        var readAtEnd = stream.Read(buffer);

        // assert
        Assert.Equal(10, read);
        Assert.Equal(_data.AsSpan(DiskSize - 10).ToArray(), buffer.AsSpan(0, 10).ToArray());
        Assert.Equal(0, readAtEnd);
    }

    [Fact]
    public void When_WritingAtUnalignedOffset_Then_TheRestOfTheSectorsIsKept()
    {
        // arrange
        var bytes = Enumerable.Repeat((byte)0xFF, 600).ToArray();

        // act
        using (var stream = Open(true))
        {
            stream.Position = 700;
            stream.Write(bytes);
        }

        // assert
        var expected = _data.ToArray();
        bytes.CopyTo(expected, 700);
        Assert.Equal(expected, File.ReadAllBytes(_path));
    }

    [Fact]
    public void When_WritingToReadOnlyDisk_Then_NotSupportedExceptionIsThrown()
    {
        // arrange
        using var stream = Open(false);

        // act and assert
        Assert.False(stream.CanWrite);
        Assert.Throws<NotSupportedException>(() => stream.Write(new byte[SectorSize]));
    }

    [Fact]
    public void When_WritingPastTheEnd_Then_IOExceptionIsThrown()
    {
        // arrange
        using var stream = Open(true);
        stream.Position = DiskSize - 1;

        // act and assert
        Assert.Throws<IOException>(() => stream.Write(new byte[2]));
    }
}
