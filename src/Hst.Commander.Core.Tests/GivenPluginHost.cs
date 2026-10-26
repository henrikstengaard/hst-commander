using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Zip;

namespace Hst.Commander.Core.Tests;

public class GivenPluginHost
{
    private static MemoryStream CreateZip() =>
        new(ZipTestData.CreateZip(("file1.txt", "content")));

    [Fact]
    public async Task When_StreamIsDetected_Then_VolumeIsOpened()
    {
        // arrange
        var host = new PluginHost(() => [VolumeProviders.Iso9660, new ZipFileVolumeProvider()]);
        await using var stream = CreateZip();
        stream.Position = 3;

        // act
        var volume = await host.OpenVolume("archive", stream);

        // assert
        Assert.NotNull(volume);
        var entry = Assert.Single(await volume.ListEntries().ToListAsync());
        Assert.Equal("file1.txt", entry.Name);
        (volume as IDisposable)?.Dispose();
    }

    [Fact]
    public async Task When_NoProviderDetectsStream_Then_NullIsReturned()
    {
        // arrange
        var host = new PluginHost(() => [VolumeProviders.Iso9660, new ZipFileVolumeProvider()]);
        await using var stream = new MemoryStream(new byte[1024]);

        // act
        var volume = await host.OpenVolume("data", stream);

        // assert
        Assert.Null(volume);
    }

    [Fact]
    public async Task When_DetectingProviderIsExcluded_Then_NullIsReturned()
    {
        // arrange
        var zipProvider = new ZipFileVolumeProvider();
        var host = new PluginHost(() => [zipProvider]);
        await using var stream = CreateZip();

        // act
        var volume = await host.OpenVolume("archive", stream, zipProvider);

        // assert
        Assert.Null(volume);
    }

    [Fact]
    public void When_BuiltInPluginsAreCreated_Then_DiskPluginIsBeforeAmigaPlugin()
    {
        // act
        var names = Plugins.BuiltIn.Select(plugin => plugin.Name).ToList();

        // assert
        Assert.True(names.IndexOf("Disk") >= 0 && names.IndexOf("Disk") < names.IndexOf("Amiga"));
    }
}
