# Hst.Commander.Plugins.Ftp

The Ftp plugin adds FTP, FTPS and SFTP servers to Hst Commander. It is included with Hst Commander by default and references only `Hst.Commander.Abstractions`, using [FluentFTP](https://github.com/robinrodricks/FluentFTP) (MIT) for FTP and FTPS and [SSH.NET](https://github.com/sshnet/SSH.NET) (MIT, with BouncyCastle.Cryptography) for SFTP.

## Features

- Choosing **FTP/SFTP** in the volume provider list (F1/F2) shows a connection dialog: protocol (FTP, FTPS or SFTP), host, port (empty for the default port, 21 or 22), path (empty for the home directory), user name and password. The dialog shows the last connection details and user name entered while Hst Commander runs.
- FTP without a user name logs in as `anonymous`. SFTP needs a user name and logs in with the password (password or keyboard-interactive authentication). When the server rejects the login, the credentials dialog asks again.
- FTPS is explicit FTP over TLS (`AUTH TLS`). Servers that don't support TLS are reported as such; implicit FTPS (port 990) isn't supported.
- Paths are shown as URLs, e.g. `sftp://server/home/user` or `ftp://server:2121/pub`, and can be entered in the location dialog (Ctrl+L) while the server is shown.
- Files and directories can be viewed, edited, copied, moved, renamed and deleted, and directories created, as the user's rights allow. Modified dates are kept when copying to servers that can set them (`MFMT` on FTP, always on SFTP). SFTP servers supporting `statvfs@openssh.com` (e.g. OpenSSH) show the free and total space next to the path.
- Images on servers (adf, RDB, disk images, archives) open read-only. Listed files aren't read in the background (`ReadListedFiles` false) to mark those that can be opened as volumes or to show `.info` and embedded icons, as FTP servers often allow one connection per user and reading the files would block browsing; images are detected when opened and the GUI shows type icons.
- Bookmarks (Ctrl+B) store the location and user name, and offer to store the password in Windows Credential Manager, the macOS Keychain or the Linux Secret Service, like SMB bookmarks.

## Server keys

The keys of the servers are trusted the first time a server is connected to, and checked each time after, stored in `~/.hst-commander/ftp-known-hosts.txt` with a line `server key-type fingerprint` per key:

- SFTP: the SHA-256 fingerprint of the SSH host key.
- FTPS: the SHA-256 fingerprint of the server's certificate, only when the operating system doesn't trust the certificate (e.g. a self-signed certificate of a NAS). Certificates the operating system trusts are accepted without being stored.

When a key changes, connecting is refused with a message showing the new fingerprint, as someone may be intercepting the connection. If the key was changed on purpose (e.g. the server was reinstalled), remove the server's line from the file to trust the new key.

## Classes

- `FtpPlugin` returns the `FtpVolumeProvider`.
- `FtpVolumeProvider` (`IConnectionVolumeProvider`) lists the protocols of the connection dialog, formats and parses locations with `RemoteLocation`, and connects in `Connect`, returning an `FtpServerVolumeProvider` for the server and credentials with the directory to start in (the home directory when the location has no path).
- `FtpServerVolumeProvider` creates a `RemoteVolume` with a new connection on each call; its `PathRoot` (`sftp://server`) makes the paths URLs.
- `RemoteVolume` implements `IVolume` over an `IRemoteClient`, with absolute paths from the server's root, connecting again when the server closed the connection.
- `RemoteConnector` (`IRemoteConnector`) connects and logs in with FluentFTP (`FtpRemoteClient`) or SSH.NET (`SftpRemoteClient`), turning rejected logins into `CredentialsRequiredException` and checking server keys with `KnownHosts`.
- `FtpRemoteClient` opens each file on a connection of its own, closed with the stream, as an FTP connection transfers one file at a time; closing a written file reads the server's reply, so a failed upload throws `IOException`. `SftpRemoteClient` opens files on its own connection, as SFTP runs several requests at once.
- The tests replace `IRemoteConnector` with `TestRemoteServer`, serving a local directory.
