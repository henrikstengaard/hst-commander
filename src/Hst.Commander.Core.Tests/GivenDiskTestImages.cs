using System.Text;
using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Disk;

namespace Hst.Commander.Core.Tests;

/// <summary>
/// The disk and file system images of TestData/Disks made with the real tools of each file system (see
/// TestData/Disks/make/make-images.sh and <see cref="DiskTestData.OpenGzippedImage" />), read and written with the
/// Disk plugin.
/// </summary>
public class GivenDiskTestImages
{
    /// <summary>File systems of the partitions of disk-gpt.img and its virtual disk images, in order; null for HFS+ (empty).</summary>
    private static readonly string?[] GptFileSystems = ["fat", "ntfs", "exfat", "ext4", "xfs", null, "squashfs"];

    /// <summary>File systems of the partitions of disk-mbr.img, in order.</summary>
    private static readonly string[] MbrFileSystems = ["fat", "ntfs", "exfat", "ext4"];

    private readonly DiskFileVolumeProvider _diskProvider = new();
    private readonly FileSystemFileVolumeProvider _fileSystemProvider = new();

    private static async Task<string> ReadTextAsync(IVolume volume, string name)
    {
        await using var stream = await volume.OpenRead(name);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        return await reader.ReadToEndAsync();
    }

    private static async Task WriteTextAsync(IVolume volume, string name, string text)
    {
        await using var stream = await volume.OpenWrite(name);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(text));
    }

    /// <summary>Reads hello.txt and dir/nested.txt of a file system holding the files of make-images.sh.</summary>
    private static async Task AssertFilesAsync(IVolume volume, string fileSystem)
    {
        Assert.Equal($"Hello from {fileSystem}\n", await ReadTextAsync(volume, "hello.txt"));
        await volume.ChangeDirectory(["dir"]);
        Assert.Equal("Nested file\n", await ReadTextAsync(volume, "nested.txt"));
        await volume.ChangeDirectory([Paths.Parent]);
    }

    [Theory]
    [InlineData("disk-gpt.img.gz")]
    [InlineData("disk-gpt.vhd.gz")]
    [InlineData("disk-gpt.vhdx.gz")]
    [InlineData("disk-gpt.vdi.gz")]
    [InlineData("disk-gpt.dmg.gz")]
    public async Task When_GptDiskIsOpened_Then_FilesOfEveryPartitionAreRead(string fileName)
    {
        // arrange
        await using var stream = await DiskTestData.OpenGzippedImage(fileName);

        // act
        var isMatch = await _diskProvider.IsMatch(fileName, stream);
        stream.Position = 0;
        using var volume = (VolumeListVolume)await _diskProvider.CreateVolume(stream);
        await volume.ChangeDirectory(["gpt"]);
        var partitions = await volume.ListEntries().ToListAsync();

        // assert
        Assert.True(isMatch);
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7"], partitions.Select(partition => partition.Name));
        for (var index = 0; index < GptFileSystems.Length; index++)
        {
            await volume.ChangeDirectory([Paths.Root, "gpt", partitions[index].Name]);
            if (GptFileSystems[index] is { } fileSystem)
            {
                await AssertFilesAsync(volume, fileSystem);
            }
            else
            {
                Assert.Empty(await volume.ListEntries().ToListAsync());
            }
        }

        Assert.Equal("6 - HFSTEST", partitions[5].DisplayName);
        // DiscUtils' XFS doesn't tell its size and free space, they are read from the superblock
        var xfsProperties = partitions[4].Details!.Sections.SelectMany(section => section).ToList();
        Assert.Contains(xfsProperties, property => property is { Label: "Capacity", Bytes: 32 * 1024 * 1024 });
        Assert.Contains(xfsProperties, property => property is { Label: "Free space", Bytes: > 0 });
        await volume.ChangeDirectory([Paths.Root, "gpt", "5"]);
        Assert.Equal(32 * 1024 * 1024, (await volume.GetSpace())!.Total);
    }

    [Fact]
    public async Task When_MbrDiskIsOpened_Then_FilesOfEveryPartitionAreRead()
    {
        // arrange
        await using var stream = await DiskTestData.OpenGzippedImage("disk-mbr.img.gz");

        // act
        using var volume = (VolumeListVolume)await _diskProvider.CreateVolume(stream);
        await volume.ChangeDirectory(["mbr"]);
        var partitions = await volume.ListEntries().ToListAsync();

        // assert
        Assert.Equal(["1 - FATTEST", "2 - NTFSTEST", "3 - EXFATTEST", "4 - EXT4TEST"],
            partitions.Select(partition => partition.DisplayName));
        for (var index = 0; index < MbrFileSystems.Length; index++)
        {
            await volume.ChangeDirectory([Paths.Root, "mbr", partitions[index].Name]);
            await AssertFilesAsync(volume, MbrFileSystems[index]);
        }
    }

    [Theory]
    [InlineData("disk-gpt.img.gz")]
    [InlineData("disk-gpt.vhd.gz")]
    [InlineData("disk-gpt.vhdx.gz")]
    [InlineData("disk-gpt.vdi.gz")]
    public async Task When_FilesAreWrittenToFatNtfsAndExFatPartitions_Then_TheyAreReadAfterOpeningAgain(
        string fileName)
    {
        // arrange
        await using var stream = await DiskTestData.OpenGzippedImage(fileName);
        string[] partitions = ["1", "2", "3"];

        // act
        using (var volume = (VolumeListVolume)await _diskProvider.CreateVolume(stream))
        {
            foreach (var partition in partitions)
            {
                await volume.ChangeDirectory([Paths.Root, "gpt", partition, "dir"]);
                await WriteTextAsync(volume, "written.txt", $"Written to {partition}");
            }
        }

        stream.Position = 0;
        using var reopened = (VolumeListVolume)await _diskProvider.CreateVolume(stream);

        // assert
        foreach (var partition in partitions)
        {
            await reopened.ChangeDirectory([Paths.Root, "gpt", partition, "dir"]);
            Assert.Equal($"Written to {partition}", await ReadTextAsync(reopened, "written.txt"));
            Assert.Equal("Nested file\n", await ReadTextAsync(reopened, "nested.txt"));
        }
    }

    [Fact]
    public async Task When_DmgIsWritten_Then_ItIsReadOnly()
    {
        // arrange
        await using var stream = await DiskTestData.OpenGzippedImage("disk-gpt.dmg.gz");
        using var volume = (VolumeListVolume)await _diskProvider.CreateVolume(stream);
        await volume.ChangeDirectory(["gpt", "1"]);

        // act & assert
        await Assert.ThrowsAsync<NotSupportedException>(() => volume.OpenWrite("written.txt"));
    }

    [Theory]
    [InlineData("zlib")]
    [InlineData("lzo")]
    public async Task When_BtrfsFileIsCompressed_Then_ItIsDecompressed(string algorithm)
    {
        // arrange
        await using var stream = await DiskTestData.OpenGzippedImage("btrfs.img.gz");
        var expected = string.Concat(Enumerable.Range(1, 2500).Select(line => $"Line {line} compressed with {algorithm}\n"))
            [..65536];

        // act
        using var volume = (DiscFileSystemVolume)await _fileSystemProvider.CreateVolume(stream);
        await volume.ChangeDirectory([algorithm]);
        var text = await ReadTextAsync(volume, "compressed.txt");

        // assert
        Assert.Equal(expected, text);
    }

    [Fact]
    public async Task When_BtrfsFileIsCompressedWithZstd_Then_ReadingItFails()
    {
        // arrange
        await using var stream = await DiskTestData.OpenGzippedImage("btrfs.img.gz");
        using var volume = (DiscFileSystemVolume)await _fileSystemProvider.CreateVolume(stream);
        await volume.ChangeDirectory(["zstd"]);

        // act & assert
        // DiscUtils.Btrfs 1.0.90 can't decompress zstd extents written by Linux ("Unknown frame descriptor")
        await Assert.ThrowsAsync<IOException>(() => ReadTextAsync(volume, "compressed.txt"));
    }

    [Fact]
    public async Task When_SquashFsImageIsOpened_Then_FilesAreRead()
    {
        // arrange
        await using var stream = await DiskTestData.OpenGzippedImage("squashfs-gzip.img.gz");

        // act
        var isMatch = await _fileSystemProvider.IsMatch("squashfs.img", stream);
        stream.Position = 0;
        using var volume = (DiscFileSystemVolume)await _fileSystemProvider.CreateVolume(stream);

        // assert
        Assert.True(isMatch);
        await AssertFilesAsync(volume, "squashfs");
    }

    [Theory]
    [InlineData("squashfs-xz.img.gz")]
    [InlineData("squashfs-zstd.img.gz")]
    [InlineData("squashfs-lzo.img.gz")]
    public async Task When_SquashFsImageIsCompressedWithoutGzip_Then_OpeningItFails(string fileName)
    {
        // arrange
        await using var stream = await DiskTestData.OpenGzippedImage(fileName);

        // act
        var isMatch = await _fileSystemProvider.IsMatch(fileName, stream);
        stream.Position = 0;

        // assert
        // DiscUtils.SquashFs 1.0.90 only reads gzip (zlib) compressed SquashFS ("Unsupported compression")
        Assert.True(isMatch);
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            using var volume = (DiscFileSystemVolume)await _fileSystemProvider.CreateVolume(stream);
            await AssertFilesAsync(volume, "squashfs");
        });
    }
}
