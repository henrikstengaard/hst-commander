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
}
