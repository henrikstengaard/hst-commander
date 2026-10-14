using System.Diagnostics;

namespace Hst.Commander.Core;

/// <summary>
///     Starts local files opened in a pane, either with the shell or, for console programs, in the terminal.
/// </summary>
public static class FileStarter
{
    private const ushort ConsoleSubsystem = 3;

    /// <summary>
    ///     Starts a file with the shell in its directory: programs are executed and other files are opened with their
    ///     associated application.
    /// </summary>
    public static void StartWithShell(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty
        });
    }

    /// <summary>
    ///     True when the file is a program that runs in the terminal, so a TUI must hand the terminal over to it while
    ///     it runs. On Windows these are console programs (.exe with the console subsystem, .com, .bat and .cmd), on
    ///     other systems files with execute permission.
    /// </summary>
    public static bool IsTerminalProgram(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return HasExecutePermission(path);
        }

        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".com":
            case ".bat":
            case ".cmd":
                return true;
            case ".exe":
                try
                {
                    using var stream = File.OpenRead(path);
                    return IsConsoleExecutable(stream);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return false;
                }
            default:
                return false;
        }
    }

    /// <summary>True when the stream is a Windows (PE) executable using the console subsystem.</summary>
    public static bool IsConsoleExecutable(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, true);
        try
        {
            if (reader.ReadUInt16() != 0x5A4D) // "MZ"
            {
                return false;
            }

            stream.Position = 0x3C;
            var peHeaderOffset = reader.ReadInt32();
            stream.Position = peHeaderOffset;
            if (reader.ReadUInt32() != 0x00004550) // "PE\0\0"
            {
                return false;
            }

            // subsystem is at the same offset in the optional header of 32-bit and 64-bit executables,
            // which follows the 4 byte signature and 20 byte file header
            stream.Position = peHeaderOffset + 4 + 20 + 68;
            return reader.ReadUInt16() == ConsoleSubsystem;
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or ArgumentException)
        {
            return false;
        }
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    internal static bool HasExecutePermission(string path)
    {
        try
        {
            return (File.GetUnixFileMode(path) &
                    (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
