using Hst.Commander.Abstractions;
using Hst.Commander.Plugins.Zip;

namespace Hst.Commander.Core;

/// <summary>
///     Plugins extending Hst Commander with volume providers and file volume providers (see
///     <see cref="VolumeProviders" />). Plugins included with Hst Commander are listed here; plugins found at startup are
///     intended to be added after them.
/// </summary>
public static class Plugins
{
    public static IReadOnlyList<IPlugin> All { get; } = [new ZipPlugin()];
}
