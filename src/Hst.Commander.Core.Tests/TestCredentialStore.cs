namespace Hst.Commander.Core.Tests;

/// <summary>Credential store keeping passwords in memory, optionally unavailable or failing to store.</summary>
public sealed class TestCredentialStore(bool isAvailable = true) : ICredentialStore
{
    public Dictionary<string, string> Passwords { get; } = new(StringComparer.Ordinal);

    /// <summary>When true, <see cref="Save" /> throws <see cref="IOException" />, e.g. like a locked keyring.</summary>
    public bool FailSave { get; set; }

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

    public string? Load(string id) => Passwords.GetValueOrDefault(id);

    public void Delete(string id) => Passwords.Remove(id);
}
