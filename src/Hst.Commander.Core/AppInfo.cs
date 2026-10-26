using System.Reflection;

namespace Hst.Commander.Core;

/// <summary>
/// Name, author and version of the application, read from the assembly attributes set by Directory.Build.props.
/// </summary>
public record AppInfo(string Name, string Author, string Version)
{
    public static AppInfo Current { get; } = FromAssembly(Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly);

    public static AppInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return new AppInfo(
            assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? assembly.GetName().Name ?? string.Empty,
            assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty,
            assembly.GetName().Version?.ToString(3) ?? "0.0.0");
    }

    public string NameAndVersion => $"{Name} v{Version}";

    public string AboutText => $"{NameAndVersion}\n\nCreated by {Author}";

    /// <summary>Question confirming quitting the application, e.g. <c>Do you want to quit Hst Commander?</c></summary>
    public string QuitQuestion => $"Do you want to quit {Name}?";
}
