using System.Reflection;
using System.Runtime.Loader;
using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     Result of loading plugins: the plugins created and an error message for each plugin that failed to load.
/// </summary>
public sealed record PluginLoadResult(IReadOnlyList<IPlugin> Plugins, IReadOnlyList<string> Errors);

/// <summary>
///     Finds and loads plugins from plugin directories. A plugin is an assembly with public classes implementing
///     <see cref="IPlugin" /> with a parameterless constructor, either:
///     <list type="bullet">
///         <item>
///             in a directory of its own named like the assembly, e.g. "plugins/MyPlugin/MyPlugin.dll", next to its
///             dependencies (resolved with its .deps.json when present), or
///         </item>
///         <item>a single assembly in the plugin directory, e.g. "plugins/MyPlugin.dll".</item>
///     </list>
///     Each plugin assembly is loaded in an <see cref="AssemblyLoadContext" /> of its own, sharing
///     <c>Hst.Commander.Abstractions</c> with Hst Commander so its interfaces are the same types. A plugin built for
///     another plugin API version than <see cref="ApiVersion" /> (see <see cref="IsCompatible" />) isn't loaded.
/// </summary>
public static class PluginLoader
{
    /// <summary>Name of the plugin directories.</summary>
    public const string DirectoryName = "plugins";

    private static readonly string SharedAssemblyName = typeof(IPlugin).Assembly.GetName().Name!;

    /// <summary>
    ///     The plugin API version: the assembly version of <c>Hst.Commander.Abstractions</c> (major and minor), which
    ///     plugins record in their reference to it when they are built.
    /// </summary>
    public static Version ApiVersion { get; } = ToApiVersion(typeof(IPlugin).Assembly.GetName().Version!);

    /// <summary>
    ///     Whether a plugin built for a plugin API version works with a plugin API version: the major versions are the
    ///     same and the plugin's minor version isn't newer. A minor version only adds to the plugin API (e.g. new
    ///     interfaces and members with default implementations), so plugins built for an older minor version keep
    ///     working; a major version changes it.
    /// </summary>
    public static bool IsCompatible(Version pluginApiVersion, Version apiVersion)
    {
        ArgumentNullException.ThrowIfNull(pluginApiVersion);
        ArgumentNullException.ThrowIfNull(apiVersion);
        return pluginApiVersion.Major == apiVersion.Major && pluginApiVersion.Minor <= apiVersion.Minor;
    }

    /// <summary>
    ///     Directories scanned for plugins at startup: "plugins" in the user's settings directory
    ///     (<c>~/.hst-commander/plugins</c>) and "plugins" in the directory Hst Commander is executed from.
    /// </summary>
    public static IReadOnlyList<string> Directories =>
    [
        Path.Combine(SettingsPaths.Directory, DirectoryName),
        Path.Combine(AppContext.BaseDirectory, DirectoryName)
    ];

    /// <summary>
    ///     Loads the plugins in plugin directories. Missing directories are skipped, as are assemblies without plugins
    ///     (e.g. dependencies) and plugins of a type already loaded (by full type name), e.g. a copy of a plugin included
    ///     with Hst Commander. A plugin failing to load is reported in <see cref="PluginLoadResult.Errors" /> and the
    ///     others are loaded.
    /// </summary>
    /// <param name="directories">Plugin directories, scanned in order; a directory listed twice is scanned once.</param>
    /// <param name="loadedPlugins">Plugins already loaded, e.g. the ones included with Hst Commander.</param>
    /// <param name="host">Host the loaded plugins are initialized with (<see cref="IPlugin.Initialize" />), null to not
    /// initialize them.</param>
    public static PluginLoadResult Load(IEnumerable<string> directories, IEnumerable<IPlugin> loadedPlugins,
        IPluginHost? host = null) => Load(directories, loadedPlugins, host, ApiVersion);

    /// <summary>Loads plugins like <see cref="Load(IEnumerable{string}, IEnumerable{IPlugin}, IPluginHost?)" />
    /// with the plugin API version given, so tests can load plugins built for another version.</summary>
    internal static PluginLoadResult Load(IEnumerable<string> directories, IEnumerable<IPlugin> loadedPlugins,
        IPluginHost? host, Version apiVersion)
    {
        ArgumentNullException.ThrowIfNull(directories);
        ArgumentNullException.ThrowIfNull(loadedPlugins);
        ArgumentNullException.ThrowIfNull(apiVersion);

        var plugins = new List<IPlugin>();
        var errors = new List<string>();
        var pluginTypeNames = loadedPlugins.Select(plugin => plugin.GetType().FullName).ToHashSet(StringComparer.Ordinal);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var scannedDirectories = new HashSet<string>(comparer);

        foreach (var directory in directories)
        {
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (!scannedDirectories.Add(fullPath) || !Directory.Exists(fullPath))
            {
                continue;
            }

            foreach (var (assemblyPath, isOwnDirectory) in FindAssemblies(fullPath, errors))
            {
                try
                {
                    foreach (var plugin in LoadPlugins(assemblyPath, apiVersion))
                    {
                        if (pluginTypeNames.Add(plugin.GetType().FullName))
                        {
                            // a plugin failing to initialize is reported like failing to load
                            if (host != null)
                            {
                                plugin.Initialize(host);
                            }

                            plugins.Add(plugin);
                        }
                    }
                }
                catch (BadImageFormatException) when (!isOwnDirectory)
                {
                    // native libraries next to single assembly plugins are not plugins
                }
                catch (PluginApiVersionException exception)
                {
                    errors.Add($"Plugin '{assemblyPath}' wasn't loaded: {exception.Message}");
                }
                catch (Exception exception)
                {
                    errors.Add($"Plugin '{assemblyPath}' failed to load: {GetMessage(exception)}");
                }
            }
        }

        return new PluginLoadResult(plugins, errors);
    }

    /// <summary>
    ///     Assemblies that may contain plugins: "Name/Name.dll" in directories of their own and "*.dll" in the plugin
    ///     directory, ordered by path.
    /// </summary>
    private static IEnumerable<(string Path, bool IsOwnDirectory)> FindAssemblies(string directory,
        List<string> errors)
    {
        var assemblies = new List<(string, bool)>();
        try
        {
            foreach (var subDirectory in Directory.GetDirectories(directory).Order(StringComparer.Ordinal))
            {
                var assemblyPath = Path.Combine(subDirectory, $"{Path.GetFileName(subDirectory)}.dll");
                if (File.Exists(assemblyPath))
                {
                    assemblies.Add((assemblyPath, true));
                }
            }

            assemblies.AddRange(Directory.GetFiles(directory, "*.dll").Order(StringComparer.Ordinal)
                .Select(path => (path, false)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Plugin directory '{directory}' can't be read: {exception.Message}");
        }

        return assemblies;
    }

    /// <summary>
    ///     Creates the plugins of an assembly loaded in a load context of its own, after checking the plugin API
    ///     version it was built for (before its types are loaded, which would fail with a less helpful error).
    /// </summary>
    private static List<IPlugin> LoadPlugins(string assemblyPath, Version apiVersion)
    {
        var loadContext = new PluginLoadContext(assemblyPath);
        var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
        CheckApiVersion(assembly, apiVersion);

        Type[] types;
        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types.OfType<Type>().ToArray();
        }

        return types
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IPlugin).IsAssignableFrom(type) &&
                           type.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => (IPlugin)Activator.CreateInstance(type)!)
            .ToList();
    }

    /// <summary>
    ///     Throws <see cref="PluginApiVersionException" /> when an assembly referencing
    ///     <c>Hst.Commander.Abstractions</c> was built for an incompatible plugin API version. Assemblies not
    ///     referencing it have no plugins and are skipped later.
    /// </summary>
    private static void CheckApiVersion(Assembly assembly, Version apiVersion)
    {
        var reference = assembly.GetReferencedAssemblies()
            .FirstOrDefault(name => string.Equals(name.Name, SharedAssemblyName, StringComparison.Ordinal));
        if (reference?.Version == null)
        {
            return;
        }

        var pluginApiVersion = ToApiVersion(reference.Version);
        if (IsCompatible(pluginApiVersion, apiVersion))
        {
            return;
        }

        var newer = pluginApiVersion.Major > apiVersion.Major ||
                    (pluginApiVersion.Major == apiVersion.Major && pluginApiVersion.Minor > apiVersion.Minor);
        throw new PluginApiVersionException(newer
            ? $"it needs plugin API {pluginApiVersion}, this version of Hst Commander has plugin API {apiVersion}. " +
              "Update Hst Commander or install a version of the plugin made for it."
            : $"it was built for plugin API {pluginApiVersion}, this version of Hst Commander has plugin API " +
              $"{apiVersion}. Install a version of the plugin made for it.");
    }

    private static Version ToApiVersion(Version version) => new(version.Major, version.Minor);

    private static string GetMessage(Exception exception) =>
        exception is TargetInvocationException { InnerException: { } innerException }
            ? innerException.Message
            : exception.Message;

    /// <summary>
    ///     Load context of a plugin assembly resolving its dependencies from its .deps.json or directory. Assemblies
    ///     shared with Hst Commander and framework assemblies are loaded from the default load context.
    /// </summary>
    private sealed class PluginLoadContext(string assemblyPath) : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver? _resolver = CreateResolver(assemblyPath);
        private readonly string _directory = Path.GetDirectoryName(assemblyPath)!;

        private static AssemblyDependencyResolver? CreateResolver(string assemblyPath)
        {
            try
            {
                return new AssemblyDependencyResolver(assemblyPath);
            }
            catch (Exception)
            {
                // e.g. without the host's dependency resolution in single-file apps, dependencies are found in the
                // plugin's directory
                return null;
            }
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == null || assemblyName.Name.Equals(SharedAssemblyName, StringComparison.Ordinal))
            {
                return null;
            }

            var path = _resolver?.ResolveAssemblyToPath(assemblyName) ??
                       Path.Combine(_directory, $"{assemblyName.Name}.dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
            _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path
                ? LoadUnmanagedDllFromPath(path)
                : IntPtr.Zero;
    }

    /// <summary>A plugin was built for a plugin API version Hst Commander doesn't support.</summary>
    private sealed class PluginApiVersionException(string message) : Exception(message);
}
