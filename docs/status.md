# Status

What works and what isn't implemented yet.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

**Working:**
- The Terminal.Gui v2 TUI and Avalonia 12 GUI dual-pane browsers, with per-pane navigation and current-directory startup.
- Local drive/directory listing, parent navigation, and read-only browsing of ISO images, zip archives (the default Zip plugin) and lha/lzx archives (the default LhaLzx plugin) as nested volumes, detected by their magic signature, also inside other images.
- Reading and writing MBR and GPT disk images (raw or vhd) with FAT and NTFS partitions, PiStorm RDB partitions (MBR type `0x76`, GPT type `3F82EEBC-87C9-4097-8165-89D6540557C0`) and hybrid MBR/RDB disks with the default Disk plugin, working with the Amiga plugin through `IPluginHost`.
- Reading and writing Amiga disk files (adf, Fast File System) and the partitions (Fast File System, PFS3) of Amiga hard disk images with a rigid disk block (raw or vhd) with the default Amiga plugin, shared between panes and file operations (see [Writable images](volumes.md#writable-images)).
- Reading and writing physical disks (internal and external, e.g. USB) with the default PhysicalDisk plugin on Windows (as administrator), Linux and macOS (as root): listed with their model in the `Physical disks` volume provider and opened like MBR, GPT and RDB disk images, shared between panes and file operations; read-only while the OS uses a volume on the disk; system disks are listed but not accessible; read and written through a block cache (32 MB per disk by default, set in the plugin's settings file) whose writes are written between file operations.
- Starting/executing local files with Enter or double-click, with executables drawn in green.
- GUI entry icons: Amiga .info icons (the default Amiga plugin's icon provider) for the .info file and the entry it belongs to, icons contained in programs and .ico files (also inside images), otherwise the host OS icons for file types, folders and drives.
- The volume abstraction layer and implementations: `LocalVolume`, `Iso9660Volume`, `ZipVolume`, `LhaVolume`, `LzxVolume`, `NestedVolume`, `EntryVolumeProvider`, and `NullVolume`.
- All unit tests in `Hst.Commander.Core.Tests`.

- Going to the root of the volume with Ctrl+\ in both front-ends.
- Quitting with F10, Ctrl+Q or File > Quit (and Esc in the TUI) after a confirm dialog in both front-ends.
- Changing location with Ctrl+L or by double-clicking the path (location dialog), including paths into images.
- Build versioning from the git commit count and a Help > About menu item in both front-ends.
- Running as administrator or root shown in the menu bar of both front-ends; choosing the physical disks when not elevated offers to restart as administrator (UAC on Windows, sudo in the TUI's terminal on Linux and macOS, pkexec in the GUI on Linux) with the panes where they were.
- Choosing the volume provider of the left/right pane with Alt+F1/Alt+F2 or Shift+F1/Shift+F2 (configurable, see [Volume provider keys](volumes.md#volume-provider-keys)) or File > Left/Right volume from `VolumeProviders.All`: the built-in Local volume provider and the PhysicalDisk plugin's `Physical disks`.
- Copying (F5) between panes and volumes, with a confirm dialog (destination, filter) and a progress dialog.
- Keeping Amiga names, protection bits and comments when copying or moving between Amiga volumes (and from lha/lzx archives), and storing/reading them as UAE metadata (UAEFSDB or FS-UAE metafiles, chosen in the settings) when copying between Amiga volumes and other volumes, shown in the properties dialog.
- Moving (F6) between panes and volumes, with the same confirm and progress dialogs as copying; a single entry moved to a plain name is renamed.
- Making a directory (F7) in the active pane, with a dialog for the directory name.
- Creating volumes (F9) with the volume creators of plugins, e.g. empty, formatted or bootable Amiga disk files with the Amiga plugin.
- Deleting (F8 or Delete) the marked entries or the entry under the cursor, with a confirm dialog and a progress dialog.
- Showing the properties of entries and drives (Ctrl+P, Alt+Enter or File > Properties), with the size and contents counted in the background, and changing attributes (local files, FAT/NTFS partitions) and Amiga protection bits (Amiga volumes, UAE metadata) with check boxes.
- Loading plugins at startup from `~/.hst-commander/plugins` and `plugins` next to the executable in both front-ends, showing load errors.
- Selecting (marking) or unselecting a group of entries with + or - (File > Select group / Unselect group), using wildcards or a regular expression, and inverting the selection with * (File > Invert selection).
- GUI navigation modes (View menu, Ctrl+1 to Ctrl+9): list, thumbnails and the plugins' modes, e.g. the Amiga Workbench with icons at their `.info` positions, which can be dragged, sorted by name and saved, and def icons (`.info` or image files in `~/.hst-commander/plugins/amiga`) for entries without an icon.

- Viewing files of any size (F3) in a text view (UTF-8 or ISO-8859-1, word wrapped with F2) or hex view, with text and regular expression search and selecting and copying text or hex values, in both front-ends.
- Editing text files (F4) in the built-in editor (search and replace with text or regular expressions, word wrap, go to line, undo, keeping the encoding and line breaks), also inside writable Amiga images, or in the system's or a custom external editor (files inside volumes are edited as temporary copies and written back), in both front-ends.
- Viewing images (F3) in the GUI (the TUI shows them in the hex view): PNG, JPEG, GIF (all frames), BMP, WebP and Windows icon files (all images) built in, Amiga icons (all images) and IFF ILBM/PBM pictures with the Amiga plugin's image provider; fitted or zoomed (10%-1600%, + and -) and moved when larger than the view.

**Not implemented:**
- Other file-management operations (e.g. creating a new file with Shift+F4); changing the viewer's encoding and going to an offset; changing the editor's encoding and line breaks.
- Writing to images other than Amiga adf and RDB images; ISO images and zip, lha and lzx archives are read-only, as are Amiga images inside other images.
- Writing an image file to a physical disk (the physical disk list is read-only).
- Changing RDB, MBR and GPT partitions, other Amiga file systems (e.g. SFS), other PC file systems (exFAT, ext, ...) and vhdx/vmdk images.
- Unloading or reloading plugins while running, and a list of loaded plugins in the UI.
- `DiskImageVolume` remains an intentional stub.


