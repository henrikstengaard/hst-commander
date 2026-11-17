using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.DirectoryOpus;

/// <summary>
/// Plugin adding the Directory Opus 4 theme: the colors of Directory Opus 4 on the Amiga, a bank of buttons in columns
/// the user can change in the settings, and parent buttons at the outer side of the panes. Included with Hst Commander
/// by default.
/// </summary>
public class DirectoryOpusPlugin : IPlugin
{
    private readonly DirectoryOpus4Theme _theme = new();

    public string Name => "DirectoryOpus";

    public IEnumerable<ITheme> Themes => [_theme];

    public void Initialize(IPluginHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _theme.Directory = host.GetPluginDirectory(Name);
    }
}
