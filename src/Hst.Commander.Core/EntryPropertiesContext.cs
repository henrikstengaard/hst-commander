using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     The entry of the properties dialog given to the <see cref="IPropertiesTabProvider" />s of the plugins, created by
///     <see cref="NavigationContext.GetPropertiesAsync" />. Its Amiga metadata is updated when changed, so the protection
///     bits of the General tab and the comment of a tab can both be saved.
/// </summary>
/// <param name="setAmigaMetadata">Changes the Amiga metadata of the entry, null when it can't be changed.</param>
internal sealed class EntryPropertiesContext(
    IVolume volume,
    IEntry entry,
    IEntry? iconFile,
    bool canWrite,
    AmigaMetadata? amigaMetadata,
    Func<AmigaMetadata, CancellationToken, Task>? setAmigaMetadata) : IPropertiesContext
{
    public IVolume Volume { get; } = volume;

    public IEntry Entry { get; } = entry;

    public IEntry? IconFile { get; } = iconFile;

    public bool CanWrite { get; } = canWrite;

    public AmigaMetadata? AmigaMetadata { get; private set; } = amigaMetadata;

    public bool CanSetAmigaMetadata => setAmigaMetadata != null;

    public async Task SetAmigaMetadata(AmigaMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (setAmigaMetadata == null)
        {
            throw new NotSupportedException($"The Amiga metadata of '{Entry.Name}' can't be changed");
        }

        await setAmigaMetadata(metadata, cancellationToken);
        AmigaMetadata = metadata;
    }
}
