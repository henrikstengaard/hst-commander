using Hst.Commander.Abstractions;

namespace Hst.Commander.Plugins.Ftp;

/// <summary>
/// Plugin browsing FTP, FTPS and SFTP servers as volumes. Choosing "FTP/SFTP" in the volume provider list shows a
/// connection dialog asking for the protocol, host, port, path, user name and password. A default plugin, as FluentFTP
/// and SSH.NET are MIT licensed.
/// </summary>
public class FtpPlugin : IPlugin
{
    public string Name => "Ftp";

    public IEnumerable<IVolumeProvider> VolumeProviders { get; } = [new FtpVolumeProvider()];
}
