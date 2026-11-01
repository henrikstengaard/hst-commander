using Hst.Commander.Abstractions;

namespace Hst.Commander.Core;

/// <summary>
///     A license notice shown in the Licenses section of the About dialog, e.g. Hst Commander's own license or the
///     notices of the third-party components it includes.
/// </summary>
/// <param name="Title">Title in the list of notices, e.g. "Third-party components".</param>
/// <param name="Text">Text of the notice: copyright notices and license texts.</param>
public sealed record LicenseNotice(string Title, string Text);

/// <summary>
///     License notices of Hst Commander shown by both front-ends in Help > About > Licenses, as the licenses of the
///     third-party components require: Hst Commander's MIT license, the notices of the third-party components
///     (THIRD-PARTY-NOTICES.txt at the root of the repository), of the native Skia and HarfBuzz libraries and of the
///     .NET runtime, and the notices of the loaded plugins (<see cref="IPlugin.ThirdPartyNotices" />), e.g. the SMB
///     plugin's SMBLibrary (LGPL). The files are also installed next to the apps.
/// </summary>
public static class Licenses
{
    /// <summary>File of the .NET runtime's notices, published next to self-contained apps by Directory.Build.targets.</summary>
    public const string DotNetNoticesFileName = "THIRD-PARTY-NOTICES-DOTNET.txt";

    /// <summary>Shown when the app isn't self-contained, so it runs on a .NET runtime installed separately.</summary>
    private const string DotNetNoticesUrl = "https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT";

    /// <summary>Summary shown above the list of notices in the About dialog.</summary>
    public const string Summary =
        "Hst Commander is licensed under the MIT license. It includes third-party components under their own " +
        "licenses, listed with their copyright notices below.";

    /// <summary>
    ///     The notices: Hst Commander's license, the third-party components, the native libraries, the .NET runtime and
    ///     each plugin with notices, in plugin order.
    /// </summary>
    /// <param name="plugins">Loaded plugins, defaults to <see cref="Plugins.All" />.</param>
    /// <param name="appDirectory">Directory of the app with the .NET notices, defaults to the app's base directory.</param>
    public static IReadOnlyList<LicenseNotice> GetNotices(IEnumerable<IPlugin>? plugins = null,
        string? appDirectory = null)
    {
        List<LicenseNotice> notices =
        [
            new("Hst Commander (MIT license)", ReadResource("LICENSE.txt")),
            new("Third-party components", ReadResource("THIRD-PARTY-NOTICES.txt")),
            new("Skia and HarfBuzz native libraries", ReadResource("THIRD-PARTY-NOTICES-SKIASHARP.txt")),
            new(".NET runtime", ReadDotNetNotices(appDirectory ?? AppContext.BaseDirectory))
        ];
        foreach (var plugin in plugins ?? Plugins.All)
        {
            string? text;
            try
            {
                text = plugin.ThirdPartyNotices;
            }
            catch (Exception)
            {
                // a failing plugin doesn't stop the About dialog
                text = null;
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                notices.Add(new LicenseNotice($"{plugin.Name} plugin", text));
            }
        }

        return notices;
    }

    private static string ReadDotNetNotices(string appDirectory)
    {
        var path = Path.Combine(appDirectory, DotNetNoticesFileName);
        try
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return "Hst Commander runs on .NET, which is licensed under the MIT license:\n\n" +
               "Copyright (c) .NET Foundation and Contributors\n\n" +
               $"This build uses a .NET runtime installed separately. Its third-party notices are at {DotNetNoticesUrl} " +
               "and in the installation of .NET.";
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(Licenses).Assembly.GetManifestResourceStream(name) ??
                           throw new InvalidOperationException($"Resource '{name}' not found");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
