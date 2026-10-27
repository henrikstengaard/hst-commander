# Entry Icons and Navigation Modes

Icon providers, GUI entry icons and the GUI navigation modes (list, thumbnails, Workbench). Read this when working on icons or navigation modes. The TUI has neither.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Entry Icons

- `ListEntriesAsync` finds each entry's icon file with the first icon provider (`VolumeProviders.Icons`) whose `GetIconFileName` names a listed file (exact match, otherwise case-insensitive), so an entry and its icon file (`file` and `file.info`, `drawer` and `drawer.info`) share the icon. `GetIconFile(entry)` returns it. Not for `..` or the drive list.
- Volume entries without a listed icon file get the providers' `GetVolumeIconFileName` (e.g. `Disk.info`), looked up when the icon is read. `HasIcon(entry)` covers both cases.
- `ReadIconAsync(entry, size)` reads the icon file into memory (with a size limit; local files on a background thread, others with the pane's volume) and calls `ReadIcon` on the thread pool. `ReadIconsAsync` does the same for all icons. Unreadable or invalid icons and plugin errors give null/empty.
- Icons inside volume entries are read with a location opened once per listing with `OpenLocationAsync` (closed when the entries are listed again or the context is disposed), on the thread pool and serialized by a semaphore, so the pane's volume isn't changed. A partition without the file, or that can't be mounted, has no icon.
- `AmigaIconProvider` (Amiga plugin) reads `.info` disk objects (and AmigaOS 4 PNG icons) with Hst.Amiga, preferring true color, then GlowIcon, then NewIcon, then the planar image drawn with the full AmigaOS palette (pen 0 transparent). `ReadIcons` returns every normal and selected image of each kind present.
- `EmbeddedIconReader` (Core) reads icons contained in files: an `.ico` is one icon with all its images; a Windows executable has an icon per icon group resource. Invalid images are skipped. On Windows 10 and later, system programs keep their icons in `C:\Windows\SystemResources\*.mun`, so their own file has none.

### GUI icons

Each row shows a 16x16 icon. `PaneView.LoadIcons` runs `FileIcons.LoadAsync` after each listing (cancelled when the pane lists again), in this order of precedence:

1. Icons of plugin icon files and inside volume entries (cached by the icon file's location, size, date and pixel size; icons inside volume entries aren't cached). Scaled into the square keeping their aspect ratio.
2. Icons contained in files (`EmbeddedIconReader`): local files on a background thread, files inside images on the UI thread, because the pane's volume isn't thread safe. When a local file has no icon of its own but the OS shows one (`IFileIconProvider.HasFileIcon`, e.g. Windows system programs and shortcuts, `.icns` on macOS), the OS icon of the file is used.
3. Type icons from the host OS, cached per extension, folder and drive: Windows shell icons (`SHGetFileInfo` with attributes, so files don't need to exist), freedesktop icons on Linux (shared MIME database and PNG icon themes; SVG-only icons aren't supported), NSWorkspace on macOS (on the UI thread). Without one, the built-in icons stay.

## Navigation Modes

- A GUI pane shows its entries in a `PaneNavigationMode`: the built-in `List` (the default) and `Thumbnail`, followed by `VolumeProviders.NavigationModes` from plugins. The View menu lists them; Ctrl+1 to Ctrl+9 choose the first nine. Each pane's mode is saved by name in `GuiSettings` (unknown names become the list).
- `Thumbnail` uses a `WrapPanel` in the same `ListBox`, so selection, marks and keys work as in the list. Up/Down move to the same column of the previous/next row (counted from the first row's containers), and from the row above a shorter last row Down goes to the last entry.

### Plugin modes

- A plugin mode hides the list and shows an `IconLayoutView` (a canvas in a scroll viewer) with the icons from `NavigationContext.LayoutAsync(mode, entries, options)`.
- A toolbar above the icons has a `..` button (parent), a check box per mode option (values kept per pane and mode for the session, not saved; changing one lays out again) and `Sort by name`, which places all icons in a grid of cells sized by the largest icon and marks the new positions as changed.
- Icons are drawn in their own size (pixels stay sharp) with the name below. Icons without a position are placed in the same kind of grid, skipping cells taken by positioned icons, and placed again when the view is resized. Icons without an image get the entry's icon. When layout fails, the error is shown and the entries are shown without positions.
- Click makes an entry current (shown with the selected image while the pane is active), Ctrl/Shift+click toggles its mark, double-click opens, clicking the background clears the current entry. Arrow keys go to the nearest icon in that direction (by image centers, weighting distance across the direction twice); Home/End and Space/Insert go in layout order. Opening a directory selects no icon, like the Workbench.
- Icons can be dragged where `CanSaveIconPositions` (not the drive list or read-only images). A bar below the pane offers Save (`SaveIconPositionsAsync`, then both panes are listed again) and Reset. Moved positions are discarded when another directory is opened.

### Workbench mode (Amiga plugin)

- `WorkbenchNavigationMode` looks like a Workbench 3.1 drawer window and shows the best image of each entry's `.info` file with its selected image (planar icons without one are complemented like the Workbench does) at the disk object's position.
- The `.info` files of listed entries and `Disk.info` are hidden (`Disk.info` in every directory, because the pane's volume doesn't tell the root of an image or partition). Other `.info` files without an entry are shown as themselves.
- Volume entries show the `Disk.info` from their root with its position, like disks on the Workbench screen: `Layout` changes into the entry, reads it and changes back.
- Entries without a valid `.info` file get a def icon (see below) of a drawer, tool (an Amiga executable, detected by its hunk header), project or disk, without a position; otherwise a built-in default icon (`DefaultIcons`, since Hst.Amiga's default disk objects only have a 1x1 image).
- Saving updates the position in the `.info` file, keeping its images, or creates one from the def icon or default icon. Volume entries get the position in the `Disk.info` of their root. An invalid `.info` file throws `IOException` and isn't overwritten.
- `Icons only` (unchecked by default) shows only entries with a `.info` file, like Show Only Icons on the Workbench; volume entries are always shown.

### Def icons

Like `ENVARC:Sys/def_*.info` on the Amiga, `DefIcons` reads `def_drawer`, `def_tool`, `def_project` and `def_disk` files (case-insensitive, `.info` first) from the Amiga plugin directory (`IPluginHost.GetPluginDirectory("Amiga")`, i.e. `~/.hst-commander/plugins/amiga`; the plugin loader ignores that directory since it has no `amiga.dll`). A `.info` def icon is shown with its normal and selected images; other files are read with `IPluginHost.ReadImages` (the first image). Too large or unreadable files are skipped. Def icons are cached by path, date and length and looked up once per layout. The plugin's `readme.md` describes them for users.
