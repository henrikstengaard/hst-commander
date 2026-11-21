namespace Hst.Commander.Core.Tests;

public class GivenErrorReport
{
    private static Exception CreateThrownException(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    [Fact]
    public void When_CreatingReport_Then_TitleHasExceptionTypeAndFrontEnd()
    {
        // act
        var report = ErrorReport.FromException(CreateThrownException("Broken"), "gui", null);

        // assert
        Assert.Equal("Crash: InvalidOperationException in the GUI", report.Title);
        Assert.Contains(report.Environment, line => line.StartsWith("Version: "));
        Assert.Contains(report.Environment, line => line == "Front-end: GUI");
        Assert.Contains("System.InvalidOperationException: Broken", report.Details);
        Assert.Contains(nameof(CreateThrownException), report.Details);
    }

    [Fact]
    public void When_MessageHasHomeDirectory_Then_ItIsReplacedWithTilde()
    {
        // arrange
        var home = OperatingSystem.IsWindows() ? @"C:\Users\someone" : "/home/someone";
        var separator = OperatingSystem.IsWindows() ? @"\" : "/";
        var exception = new IOException($"Failed to read '{home}{separator}secret.txt'");

        // act
        var report = ErrorReport.FromException(exception, "tui", home);

        // assert
        Assert.DoesNotContain("someone", report.Text);
        Assert.Contains($"'~{separator}secret.txt'", report.Details);
    }

    [Fact]
    public void When_TextHasHomeDirectoryWithOtherSeparators_Then_ItIsReplacedWithTilde()
    {
        // act
        var text = ErrorReport.Redact(@"at C:\Users\someone\src\a.cs and C:/Users/someone/src/b.cs",
            @"C:\Users\someone");

        // assert
        Assert.Equal(@"at ~\src\a.cs and ~/src/b.cs", text);
    }

    [Fact]
    public void When_CreatingIssueUrl_Then_TitleAndBodyAreEncoded()
    {
        // arrange
        var report = ErrorReport.FromException(CreateThrownException("a & b"), "gui", null);

        // act
        var url = report.GitHubIssueUrl;

        // assert
        Assert.StartsWith($"{ErrorReport.IssuesUrl}?title=Crash%3A%20InvalidOperationException", url);
        Assert.Contains("&body=", url);
        Assert.Contains("a%20%26%20b", url);
        Assert.DoesNotContain(" ", url);
    }

    [Fact]
    public void When_DetailsAreLong_Then_IssueUrlIsCutToMaxLength()
    {
        // arrange
        var report = new ErrorReport("Crash: Test", ["Version: test"], string.Join("\n",
            Enumerable.Range(0, 2000).Select(index => $"   at Some.Namespace.Type.Method{index}() in /src/File.cs:line {index}")));

        // act
        var url = report.GitHubIssueUrl;

        // assert
        Assert.True(url.Length <= ErrorReport.MaxUrlLength, $"URL is {url.Length} characters");
        Assert.Contains(Uri.EscapeDataString("please attach the log file"), url);
        Assert.Contains(Uri.EscapeDataString("Method0()"), url);
    }

    [Fact]
    public void When_ParsingReportText_Then_ReportIsTheSame()
    {
        // arrange
        var report = ErrorReport.FromException(CreateThrownException("Broken"), "tui", null);

        // act
        var parsed = ErrorReport.Parse(report.Text);

        // assert
        Assert.Equal(report.Title, parsed.Title);
        Assert.Equal(report.Environment, parsed.Environment);
        Assert.Equal(report.Details.ReplaceLineEndings("\n"), parsed.Details);
    }
}
