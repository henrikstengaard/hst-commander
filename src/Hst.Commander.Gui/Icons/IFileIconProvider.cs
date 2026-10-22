using Hst.Commander.Abstractions;

namespace Hst.Commander.Gui.Icons;

/// <summary>
///     Icons the host OS has registered for file types, folders and drives.
/// </summary>
internal interface IFileIconProvider
{
    /// <summary>True when the provider must be called on the UI thread, otherwise it is called on a background thread.</summary>
    bool RequiresUiThread { get; }

    /// <summary>Icon of files with the extension (including the dot, empty for none), null when unknown.</summary>
    IconImage? GetFileTypeIcon(string extension, int size);

    /// <summary>Generic folder icon, null when unknown.</summary>
    IconImage? GetFolderIcon(int size);

    /// <summary>Generic drive icon or the icon of the drive at the root path, null when unknown.</summary>
    IconImage? GetDriveIcon(string rootPath, int size);

    /// <summary>True when the OS shows files with the name with an icon of their own, e.g. a Windows shortcut.</summary>
    bool HasFileIcon(string name);

    /// <summary>Icon of a local file with an icon of its own (see <see cref="HasFileIcon" />), null when unknown.</summary>
    IconImage? GetFileIcon(string path, int size);
}
