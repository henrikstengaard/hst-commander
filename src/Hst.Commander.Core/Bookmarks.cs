using System.Text.Json;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Bookmarked location of a pane with a description, listed with the volume providers (Alt+F1/Alt+F2) and made by
///     <see cref="NavigationContext.CreateBookmark" />. The password of the credentials isn't part of it, only stored in
///     the secure credential store (see <see cref="BookmarkStore" />).
/// </summary>
/// <param name="Id">Id of the bookmark, also naming its password in the credential store.</param>
/// <param name="Description">Description shown in the volume provider list.</param>
/// <param name="VolumeProviderName">
///     Name of the volume provider, null for local directories. For a volume provider asking for a location (e.g. SMB),
///     the name of that provider.
/// </param>
/// <param name="Location">
///     Location connected to for a volume provider asking for one, e.g. "\\server\share\dir", otherwise null.
/// </param>
/// <param name="Path">Path changed to, e.g. "c:\images\disk.iso\dir1" or "\\server\share\disk.adf\dir".</param>
/// <param name="UserName">User name connected with, null for none.</param>
/// <param name="HasPassword">True when the password is stored in the credential store.</param>
public sealed record Bookmark(
    string Id,
    string Description,
    string? VolumeProviderName,
    string? Location,
    string Path,
    string? UserName,
    bool HasPassword);

/// <summary>
///     Bookmarks stored in a JSON file shared by both front-ends (<see cref="Default" />,
///     <c>~/.hst-commander/bookmarks.json</c>), read again on each call, so both can run at once. Passwords are only
///     stored in a secure credential store (see <see cref="CredentialStores" />), never in the file.
/// </summary>
public sealed class BookmarkStore(string filePath, ICredentialStore credentialStore)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly Lock FileLock = new();

    /// <summary>Bookmarks in the settings directory with the credential store of the operating system.</summary>
    public static BookmarkStore Default { get; } =
        new(System.IO.Path.Combine(SettingsPaths.Directory, "bookmarks.json"), CredentialStores.Default);

    /// <summary>Path of the bookmarks file.</summary>
    public string FilePath => filePath;

    /// <summary>Credential store holding the passwords.</summary>
    public ICredentialStore CredentialStore => credentialStore;

    /// <summary>The bookmarks, sorted by description; empty when the file doesn't exist or can't be read.</summary>
    public IReadOnlyList<Bookmark> Load()
    {
        lock (FileLock)
        {
            return Read().OrderBy(bookmark => bookmark.Description, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    /// <summary>Adds a bookmark. Throws <see cref="IOException" /> when the file can't be written.</summary>
    public void Add(Bookmark bookmark)
    {
        ArgumentNullException.ThrowIfNull(bookmark);

        Update(bookmarks => bookmarks.Add(bookmark with { HasPassword = false }));
    }

    /// <summary>
    ///     Stores the password of a bookmark in the credential store and marks the bookmark as having it. Throws
    ///     <see cref="IOException" /> when the credential store isn't available or fails, e.g. no keyring is running.
    /// </summary>
    public void StorePassword(Bookmark bookmark, string password)
    {
        ArgumentNullException.ThrowIfNull(bookmark);
        ArgumentNullException.ThrowIfNull(password);

        if (!credentialStore.IsAvailable)
        {
            throw new IOException(GetUnavailableMessage());
        }

        credentialStore.Save(bookmark.Id, password);
        Update(bookmarks =>
        {
            var index = bookmarks.FindIndex(existing => existing.Id == bookmark.Id);
            if (index >= 0)
            {
                bookmarks[index] = bookmarks[index] with { HasPassword = true };
            }
        });
    }

    /// <summary>Changes the description of a bookmark.</summary>
    public void Rename(string id, string description)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(description);

        Update(bookmarks =>
        {
            var index = bookmarks.FindIndex(existing => existing.Id == id);
            if (index >= 0)
            {
                bookmarks[index] = bookmarks[index] with { Description = description.Trim() };
            }
        });
    }

    /// <summary>Deletes a bookmark and its password from the credential store.</summary>
    public void Delete(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        var hasPassword = false;
        Update(bookmarks =>
        {
            hasPassword = bookmarks.Any(bookmark => bookmark.Id == id && bookmark.HasPassword);
            bookmarks.RemoveAll(bookmark => bookmark.Id == id);
        });

        if (!hasPassword || !credentialStore.IsAvailable)
        {
            return;
        }

        try
        {
            credentialStore.Delete(id);
        }
        catch (IOException)
        {
            // the bookmark is gone; a password left behind can't be used by Hst Commander
        }
    }

    /// <summary>
    ///     Credentials of a bookmark with a stored password, null when it has none or the credential store can't read
    ///     it (the user is then asked for them).
    /// </summary>
    public VolumeCredentials? GetCredentials(Bookmark bookmark)
    {
        ArgumentNullException.ThrowIfNull(bookmark);

        if (bookmark is not { HasPassword: true, UserName: { } userName } || !credentialStore.IsAvailable)
        {
            return null;
        }

        try
        {
            return credentialStore.Load(bookmark.Id) is { } password ? new VolumeCredentials(userName, password) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Question asked when bookmarking a location connected to with a user name and password.</summary>
    public string GetStorePasswordQuestion(Bookmark bookmark) =>
        $"Store the password of '{bookmark.UserName}' for {bookmark.Location ?? bookmark.Path} securely in " +
        $"{credentialStore.Name}? Otherwise the bookmark asks for it when opened.";

    /// <summary>Message shown when credentials can't be stored securely on this system.</summary>
    public static string GetUnavailableMessage() =>
        "The user name and password can't be stored securely on this system, as there is no credential store " +
        "(e.g. a keyring with secret-tool on Linux). The bookmark asks for them when opened.";

    private List<Bookmark> Read()
    {
        try
        {
            return File.Exists(filePath)
                ? JsonSerializer.Deserialize<List<Bookmark>>(File.ReadAllText(filePath), JsonOptions)?
                    .Where(bookmark => bookmark is { Id: not null, Description: not null, Path: not null })
                    .ToList() ?? []
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or JsonException)
        {
            return [];
        }
    }

    private void Update(Action<List<Bookmark>> change)
    {
        lock (FileLock)
        {
            var bookmarks = Read();
            change(bookmarks);
            try
            {
                if (System.IO.Path.GetDirectoryName(filePath) is { Length: > 0 } directory)
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(filePath, JsonSerializer.Serialize(bookmarks, JsonOptions));
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new IOException(exception.Message, exception);
            }
        }
    }
}

/// <summary>
///     Opens bookmarks (see <see cref="Bookmark" />) in a pane for both front-ends: local paths, paths of volume providers
///     and locations of volume providers asking for one (e.g. SMB), connected with the stored credentials, asking for
///     them when there are none or they are rejected.
/// </summary>
public static class BookmarkOpener
{
    /// <summary>
    ///     Volume provider of a bookmark: <see cref="VolumeProviders.Local" /> for local paths, otherwise the volume
    ///     provider with its name. Throws <see cref="IOException" /> when there is none, e.g. a plugin not loaded.
    /// </summary>
    public static IVolumeProvider GetVolumeProvider(Bookmark bookmark,
        IReadOnlyList<IVolumeProvider>? volumeProviders = null)
    {
        ArgumentNullException.ThrowIfNull(bookmark);

        if (bookmark.VolumeProviderName == null)
        {
            return VolumeProviders.Local;
        }

        return (volumeProviders ?? VolumeProviders.All).FirstOrDefault(volumeProvider =>
                   volumeProvider.Name.Equals(bookmark.VolumeProviderName, StringComparison.OrdinalIgnoreCase)) ??
               throw new IOException(
                   $"Volume '{bookmark.VolumeProviderName}' of bookmark '{bookmark.Description}' isn't available");
    }

    /// <summary>
    ///     Opens a bookmark in a pane's navigation context. The location is unchanged when it can't be opened, throwing
    ///     <see cref="IOException" />, or when the credentials dialog is cancelled, returning false.
    /// </summary>
    /// <param name="context">Navigation context of the pane.</param>
    /// <param name="bookmark">Bookmark to open.</param>
    /// <param name="store">Bookmark store with the stored credentials.</param>
    /// <param name="askCredentials">Asks the user for credentials (see <see cref="VolumeConnector.ConnectAsync" />).</param>
    /// <param name="show">Shows the progress while connecting.</param>
    /// <param name="hide">Hides the progress.</param>
    /// <param name="volumeProviders">Volume providers, defaults to <see cref="VolumeProviders.All" />.</param>
    /// <param name="cancellationToken"></param>
    public static async Task<bool> OpenAsync(NavigationContext context, Bookmark bookmark, BookmarkStore store,
        Func<CredentialsRequest, Task<VolumeCredentials?>> askCredentials, Action<string> show, Action hide,
        IReadOnlyList<IVolumeProvider>? volumeProviders = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(bookmark);
        ArgumentNullException.ThrowIfNull(store);

        var volumeProvider = GetVolumeProvider(bookmark, volumeProviders);
        if (volumeProvider is not ILocationVolumeProvider locationVolumeProvider)
        {
            await context.ChangeLocationAsync(volumeProvider, bookmark.Path, cancellationToken);
            return true;
        }

        var connection = await VolumeConnector.ConnectAsync(locationVolumeProvider,
            bookmark.Location ?? bookmark.Path, askCredentials, show, hide, store.GetCredentials(bookmark),
            bookmark.UserName, cancellationToken);
        if (connection == null)
        {
            return false;
        }

        await context.ChangeVolumeProviderAsync(connection, bookmark.Path, cancellationToken);
        return true;
    }
}

/// <summary>
///     Item of the volume provider list (Alt+F1/Alt+F2): a volume provider, the bookmarks item opening the bookmark
///     list (<see cref="Bookmarks" />), or a bookmark of the bookmark list.
/// </summary>
public sealed record VolumeChoice(IVolumeProvider? VolumeProvider, Bookmark? Bookmark)
{
    /// <summary>The item of the volume provider list opening the bookmark list.</summary>
    public static VolumeChoice Bookmarks { get; } = new(null, null);

    /// <summary>Message shown instead of the bookmark list when there are no bookmarks.</summary>
    public const string NoBookmarksMessage = "There are no bookmarks. Add one with Ctrl+B or File > Add bookmark.";

    /// <summary>Name shown: the volume provider's name, the bookmark's description or "Bookmarks".</summary>
    public string Name => VolumeProvider?.Name ?? Bookmark?.Description ?? "Bookmarks";

    /// <summary>Description shown: the volume provider's description, the bookmark's path or the bookmarks item's.</summary>
    public string Description => VolumeProvider?.Description ?? Bookmark?.Path ?? "Bookmarked locations";

    /// <summary>True for the bookmarks item opening the bookmark list.</summary>
    public bool IsBookmarks => VolumeProvider == null && Bookmark == null;

    /// <summary>The volume providers followed by the bookmarks item.</summary>
    public static IReadOnlyList<VolumeChoice> List(IEnumerable<IVolumeProvider> volumeProviders) =>
        [..volumeProviders.Select(volumeProvider => new VolumeChoice(volumeProvider, null)), Bookmarks];

    /// <summary>The bookmarks of the bookmark list.</summary>
    public static IReadOnlyList<VolumeChoice> List(IEnumerable<Bookmark> bookmarks) =>
        [..bookmarks.Select(bookmark => new VolumeChoice(null, bookmark))];
}
