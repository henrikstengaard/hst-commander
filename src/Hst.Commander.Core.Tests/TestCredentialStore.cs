namespace Hst.Commander.Core.Tests;

/// <summary>Credential store keeping passwords in memory, optionally unavailable or failing to store or read.</summary>
public sealed class TestCredentialStore(bool isAvailable = true) : ICredentialStore
{
    public Dictionary<string, string> Passwords { get; } = new(StringComparer.Ordinal);

    /// <summary>When true, <see cref="Save" /> throws <see cref="IOException" />, e.g. like a locked keyring.</summary>
    public bool FailSave { get; set; }

    /// <summary>When true, <see cref="Load" /> throws <see cref="IOException" />, e.g. like a locked keyring.</summary>
    public bool FailLoad { get; set; }

    public string Name => "Test store";

    public bool IsAvailable => isAvailable;

    public void Save(string id, string password)
    {
        if (FailSave)
        {
            throw new IOException("Keyring is locked");
        }

        Passwords[id] = password;
    }

    public string? Load(string id) =>
        FailLoad ? throw new IOException("Keyring is locked") : Passwords.GetValueOrDefault(id);

    public void Delete(string id) => Passwords.Remove(id);
}
