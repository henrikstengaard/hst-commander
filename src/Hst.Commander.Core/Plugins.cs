using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Amiga;
using Hst.Commander.Plugins.LhaLzx;
using Hst.Commander.Plugins.Zip;

namespace Hst.Commander.Core;

/// <summary>
///     Plugins extending Hst Commander with volume providers and file volume providers (see
///     <see cref="VolumeProviders" />): the plugins included with Hst Commander followed by the plugins found in the
///     plugin directories with <see cref="Load" /> at startup.
/// </summary>
public static class Plugins
{
    private static readonly Lock LoadLock = new();
    private static IReadOnlyList<IPlugin>? _loaded;
    private static PluginLoadResult? _loadResult;

    /// <summary>Plugins included with Hst Commander.</summary>
    public static IReadOnlyList<IPlugin> BuiltIn { get; } =
        [new ZipPlugin(), new LhaLzxPlugin(), new AmigaPlugin()];

    /// <summary>All plugins: the built-in plugins and, once loaded, the plugins found in the plugin directories.</summary>
    public static IReadOnlyList<IPlugin> All => _loaded ?? BuiltIn;

    /// <summary>
    ///     Loads the plugins in <see cref="PluginLoader.Directories" /> once, called by the front-ends at startup before
    ///     the panes are created. Returns the error messages of plugins that failed to load, shown to the user.
    /// </summary>
    public static IReadOnlyList<string> Load()
    {
        lock (LoadLock)
        {
            if (_loadResult == null)
            {
                _loadResult = PluginLoader.Load(PluginLoader.Directories, BuiltIn);
                _loaded = [..BuiltIn, .._loadResult.Plugins];
            }

            return _loadResult.Errors;
        }
    }

    /// <summary>Error message shown to the user for plugins that failed to load, null when none failed.</summary>
    public static string? FormatErrors(IReadOnlyList<string> errors) =>
        errors.Count == 0 ? null : string.Join(Environment.NewLine, errors);
}
