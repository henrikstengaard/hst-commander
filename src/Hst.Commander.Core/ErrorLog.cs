using System.Globalization;
using System.Text;

namespace Hst.Commander.Core;

/// <summary>
///     Log file of the errors shown to the user and the unhandled exceptions of a front-end, one file per day in
///     <c>~/.hst-commander/logs</c> (e.g. <c>gui-2026-11-21.log</c>). Logging never throws: a log that can't be written
///     must not turn an error into a crash.
/// </summary>
public static class ErrorLog
{
    /// <summary>Days log files are kept, older files are deleted when the log is initialized.</summary>
    public const int RetentionDays = 30;

    private static readonly Lock WriteLock = new();
    private static string _frontEnd = "app";

    /// <summary>Directory of the log files, null until initialized.</summary>
    public static string? LogDirectory { get; private set; }

    /// <summary>Front-end writing the log, <c>tui</c> or <c>gui</c>.</summary>
    public static string FrontEnd => _frontEnd;

    /// <summary>The log file entries are written to today, null until initialized.</summary>
    public static string? CurrentFile => LogDirectory == null
        ? null
        : Path.Combine(LogDirectory,
            $"{_frontEnd}-{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log");

    /// <summary>Default directory of the log files, <c>logs</c> in the settings directory.</summary>
    public static string DefaultDirectory => Path.Combine(SettingsPaths.Directory, "logs");

    /// <summary>Starts logging for a front-end (<c>tui</c> or <c>gui</c>) in the default directory.</summary>
    public static void Initialize(string frontEnd) => Initialize(frontEnd, DefaultDirectory);

    /// <summary>Starts logging for a front-end in a directory, deleting log files older than the retention.</summary>
    public static void Initialize(string frontEnd, string directory)
    {
        ArgumentNullException.ThrowIfNull(frontEnd);
        ArgumentNullException.ThrowIfNull(directory);
        lock (WriteLock)
        {
            _frontEnd = frontEnd;
            LogDirectory = directory;
        }

        DeleteOldFiles(directory, DateTime.Now.AddDays(-RetentionDays));
    }

    /// <summary>Logs an error shown to the user, with the exception causing it when there is one.</summary>
    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    /// <summary>Logs an unhandled exception and where it was caught (e.g. the UI thread).</summary>
    public static void Unhandled(Exception exception, string source) =>
        Write("UNHANDLED", $"Unhandled exception ({source})", exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var entry = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append(' ').Append(level)
                .Append(" [").Append(AppInfo.Current.NameAndVersion).Append(' ').Append(_frontEnd).Append("] ")
                .AppendLine(message);
            if (exception != null)
            {
                entry.AppendLine(exception.ToString());
            }

            lock (WriteLock)
            {
                if (CurrentFile is not { } file)
                {
                    return;
                }

                Directory.CreateDirectory(LogDirectory!);
                // opened and closed for each entry, so an entry is on disk even when the process ends right after
                File.AppendAllText(file, entry.ToString());
            }
        }
        catch (Exception)
        {
            // a log that can't be written is ignored
        }
    }

    private static void DeleteOldFiles(string directory, DateTime olderThan)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < olderThan)
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception)
                {
                    // e.g. a log file used by the other front-end
                }
            }
        }
        catch (Exception)
        {
            // the directory can't be read
        }
    }
}
