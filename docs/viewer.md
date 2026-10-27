# Viewer

The F3 viewer: text, hex and image views, searching, selecting and copying. Read this when working on `ViewerDocument`, `ViewerSession` or a viewer front-end.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Opening

- F3, the `F3 View` button or File > View opens the file under the cursor (not directories or `..`). `NavigationContext.OpenViewerAsync(entry)` returns a `ViewerSession` over a `ViewerDocument`.
- Local files are read directly with sharing that lets other programs keep writing and deleting; files inside images and other volumes use a volume of their own from `OpenLocationAsync`, so the viewer can read and search on a background thread. A stream that isn't seekable (e.g. inside a zip) is copied to a temporary file first. The document owns the stream and the location.

## ViewerDocument

- Reads through a small block cache, so only what's shown or searched is read and files of any size can be viewed. Positions are byte offsets.
- Text is split into `ViewerLine`s at LF, CR LF or CR, or after `MaxLineLength` bytes (never inside a UTF-8 character). The encoding is detected from the start of the file: a UTF-8 BOM (skipped) or valid UTF-8 is UTF-8, otherwise ISO-8859-1. Tabs are expanded to 8 columns and control characters shown as `.`.
- `GetLineOffset(position)` finds a line start by looking back a limited distance (a longer line is split there); it's used for scrolling up and switching from the hex view.
- A file with zero bytes at its start is `IsBinary` and opens in the hex view.
- The hex view shows 16 bytes per row: offset (8 hex digits, 16 above 4 GB), hex values with an extra space after 8, and printable ASCII as text.

## Searching

- `FindAsync(ViewerSearchOptions)` runs on the thread pool line by line in the *shown* text (so a tab in the search text doesn't match an expanded tab), case-insensitive unless case sensitive. Regular expressions match within a line, with a timeout per line; empty matches are ignored. Empty text or an invalid regex throws `IOException`.
- A `ViewerMatch` has the line offset, column and length in the shown text and the byte range (used in the hex view). Progress is reported as a fraction of the file.
- The session searches from the top of the view; `FindNextAsync(backwards)` continues from the last match with the last options. A match not in view is scrolled to a third from the top (and horizontally into view).

## ViewerSession

Holds the state both front-ends share: text or hex view (`ToggleHex` keeps the position), top offset, horizontal scroll of the text view, scrolling by lines and pages (stopping at the last page), scroll bar positions, the match and the status text (e.g. `Offset 1,024 of 4,096 bytes, 50%, UTF-8`).

### Selecting and copying

- The `Selection` is a byte range, so it survives scrolling and switching views. `StartSelection`/`ExtendSelection` take cells of the view and include whole characters at both ends; a cell after the end of a line selects its line break, below the last line goes to the end of the file.
- In the hex view, whether selecting started in the hex values or the text column decides what is copied (`IsHexValues`).
- `GetSelectedText`: in the text view the text decoded as it is in the file (tabs and line breaks kept); in the hex view hex values with a line per row, or the text column. More than `MaxCopyLength` throws `IOException`.

### Images

GUI only. The TUI passes no image providers, as a terminal can't show images well, so images open in the hex view like other binary files.

- `ViewerDocument.ReadImagesAsync` reads the whole file into memory (with a size limit, and a much lower one for files that aren't `IsBinary`, since image headers contain zero bytes) and asks `VolumeProviders.Images` in order on the thread pool, a fresh stream each; the first non-empty result wins and provider errors are ignored.
- A session with images starts in the image view. F4 switches between the image and hex views. The keys and mouse wheel that would scroll show the next/previous image instead; searching switches to the hex view first; selecting is off.
- Zoom: fitted (the default) or one of `ViewerSession.ZoomLevels`, kept when showing another image. `GetImageLayout(viewWidth, viewHeight)` centers images smaller than the view; for larger ones the session keeps the image point at the center of the view (as fractions). `ZoomIn`/`ZoomOut` step from the scale shown, also from fitted. `PanImage` moves an image larger than the view while keeping the view filled, and returns false when there's nothing to move.
- When an image is larger than the view, arrow keys, the mouse wheel and dragging move it; otherwise they change the image. PageUp/PageDown and Home/End always change the image.
- Built-in providers: `WindowsIconImageProvider` (`.ico`, all images, largest first) and `StandardImageProvider` (SkiaSharp: PNG, JPEG, GIF, BMP, WebP; every frame of animated GIF/WebP up to a limit, frames drawn over the frame they depend on; very large images are decoded smaller where the format allows it, otherwise not shown).
- `AmigaImageProvider` (Amiga plugin): Amiga icons (every image `AmigaIconProvider.ReadIcons` reads) and IFF pictures read by `IlbmReader` (ILBM with 1-8 planes, EHB, HAM6/HAM8, 24/32-bit deep, ByteRun1, masks; PBM; several in a LIST or CAT; the first frame of an ANIM).
- `ImagePixels` has the shared pixel helpers: decoding PNG to BGRA, rendering a scaled area without a bitmap, and fitting (small images are enlarged by a whole factor so pixels stay sharp).
- The GUI draws the image without smoothing when enlarged and with smoothing when reduced. It shows scroll bars only when a zoomed image is wider or taller than the view, decided for the area with the scroll bars shown so they hide again when zooming out. They are toggled with `ScrollBar.Visibility`, because Avalonia sets `IsVisible` again from it when the range changes.

## Keys

Both viewers: arrows, PageUp/PageDown (Space), Home/End, mouse wheel, F4 hex/text, F7, Ctrl+F or `/` search, F5 next, F6 previous, Ctrl+C or Ctrl+Insert copy, Ctrl+A select all, Esc or F10 close (Esc cancels a running search instead). Dragging selects (dragging outside the view scrolls), Shift+click extends and a click clears. In the image view `+`/`-` zoom, `0` fits and `1` shows 100%.
