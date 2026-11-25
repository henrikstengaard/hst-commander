# Status

What works and what isn't implemented yet.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

**Working:**
- The Terminal.Gui v2 TUI and Avalonia 12 GUI dual-pane browsers, with per-pane navigation and current-directory startup.
- Local drive/directory listing, parent navigation, and read-only browsing of ISO images, zip archives (the default Zip plugin) lha/lzx archives (the default LhaLzx plugin) and rar/7z/tar archives and gzip/xz files, including compressed tar archives (the default Archives plugin) as nested volumes, detected by their magic signature, also inside other images.
- Reading and writing MBR and GPT disk images (raw, VHD, VHDX or VDI; DMG read-only) with FAT, exFAT and NTFS partitions, reading ext2/3/4, XFS, Btrfs, HFS+, SquashFS and UDF partitions, PiStorm RDB partitions (MBR type `0x76`, GPT type `3F82EEBC-87C9-4097-8165-89D6540557C0`) and hybrid MBR/RDB disks, and FAT, exFAT, NTFS (read and write) and ext2/3/4, XFS, Btrfs, HFS+, SquashFS and UDF (read) file systems without a partition table or in a virtual disk image, with the default Disk plugin, working with the Amiga plugin through `IPluginHost`.
- Reading and writing Amiga disk files (adf, Fast File System) and the partitions (Fast File System, PFS3, SFS) of Amiga hard disk images with a rigid disk block (raw or vhd) with the default Amiga plugin, shared between panes and file operations (see [Writable images](volumes.md#writable-images)).
- Reading and writing physical disks (internal and external, e.g. USB) with the default PhysicalDisk plugin on Windows (as administrator), Linux and macOS (as root): listed with their model in the `Physical disks` volume provider and opened like MBR, GPT and RDB disk images, shared between panes and file operations; read-only while the OS uses a volume on the disk; system disks are listed but not accessible; read and written through a block cache (32 MB per disk by default, set in the plugin's settings file) whose writes are written between file operations.
- Browsing, reading and writing Windows file shares and other SMB 2/3 servers with the external Smb plugin (installed separately from the release's `smb-plugin.zip`): the `SMB` volume provider asks for an SMB path (`\\server\share` or `smb://server/share`, `\` or `/`) and for a user name and password when the server or share needs them. Images on shares (adf, RDB and disk images) are writable and shared between panes like local ones.
- Browsing, reading and writing FTP, FTPS (explicit TLS) and SFTP servers with the default Ftp plugin: the `FTP/SFTP` volume provider shows a connection dialog with the protocol, host, port, path, user name and password. SSH host keys and untrusted FTPS certificates are trusted on first use and checked afterwards.
- Starting/executing local files with Enter or double-click, with executables drawn in green.
- GUI entry icons: Amiga .info icons (the default Amiga plugin's icon provider) for the .info file and the entry it belongs to, icons contained in programs and .ico files (also inside images), otherwise the host OS icons for file types, folders and drives.
- Clicking anywhere on a pane's path panel opens the location dialog; GUI drops from Ubuntu's Files (and other Linux apps suggesting move) are taken.
- SMB listing and other reading requests wait up to 30 seconds for the server and are sent again once on a new connection when it doesn't answer in time (e.g. a NAS spinning up its disks).
- The properties dialog changes the read, write and execute permissions of the owner, group and others of local files on Linux and macOS.
- Files split by 7-Zip (`name.001`, `name.002`, ...) open as the joined file, e.g. a split 7z archive; opening a large archive shows the entries read after 2 seconds and can be cancelled.
- The volume abstraction layer and implementations: `LocalVolume`, `Iso9660Volume`, `ZipVolume`, `LhaVolume`, `LzxVolume`, `SharpCompressVolume` (rar, 7z and tar), their base class `ArchiveVolume<T>`, `NestedVolume`, `EntryVolumeProvider`, and `NullVolume`.
- All unit tests in `Hst.Commander.Core.Tests`.

- Going to the root of the volume with Ctrl+\ or by typing `/` or `\` in both front-ends.
- Quitting with F10, Ctrl+Q or File > Quit (and Esc in the TUI) after a confirm dialog in both front-ends.
- Saving where the panes are when quitting and restoring them on the next start (both panes in the GUI; the right pane in the TUI, whose left pane starts in the current directory) in both front-ends.
- Refreshing the active pane with Ctrl+R and swapping the left and right panes with Ctrl+U (Go > Refresh, Go > Swap panes) in both front-ends.
- Showing the active pane's location in the other pane with Ctrl+D (Go > Same location in other pane), also inside images and on servers connected to, in both front-ends.
- Changing location with Ctrl+L or by clicking the path panel (location dialog), including paths into images, and going to the home directory with Ctrl+H.
- Searching for entries with Ctrl+S (Commands > Search) in both front-ends: by name with wildcards or a regular expression, by contents (text, regular expression or hex values), from any location, in all subdirectories, the current directory or a number of levels, optionally inside images and archives, going to a result in the pane (see [Searching](file-operations.md#searching)).
- Quick search with Ctrl+F (Commands > Quick search) in both front-ends: typing moves the cursor to the first entry starting with the text; Up/Down/Ctrl+F go through the matches (see [Quick search](file-operations.md#quick-search)).
- Bookmarking locations with Ctrl+B (Go > Add bookmark) in both front-ends, listed by the Bookmarks item of the volume provider list (F1/F2), edited with F4 (description, location, path, user name and stored password) and deleted with Del; SMB, FTP and SFTP passwords can be stored with a bookmark in Windows Credential Manager, the macOS Keychain or the Linux Secret Service (see [Bookmarks](volumes.md#bookmarks)).
- Build versioning from the git commit count and a Help > About menu item in both front-ends, with a Licenses section showing the notices of Hst Commander, its third-party components and the loaded plugins, also installed next to the apps.
- An error log in `~/.hst-commander/logs` of the errors shown and the unhandled exceptions in both front-ends, which show unhandled exceptions and keep running, offer to report a crash on the next start, and report errors as a pre-filled GitHub issue the user reviews before submitting.
- Running as administrator or root shown in the menu bar of both front-ends; choosing the physical disks when not elevated offers to restart as administrator (UAC on Windows, sudo in the TUI's terminal on Linux and macOS, pkexec in the GUI on Linux, macOS's password dialog with osascript in the GUI on macOS) with the panes where they were, as does File > Restart as administrator/root.
- Choosing the volume provider of the left/right pane with F1/F2, Alt+F1/Alt+F2 or Shift+F1/Shift+F2 (configurable, see [Volume provider keys](volumes.md#volume-provider-keys)) or Go > Left/Right volume from `VolumeProviders.All`: the built-in Local volume provider, the PhysicalDisk plugin's `Physical disks`, the Ftp plugin's `FTP/SFTP` and, when installed, the Smb plugin's `SMB`.
- Copying (F5) between panes and volumes, with a confirm dialog (destination, filter) and a progress dialog.
- Drag and drop in the GUI: dragging entries to other applications (entries in images and other volumes as virtual files on Windows and file promises on macOS, read when dropped) and between panes, and dropping files from other applications on a pane or one of its directories, copying or moving with Shift after the copy or move dialog (see [Drag and drop](gui.md#drag-and-drop)).
- Keeping Amiga names, protection bits and comments when copying or moving between Amiga volumes (and from lha/lzx archives), and storing/reading them as UAE metadata (UAEFSDB or FS-UAE metafiles, chosen in the settings) when copying between Amiga volumes and other volumes, shown in the properties dialog.
- Moving (F6) between panes and volumes, with the same confirm and progress dialogs as copying; a single entry moved to a plain name is renamed.
- Renaming the entry under the cursor (Shift+F6) with a dialog.
- A context menu on the entries (right-click) with Copy, Cut, Paste, Rename, Delete and Properties and PowerShell here, Terminal here and Command prompt here (only Terminal here on macOS and Linux) in both front-ends, opened with the menu key or Shift+F10 too, and copying, cutting and pasting with Ctrl+C, Ctrl+X and Ctrl+V, also to and from Explorer through the Windows clipboard (local files and entries of images as virtual files, cut ones moved, in both front-ends) and other applications in the GUI on macOS and Linux (see [Clipboard](file-operations.md#clipboard)).
- Making a directory (F7) in the active pane, with a dialog for the directory name.
- A command line above the bottom area in both front-ends (Ctrl+T), with entry names and paths inserted with Ctrl+Enter and Ctrl+Shift+Enter, run with the shell in the active pane's local directory: in the terminal in the TUI, started without showing its output in the GUI (see [Command line](command-line.md)).
- Creating volumes (F9) with the volume creators of plugins, e.g. empty, formatted (DOS0-DOS7) or bootable double or high density Amiga disk files with the Amiga plugin, and exFAT file systems as sparse files with the Disk plugin.
- Deleting (F8 or Delete) the marked entries or the entry under the cursor, with a confirm dialog and a progress dialog.
- Showing the properties of entries and drives (Ctrl+P, Alt+Enter or File > Properties), with the size and contents counted in the background, and changing attributes (local files, FAT/exFAT/NTFS partitions) and Amiga protection bits (Amiga volumes, UAE metadata) with check boxes, plus tabs of plugins: the Amiga comment of any entry (in Amiga file systems or as UAE metadata, created and deleted as needed) and the tooltypes of Amiga icons, added, edited and deleted, saved with Save.
- Loading plugins at startup from `~/.hst-commander/plugins` and `plugins` next to the executable in both front-ends, showing load errors.
- Sorting the panes by name, size or modified date by clicking a column header, ascending or descending with ▲ or ▼ in the header, saved in the settings, in both front-ends.
- Selecting all entries with Ctrl+A (Mark > Select all), selecting (marking) or unselecting a group of entries with + or - (Mark > Select group / Unselect group), using wildcards or a regular expression, and inverting the selection with * (Mark > Invert selection).
- Starting the GUI maximized (Settings > Appearance > Start maximized).
- GUI navigation modes (View menu, Ctrl+1 to Ctrl+9): list, thumbnails and the plugins' modes, e.g. the Amiga Workbench with icons at their `.info` positions, which can be dragged, sorted by name and saved, and def icons (`.info` or image files in `~/.hst-commander/plugins/amiga`) for entries without an icon.

- Themes added by plugins in both front-ends, with their own colors, buttons in the bottom area and parent buttons at the outer side of the panes; the default DirectoryOpus plugin's Directory Opus 4 theme has a button bank the user changes in the settings (dragging buttons and columns in the GUI, with keys in the TUI), see [Themes](themes.md).
- Choosing the font and size of the panes and the bottom area in the GUI (Settings > Appearance); themes can name a font, e.g. Topaz for Directory Opus 4 when it's installed
- Viewing files of any size (F3) in a text view (UTF-8 or ISO-8859-1, word wrapped with F2) or hex view, with text and regular expression search and selecting and copying text or hex values, in both front-ends.
- Editing text files (F4) in the built-in editor (search and replace with text or regular expressions, word wrap, go to line, undo, keeping the encoding and line breaks), also inside writable Amiga images, or in the system's or a custom external editor (files inside volumes are edited as temporary copies and written back), in both front-ends.
- Viewing images (F3) in the GUI (the TUI shows them in the hex view): PNG, JPEG, GIF (all frames), BMP, WebP and Windows icon files (all images) built in, Amiga icons (all images) and IFF ILBM/PBM pictures with the Amiga plugin's image provider; fitted or zoomed (10%-1600%, + and -) and moved when larger than the view.

**Not implemented:**
- Other file-management operations (e.g. creating a new file with Shift+F4); changing the viewer's encoding and going to an offset; changing the editor's encoding and line breaks.
- Writing to images other than Amiga adf, RDB and partition images and FAT, exFAT and NTFS images (also in VHD, VHDX and VDI images); ISO images, zip, lha, lzx, rar, 7z and tar archives and gzip and xz files are read-only, as are Amiga images inside other images.
- Writing an image file to a physical disk (the physical disk list is read-only).
- Changing RDB, MBR and GPT partitions, other Amiga file systems (e.g. SFS\2), writing ext2/3/4, XFS, Btrfs, HFS+, SquashFS and UDF, SquashFS compressed with other than gzip, zstd compressed Btrfs files, APFS, Apple partition maps, differencing VHDX and VDI images and vmdk images.
- Unloading or reloading plugins while running, and a list of loaded plugins in the UI.
- SMB 1 servers, SMB ports other than 445 and browsing the servers of the network. SMB credentials are only remembered with bookmarks.
- Implicit FTPS (port 990), SFTP login with private keys, asking before trusting a new server key, and writable images on FTP and SFTP servers.


