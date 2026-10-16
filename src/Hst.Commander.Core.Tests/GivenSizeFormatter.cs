using Hst.Commander.Core.Volumes;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenSizeFormatter
{
    [Theory]
    [InlineData(0L, "0B")]
    [InlineData(512L, "512B")]
    [InlineData(1023L, "1023B")]
    [InlineData(1024L, "1K")]
    [InlineData(1536L, "2K")]
    [InlineData(1048575L, "1M")]
    [InlineData(93415538688L, "87G")]
    [InlineData(274877906944L, "256G")]
    [InlineData(2199023255552L, "2T")]
    public void When_FormatHumanized_Then_SizeIsRoundedIntegerWithUnit(long size, string expected)
    {
        // act
        var formatted = SizeFormatter.FormatHumanized(size);

        // assert
        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData(0L, "0 bytes")]
    [InlineData(1L, "1 byte")]
    [InlineData(512L, "512 bytes")]
    [InlineData(1024L, "1.00 KB (1,024 bytes)")]
    [InlineData(1234567L, "1.17 MB (1,234,567 bytes)")]
    [InlineData(15728639L, "14.9 MB (15,728,639 bytes)")]
    [InlineData(274877906944L, "256 GB (274,877,906,944 bytes)")]
    public void When_FormatWithBytes_Then_SizeHasThreeDigitsAndExactBytes(long size, string expected)
    {
        // act
        var formatted = SizeFormatter.FormatWithBytes(size);

        // assert
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void When_FormatEntrySizeForDirectory_Then_DirIsReturned()
    {
        // act
        var formatted = SizeFormatter.FormatEntrySize(new Entry("dir1", EntryType.Container, 0));

        // assert
        Assert.Equal("<DIR>", formatted);
    }

    [Theory]
    [InlineData(512L, "512B")]
    [InlineData(1536L, "2K")]
    public void When_FormatEntrySizeForFile_Then_SizeWithUnitIsReturned(long size, string expected)
    {
        // act
        var formatted = SizeFormatter.FormatEntrySize(new Entry("file1", EntryType.Item, size));

        // assert
        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData(0d, "0.0 B/s")]
    [InlineData(512d, "512.0 B/s")]
    [InlineData(1536d, "1.5 KB/s")]
    [InlineData(12.3d * 1024 * 1024, "12.3 MB/s")]
    [InlineData(2d * 1024 * 1024 * 1024, "2.0 GB/s")]
    public void When_FormatSpeed_Then_SpeedWithUnitPerSecondIsReturned(double bytesPerSecond, string expected)
    {
        // act
        var formatted = SizeFormatter.FormatSpeed(bytesPerSecond);

        // assert
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void When_FormatEta_Then_HoursMinutesAndSecondsAreReturned()
    {
        // arrange
        var progress = new CopyProgress("file1", 0, 0, 0, 0, 0, 0, 0, new TimeSpan(1, 2, 3));
        var unknownProgress = progress with { Eta = null };

        // assert
        Assert.Equal("1:02:03", progress.EtaText);
        Assert.Equal("--:--:--", unknownProgress.EtaText);
    }
}
