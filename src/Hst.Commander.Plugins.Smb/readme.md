# Hst.Commander.Plugins.Smb

The SMB plugin adds Windows file shares and other SMB 2 and 3 servers (Samba, network attached storage) to Hst Commander. It uses [SMBLibrary](https://github.com/TalAloni/SMBLibrary), a managed SMB client, so it works the same on Windows, Linux and macOS without mounting the share.

It is an **external plugin**, not included with Hst Commander: SMBLibrary is licensed under the LGPL-3.0-or-later, so the plugin is released separately with `SMBLibrary.dll` as a separate file that can be replaced, its notices and the source of SMBLibrary. The plugin itself is MIT licensed like Hst Commander.

## Installing

1. Download `hst-commander_v<version>_smb-plugin.zip` from the Hst Commander release (the same zip for Windows, Linux and macOS).
2. Copy the `Hst.Commander.Plugins.Smb` directory from the zip to `~/.hst-commander/plugins/` (`%USERPROFILE%\.hst-commander\plugins\` on Windows), or to `plugins` next to the Hst Commander executable.
3. Start Hst Commander. **SMB** is listed in the volume provider list (Alt+F1/Alt+F2), and Help > About > Licenses shows the plugin's notices.

The zip also holds `SMBLibrary-source-<version>.zip`, the source code of the SMBLibrary version included.

To build and install it from the source:

```shell
dotnet publish src/Hst.Commander.Plugins.Smb/Hst.Commander.Plugins.Smb.csproj -c Release -o ~/.hst-commander/plugins/Hst.Commander.Plugins.Smb
```

## SMB shares

- Choosing **SMB** asks for an SMB path, e.g. `\\server\share`, `\\server\share\dir`, `//server/share` or `smb://server/share/dir`. Backslashes and slashes both work and can be mixed. URLs can escape characters (`smb://nas/My%20Share`) and hold a user name and password (`smb://user:password@nas/share`). A server alone (`\\server`) lists its shares; hidden shares ending with `$` aren't listed but can be entered, e.g. `\\server\c$`.
- Hst Commander connects as guest first. When the server or the share needs a user name and password, a dialog asks for them, and again when they're rejected. User names can be `user`, `DOMAIN\user` or `user@domain`. The credentials are kept in memory for the pane until another volume is chosen; they aren't saved.
- Paths are shown as UNC paths, e.g. `\\server\share\dir` (`//server/share/dir` on Linux and macOS), and can be entered in the location dialog (Ctrl+L) while the server is shown.
- Files and directories can be viewed, edited, copied, moved, renamed and deleted, and their read-only, hidden, system and archive attributes changed, as the user's rights allow. The free and total space of the share is shown next to the path.
- Each file operation uses a connection of its own. A connection closed by the server, e.g. after being idle, is connected again.
- Only port 445 and SMB 2 and 3 are supported. Images on shares (e.g. adf files) open read-only.

## Licenses

- `THIRD-PARTY-NOTICES.txt` holds SMBLibrary's copyright notice, the commit of its source and the LGPL and GPL version 3 texts. It is embedded in the plugin and returned by `SmbPlugin.ThirdPartyNotices`, so Help > About > Licenses shows it, and published next to the plugin.
- `THIRD-PARTY-NOTICES.txt` is made by `src/notices/make-notices.py`. When changing the SMBLibrary version, set `SMBLIBRARY_VERSION` and `SMBLIBRARY_COMMIT` (the commit of that version's source) in `.github/workflows/build-and-release.yml` and run the script; `GivenPluginLoader` checks the notices name the version referenced.

## Classes

- `SmbPlugin` returns the `SmbVolumeProvider` and the notices.
- `SmbVolumeProvider` (`ILocationVolumeProvider`) parses the path with `SmbPath` and connects in `Connect`, throwing `CredentialsRequiredException` when credentials are needed, and returns an `SmbServerVolumeProvider` for the server and credentials with the share and directories to start in.
- `SmbServerVolumeProvider` creates an `SmbVolume` with a new connection on each call; its `PathRoot` (`\\server`) makes the paths UNC paths.
- `SmbVolume` lists the shares at its root and the files of a share below it, running the synchronous requests of SMBLibrary on the thread pool.
- `SmbFileStream` reads and writes an opened file with requests of at most 1 MiB.
- `ISmbConnector`/`ISmbConnection` connect and log in (`SmbClientConnector` with SMBLibrary's `SMB2Client`), replaced by a test server in the tests. `SmbStatus` turns failed statuses into exceptions.
