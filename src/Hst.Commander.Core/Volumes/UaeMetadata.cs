using System.Text.RegularExpressions;
using Hst.Amiga.DataTypes.UaeFsDbs;
using Hst.Amiga.DataTypes.UaeMetafiles;
using Hst.Amiga.FileSystems;
using Hst.Commander.Abstractions;
using EntryType = Hst.Commander.Abstractions.EntryType;
using FsDbConstants = Hst.Amiga.DataTypes.UaeFsDbs.Constants;
using MetafileConstants = Hst.Amiga.DataTypes.UaeMetafiles.Constants;

namespace Hst.Commander.Core.Volumes;

/// <summary>Amiga name, protection bits and comment of an entry stored with a normal name as UAE metadata.</summary>
internal sealed record UaeMetadataNode(string AmigaName, string NormalName, AmigaMetadata Metadata);

/// <summary>A node of a "_UAEFSDB.___" file and its bytes, kept to write the file again.</summary>
internal sealed record UaeFsDbNodeData(UaeMetadataNode Node, byte[] Bytes);

/// <summary>
///     UAE metadata of a directory of a volume that doesn't store Amiga metadata, e.g. a local directory: the
///     "_UAEFSDB.___" file of WinUAE and FS-UAE (or WinUAE's alternate data streams on NTFS) and the ".uaem" metafiles
///     of FS-UAE.
/// </summary>
internal sealed class UaeDirectoryMetadata(
    IReadOnlyList<UaeMetadataNode> nodes,
    string? fsDbFileName,
    IReadOnlyList<UaeFsDbNodeData> fsDbNodes,
    IReadOnlyDictionary<string, string> metafileNames)
{
    public static UaeDirectoryMetadata Empty { get; } = new([], null, [], new Dictionary<string, string>());

    /// <summary>Metadata of the entries, from the "_UAEFSDB.___" file, alternate data streams and metafiles.</summary>
    public IReadOnlyList<UaeMetadataNode> Nodes { get; } = nodes;

    /// <summary>Name of the "_UAEFSDB.___" file as listed, null when the directory has none.</summary>
    public string? FsDbFileName { get; } = fsDbFileName;

    /// <summary>Valid nodes of the "_UAEFSDB.___" file, in the order of the file.</summary>
    public IReadOnlyList<UaeFsDbNodeData> FsDbNodes { get; } = fsDbNodes;

    /// <summary>Names of the ".uaem" metafiles by the normal name of their entry.</summary>
    public IReadOnlyDictionary<string, string> MetafileNames { get; } = metafileNames;

    public bool IsEmpty => Nodes.Count == 0 && FsDbFileName == null && MetafileNames.Count == 0;

    /// <summary>True for the files holding the metadata, which aren't copied to Amiga volumes.</summary>
    public bool IsMetadataFile(string name) =>
        name.Equals(FsDbFileName, StringComparison.Ordinal) || MetafileNames.Values.Contains(name);

    /// <summary>Finds the metadata of an entry by its normal name, matched exactly, otherwise case-insensitive.</summary>
    public UaeMetadataNode? Find(string normalName) =>
        Nodes.FirstOrDefault(node => node.NormalName.Equals(normalName, StringComparison.Ordinal)) ??
        Nodes.FirstOrDefault(node => node.NormalName.Equals(normalName, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the node is in the "_UAEFSDB.___" file.</summary>
    public bool IsFsDbNode(UaeMetadataNode node) => FsDbNodes.Any(data => data.Node == node);

    /// <summary>
    ///     True when the node is in an alternate data stream of the entry, which moves with the entry, rather than
    ///     in the "_UAEFSDB.___" file or a metafile.
    /// </summary>
    public bool IsAlternateDataStreamNode(UaeMetadataNode node) =>
        !IsFsDbNode(node) && !MetafileNames.ContainsKey(node.NormalName);

    /// <summary>Finds the metadata of another entry than <paramref name="normalName" /> with the Amiga name.</summary>
    public UaeMetadataNode? FindOtherByAmigaName(string amigaName, string? normalName) =>
        Nodes.FirstOrDefault(node => node.AmigaName.Equals(amigaName, StringComparison.OrdinalIgnoreCase) &&
                                     !node.NormalName.Equals(normalName, StringComparison.Ordinal));
}

/// <summary>Reads UAE metadata of a directory through <see cref="IVolume" />, so it works for any volume.</summary>
internal static class UaeMetadataReader
{
    /// <summary>Metadata files larger than this are not metadata and copied like other files.</summary>
    private const long MaxMetadataFileSize = 16 * 1024 * 1024;

    /// <summary>
    ///     Reads the UAE metadata of the current directory of a volume with its entries. Files that can't be read or
    ///     aren't valid metadata are normal files. With <paramref name="readAlternateDataStreams" />, the
    ///     "_UAEFSDB.___" alternate data streams WinUAE writes on NTFS are read when the directory has no
    ///     "_UAEFSDB.___" file (local directories on Windows only), from <paramref name="localDirectory" /> or the
    ///     directory of a <see cref="LocalVolume" />.
    /// </summary>
    public static async Task<UaeDirectoryMetadata> ReadAsync(IVolume volume, IReadOnlyCollection<IEntry> entries,
        bool readAlternateDataStreams, CancellationToken cancellationToken, string? localDirectory = null)
    {
        var items = entries.Where(entry => entry.Type == EntryType.Item).ToList();
        var nodes = new List<UaeMetadataNode>();
        string? fsDbFileName = null;
        var fsDbNodes = new List<UaeFsDbNodeData>();

        var fsDbEntry = items.FirstOrDefault(entry =>
            entry.Name.Equals(FsDbConstants.UaeFsDbFileName, StringComparison.OrdinalIgnoreCase));
        if (fsDbEntry != null && await ReadBytesAsync(volume, fsDbEntry, cancellationToken) is { } fsDbBytes &&
            ReadFsDbNodes(fsDbBytes) is { } readNodes)
        {
            fsDbFileName = fsDbEntry.Name;
            fsDbNodes.AddRange(readNodes);
            nodes.AddRange(readNodes.Select(node => node.Node));
        }
        else if (readAlternateDataStreams && OperatingSystem.IsWindows() &&
                 (localDirectory ?? (volume as LocalVolume)?.LocalPath) is { } localPath)
        {
            nodes.AddRange(ReadAlternateDataStreams(localPath, entries));
        }

        var names = entries.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        var metafileNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items.Where(IsMetafileName))
        {
            var normalName = item.Name[..^MetafileConstants.UaeMetafileExtension.Length];
            if (!names.Contains(normalName) ||
                await ReadBytesAsync(volume, item, cancellationToken) is not { } bytes ||
                ReadMetafile(bytes) is not { } metadata)
            {
                continue;
            }

            metafileNames[normalName] = item.Name;
            if (nodes.All(node => !node.NormalName.Equals(normalName, StringComparison.Ordinal)))
            {
                nodes.Add(new UaeMetadataNode(DecodeMetafileName(normalName), normalName, metadata));
            }
        }

        return nodes.Count == 0 && fsDbFileName == null && metafileNames.Count == 0
            ? UaeDirectoryMetadata.Empty
            : new UaeDirectoryMetadata(nodes, fsDbFileName, fsDbNodes, metafileNames);
    }

    private static bool IsMetafileName(IEntry entry) =>
        entry.Name.Length > MetafileConstants.UaeMetafileExtension.Length &&
        entry.Name.EndsWith(MetafileConstants.UaeMetafileExtension, StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]?> ReadBytesAsync(IVolume volume, IEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.Size > MaxMetadataFileSize)
        {
            return null;
        }

        try
        {
            await using var stream = await volume.OpenRead(entry.Name, cancellationToken);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            return memoryStream.ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Reads the valid nodes of a "_UAEFSDB.___" file (version 1 nodes), null when it's not valid.</summary>
    internal static IReadOnlyList<UaeFsDbNodeData>? ReadFsDbNodes(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length % FsDbConstants.UaeFsDbNodeVersion1Size != 0)
        {
            return null;
        }

        var nodes = new List<UaeFsDbNodeData>();
        try
        {
            for (var offset = 0; offset < bytes.Length; offset += FsDbConstants.UaeFsDbNodeVersion1Size)
            {
                var node = UaeFsDbReader.ReadFromBytes(bytes, offset);
                if (node.Valid == 0 || string.IsNullOrEmpty(node.AmigaName) || string.IsNullOrEmpty(node.NormalName))
                {
                    continue;
                }

                nodes.Add(new UaeFsDbNodeData(CreateNode(node.AmigaName, node.NormalName, node),
                    bytes[offset..(offset + FsDbConstants.UaeFsDbNodeVersion1Size)]));
            }
        }
        catch (ArgumentException)
        {
            return null;
        }

        return nodes;
    }

    /// <summary>Reads the version 2 nodes WinUAE writes as an alternate data stream of each entry on NTFS.</summary>
    private static IEnumerable<UaeMetadataNode> ReadAlternateDataStreams(string localPath,
        IEnumerable<IEntry> entries)
    {
        foreach (var entry in entries)
        {
            var streamPath = string.Concat(Path.Combine(localPath, entry.Name), ":", FsDbConstants.UaeFsDbFileName);
            UaeMetadataNode? node = null;
            try
            {
                if (File.Exists(streamPath))
                {
                    var bytes = File.ReadAllBytes(streamPath);
                    var version = bytes.Length >= FsDbConstants.UaeFsDbNodeVersion2Size
                        ? UaeFsDbNode.NodeVersion.Version2
                        : UaeFsDbNode.NodeVersion.Version1;
                    var fsDbNode = UaeFsDbReader.ReadFromBytes(bytes, 0, version);
                    var amigaName = string.IsNullOrEmpty(fsDbNode.AmigaNameUnicode)
                        ? fsDbNode.AmigaName
                        : fsDbNode.AmigaNameUnicode;
                    if (!string.IsNullOrEmpty(amigaName))
                    {
                        node = CreateNode(amigaName, entry.Name, fsDbNode);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                  or NotSupportedException or ArgumentException)
            {
                // an unreadable stream is no metadata
            }

            if (node != null)
            {
                yield return node;
            }
        }
    }

    private static UaeMetadataNode CreateNode(string amigaName, string normalName, UaeFsDbNode node) =>
        new(amigaName, normalName, new AmigaMetadata((int)(node.Mode & 0xff), node.Comment ?? string.Empty));

    /// <summary>Reads the protection bits and comment of a ".uaem" metafile, null when it's not valid.</summary>
    private static AmigaMetadata? ReadMetafile(byte[] bytes)
    {
        try
        {
            var metafile = UaeMetafileReader.Read(bytes);
            var protectionBits = ProtectionBitsConverter.ParseProtectionBits(metafile.ProtectionBits);
            return new AmigaMetadata(ProtectionBitsConverter.ToProtectionValue(protectionBits),
                metafile.Comment ?? string.Empty);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Amiga name of a name with "%xx" encoded characters, as written by FS-UAE.</summary>
    private static string DecodeMetafileName(string normalName)
    {
        try
        {
            return UaeMetafileHelper.DecodeFilename(normalName);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return normalName;
        }
    }

    /// <summary>
    ///     Removes the metadata of entries no longer in the current directory, e.g. after they are moved: their
    ///     metafiles are deleted and the "_UAEFSDB.___" file is written without their nodes, or deleted when no nodes
    ///     are left, so the directory can be deleted when empty.
    /// </summary>
    public static async Task RemoveMissingAsync(IVolume volume, UaeDirectoryMetadata metadata,
        CancellationToken cancellationToken)
    {
        var names = await volume.ListEntries(cancellationToken)
            .Select(entry => entry.Name)
            .ToHashSetAsync(StringComparer.Ordinal, cancellationToken);

        foreach (var (normalName, metafileName) in metadata.MetafileNames)
        {
            if (!names.Contains(normalName) && names.Contains(metafileName))
            {
                await volume.Delete(metafileName, cancellationToken);
            }
        }

        if (metadata.FsDbFileName is not { } fsDbFileName || !names.Contains(fsDbFileName))
        {
            return;
        }

        var remainingNodes = metadata.FsDbNodes.Where(node => names.Contains(node.Node.NormalName)).ToList();
        if (remainingNodes.Count == metadata.FsDbNodes.Count)
        {
            return;
        }

        if (remainingNodes.Count == 0)
        {
            await volume.Delete(fsDbFileName, cancellationToken);
            return;
        }

        await UaeMetadataWriter.WriteFsDbAsync(volume, fsDbFileName, remainingNodes, cancellationToken);
    }
}

/// <summary>
///     Writes Amiga names, protection bits and comments as UAE metadata in a directory of a volume that doesn't store
///     them, e.g. a local directory. Names that can't be used are given normal names: safe names starting with
///     "__uae___" for UAEFSDB, "%xx" encoded characters for metafiles or invalid characters replaced with "_" without
///     metadata. Existing metadata in the directory is kept, an entry copied again keeps its normal name.
/// </summary>
internal sealed class UaeMetadataWriter
{
    private const string FsDbNamePrefix = "__uae___";
    private const string RandomNameCharacters = "_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int MaxCommentLength = 80;

    private static readonly char[] InvalidNameCharacters = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly Regex WindowsReservedNameRegex = new("^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly UaeMetadataFormat _format;
    private readonly HashSet<string> _names;
    private readonly List<UaeFsDbNodeData> _fsDbNodes;
    private readonly string _fsDbFileName;
    private readonly IReadOnlyDictionary<string, string> _metafileNames;
    private readonly Dictionary<string, UaeMetadataNode> _nodesByAmigaName;
    private readonly bool _hasFsDbFile;
    private bool _isFsDbChanged;

    private UaeMetadataWriter(UaeMetadataFormat format, IEnumerable<IEntry> entries, UaeDirectoryMetadata metadata,
        IEnumerable<UaeMetadataNode> nodes)
    {
        _format = format;
        _names = entries.Select(entry => entry.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _fsDbNodes = metadata.FsDbNodes.ToList();
        _fsDbFileName = metadata.FsDbFileName ?? FsDbConstants.UaeFsDbFileName;
        _hasFsDbFile = metadata.FsDbFileName != null;
        _metafileNames = metadata.MetafileNames;
        _nodesByAmigaName = new Dictionary<string, UaeMetadataNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            _nodesByAmigaName[node.AmigaName] = node;
        }
    }

    /// <summary>Creates a writer for the current directory of a volume, reading its existing metadata.</summary>
    public static async Task<UaeMetadataWriter> CreateAsync(IVolume volume, UaeMetadataFormat format,
        CancellationToken cancellationToken)
    {
        var entries = await volume.ListEntries(cancellationToken).ToListAsync(cancellationToken);
        var metadata = format == UaeMetadataFormat.None
            ? UaeDirectoryMetadata.Empty
            : await UaeMetadataReader.ReadAsync(volume, entries, false, cancellationToken);

        // only metadata of the format written is kept, so an entry copied again keeps its normal name
        var nodes = format == UaeMetadataFormat.UaeFsDb
            ? metadata.FsDbNodes.Select(node => node.Node)
            : metadata.MetafileNames.Keys.Select(metadata.Find).OfType<UaeMetadataNode>();
        return new UaeMetadataWriter(format, entries, metadata, nodes);
    }

    /// <summary>
    ///     Normal name of an entry with an Amiga name in the directory: the name of existing metadata with the Amiga
    ///     name, the Amiga name when it can be used or a new normal name.
    /// </summary>
    public string GetName(string amigaName)
    {
        if (_format != UaeMetadataFormat.None && _nodesByAmigaName.TryGetValue(amigaName, out var node))
        {
            return node.NormalName;
        }

        if (!RequiresNormalName(_format, amigaName))
        {
            _names.Add(amigaName);
            return amigaName;
        }

        var name = _format switch
        {
            UaeMetadataFormat.UaeFsDb => GetUniqueName(FsDbNamePrefix + UaeFsDbNodeHelper.MakeSafeFilename(amigaName)),
            UaeMetadataFormat.UaeMetafile => IsWindowsReservedName(amigaName)
                ? UaeMetafileHelper.EncodeFilename(amigaName)
                : UaeMetafileHelper.EncodeFilenameSpecialChars(amigaName),
            _ => GetUniqueName(ReplaceInvalidCharacters(amigaName))
        };
        _names.Add(name);
        return name;
    }

    /// <summary>
    ///     Removes the metadata of an entry by its normal name, e.g. before it is renamed, so the name can be used
    ///     again. A removed "_UAEFSDB.___" node is written by <see cref="FlushAsync" />, metafiles aren't deleted.
    /// </summary>
    public void Remove(string normalName)
    {
        _names.Remove(normalName);
        foreach (var (amigaName, _) in _nodesByAmigaName
                     .Where(pair => pair.Value.NormalName.Equals(normalName, StringComparison.Ordinal)).ToList())
        {
            _nodesByAmigaName.Remove(amigaName);
        }

        if (_fsDbNodes.RemoveAll(data => data.Node.NormalName.Equals(normalName, StringComparison.Ordinal)) > 0)
        {
            _isFsDbChanged = true;
        }
    }

    /// <summary>
    ///     Stores the Amiga name, protection bits and comment of an entry written with a normal name in the current
    ///     directory of the volume. Metafiles are written right away, the "_UAEFSDB.___" file by
    ///     <see cref="FlushAsync" />. Entries without metadata whose name can be used need none.
    /// </summary>
    public async Task SetAsync(IVolume volume, string amigaName, string normalName, AmigaMetadata? metadata,
        DateTime? modifiedDate, CancellationToken cancellationToken)
    {
        metadata ??= new AmigaMetadata(0, string.Empty);
        var comment = metadata.Comment.Length > MaxCommentLength
            ? metadata.Comment[..MaxCommentLength]
            : metadata.Comment;
        var hasNode = _nodesByAmigaName.TryGetValue(amigaName, out var existingNode);
        var isNeeded = !amigaName.Equals(normalName, StringComparison.Ordinal) || !metadata.IsDefault;

        switch (_format)
        {
            case UaeMetadataFormat.UaeFsDb:
            {
                if (!isNeeded && !hasNode)
                {
                    return;
                }

                var fsDbNode = new UaeFsDbNode
                {
                    Version = UaeFsDbNode.NodeVersion.Version1,
                    Valid = 1,
                    Mode = (uint)metadata.ProtectionBits,
                    AmigaName = amigaName,
                    NormalName = normalName,
                    Comment = comment
                };
                var node = new UaeMetadataNode(amigaName, normalName, metadata with { Comment = comment });
                var nodeData = new UaeFsDbNodeData(node, UaeFsDbWriter.Build(fsDbNode));
                var index = existingNode == null ? -1 : _fsDbNodes.FindIndex(data => data.Node == existingNode);
                if (index >= 0)
                {
                    _fsDbNodes[index] = nodeData;
                }
                else
                {
                    _fsDbNodes.Add(nodeData);
                }

                _nodesByAmigaName[amigaName] = node;
                _isFsDbChanged = true;
                break;
            }
            case UaeMetadataFormat.UaeMetafile:
            {
                if (!isNeeded && !_metafileNames.ContainsKey(normalName))
                {
                    return;
                }

                // Amiga dates are local time
                var date = modifiedDate ?? DateTime.UtcNow;
                var metafile = new UaeMetafile
                {
                    ProtectionBits = EntryFormatter
                        .FormatProtectionBits(ProtectionBitsConverter.ToProtectionBits(metadata.ProtectionBits))
                        .ToLowerInvariant(),
                    Date = date.Kind == DateTimeKind.Local
                        ? date
                        : DateTime.SpecifyKind(date, DateTimeKind.Utc).ToLocalTime(),
                    Comment = comment
                };
                var bytes = UaeMetafileWriter.Build(metafile);
                await using var stream = await volume.OpenWrite(normalName + MetafileConstants.UaeMetafileExtension,
                    cancellationToken);
                await stream.WriteAsync(bytes, cancellationToken);
                _nodesByAmigaName[amigaName] = new UaeMetadataNode(amigaName, normalName, metadata);
                break;
            }
        }
    }

    /// <summary>
    ///     Writes the "_UAEFSDB.___" file in the current directory of the volume when nodes were set or removed, or
    ///     deletes it when no nodes are left.
    /// </summary>
    public async Task FlushAsync(IVolume volume, CancellationToken cancellationToken)
    {
        if (!_isFsDbChanged)
        {
            return;
        }

        if (_fsDbNodes.Count > 0)
        {
            await WriteFsDbAsync(volume, _fsDbFileName, _fsDbNodes, cancellationToken);
        }
        else if (_hasFsDbFile)
        {
            await volume.Delete(_fsDbFileName, cancellationToken);
        }

        _isFsDbChanged = false;
    }

    internal static async Task WriteFsDbAsync(IVolume volume, string fsDbFileName,
        IEnumerable<UaeFsDbNodeData> nodes, CancellationToken cancellationToken)
    {
        await using var stream = await volume.OpenWrite(fsDbFileName, cancellationToken);
        foreach (var node in nodes)
        {
            await stream.WriteAsync(node.Bytes, cancellationToken);
        }
    }

    /// <summary>True when an Amiga name can't be used as it is with the format, so it needs a normal name.</summary>
    internal static bool RequiresNormalName(UaeMetadataFormat format, string name) =>
        (OperatingSystem.IsWindows() && IsWindowsReservedName(name)) || format switch
        {
            UaeMetadataFormat.UaeFsDb => UaeFsDbNodeHelper.HasSpecialFilenameChars(name),
            UaeMetadataFormat.UaeMetafile => UaeMetafileHelper.HasSpecialFilenameChars(name),
            _ => !ReplaceInvalidCharacters(name).Equals(name, StringComparison.Ordinal)
        };

    /// <summary>True when an Amiga name is a Windows reserved name, e.g. "CON" or "nul.txt".</summary>
    internal static bool IsWindowsReservedName(string name) =>
        WindowsReservedNameRegex.IsMatch(name) || WindowsReservedNameRegex.IsMatch(Path.GetFileNameWithoutExtension(name));

    /// <summary>Name with invalid characters replaced with "_" and Windows reserved names prefixed with "_".</summary>
    private static string ReplaceInvalidCharacters(string name)
    {
        if (name is "." or "..")
        {
            return new string('_', name.Length);
        }

        var characters = name
            .Select(character => character < 32 || InvalidNameCharacters.Contains(character) ? '_' : character)
            .ToArray();
        var replacedName = new string(characters);
        return OperatingSystem.IsWindows() && IsWindowsReservedName(replacedName) ? "_" + replacedName : replacedName;
    }

    /// <summary>A name not used in the directory, with random characters added when the name is used.</summary>
    private string GetUniqueName(string name)
    {
        var uniqueName = name;
        while (_names.Contains(uniqueName))
        {
            uniqueName = name + new string(Enumerable.Range(0, 8)
                .Select(_ => RandomNameCharacters[Random.Shared.Next(RandomNameCharacters.Length)]).ToArray());
        }

        return uniqueName;
    }
}

/// <summary>
///     Keeps the UAE metadata of directories up to date when entries are renamed, moved or deleted in volumes not
///     storing Amiga metadata, e.g. local directories, so the entries keep their Amiga names, protection bits and
///     comments and no metadata is left of entries that are gone.
/// </summary>
internal static class UaeMetadataUpdater
{
    /// <summary>
    ///     Reads the UAE metadata of the current directory of a volume when it's handled: the format isn't
    ///     <see cref="UaeMetadataFormat.None" /> and the volume doesn't store Amiga metadata itself. Empty otherwise.
    /// </summary>
    public static async Task<UaeDirectoryMetadata> ReadAsync(IVolume volume, UaeMetadataFormat format,
        bool readAlternateDataStreams, string? localDirectory, CancellationToken cancellationToken)
    {
        if (format == UaeMetadataFormat.None || await volume.SupportsAmigaMetadata(cancellationToken))
        {
            return UaeDirectoryMetadata.Empty;
        }

        var entries = await volume.ListEntries(cancellationToken).ToListAsync(cancellationToken);
        return await UaeMetadataReader.ReadAsync(volume, entries, readAlternateDataStreams, cancellationToken,
            localDirectory);
    }

    /// <summary>
    ///     True when a name can be used as it is in the directory: without characters other file systems can't store
    ///     (local directories on other systems than Windows only can't store "/"), not a Windows reserved name.
    /// </summary>
    public static bool IsUsableName(string name, string? localDirectory) =>
        localDirectory != null && !OperatingSystem.IsWindows()
            ? name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            : !UaeMetadataWriter.RequiresNormalName(UaeMetadataFormat.None, name);

    /// <summary>
    ///     Renames an entry in the current directory to a new Amiga name, like renaming it on an Amiga with UAE: the
    ///     entry gets a normal name for the Amiga name (the name itself when it can be used) and its protection bits
    ///     and comment are kept. Entries with metadata in the "_UAEFSDB.___" file or a metafile keep using it, other
    ///     entries get metadata in <paramref name="format" /> (WinUAE's alternate data streams are replaced). Returns
    ///     the normal name, or null when the entry has no metadata and the name can be used, so it's renamed as usual.
    /// </summary>
    public static async Task<string?> RenameAsync(IVolume volume, IEntry entry, string newName,
        UaeMetadataFormat format, string? localDirectory, CancellationToken cancellationToken)
    {
        var metadata = await ReadAsync(volume, format, true, localDirectory, cancellationToken);
        var node = metadata.Find(entry.Name) is { } foundNode &&
                   foundNode.NormalName.Equals(entry.Name, StringComparison.Ordinal)
            ? foundNode
            : null;
        if (node == null && IsUsableName(newName, localDirectory))
        {
            return null;
        }

        if (newName.Contains(':'))
        {
            throw new IOException($"Invalid name '{newName}'");
        }

        if (metadata.FindOtherByAmigaName(newName, entry.Name) is { } otherNode)
        {
            throw new IOException($"'{otherNode.AmigaName}' already exists");
        }

        var metafileName = metadata.MetafileNames.GetValueOrDefault(entry.Name);
        var writerFormat = node != null && metadata.IsFsDbNode(node)
            ? UaeMetadataFormat.UaeFsDb
            : metafileName != null
                ? UaeMetadataFormat.UaeMetafile
                : format;
        var writer = await UaeMetadataWriter.CreateAsync(volume, writerFormat, cancellationToken);
        writer.Remove(entry.Name);
        var normalName = writer.GetName(newName);
        if (!normalName.Equals(entry.Name, StringComparison.Ordinal))
        {
            await volume.Rename(entry.Name, normalName, cancellationToken);
            if (metafileName != null)
            {
                await volume.Delete(metafileName, cancellationToken);
            }
        }

        if (node != null && metadata.IsAlternateDataStreamNode(node) && localDirectory != null)
        {
            DeleteAlternateDataStream(Path.Combine(localDirectory, normalName));
        }

        await writer.SetAsync(volume, newName, normalName, node?.Metadata, entry.ModifiedDate, cancellationToken);
        await writer.FlushAsync(volume, cancellationToken);
        return normalName;
    }

    /// <summary>
    ///     Sets the protection bits and comment of an entry with UAE metadata in the current directory, keeping its
    ///     Amiga name. Entries with metadata in the "_UAEFSDB.___" file or a metafile keep using it, other entries get
    ///     metadata in <paramref name="format" /> (WinUAE's alternate data streams are replaced). Throws
    ///     <see cref="IOException" /> when the entry has no UAE metadata.
    /// </summary>
    public static async Task SetMetadataAsync(IVolume volume, IEntry entry, AmigaMetadata amigaMetadata,
        UaeMetadataFormat format, string? localDirectory, CancellationToken cancellationToken)
    {
        var metadata = await ReadAsync(volume, format, true, localDirectory, cancellationToken);
        if (metadata.Find(entry.Name) is not { } node || !node.NormalName.Equals(entry.Name, StringComparison.Ordinal))
        {
            throw new IOException($"'{entry.Name}' has no UAE metadata");
        }

        var writerFormat = metadata.IsFsDbNode(node)
            ? UaeMetadataFormat.UaeFsDb
            : metadata.MetafileNames.ContainsKey(entry.Name)
                ? UaeMetadataFormat.UaeMetafile
                : format;
        var writer = await UaeMetadataWriter.CreateAsync(volume, writerFormat, cancellationToken);
        await writer.SetAsync(volume, node.AmigaName, entry.Name, amigaMetadata, entry.ModifiedDate,
            cancellationToken);
        await writer.FlushAsync(volume, cancellationToken);
        if (metadata.IsAlternateDataStreamNode(node) && localDirectory != null)
        {
            DeleteAlternateDataStream(Path.Combine(localDirectory, entry.Name));
        }
    }

    /// <summary>
    ///     Moves the metadata of entries moved from the current directory of the source volume to the current
    ///     directory of the destination volume with their names (e.g. with <see cref="File.Move(string, string)" />):
    ///     "_UAEFSDB.___" nodes are written to the destination's "_UAEFSDB.___" file and metafiles are moved (WinUAE's
    ///     alternate data streams move with the entries). The metadata is then removed from the source.
    /// </summary>
    public static async Task MoveAsync(IVolume source, UaeDirectoryMetadata sourceMetadata, IVolume destination,
        IEnumerable<IEntry> movedEntries, CancellationToken cancellationToken)
    {
        UaeMetadataWriter? writer = null;
        try
        {
            foreach (var entry in movedEntries)
            {
                if (sourceMetadata.MetafileNames.GetValueOrDefault(entry.Name) is { } metafileName)
                {
                    await MoveItemAsync(source, destination, metafileName, cancellationToken);
                }

                if (sourceMetadata.Find(entry.Name) is { } node && sourceMetadata.IsFsDbNode(node))
                {
                    writer ??= await UaeMetadataWriter.CreateAsync(destination, UaeMetadataFormat.UaeFsDb,
                        cancellationToken);
                    await writer.SetAsync(destination, node.AmigaName, node.NormalName, node.Metadata,
                        entry.ModifiedDate, cancellationToken);
                }
            }
        }
        catch
        {
            // the metadata of the entries moved so far is kept, also when moving fails
            try
            {
                if (writer != null)
                {
                    await writer.FlushAsync(destination, CancellationToken.None);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                                  or NotSupportedException)
            {
                // keep the error that stopped the move rather than the failed metadata
            }

            await TryRemoveMissingAsync(source, sourceMetadata);
            throw;
        }

        if (writer != null)
        {
            await writer.FlushAsync(destination, cancellationToken);
        }

        await UaeMetadataReader.RemoveMissingAsync(source, sourceMetadata, cancellationToken);
    }

    /// <summary>Removes the metadata of entries no longer in the directory, ignoring errors.</summary>
    public static async Task TryRemoveMissingAsync(IVolume volume, UaeDirectoryMetadata metadata)
    {
        try
        {
            await UaeMetadataReader.RemoveMissingAsync(volume, metadata, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            // keep the error that stopped the operation rather than the failed metadata
        }
    }

    private static async Task MoveItemAsync(IVolume source, IVolume destination, string name,
        CancellationToken cancellationToken)
    {
        await using (var sourceStream = await source.OpenRead(name, cancellationToken))
        await using (var destinationStream = await destination.OpenWrite(name, cancellationToken))
        {
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }

        await source.Delete(name, cancellationToken);
    }

    private static void DeleteAlternateDataStream(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var streamPath = string.Concat(path, ":", FsDbConstants.UaeFsDbFileName);
        if (File.Exists(streamPath))
        {
            File.Delete(streamPath);
        }
    }
}
