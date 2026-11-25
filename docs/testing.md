# Testing

Test helpers in `Hst.Commander.Core.Tests` and the manual smoke test before a release.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Test Helpers

- `TestVolume` (`Hst.Commander.Core.Tests/TestVolume.cs`): in-memory `IVolume` backed by a `Dictionary<string, List<TestEntry>>`. Supports `..` in `ChangeDirectory`, reading/writing `TestEntry.Data`, creating, deleting and renaming entries; `GetEntry(pathComponents)` looks up an entry for assertions.
- `TestVolumeProvider` (`Hst.Commander.Core.Tests/TestVolumeProvider.cs`): `IVolumeProvider` creating a new `TestVolume` filled by a setup action on each `CreateVolume`.
- `TestCredentialStore` (`Hst.Commander.Core.Tests/TestCredentialStore.cs`): in-memory `ICredentialStore` for `BookmarkStore` tests (`Passwords`), unavailable when created with `false` and failing to store with `FailSave`; `GivenWindowsCredentialStore` uses the real Windows Credential Manager on Windows only.
- `TestEntry` (`Hst.Commander.Core.Tests/TestEntry.cs`): in-memory `IEntry` with optional `byte[]? Data`.
- Use `AddEntry(pathComponents, entry)` to build the in-memory tree.
- `ZipTestData` (`Hst.Commander.Core.Tests/ZipTestData.cs`): creates zip archives in memory (`CreateZip((name, content)...)`, names ending with `/` are directories).
- `AmigaTestData` (`Hst.Commander.Core.Tests/AmigaTestData.cs`): creates a formatted adf (`CreateAdf`), raw and dynamic vhd RDB images with Fast File System (`Dos3DosType`), PFS3 (`Pfs3DosType`) and Smart File System (`SfsDosType`) partitions (`CreateRdbImage`, `CreateVhdRdbImage`) partition images without a rigid disk block with any of them (`CreatePartitionImage`, `WritePartitionImage`) and lists an adf mounted again (`ListAdfNames`).
- `SingleThreadSynchronizationContext` (`Hst.Commander.Core.Tests/SingleThreadSynchronizationContext.cs`): `Run(operation, timeout)` runs an operation with continuations on one thread like a UI thread, throwing `TimeoutException` on a deadlock.
- `AmigaIconTestData` (`Hst.Commander.Core.Tests/AmigaIconTestData.cs`): creates Amiga .info icons with a planar image (`CreatePlanarIcon`, optionally with a selected image and a Workbench position), an 8 color planar image (`CreateEightColorPlanarIcon`) or a NewIcon image (`CreateNewIcon`).
- `DiskTestData` (`Hst.Commander.Core.Tests/DiskTestData.cs`): creates an MBR disk with a FAT partition (`CreateMbrDisk`), a GPT disk with FAT and NTFS partitions (`CreateGptDisk`), an MBR disk with an exFAT partition (`CreateExFatDisk`), an MBR disk with an ext4 partition copied from `TestData/Disks/ext4.img.gz` (`CreateExtDisk`, made with `mkfs.ext4 -d` as described at `DiskTestData.ExtLabel`, as DiscUtils can't create ext file systems), GPT and MBR disks with 4096 byte sectors (`CreateLargeSectorDisk`), an MBR disk with a small partition that would also fit with 4096 byte sectors (`CreateSmallPartitionMbrDisk`), PiStorm MBR and GPT disks with a `0x76` or `3F82EEBC-...` partition holding a rigid disk block (`CreatePiStormDisk`, `CreateGptPiStormDisk`, using `AmigaTestData.WriteRdbToStream`) a hybrid disk with the rigid disk block in block 2 and an MBR in block 0 (`CreateHybridDisk`), and FAT, NTFS and ext4 file systems without a partition table (`CreateFatImage`, `CreateNtfsImage`, `CreateExtImage`), a SquashFS file system (`CreateSquashFsImage`), dynamic VHD, VHDX and VDI images holding any of them (`CreateVirtualDisk`), an MBR disk with a Linux partition holding a file system image (`CreateLinuxPartitionDisk`), and reads the gzipped images of `TestData/Disks` (`OpenGzippedImage`).
- `TestData/Disks` holds disk and file system images made with the real tools of each file system, not DiscUtils, which reads them in the tests (`GivenDiskTestImages`): XFS, Btrfs (with zlib, lzo and zstd compressed files), UDF, HFS+ (empty: nothing in Docker can write HFS+) and SquashFS (gzip, xz, zstd and lzo) file systems, an MBR disk with FAT, NTFS, exFAT and ext4 partitions, and a GPT disk with FAT, NTFS, exFAT, ext4, XFS, HFS+ and SquashFS partitions, raw and as dynamic VHD, VHDX and VDI images (qemu-img) and a zlib compressed DMG (`make/make-dmg.py`, checked with qemu's DMG reader). `make/make-images.sh` makes them again in a privileged Ubuntu 24.04 Docker container (the command is in the script); `ext4.img.gz` is older and left as it is.
- `NonSeekableStream` (`Hst.Commander.Core.Tests/NonSeekableStream.cs`): a read-only stream that can't seek, for checking that file volume providers detect images from streams like those of remote volumes.
- `ExFatValidator` (`Hst.Commander.Core.Tests/ExFatValidator.cs`, from Hst Imager): checks the structures of an empty exFAT file system against the exFAT specification (boot regions and checksum, FAT, allocation bitmap, up-case table, root directory), independently of `ExFatFormatter`; files and directories added are reported as unexpected entries.
- `TestPhysicalDisks` (`Hst.Commander.Core.Tests/TestPhysicalDisks.cs`): `IPhysicalDisks` with image files opened like physical disks through `DiskStream` (`AddDisk(name, path, model)`); `ReadOnlyDisks` can only be read (like a disk with a volume in use) and `DeniedDisks` can't be opened (like without administrator rights) and `SystemDisks` are marked as system disks.
- `GivenLicenses` reads the .NET notices from `TestData/dotnet-notices.txt`, embedded in the test assembly like in a self-contained app, and reads the TUI's and GUI's `obj/project.assets.json` (restored with the solution) to check that `THIRD-PARTY-NOTICES.txt` lists every package they include; `GivenPluginLoader` loads the SMB plugin as an external plugin from a directory with `SMBLibrary.dll`.
- `TestSmbServer` (`Hst.Commander.Core.Tests/TestSmbServer.cs`): `ISmbConnector` serving local directories as shares (`AddShare(name, path)`) through `DirectoryFileStore`, an SMBLibrary `INTFileStore` over a directory (attributes kept in memory); `Credentials` must be given to log in when set, `GuestDeniedShares` deny guests, and `Connections` records the connections (set `IsConnected` false to make the server close one). Reads and writes are split in requests of 7 and 5 bytes.
- `TestRemoteServer` (`Hst.Commander.Core.Tests/TestRemoteServer.cs`): `IRemoteConnector` of the Ftp plugin serving a local directory as the root of an FTP or SFTP server, with `HomeDirectory` the directory users start in; `Credentials` must be given to log in when set, `Servers` records the servers connected to and `Connections` the connections (set `IsConnected` false to make the server close one). The FluentFTP and SSH.NET clients themselves aren't covered by the unit tests, as they need real servers.
- `IlbmTestData` (`Hst.Commander.Core.Tests/IlbmTestData.cs`): creates IFF ILBM pictures from color indices (`CreateIlbm`: bitplanes, optionally ByteRun1 compressed, a CMAP, CAMG view modes and a mask plane) and a LIST of them (`CreateList`).
- `ArchiveTestData` (`Hst.Commander.Core.Tests/ArchiveTestData.cs`): archives in `TestData/Archives`: lha and lzx archives (from the Hst.Compression tests), e.g. `dirs-files.lha`/`.lzx` (`ArchiveTestData.DirsFiles`), the same tree as `dirs-files.7z` (made with 7-Zip) and `dirs-files.rar`/`dirs-files-rar4.rar` (rar 5 and 4 with stored entries, written by hand as WinRAR is needed to make rar archives), `solid.7z` (one solid block, for `SolidArchiveReader`) and `deflate.7z` (a solid deflate block SharpCompress 0.50.5 read only partly), and `special_chars.lha`/`.lzx` (from the Hst Imager tests) with Amiga names, protection bits and comments; `dirs-files.tar` (GNU), `dirs-files-posix.tar`, `dirs-files-v7.tar`, `dirs-files.tar.gz`/`.tgz`/`.tar.xz`/`.tar.bz2`/`.tar.zst`, `links.tar` (a symbolic and a hard link), `hello.txt.gz` (with its name and date), `noname.txt.gz`, `hello.txt.xz`/`.bz2`/`.zst`, `nosize.txt.zst` (without its size) and `multi.txt.gz`/`.xz`/`.bz2`/`.zst` (two compressed files joined), made with GNU tar, gzip, xz, bzip2 and zstd by `TestData/Archives/make/make-archives.sh` in Docker (the command is in the script).

## Manual Smoke Test

The unit tests cover Core and the plugins, not the front-ends, the packages or the operating systems. Before a release, go through this list with the packages of the draft release made by `build-and-release.yml` (signed, not `artifacts_only`), for both front-ends on each OS:

- **Windows** x64 (and arm64 if available): the setup and the portable zip; the TUI in Windows Terminal and in the classic console.
- **macOS** arm64 (and x64 if available): the dmgs; the TUI in Terminal.app (see [macOS Terminal](volumes.md#macos-terminal) for F1/F2).
- **Linux** x64 and arm64 (e.g. a Raspberry Pi): the AppImage and the deb; the TUI in a desktop terminal (see [Linux terminals](volumes.md#linux-terminals)) and over SSH.

Copy the list into the release's pull request or an issue and tick it per OS and front-end. Items marked (GUI) or (TUI) apply to that front-end only. Work in a scratch folder with copies of `Hst.Commander.Core.Tests/TestData/Archives` and some files of your own, and back up `~/.hst-commander` first.

### Install and start

- [ ] Installs and starts without warnings: Windows shows the publisher in SmartScreen and UAC, macOS opens the app without a Gatekeeper warning (notarized), the AppImage runs after `chmod +x` and the deb installs with a menu entry (GUI) and a command; the GUI's window shows the app's icon in the taskbar or dock (from the deb, and from the AppImage when integrated, e.g. with Gear Lever or AppImageLauncher).
- [ ] Help > About shows the release version (`1.0.<commits>`) and the build date, and Licenses lists the notices of Hst Commander, its components and .NET.
- [ ] Installing over the previous release keeps the settings and bookmarks (bookmarks saved before 1.0 are a list and are still read).
- [ ] Uninstalling (Windows setup, deb) removes the app and keeps `~/.hst-commander`.
- [ ] The install command of the release text installs the TUI on each platform (Windows PowerShell 5.1 and PowerShell 7, macOS, Linux); `hst.commander` starts in a new terminal, running it again updates it, and `-Uninstall`/`--uninstall` removes it.

### Panes and navigation

- [ ] Both panes list the start directory; Tab switches, Enter opens a directory, Backspace goes up, Ctrl+\ and typing `/` or `\` go to the root and Ctrl+H home.
- [ ] F1/F2 list the volume providers (Bookmarks, Local, Physical disks, FTP/SFTP and SMB when installed); choosing Local lists the drives or mount points.
- [ ] Ctrl+L and clicking anywhere on the path panel (also beside the path and on the disk space) change location, also to a path inside an archive.
- [ ] Clicking a column header sorts, and the order is kept after a restart; Ctrl+F quick search finds entries; Ctrl+R refreshes and Ctrl+U swaps the panes.
- [ ] Resizing and maximizing the window keeps the layout: the panes reach the bottom area and the right edge. With Settings > Appearance > Start maximized, the GUI starts maximized.
- [ ] Insert/Space, Ctrl+A, `+`, `-` and `*` mark entries, and the panel above the pane summarizes them.
- [ ] Quitting and starting again restores both panes of the GUI, and the right pane of the TUI with its left pane in the current directory.
- [ ] The File, Mark, Go and Commands menus (and View in the GUI) run their items; Go > Same location in other pane (Ctrl+D) shows the active pane's location, also inside an archive, in the other pane.

### Images and archives

- [ ] Enter opens `dirs-files.zip` (make one), `.lha`, `.lzx`, `.7z` and `.rar` from the test data and an ISO image; Backspace at their root goes back. Ctrl+PageDown opens a file as a volume.
- [ ] F9 creates an Amiga disk file (formatted) and an exFAT image; both open, files copied into them are listed after leaving and opening them again, and the other pane sees the same contents.
- [ ] `special_chars.lha` copied to a local directory keeps the Amiga names, protection bits and comments as UAE metadata (Properties shows them), and copied into the adf they're kept as they are.

### File operations

- [ ] F5 copies and F6 moves marked entries between the panes, with the progress dialog (and the taskbar or terminal tab progress); copying over existing files asks to overwrite; Cancel stops.
- [ ] Shift+F6 renames, F7 makes a directory, F8 and Delete delete after confirming.
- [ ] Ctrl+C/Ctrl+X/Ctrl+V (Alt+V in the TUI) copy, cut and paste between the panes; on Windows also to and from Explorer, including files inside an archive. (GUI) Also with Finder and the Linux file manager.
- [ ] (GUI) Dragging entries to the file manager (Explorer, Finder, Files on Ubuntu) and dropping files from it on a pane copies them, also without Shift from Files, which suggests a move.
- [ ] Ctrl+S searches by name and by contents, optionally inside archives, and going to a result selects it in the pane.
- [ ] Ctrl+P shows the properties with the size counted; changing an attribute (Windows) or the Amiga protection bits and comment (in the adf) is saved.

### Viewing and editing

- [ ] F3 views a text file and a large binary file (text, hex with F4, word wrap with F2, search) and Esc closes.
- [ ] (GUI) F3 views a PNG, a JPEG and an Amiga icon.
- [ ] F4 edits a text file, also inside the adf; saving keeps the encoding and line breaks; an external editor chosen in the settings opens the file and the changes are written back.

### Locations

- [ ] FTP/SFTP: connecting to a test server (e.g. a local SFTP server) asks for the password, lists, copies both ways, and a new host key is trusted on first use.
- [ ] SMB: copying the plugin from `smb-plugin.zip` to `~/.hst-commander/plugins/` adds SMB to F1/F2 and its notice to Licenses; connecting to a share lists it and copies both ways.
- [ ] SMB on Windows: `smb-plugin_windows_setup.exe` installs without administrator rights and shows the publisher; the TUI and GUI list SMB, and uninstalling it in Settings > Apps removes it. `-Smb`/`--smb` of the install scripts installs it too.
- [ ] Ctrl+B bookmarks a local directory and a server location with its password; the bookmark opens after a restart without asking (Windows Credential Manager, macOS Keychain, Linux Secret Service).
- [ ] File > Restart as administrator (root on Linux and macOS) asks first (Cancel stays), then restarts elevated with both panes where they were and the same pane active; the item is hidden once elevated.
- [ ] Physical disks: choosing them when not elevated offers to restart as administrator (UAC, sudo, pkexec, macOS's password dialog in the macOS GUI); elevated, the disks are listed with their model and a USB stick opens (copy to it only on a stick that may be erased).

### Interface

- [ ] Ctrl+T runs a command (e.g. `dir` or `ls`) in the active pane's directory; Ctrl+Enter inserts the entry name. (TUI) The output is shown and Ctrl+O shows it again, and the panes come back afterwards.
- [ ] The context menu (right-click, Shift+F10, Ctrl+F10) works, and Terminal here (PowerShell and Command prompt here on Windows) opens a terminal in the directory.
- [ ] The settings dialog changes the theme (Midnight Commander, Light, Dark, Directory Opus 4) with a preview, and Directory Opus 4's button editor saves its buttons; the settings are kept after a restart.
- [ ] (GUI) Ctrl+1 to Ctrl+9 change the navigation mode: list, thumbnails and Workbench in the adf (dragging an icon and saving its position); entry icons show for `.info` files and programs.
- [ ] F10 (and Esc in the TUI) asks before quitting; closing the editor with changes asks to save them.
