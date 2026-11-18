namespace Hst.Commander.Abstractions;

/// <summary>
/// Interface for adding tabs to the properties dialog (Alt+Enter) of a single entry, e.g. the comment and the
/// tooltypes of Amiga entries. Each tab shows a text that can be added, edited and deleted. Changes are only saved
/// when the user clicks Save in the dialog, by calling <see cref="PropertiesTab.Save" /> of the changed tabs.
/// </summary>
public interface IPropertiesTabProvider
{
    /// <summary>
    /// Name of the tab provider, e.g. "Amiga".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Creates the tabs of an entry, shown after the General tab in the order returned. Called on the thread using the
    /// pane's volume (<see cref="IPropertiesContext.Volume" />), so it must not keep using it in the background.
    /// </summary>
    /// <param name="context">The entry, its location and what can be changed of it.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>Tabs of the entry, empty when it has none.</returns>
    Task<IReadOnlyList<PropertiesTab>> GetTabs(IPropertiesContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The entry the properties dialog is shown for and its location, given to
/// <see cref="IPropertiesTabProvider.GetTabs" /> and used by <see cref="PropertiesTab.Save" />.
/// </summary>
public interface IPropertiesContext
{
    /// <summary>
    /// The pane's volume, with the directory of <see cref="Entry" /> as its current directory. Only use it in
    /// <see cref="IPropertiesTabProvider.GetTabs" /> and <see cref="PropertiesTab.Save" />.
    /// </summary>
    IVolume Volume { get; }

    /// <summary>
    /// The entry the properties are shown for.
    /// </summary>
    IEntry Entry { get; }

    /// <summary>
    /// The icon file holding the icon of <see cref="Entry" /> in the same directory, found by an icon provider (see
    /// <see cref="IIconProvider.GetIconFileName" />), e.g. "file.info" for "file" and for "file.info" itself. Null
    /// when it has none.
    /// </summary>
    IEntry? IconFile { get; }

    /// <summary>
    /// True when files in the current directory of <see cref="Volume" /> can be written, e.g. not inside a read-only
    /// image.
    /// </summary>
    bool CanWrite { get; }

    /// <summary>
    /// Amiga protection bits and comment of <see cref="Entry" />: those of the entry (<see cref="IEntry.AmigaMetadata" />)
    /// or those stored as UAE metadata, e.g. for an Amiga file copied to a local directory. Null when it has none.
    /// Updated when changed with <see cref="SetAmigaMetadata" />, also by the protection bits of the General tab,
    /// which are saved before the tabs.
    /// </summary>
    AmigaMetadata? AmigaMetadata { get; }

    /// <summary>
    /// True when <see cref="SetAmigaMetadata" /> can change the Amiga metadata of <see cref="Entry" />, also when
    /// <see cref="AmigaMetadata" /> is null and it's created as UAE metadata, e.g. in a local directory.
    /// </summary>
    bool CanSetAmigaMetadata { get; }

    /// <summary>
    /// Changes the Amiga protection bits and comment of <see cref="Entry" />, in the volume (e.g. an Amiga file system)
    /// or in its UAE metadata, which is created when it has none and removed when the protection bits are "----rwed"
    /// and there is no comment. Throws <see cref="NotSupportedException" /> when <see cref="CanSetAmigaMetadata" /> is
    /// false.
    /// </summary>
    Task SetAmigaMetadata(AmigaMetadata metadata, CancellationToken cancellationToken = default);
}

/// <summary>
/// A tab of the properties dialog added by a <see cref="IPropertiesTabProvider" />, showing a text of the entry, e.g.
/// its comment. Without a text the tab shows <see cref="NoTextMessage" /> and an add button showing an empty text
/// field; with a text it shows the text in a text field and a delete button. Both buttons are only enabled when the
/// text can be changed (<see cref="Save" /> isn't null).
/// </summary>
/// <param name="title">Title of the tab, e.g. "Comment".</param>
/// <param name="text">The text, null when there is none.</param>
public sealed class PropertiesTab(string title, string? text)
{
    /// <summary>Title of the tab, e.g. "Comment".</summary>
    public string Title { get; } = title;

    /// <summary>The text, null when there is none. Lines of multi-line texts are separated by "\n".</summary>
    public string? Text { get; } = text;

    /// <summary>True when the text has several lines, e.g. one tooltype per line.</summary>
    public bool IsMultiline { get; init; }

    /// <summary>Maximum length of the text (of each line when multi-line), 0 for no limit.</summary>
    public int MaxLength { get; init; }

    /// <summary>Message shown when there is no text, e.g. "No comment".</summary>
    public string NoTextMessage { get; init; } = "None";

    /// <summary>Text of the button adding a text, e.g. "Add comment".</summary>
    public string AddText { get; init; } = "Add";

    /// <summary>Text of the button deleting the text, e.g. "Delete comment".</summary>
    public string DeleteText { get; init; } = "Delete";

    /// <summary>
    /// Saves a changed text when the user clicks Save, given the new text (lines separated by "\n"), null when deleted
    /// or empty. Called on the thread using the pane's volume after the General tab is saved, only when the text
    /// changed. Null when the text can't be changed, e.g. inside a read-only image.
    /// </summary>
    public Func<string?, CancellationToken, Task>? Save { get; init; }

    /// <summary>True when the text can be changed.</summary>
    public bool CanChange => Save != null;
}
