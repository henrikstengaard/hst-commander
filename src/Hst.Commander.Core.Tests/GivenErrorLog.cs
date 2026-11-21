namespace Hst.Commander.Core.Tests;

/// <summary>Tests of <see cref="ErrorLog" /> and <see cref="CrashMarker" />, which share the static log state.</summary>
[Collection(nameof(ErrorLog))]
public class GivenErrorLog : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"hst-commander-log-{Guid.NewGuid():N}");

    public GivenErrorLog() => ErrorLog.Initialize("test", _directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public void When_LoggingAnErrorWithException_Then_FileHasMessageAndException()
    {
        // act
        ErrorLog.Error("Failed to copy", new IOException("Disk full"));

        // assert
        var text = File.ReadAllText(ErrorLog.CurrentFile!);
        Assert.StartsWith("test-", Path.GetFileName(ErrorLog.CurrentFile));
        Assert.Contains("ERROR", text);
        Assert.Contains("Failed to copy", text);
        Assert.Contains("System.IO.IOException: Disk full", text);
    }

    [Fact]
    public void When_LoggingAnUnhandledException_Then_FileHasSourceAndException()
    {
        // act
        ErrorLog.Unhandled(new InvalidOperationException("Broken"), "UI thread");

        // assert
        var text = File.ReadAllText(ErrorLog.CurrentFile!);
        Assert.Contains("UNHANDLED", text);
        Assert.Contains("Unhandled exception (UI thread)", text);
        Assert.Contains("System.InvalidOperationException: Broken", text);
    }

    [Fact]
    public void When_InitializingWithOldLogFiles_Then_OldFilesAreDeleted()
    {
        // arrange
        Directory.CreateDirectory(_directory);
        var oldFile = Path.Combine(_directory, "gui-2000-01-01.log");
        var newFile = Path.Combine(_directory, "gui-new.log");
        File.WriteAllText(oldFile, "old");
        File.WriteAllText(newFile, "new");
        File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-ErrorLog.RetentionDays - 1));

        // act
        ErrorLog.Initialize("test", _directory);

        // assert
        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
    }

    [Fact]
    public void When_LogDirectoryCantBeCreated_Then_LoggingDoesNotThrow()
    {
        // arrange: a file where the directory should be
        var file = Path.Combine(Path.GetTempPath(), $"hst-commander-log-file-{Guid.NewGuid():N}");
        File.WriteAllText(file, string.Empty);
        try
        {
            ErrorLog.Initialize("test", file);

            // act
            var exception = Record.Exception(() => ErrorLog.Error("Failed", new IOException("Disk full")));

            // assert
            Assert.Null(exception);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void When_CrashMarkerIsWritten_Then_ItIsTakenOnce()
    {
        // arrange
        var report = ErrorReport.FromException(new InvalidOperationException("Broken"), "gui", null);

        // act
        CrashMarker.Write(report);
        var first = CrashMarker.TakePending();
        var second = CrashMarker.TakePending();

        // assert
        Assert.Equal(report.Text, first);
        Assert.Null(second);
    }

    [Fact]
    public void When_NoCrashMarker_Then_NothingIsPending()
    {
        // act & assert
        Assert.Null(CrashMarker.TakePending());
    }
}
