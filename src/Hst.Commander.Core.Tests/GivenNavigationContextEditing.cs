using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenNavigationContextEditing : IDisposable
{
    private readonly string _rootPath =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"hst-commander-{Guid.NewGuid()}")).FullName;

    public void Dispose()
    {
        Directory.Delete(_rootPath, true);
        GC.SuppressFinalize(this);
    }

    private static async Task<IEntry> FindAsync(NavigationContext navigationContext, string name) =>
        (await navigationContext.ListEntriesAsync()).First(entry => entry.Name == name);

    private static async Task OpenAsync(NavigationContext navigationContext, string name) =>
        await navigationContext.OpenEntryAsync(await FindAsync(navigationContext, name));

    /// <summary>Copies a local file into the adf opened in the context.</summary>
    private async Task CopyToAdfAsync(NavigationContext adfContext, string name, string content)
    {
        await File.WriteAllTextAsync(Path.Combine(_rootPath, name), content);
        using var localContext = new NavigationContext(_rootPath);
        await localContext.CopyAsync([await FindAsync(localContext, name)], adfContext, adfContext.CurrentPath, "*");
    }

    [Fact]
    public async Task When_EditingLocalFile_Then_SavedTextKeepsLineBreaks()
    {
        // arrange
        var path = Path.Combine(_rootPath, "a.txt");
        await File.WriteAllTextAsync(path, "line 1\r\nline 2\r\n");
        using var navigationContext = new NavigationContext(_rootPath);

        // act
        using var session = await navigationContext.OpenEditorAsync(await FindAsync(navigationContext, "a.txt"));
        Assert.False(session.IsReadOnly);
        Assert.Equal("line 1\nline 2\n", session.Document.Text);
        await session.SaveAsync("line 1\nchanged\n");

        // assert
        Assert.Equal("line 1\r\nchanged\r\n", await File.ReadAllTextAsync(path));
        Assert.True(session.HasSaved);
        Assert.False(session.IsModified("line 1\nchanged\n"));
    }

    [Fact]
    public async Task When_EditingDirectory_Then_IOExceptionIsThrown()
    {
        // arrange
        Directory.CreateDirectory(Path.Combine(_rootPath, "dir"));
        using var navigationContext = new NavigationContext(_rootPath);
        var entry = await FindAsync(navigationContext, "dir");

        // act & assert
        await Assert.ThrowsAsync<IOException>(() => navigationContext.OpenEditorAsync(entry));
        await Assert.ThrowsAsync<IOException>(() => navigationContext.OpenExternalEditAsync(entry));
    }

    [Fact]
    public async Task When_EditingFileInZip_Then_ItIsReadOnly()
    {
        // arrange
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "archive.zip"),
            ZipTestData.CreateZip(("file.txt", "zipped")));
        using var navigationContext = new NavigationContext(_rootPath);
        await OpenAsync(navigationContext, "archive.zip");
        var entry = await FindAsync(navigationContext, "file.txt");

        // act
        using var session = await navigationContext.OpenEditorAsync(entry);

        // assert
        Assert.Equal("zipped", session.Document.Text);
        Assert.True(session.IsReadOnly);
        await Assert.ThrowsAsync<NotSupportedException>(() => session.SaveAsync("changed"));
        await Assert.ThrowsAsync<NotSupportedException>(() => navigationContext.OpenExternalEditAsync(entry));
    }

    [Fact]
    public async Task When_EditingFileInAdf_Then_SavedTextIsWrittenToAdf()
    {
        // arrange
        await AmigaTestData.CreateAdf(Path.Combine(_rootPath, "disk.adf"));
        using var adfContext = new NavigationContext(_rootPath);
        await OpenAsync(adfContext, "disk.adf");
        await CopyToAdfAsync(adfContext, "s.txt", "echo hi\n");

        // act
        using (var session = await adfContext.OpenEditorAsync(await FindAsync(adfContext, "s.txt")))
        {
            Assert.False(session.IsReadOnly);
            await session.SaveAsync("echo changed\n");
        }

        // assert
        using var viewer = await adfContext.OpenViewerAsync(await FindAsync(adfContext, "s.txt"));
        Assert.Equal("echo changed\n", viewer.Document.ReadText(0, (int)viewer.Document.Length));
    }

    [Fact]
    public async Task When_EditingFileInAdfExternally_Then_ChangedCopyIsWrittenBackAndDeleted()
    {
        // arrange
        await AmigaTestData.CreateAdf(Path.Combine(_rootPath, "disk.adf"));
        using var adfContext = new NavigationContext(_rootPath);
        await OpenAsync(adfContext, "disk.adf");
        await CopyToAdfAsync(adfContext, "s.txt", "original");

        // act
        var file = await adfContext.OpenExternalEditAsync(await FindAsync(adfContext, "s.txt"));
        string path;
        using (file)
        {
            path = file.Path;
            Assert.True(file.IsTemporary);
            Assert.Equal("s.txt", Path.GetFileName(path));
            Assert.False(await file.SaveChangesAsync());
            await File.WriteAllTextAsync(path, "edited");
            Assert.True(await file.SaveChangesAsync());
            Assert.False(await file.SaveChangesAsync());
        }

        // assert
        Assert.False(File.Exists(path));
        var entry = await FindAsync(adfContext, "s.txt");
        Assert.Equal(6, entry.Size);
    }

    [Fact]
    public async Task When_EditingLocalFileExternally_Then_FileIsEditedInPlace()
    {
        // arrange
        var path = Path.Combine(_rootPath, "a.txt");
        await File.WriteAllTextAsync(path, "a");
        using var navigationContext = new NavigationContext(_rootPath);

        // act
        using var file = await navigationContext.OpenExternalEditAsync(await FindAsync(navigationContext, "a.txt"));

        // assert
        Assert.False(file.IsTemporary);
        Assert.Equal(Path.GetFullPath(path), Path.GetFullPath(file.Path));
        Assert.False(await file.SaveChangesAsync());
    }
}
