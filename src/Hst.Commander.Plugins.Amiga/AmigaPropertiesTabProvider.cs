using Hst.Amiga.DataTypes.DiskObjects;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Amiga;

/// <summary>
/// Adds the Comment tab to the properties dialog of all entries, changed in Amiga file systems or as UAE metadata in
/// other volumes (read-only when it can't be changed, e.g. in lha/lzx archives) and the Tooltypes tab to entries with an Amiga icon (.info file) next to them, one
/// tooltype per line. The NewIcon image of an icon is stored in tooltypes after a "*** DON'T EDIT THE FOLLOWING
/// LINES!! ***" line; those tooltypes aren't shown and are kept when the tooltypes are saved.
/// </summary>
public class AmigaPropertiesTabProvider : IPropertiesTabProvider
{
    /// <summary>Maximum length of a comment of the Amiga file systems.</summary>
    public const int MaxCommentLength = 79;

    /// <summary>Tooltype starting the tooltypes holding the image of a NewIcon.</summary>
    private const string NewIconHeader = "*** DON'T EDIT THE FOLLOWING LINES!! ***";

    /// <summary>Largest icon file read, icons are much smaller.</summary>
    private const long MaxIconFileSize = 4 * 1024 * 1024;

    private static readonly byte[] DiskObjectMagic = [0xe3, 0x10];

    public string Name => AmigaPlugin.PluginName;

    public async Task<IReadOnlyList<PropertiesTab>> GetTabs(IPropertiesContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tabs = new List<PropertiesTab> { CreateCommentTab(context) };

        if (await CreateToolTypesTabAsync(context, cancellationToken) is { } toolTypesTab)
        {
            tabs.Add(toolTypesTab);
        }

        return tabs;
    }

    private static PropertiesTab CreateCommentTab(IPropertiesContext context)
    {
        var comment = context.AmigaMetadata?.Comment;
        return new PropertiesTab("Comment", string.IsNullOrEmpty(comment) ? null : comment)
        {
            MaxLength = MaxCommentLength,
            NoTextMessage = "No comment",
            AddText = "Add comment",
            DeleteText = "Delete comment",
            Save = context.CanSetAmigaMetadata
                ? (text, cancellationToken) => SaveCommentAsync(context, text, cancellationToken)
                : null
        };
    }

    private static Task SaveCommentAsync(IPropertiesContext context, string? text,
        CancellationToken cancellationToken)
    {
        var comment = (text ?? string.Empty).ReplaceLineEndings(" ");
        if (comment.Length > MaxCommentLength)
        {
            throw new IOException($"The comment is longer than {MaxCommentLength} characters");
        }

        // the current metadata, as the protection bits may have been changed in the General tab; entries without
        // metadata get it with the protection bits "----rwed"
        var metadata = context.AmigaMetadata ?? new AmigaMetadata(0, string.Empty);
        return context.SetAmigaMetadata(metadata with { Comment = comment }, cancellationToken);
    }

    private static async Task<PropertiesTab?> CreateToolTypesTabAsync(IPropertiesContext context,
        CancellationToken cancellationToken)
    {
        if (context.IconFile is not { Type: EntryType.Item } iconFile ||
            !iconFile.Name.EndsWith(AmigaIconProvider.InfoExtension, StringComparison.OrdinalIgnoreCase) ||
            iconFile.Size > MaxIconFileSize)
        {
            return null;
        }

        var bytes = await ReadAllAsync(context.Volume, iconFile.Name, cancellationToken);
        var isDiskObject = bytes.AsSpan().StartsWith(DiskObjectMagic);
        AmigaIcon? amigaIcon;
        try
        {
            amigaIcon = await AmigaIconHelper.ReadAmigaIcon(new MemoryStream(bytes), false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // not a valid icon, so it has no tooltypes
            return null;
        }

        if (amigaIcon?.DiskObject is not { } diskObject)
        {
            return null;
        }

        var toolTypes = GetEditableToolTypes(diskObject);
        // true color (PNG) icons store their tooltypes in a chunk of the PNG, which isn't written
        var canChange = isDiskObject && context.CanWrite;
        return new PropertiesTab("Tooltypes", toolTypes.Count == 0 ? null : string.Join('\n', toolTypes))
        {
            IsMultiline = true,
            NoTextMessage = "No tooltypes",
            AddText = "Add tooltypes",
            DeleteText = "Delete tooltypes",
            Save = canChange
                ? (text, token) => SaveToolTypesAsync(context.Volume, iconFile.Name, text, token)
                : null
        };
    }

    /// <summary>Tooltypes shown in the tab: those before the tooltypes of a NewIcon image.</summary>
    private static List<string> GetEditableToolTypes(DiskObject diskObject)
    {
        var toolTypes = DiskObjectHelper.ConvertToolTypesToStrings(diskObject.ToolTypes).ToList();
        return toolTypes.Take(GetNewIconIndex(toolTypes)).ToList();
    }

    /// <summary>
    /// Index of the first tooltype of a NewIcon image: the header, or the empty (" ") tooltype separating it from
    /// the other tooltypes. The number of tooltypes when the icon has no NewIcon.
    /// </summary>
    private static int GetNewIconIndex(IReadOnlyList<string> toolTypes)
    {
        var headerIndex = toolTypes.ToList().FindIndex(toolType => toolType == NewIconHeader);
        if (headerIndex < 0)
        {
            return toolTypes.Count;
        }

        return headerIndex > 0 && string.IsNullOrWhiteSpace(toolTypes[headerIndex - 1])
            ? headerIndex - 1
            : headerIndex;
    }

    /// <summary>
    /// Writes the tooltypes of an icon file, one per line, keeping the tooltypes of a NewIcon image and the data after
    /// the disk object (e.g. a GlowIcon) unchanged.
    /// </summary>
    internal static async Task SaveToolTypesAsync(IVolume volume, string iconFileName, string? text,
        CancellationToken cancellationToken)
    {
        var bytes = await ReadAllAsync(volume, iconFileName, cancellationToken);
        if (!bytes.AsSpan().StartsWith(DiskObjectMagic))
        {
            throw new IOException($"'{iconFileName}' is not an Amiga icon with tooltypes that can be changed");
        }

        var input = new MemoryStream(bytes);
        var diskObject = await DiskObjectReader.Read(input);
        var trailingData = bytes.AsSpan((int)input.Position).ToArray();

        var toolTypes = diskObject.ToolTypes?.TextDatas.ToList() ?? [];
        var newIconIndex = GetNewIconIndex(DiskObjectHelper.ConvertToolTypesToStrings(diskObject.ToolTypes).ToList());
        var lines = string.IsNullOrEmpty(text) ? [] : text.ReplaceLineEndings("\n").Split('\n');
        var textDatas = DiskObjectHelper.ConvertStringsToTextDatas(lines).Concat(toolTypes.Skip(newIconIndex))
            .ToList();
        diskObject.ToolTypes = textDatas.Count > 0 ? new ToolTypes { TextDatas = textDatas } : null;
        diskObject.ToolTypesPointer = textDatas.Count > 0 ? 1U : 0U;

        var output = new MemoryStream();
        await DiskObjectWriter.Write(diskObject, output);
        await output.WriteAsync(trailingData, cancellationToken);

        await using var stream = await volume.OpenWrite(iconFileName, cancellationToken);
        await stream.WriteAsync(output.ToArray(), cancellationToken);
    }

    private static async Task<byte[]> ReadAllAsync(IVolume volume, string name, CancellationToken cancellationToken)
    {
        await using var stream = await volume.OpenRead(name, cancellationToken);
        var data = new MemoryStream();
        await stream.CopyToAsync(data, cancellationToken);
        return data.ToArray();
    }
}
