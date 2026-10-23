using System.Globalization;
using Hst.Commander.Core.Volumes;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>A line of the properties dialog, e.g. "Type" and "Text Document (.txt)".</summary>
public sealed record PropertyLine(string Label, string Value);

/// <summary>
///     Properties of entries shown in the properties dialog (Alt+Enter) of both front-ends, with similar details as the
///     properties of Windows Explorer: type, location, size, contents, dates and attributes of files and directories,
///     or the type, file system and space of drives. Created by <see cref="NavigationContext.GetProperties" />.
///     The size and contents of the entries are counted in the background with
///     <see cref="NavigationContext.CountContentsAsync" /> when <see cref="HasContents" /> is true.
/// </summary>
public sealed class EntryProperties
{
    /// <summary>Value of the lines counted by <see cref="NavigationContext.CountContentsAsync" /> until counted.</summary>
    public const string Calculating = "Calculating...";

    private readonly IReadOnlyList<IReadOnlyList<Field>> _sections;

    private EntryProperties(string name, IReadOnlyList<IReadOnlyList<Field>> sections, double? usedFraction = null)
    {
        Name = name;
        _sections = sections;
        UsedFraction = usedFraction;
    }

    /// <summary>Name shown at the top, e.g. "a.txt" or "3 Files, 2 Folders".</summary>
    public string Name { get; }

    /// <summary>Fraction of the space used of drives, between 0 and 1. Null for files and directories.</summary>
    public double? UsedFraction { get; }

    /// <summary>True when lines show contents counted by <see cref="NavigationContext.CountContentsAsync" />.</summary>
    public bool HasContents => _sections.Any(section => section.Any(line => line.Contents != ContentsField.None));

    /// <summary>
    ///     Sections of lines shown separated in the dialog, with the contents counted so far: lines showing the size or
    ///     contents are "Calculating..." when <paramref name="contents" /> is null. The number of lines doesn't depend
    ///     on the contents, so the dialogs only update the values.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<PropertyLine>> GetSections(EntryContents? contents = null) =>
        _sections
            .Select(section => (IReadOnlyList<PropertyLine>)section
                .Select(field => new PropertyLine(field.Label, GetValue(field, contents)))
                .ToList())
            .ToList();

    private static string GetValue(Field field, EntryContents? contents) => field.Contents switch
    {
        ContentsField.None => field.Value,
        _ when contents == null => Calculating,
        ContentsField.Size => SizeFormatter.FormatWithBytes(contents.Size),
        ContentsField.SizeOnDisk => contents.SizeOnDisk is { } sizeOnDisk
            ? SizeFormatter.FormatWithBytes(sizeOnDisk)
            : "Unknown",
        _ => FormatCount(contents.Files, contents.Folders)
    };

    /// <summary>Number of files and folders like Windows Explorer, e.g. "1 File, 2 Folders".</summary>
    public static string FormatCount(int files, int folders) => string.Create(CultureInfo.InvariantCulture,
        $"{files:N0} {(files == 1 ? "File" : "Files")}, {folders:N0} {(folders == 1 ? "Folder" : "Folders")}");

    /// <summary>Formats a date like Windows Explorer in the current culture; unspecified kinds are UTC.</summary>
    public static string FormatDate(DateTime date)
    {
        var localDate = date.Kind == DateTimeKind.Local
            ? date
            : DateTime.SpecifyKind(date, DateTimeKind.Utc).ToLocalTime();
        return localDate.ToString("F", CultureInfo.CurrentCulture);
    }

    /// <summary>
    ///     Creates the properties of entries in a location.
    /// </summary>
    /// <param name="entries">Entries in the location, at least one.</param>
    /// <param name="location">Path of the location, e.g. <see cref="NavigationContext.CurrentPath" />.</param>
    /// <param name="localDirectory">Local directory of the location, null inside images and other volumes.</param>
    /// <param name="isDriveList">True when the entries are local drives.</param>
    /// <param name="uaeMetadata">UAE metadata of a single entry, shown with its Amiga name.</param>
    internal static EntryProperties Create(IReadOnlyList<IEntry> entries, string location, string? localDirectory,
        bool isDriveList, UaeMetadataNode? uaeMetadata = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            throw new ArgumentException("No entries", nameof(entries));
        }

        if (isDriveList)
        {
            return CreateForDrives(entries);
        }

        return entries.Count == 1
            ? CreateForEntry(entries[0], location, localDirectory, uaeMetadata)
            : CreateForEntries(entries, location, localDirectory);
    }

    /// <summary>
    ///     Amiga protection bits as shown by the Amiga List command, e.g. "----rwed": "hspa" when set, "rwed" when
    ///     allowed (not set in the protection bits stored on disk).
    /// </summary>
    public static string FormatProtectionBits(int protectionBits)
    {
        const string letters = "hsparwed";
        var characters = new char[letters.Length];
        for (var index = 0; index < letters.Length; index++)
        {
            var bit = letters.Length - 1 - index;
            var isSet = (protectionBits & (1 << bit)) != 0;
            characters[index] = isSet == bit >= 4 ? letters[index] : '-';
        }

        return new string(characters);
    }

    private static EntryProperties CreateForEntry(IEntry entry, string location, string? localDirectory,
        UaeMetadataNode? uaeMetadata)
    {
        var isContainer = entry.Type == EntryType.Container;
        var info = GetLocalInfo(entry, localDirectory);

        var details = entry.Details;
        var typeSection = new List<Field> { new("Type", GetTypeName(entry)) };
        if (info?.LinkTarget is { } linkTarget)
        {
            typeSection.Add(new Field("Link target", linkTarget));
        }

        var sizeSection = new List<Field> { new("Location", location) };
        AddSizeFields(sizeSection, localDirectory, isContainer);

        var dateSection = new List<Field>();
        if (info != null)
        {
            dateSection.Add(new Field("Created", FormatDate(info.CreationTimeUtc)));
            dateSection.Add(new Field("Modified", FormatDate(info.LastWriteTimeUtc)));
            dateSection.Add(new Field("Accessed", FormatDate(info.LastAccessTimeUtc)));
        }
        else if (entry.ModifiedDate is { } modifiedDate)
        {
            dateSection.Add(new Field("Modified", FormatDate(modifiedDate)));
        }

        var attributeSection = new List<Field>();
        if (info != null)
        {
            attributeSection.Add(new Field("Attributes", FileSystemDetails.FormatAttributes(info.Attributes)));
            if (!OperatingSystem.IsWindows())
            {
                attributeSection.Add(new Field("Permissions", FileSystemDetails.FormatUnixMode(info.UnixFileMode)));
            }
        }

        // Amiga entries and entries with UAE metadata, e.g. copied from an Amiga volume to a local directory
        var amigaSection = new List<Field>();
        if (uaeMetadata != null)
        {
            amigaSection.Add(new Field("Amiga name", uaeMetadata.AmigaName));
        }

        if ((entry.AmigaMetadata ?? uaeMetadata?.Metadata) is { } amigaMetadata)
        {
            amigaSection.Add(new Field("Protection bits", FormatProtectionBits(amigaMetadata.ProtectionBits)));
            amigaSection.Add(new Field("Comment", amigaMetadata.Comment));
        }

        var detailSections = details?.Sections
            .Select(section => section.Select(CreateField).ToList())
            .ToArray() ?? [];
        return new EntryProperties(entry.DisplayName,
            WithoutEmpty([typeSection, ..detailSections, sizeSection, dateSection, attributeSection, amigaSection]),
            details?.UsedFraction is { } usedFraction ? Math.Clamp(usedFraction, 0, 1) : null);
    }

    /// <summary>Type of an entry: the type of its details (e.g. "Amiga partition") or the file type.</summary>
    private static string GetTypeName(IEntry entry) =>
        entry.Details?.TypeName ?? FileSystemDetails.GetTypeName(entry.Name, entry.Type == EntryType.Container);

    private static Field CreateField(EntryProperty property) => new(property.Label,
        property.Bytes is { } bytes ? SizeFormatter.FormatWithBytes(bytes) : property.Value);

    private static EntryProperties CreateForEntries(IReadOnlyList<IEntry> entries, string location,
        string? localDirectory)
    {
        var files = entries.Count(entry => entry.Type != EntryType.Container);
        var folders = entries.Count - files;
        var typeNames = entries
            .Select(GetTypeName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var typeSection = new List<Field>
        {
            new("Type", typeNames.Count == 1 ? $"All of type {typeNames[0]}" : "Multiple types")
        };
        var sizeSection = new List<Field> { new("Location", $"All in {location}") };
        AddSizeFields(sizeSection, localDirectory, true);

        return new EntryProperties(FormatCount(files, folders), WithoutEmpty(typeSection, sizeSection));
    }

    private static void AddSizeFields(List<Field> section, string? localDirectory, bool contains)
    {
        section.Add(new Field("Size", string.Empty, ContentsField.Size));
        // the size on disk is only known for local files on Windows
        if (localDirectory != null && OperatingSystem.IsWindows())
        {
            section.Add(new Field("Size on disk", string.Empty, ContentsField.SizeOnDisk));
        }

        if (contains)
        {
            section.Add(new Field("Contains", string.Empty, ContentsField.Contains));
        }
    }

    private static FileSystemInfo? GetLocalInfo(IEntry entry, string? localDirectory)
    {
        if (localDirectory == null)
        {
            return null;
        }

        var path = Path.Combine(localDirectory, entry.Name);
        FileSystemInfo info = entry.Type == EntryType.Container ? new DirectoryInfo(path) : new FileInfo(path);
        return info.Exists ? info : null;
    }

    private static EntryProperties CreateForDrives(IReadOnlyList<IEntry> entries)
    {
        var drives = entries.Select(entry => new DriveInfo(entry.Name)).ToList();
        var readyDrives = drives.Where(IsReady).ToList();
        var typeNames = drives.Select(drive => FileSystemDetails.GetDriveTypeName(drive.DriveType)).Distinct().ToList();

        var typeSection = new List<Field>
        {
            new("Type", drives.Count == 1
                ? typeNames[0]
                : typeNames.Count == 1 ? $"All of type {typeNames[0]}" : "Multiple types")
        };
        if (drives.Count == 1)
        {
            typeSection.Add(new Field("Drive", drives[0].Name));
            typeSection.Add(readyDrives.Count == 1
                ? new Field("File system", readyDrives[0].DriveFormat)
                : new Field("Status", "Not ready"));
        }

        var spaceSection = new List<Field>();
        double? usedFraction = null;
        if (readyDrives.Count > 0)
        {
            var totalSize = readyDrives.Sum(drive => drive.TotalSize);
            var freeSpace = readyDrives.Sum(drive => drive.TotalFreeSpace);
            spaceSection.Add(new Field("Used space", SizeFormatter.FormatWithBytes(totalSize - freeSpace)));
            spaceSection.Add(new Field("Free space", SizeFormatter.FormatWithBytes(freeSpace)));
            spaceSection.Add(new Field("Capacity", SizeFormatter.FormatWithBytes(totalSize)));
            usedFraction = totalSize > 0 ? Math.Clamp((double)(totalSize - freeSpace) / totalSize, 0, 1) : null;
        }

        var name = drives.Count == 1
            ? GetDriveName(drives[0])
            : $"{drives.Count.ToString(CultureInfo.InvariantCulture)} Drives";
        return new EntryProperties(name, WithoutEmpty(typeSection, spaceSection), usedFraction);
    }

    /// <summary>Volume label and drive like Windows Explorer, e.g. "Windows (C:)", or the drive name.</summary>
    private static string GetDriveName(DriveInfo drive)
    {
        // a root like "C:\" isn't trimmed by Path.TrimEndingDirectorySeparator
        var driveName = drive.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var label = IsReady(drive) ? GetVolumeLabel(drive) : null;
        // other systems than Windows use the mount point as label
        return string.IsNullOrWhiteSpace(label) || label == drive.Name ? drive.Name : $"{label} ({driveName})";
    }

    private static string? GetVolumeLabel(DriveInfo drive)
    {
        try
        {
            return drive.VolumeLabel;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsReady(DriveInfo drive)
    {
        try
        {
            return drive.IsReady;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IReadOnlyList<IReadOnlyList<Field>> WithoutEmpty(params List<Field>[] sections) =>
        sections.Where(section => section.Count > 0).ToList();

    private enum ContentsField
    {
        None,
        Size,
        SizeOnDisk,
        Contains
    }

    private sealed record Field(string Label, string Value, ContentsField Contents = ContentsField.None);
}
