using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Zip;

namespace Hst.Commander.Core.Tests;

public class GivenPluginLoader
{
    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}");
        Directory.CreateDirectory(path);
        return path;
    }


    /// <summary>Deletes a plugin directory, if possible: loaded plugin assemblies stay locked on Windows.</summary>
    private static void DeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
    /// <summary>Copies the zip plugin assembly to a plugin directory, as the plugin to load.</summary>
    private static string CopyZipPlugin(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.Copy(typeof(ZipPlugin).Assembly.Location, path);
        return path;
    }

    [Fact]
    public async Task When_PluginIsInDirectoryOfItsOwn_Then_PluginIsLoadedWithSharedAbstractions()
    {
        // arrange
        var pluginsPath = CreateTempDirectory();
        CopyZipPlugin(Path.Combine(pluginsPath, "Zip"), "Zip.dll");
        try
        {
            // act
            var result = PluginLoader.Load([pluginsPath], []);

            // assert - loaded in a load context of its own, but implementing the same interfaces
            Assert.Empty(result.Errors);
            var plugin = Assert.Single(result.Plugins);
            Assert.Equal(typeof(ZipPlugin).FullName, plugin.GetType().FullName);
            Assert.NotSame(typeof(ZipPlugin), plugin.GetType());
            var fileVolumeProvider = Assert.Single(plugin.FileVolumeProviders);
            var volume = await fileVolumeProvider.CreateVolume(
                new MemoryStream(ZipTestData.CreateZipWithDirectoriesAndFiles()));
            Assert.Equal(3, (await volume.ListEntries().ToListAsync()).Count);
        }
        finally
        {
            DeleteDirectory(pluginsPath);
        }
    }

    [Fact]
    public void When_PluginIsSingleAssemblyInPluginDirectory_Then_PluginIsLoaded()
    {
        // arrange
        var pluginsPath = CreateTempDirectory();
        CopyZipPlugin(pluginsPath, "Zip.dll");
        try
        {
            // act
            var result = PluginLoader.Load([pluginsPath], []);

            // assert
            Assert.Empty(result.Errors);
            Assert.Single(result.Plugins);
        }
        finally
        {
            DeleteDirectory(pluginsPath);
        }
    }

    [Fact]
    public void When_PluginIsAlreadyLoadedOrDirectoryListedTwice_Then_PluginIsLoadedOnce()
    {
        // arrange
        var pluginsPath = CreateTempDirectory();
        CopyZipPlugin(Path.Combine(pluginsPath, "Zip"), "Zip.dll");
        CopyZipPlugin(pluginsPath, "Zip.dll");
        try
        {
            // act
            var builtInResult = PluginLoader.Load([pluginsPath], Plugins.BuiltIn);
            var result = PluginLoader.Load([pluginsPath, pluginsPath + Path.DirectorySeparatorChar], []);

            // assert - a copy of a built-in plugin isn't loaded again
            Assert.Empty(builtInResult.Plugins);
            Assert.Empty(builtInResult.Errors);
            Assert.Single(result.Plugins);
        }
        finally
        {
            DeleteDirectory(pluginsPath);
        }
    }

    [Fact]
    public void When_PluginAssemblyIsInvalid_Then_ErrorIsReportedAndOtherPluginsAreLoaded()
    {
        // arrange - an invalid plugin in a directory of its own and a native library (not a plugin) next to plugins
        var pluginsPath = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(pluginsPath, "Broken"));
        File.WriteAllText(Path.Combine(pluginsPath, "Broken", "Broken.dll"), "not an assembly");
        File.WriteAllText(Path.Combine(pluginsPath, "native.dll"), "not an assembly");
        CopyZipPlugin(Path.Combine(pluginsPath, "Zip"), "Zip.dll");
        try
        {
            // act
            var result = PluginLoader.Load([pluginsPath], []);

            // assert
            Assert.Single(result.Plugins);
            var error = Assert.Single(result.Errors);
            Assert.Contains("Broken.dll", error);
        }
        finally
        {
            DeleteDirectory(pluginsPath);
        }
    }

    [Fact]
    public void When_PluginDirectoryIsMissing_Then_NoPluginsAreLoaded()
    {
        var result = PluginLoader.Load([Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")], []);

        Assert.Empty(result.Plugins);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void When_PluginDirectories_Then_UserAndExecutableDirectoriesAreScanned()
    {
        Assert.Equal(
        [
            Path.Combine(SettingsPaths.Directory, "plugins"),
            Path.Combine(AppContext.BaseDirectory, "plugins")
        ], PluginLoader.Directories);
        Assert.IsAssignableFrom<IPlugin>(Plugins.BuiltIn[0]);
    }
}
