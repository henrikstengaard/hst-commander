namespace Hst.Commander.Core;

/// <summary>
///     Process-wide handling of unhandled exceptions, installed by both front-ends at startup: exceptions ending the
///     process are logged and saved as a <see cref="CrashMarker" />, unobserved task exceptions are logged. Exceptions
///     on the UI thread are handled by each front-end, which shows them and keeps running.
/// </summary>
public static class CrashHandler
{
    /// <summary>
    ///     Environment variable making the front-ends throw an exception for testing the error handling: <c>ui</c>
    ///     throws on the UI thread once the window is shown, <c>thread</c> on another thread, ending the process.
    /// </summary>
    public const string TestCrashVariable = "HST_COMMANDER_TEST_CRASH";

    private static int _installed;

    /// <summary>Initializes the <see cref="ErrorLog" /> of a front-end and hooks the unhandled exception events.</summary>
    public static void Install(string frontEnd)
    {
        ErrorLog.Initialize(frontEnd);
        if (Interlocked.Exchange(ref _installed, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                ReportCrash(exception, "process");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ErrorLog.Unhandled(args.Exception, "unobserved task");
            args.SetObserved();
        };
    }

    /// <summary>Logs an exception ending the process and saves it so the next start offers to report it.</summary>
    public static void ReportCrash(Exception exception, string source)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ErrorLog.Unhandled(exception, source);
        try
        {
            CrashMarker.Write(ErrorReport.FromException(exception, ErrorLog.FrontEnd));
        }
        catch (Exception)
        {
            // the crash is in the log
        }
    }

    /// <summary>Value of <see cref="TestCrashVariable" /> (<c>ui</c> or <c>thread</c>), null when not set.</summary>
    public static string? TestCrash => System.Environment.GetEnvironmentVariable(TestCrashVariable)?.Trim()
        .ToLowerInvariant() is { Length: > 0 } value
        ? value
        : null;

    /// <summary>
    ///     Throws the test exception of <see cref="TestCrashVariable" /> <c>thread</c> on a new thread, ending the
    ///     process. Does nothing for other values.
    /// </summary>
    public static void StartTestCrashThread()
    {
        if (TestCrash != "thread")
        {
            return;
        }

        new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(2));
            throw new InvalidOperationException($"Test crash on another thread ({TestCrashVariable}=thread)");
        }) { IsBackground = true }.Start();
    }

    /// <summary>Exception thrown on the UI thread for <see cref="TestCrashVariable" /> <c>ui</c>.</summary>
    public static Exception CreateTestUiException() =>
        new InvalidOperationException($"Test error on the UI thread ({TestCrashVariable}=ui)");
}
