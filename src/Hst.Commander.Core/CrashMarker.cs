namespace Hst.Commander.Core;

/// <summary>
///     Report of an exception ending the process, saved as <c>last-crash-&lt;front-end&gt;.txt</c> in the log directory
///     so the next start of the front-end can offer to report it, as no dialog can be shown while the process ends.
/// </summary>
public static class CrashMarker
{
    /// <summary>File of the crash marker, null until the log is initialized.</summary>
    public static string? FilePath => ErrorLog.LogDirectory is { } directory
        ? Path.Combine(directory, $"last-crash-{ErrorLog.FrontEnd}.txt")
        : null;

    /// <summary>Saves the report of a crash. Never throws.</summary>
    public static void Write(ErrorReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        try
        {
            if (FilePath is not { } file)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, report.Text);
        }
        catch (Exception)
        {
            // the crash is still in the log, if that could be written
        }
    }

    /// <summary>Returns the report of the last crash and deletes it, null when the last run didn't crash.</summary>
    public static string? TakePending()
    {
        try
        {
            if (FilePath is not { } file || !File.Exists(file))
            {
                return null;
            }

            var text = File.ReadAllText(file);
            File.Delete(file);
            return text;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
