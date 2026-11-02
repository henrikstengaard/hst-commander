using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Hst.Commander.Core;

/// <summary>
///     Secure store of the passwords of bookmarks (see <see cref="BookmarkStore" />), kept by the operating system and
///     protected by the user's login, never in a file of Hst Commander.
/// </summary>
public interface ICredentialStore
{
    /// <summary>Name shown to the user, e.g. "Windows Credential Manager".</summary>
    string Name { get; }

    /// <summary>True when passwords can be stored on this system.</summary>
    bool IsAvailable { get; }

    /// <summary>Stores a password, replacing one stored with the id. Throws <see cref="IOException" /> on failure.</summary>
    void Save(string id, string password);

    /// <summary>The password stored with the id, null when none. Throws <see cref="IOException" /> on failure.</summary>
    string? Load(string id);

    /// <summary>Deletes the password stored with the id, if any. Throws <see cref="IOException" /> on failure.</summary>
    void Delete(string id);
}

/// <summary>The credential stores of the operating systems.</summary>
public static class CredentialStores
{
    /// <summary>Service (macOS), target prefix (Windows) and application attribute (Linux) of the stored passwords.</summary>
    internal const string ServiceName = "Hst Commander";

    /// <summary>
    ///     Credential store of the operating system: Windows Credential Manager, the macOS Keychain or the Secret Service
    ///     (e.g. GNOME Keyring or KWallet) through secret-tool on Linux; one that isn't available otherwise.
    /// </summary>
    public static ICredentialStore Default { get; } = Create();

    private static ICredentialStore Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacKeychainStore();
        }

        return OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD()
            ? new SecretServiceStore()
            : new NullCredentialStore();
    }
}

/// <summary>Credential store that can't store passwords, e.g. on a system without one.</summary>
public sealed class NullCredentialStore : ICredentialStore
{
    public string Name => "None";

    public bool IsAvailable => false;

    public void Save(string id, string password) => throw new IOException(BookmarkStore.GetUnavailableMessage());

    public string? Load(string id) => null;

    public void Delete(string id)
    {
    }
}

/// <summary>
///     Passwords in Windows Credential Manager as generic credentials named "Hst Commander/bookmark/&lt;id&gt;",
///     encrypted by Windows for the user.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ICredentialStore
{
    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string Name => "Windows Credential Manager";

    public bool IsAvailable => true;

    private static string GetTarget(string id) => $"{CredentialStores.ServiceName}/bookmark/{id}";

    public void Save(string id, string password)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(password);

        var blob = Encoding.Unicode.GetBytes(password);
        var blobPointer = Marshal.AllocHGlobal(Math.Max(blob.Length, 1));
        try
        {
            Marshal.Copy(blob, 0, blobPointer, blob.Length);
            var credential = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = GetTarget(id),
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = blobPointer,
                Persist = CredPersistLocalMachine,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref credential, 0))
            {
                throw CreateException("store");
            }
        }
        finally
        {
            Marshal.Copy(new byte[blob.Length], 0, blobPointer, blob.Length);
            Marshal.FreeHGlobal(blobPointer);
            Array.Clear(blob);
        }
    }

    public string? Load(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (!CredRead(GetTarget(id), CredTypeGeneric, 0, out var credentialPointer))
        {
            return Marshal.GetLastWin32Error() == ErrorNotFound ? null : throw CreateException("read");
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return string.Empty;
            }

            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            var password = Encoding.Unicode.GetString(blob);
            Array.Clear(blob);
            return password;
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public void Delete(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (!CredDelete(GetTarget(id), CredTypeGeneric, 0) && Marshal.GetLastWin32Error() != ErrorNotFound)
        {
            throw CreateException("delete");
        }
    }

    private static IOException CreateException(string action) =>
        new($"Failed to {action} the password in Windows Credential Manager: " +
            new Win32Exception(Marshal.GetLastWin32Error()).Message);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}

/// <summary>
///     Passwords in the user's macOS Keychain as generic passwords of the service "Hst Commander" with the bookmark id
///     as account.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacKeychainStore : ICredentialStore
{
    private const string SecurityLibrary = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int ErrSecItemNotFound = -25300;

    private static readonly byte[] Service = Encoding.UTF8.GetBytes(CredentialStores.ServiceName);

    public string Name => "Keychain";

    public bool IsAvailable => true;

    public void Save(string id, string password)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(password);

        var account = Encoding.UTF8.GetBytes(id);
        var data = Encoding.UTF8.GetBytes(password);
        try
        {
            var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service,
                (uint)account.Length, account, out _, IntPtr.Zero, out var item);
            if (status == 0)
            {
                try
                {
                    status = SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)data.Length, data);
                }
                finally
                {
                    CFRelease(item);
                }
            }
            else if (status == ErrSecItemNotFound)
            {
                status = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)Service.Length, Service,
                    (uint)account.Length, account, (uint)data.Length, data, out item);
                if (status == 0 && item != IntPtr.Zero)
                {
                    CFRelease(item);
                }
            }

            if (status != 0)
            {
                throw CreateException("store", status);
            }
        }
        finally
        {
            Array.Clear(data);
        }
    }

    public string? Load(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        var account = Encoding.UTF8.GetBytes(id);
        var dataPointer = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service,
                (uint)account.Length, account, out var length, dataPointer, out var item);
            if (status == ErrSecItemNotFound)
            {
                return null;
            }

            if (status != 0)
            {
                throw CreateException("read", status);
            }

            var data = Marshal.ReadIntPtr(dataPointer);
            try
            {
                var bytes = new byte[length];
                Marshal.Copy(data, bytes, 0, bytes.Length);
                var password = Encoding.UTF8.GetString(bytes);
                Array.Clear(bytes);
                return password;
            }
            finally
            {
                SecKeychainItemFreeContent(IntPtr.Zero, data);
                CFRelease(item);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(dataPointer);
        }
    }

    public void Delete(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        var account = Encoding.UTF8.GetBytes(id);
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service,
            (uint)account.Length, account, out _, IntPtr.Zero, out var item);
        if (status == ErrSecItemNotFound)
        {
            return;
        }

        if (status != 0)
        {
            throw CreateException("delete", status);
        }

        try
        {
            status = SecKeychainItemDelete(item);
            if (status != 0)
            {
                throw CreateException("delete", status);
            }
        }
        finally
        {
            CFRelease(item);
        }
    }

    private static IOException CreateException(string action, int status) =>
        new($"Failed to {action} the password in the Keychain (error {status})");

    [DllImport(SecurityLibrary)]
    private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceNameLength,
        byte[] serviceName, uint accountNameLength, byte[] accountName, uint passwordLength, byte[] passwordData,
        out IntPtr itemRef);

    /// <param name="passwordData">Pointer receiving the password data, or zero when it isn't wanted.</param>
    [DllImport(SecurityLibrary)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength,
        byte[] serviceName, uint accountNameLength, byte[] accountName, out uint passwordLength, IntPtr passwordData,
        out IntPtr itemRef);

    [DllImport(SecurityLibrary)]
    private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length,
        byte[] data);

    [DllImport(SecurityLibrary)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(SecurityLibrary)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(CoreFoundationLibrary)]
    private static extern void CFRelease(IntPtr cf);
}

/// <summary>
///     Passwords in the Secret Service of the desktop (e.g. GNOME Keyring or KWallet) through the secret-tool command
///     of libsecret, with the attributes "application hst-commander bookmark &lt;id&gt;". The password is passed on
///     standard input, never on the command line. Available when secret-tool is installed.
/// </summary>
public sealed class SecretServiceStore : ICredentialStore
{
    private const string SecretTool = "secret-tool";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    public string Name => "Secret Service keyring";

    public bool IsAvailable { get; } = FindSecretTool() != null;

    public void Save(string id, string password)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(password);

        var result = Run(["store", "--label", $"{CredentialStores.ServiceName} bookmark", ..GetAttributes(id)],
            password);
        if (result.ExitCode != 0)
        {
            throw CreateException("store", result.Error);
        }
    }

    public string? Load(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        // exits with 1 and no output when not found
        var result = Run(["lookup", ..GetAttributes(id)], null);
        if (result.ExitCode != 0)
        {
            return result.Error.Trim().Length == 0 ? null : throw CreateException("read", result.Error);
        }

        return result.Output;
    }

    public void Delete(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        var result = Run(["clear", ..GetAttributes(id)], null);
        if (result.ExitCode != 0 && result.Error.Trim().Length > 0)
        {
            throw CreateException("delete", result.Error);
        }
    }

    private static string[] GetAttributes(string id) => ["application", "hst-commander", "bookmark", id];

    private static IOException CreateException(string action, string error) =>
        new($"Failed to {action} the password in the keyring: {error.Trim()}");

    private static string? FindSecretTool() =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(directory => Path.Combine(directory, SecretTool))
        .FirstOrDefault(File.Exists);

    private sealed record Result(int ExitCode, string Output, string Error);

    private Result Run(IEnumerable<string> arguments, string? input)
    {
        if (!IsAvailable)
        {
            throw new IOException(BookmarkStore.GetUnavailableMessage());
        }

        var startInfo = new ProcessStartInfo(SecretTool)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo) ?? throw new IOException($"{SecretTool} can't be started");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (input != null)
            {
                process.StandardInput.Write(input);
            }

            process.StandardInput.Close();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill();
                throw new IOException($"{SecretTool} didn't answer");
            }

            return new Result(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }
        catch (Win32Exception exception)
        {
            throw new IOException($"{SecretTool} can't be started: {exception.Message}", exception);
        }
    }
}
