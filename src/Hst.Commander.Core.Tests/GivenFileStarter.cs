namespace Hst.Commander.Core.Tests;

public class GivenFileStarter
{
    [Theory]
    [InlineData(3, true)]
    [InlineData(2, false)]
    public void When_IsConsoleExecutable_Then_SubsystemIsChecked(ushort subsystem, bool expected)
    {
        // arrange
        using var stream = new MemoryStream(CreateExecutable(subsystem));

        // act
        var isConsole = FileStarter.IsConsoleExecutable(stream);

        // assert
        Assert.Equal(expected, isConsole);
    }

    [Fact]
    public void When_IsConsoleExecutableWithoutHeader_Then_FalseIsReturned()
    {
        // arrange
        using var stream = new MemoryStream("not an executable"u8.ToArray());

        // act
        var isConsole = FileStarter.IsConsoleExecutable(stream);

        // assert
        Assert.False(isConsole);
    }

    [Fact]
    public void When_IsTerminalProgramForTextFile_Then_FalseIsReturned()
    {
        // arrange
        var path = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}.txt");
        File.WriteAllText(path, "text");
        try
        {
            // act
            var isTerminalProgram = FileStarter.IsTerminalProgram(path);

            // assert
            Assert.False(isTerminalProgram);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Minimal executable with a DOS header pointing to a PE header with the subsystem.</summary>
    private static byte[] CreateExecutable(ushort subsystem)
    {
        const int peHeaderOffset = 0x80;
        var data = new byte[peHeaderOffset + 4 + 20 + 96];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BitConverter.GetBytes(peHeaderOffset).CopyTo(data, 0x3C);
        "PE\0\0"u8.ToArray().CopyTo(data, peHeaderOffset);
        BitConverter.GetBytes(subsystem).CopyTo(data, peHeaderOffset + 4 + 20 + 68);
        return data;
    }
}
