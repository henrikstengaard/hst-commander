using System.Text.Json;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenLicenses
{
    private sealed class NoticesPlugin(string name, string? notices) : IPlugin
    {
        public string Name => name;

        public string? ThirdPartyNotices => notices ?? throw new InvalidOperationException("Broken plugin");
    }

    private static string CreateTempDirectory() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    /// <summary>
    ///     Root of the repository above the test output, with LICENSE (the test output has copies of the notices, but
    ///     only LICENSE.txt).
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "LICENSE")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }

    [Fact]
    public void When_GettingNotices_Then_HstCommanderThirdPartyAndPluginNoticesAreReturned()
    {
        // arrange
        var appDirectory = CreateTempDirectory();
        try
        {
            // act
            var notices = Licenses.GetNotices(
                [new NoticesPlugin("WithNotices", "Library notices"), new NoticesPlugin("Broken", null)], appDirectory);

            // assert
            Assert.Equal(
            [
                "Hst Commander (MIT license)", "Third-party components", "Skia and HarfBuzz native libraries",
                ".NET runtime", "WithNotices plugin"
            ], notices.Select(notice => notice.Title));
            Assert.Contains("MIT License", notices[0].Text);
            Assert.Contains("Terminal.Gui", notices[1].Text);
            Assert.Contains("Avalonia", notices[1].Text);
            Assert.Contains("Skia", notices[2].Text);
            Assert.Contains("installed separately", notices[3].Text);
            Assert.Equal("Library notices", notices[4].Text);
        }
        finally
        {
            Directory.Delete(appDirectory, true);
        }
    }

    [Fact]
    public void When_DotNetNoticesArePublishedNextToApp_Then_TheyAreReturned()
    {
        // arrange
        var appDirectory = CreateTempDirectory();
        File.WriteAllText(Path.Combine(appDirectory, Licenses.DotNetNoticesFileName), ".NET runtime notices");
        try
        {
            // act
            var notices = Licenses.GetNotices([], appDirectory);

            // assert
            Assert.Equal(".NET runtime notices", notices.Single(notice => notice.Title == ".NET runtime").Text);
        }
        finally
        {
            Directory.Delete(appDirectory, true);
        }
    }

    [Fact]
    public void When_DotNetNoticesAreEmbeddedInApp_Then_TheyAreReturnedInsteadOfTheFile()
    {
        // arrange - the test assembly embeds notices like a self-contained app
        var appDirectory = CreateTempDirectory();
        File.WriteAllText(Path.Combine(appDirectory, Licenses.DotNetNoticesFileName), ".NET runtime notices file");
        try
        {
            // act
            var notices = Licenses.GetNotices([], appDirectory, typeof(GivenLicenses).Assembly);

            // assert
            Assert.Equal("Embedded .NET runtime notices",
                notices.Single(notice => notice.Title == ".NET runtime").Text.Trim());
        }
        finally
        {
            Directory.Delete(appDirectory, true);
        }
    }

    [Fact]
    public void When_NoticesAreEmbedded_Then_TheyAreTheFilesOfTheRepository()
    {
        // act
        var notices = Licenses.GetNotices([]);

        // assert
        var root = FindRepositoryRoot();
        Assert.Equal(File.ReadAllText(Path.Combine(root, "LICENSE")), notices[0].Text);
        Assert.Equal(File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.txt")), notices[1].Text);
    }

    /// <summary>
    ///     Every package the TUI and GUI include (with runtime or native files, restored in their project.assets.json)
    ///     must be listed in THIRD-PARTY-NOTICES.txt, so a new or changed dependency gets its notice. The SMB plugin's
    ///     SMBLibrary (LGPL) must not be included with the apps.
    /// </summary>
    [Theory]
    [InlineData("Hst.Commander.Tui")]
    [InlineData("Hst.Commander.Gui")]
    public void When_AppIsRestored_Then_EveryPackageItIncludesIsListedInThirdPartyNotices(string project)
    {
        // arrange
        var root = FindRepositoryRoot();
        var assetsPath = Path.Combine(root, "src", project, "obj", "project.assets.json");
        Assert.True(File.Exists(assetsPath), $"{assetsPath} not found, restore the solution first");
        var listedPackages = File.ReadLines(Path.Combine(root, "THIRD-PARTY-NOTICES.txt"))
            .Where(line => line.StartsWith("Packages: ", StringComparison.Ordinal))
            .SelectMany(line => line["Packages: ".Length..].Split(',', StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // act
        var includedPackages = GetIncludedPackages(assetsPath);

        // assert
        Assert.NotEmpty(includedPackages);
        // add a section for the package to src/notices/make-notices.py and run it
        Assert.Empty(includedPackages.Where(package => !listedPackages.Contains(package)));
        Assert.DoesNotContain("SMBLibrary", includedPackages, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Packages with runtime or native files in any target of a project.assets.json.</summary>
    private static IReadOnlyList<string> GetIncludedPackages(string assetsPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = document.RootElement;
        var packages = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in root.GetProperty("targets").EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                if (library.Value.GetProperty("type").GetString() != "package")
                {
                    continue;
                }

                var hasFiles = new[] { "runtime", "native", "runtimeTargets" }.Any(kind =>
                    library.Value.TryGetProperty(kind, out var files) &&
                    files.EnumerateObject().Any(file => !file.Name.EndsWith("/_._", StringComparison.Ordinal)));
                if (hasFiles)
                {
                    packages.Add(library.Name[..library.Name.IndexOf('/')]);
                }
            }
        }

        return packages.ToList();
    }
}
