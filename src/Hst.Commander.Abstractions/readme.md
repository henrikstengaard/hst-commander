# Hst.Commander.Abstractions

The plugin API of Hst Commander. It is a class library without external dependencies with the interfaces volumes and plugins implement, so any container, such as a local directory, a disk image or an archive, can be browsed in the same dual-pane UI.

Hst Commander Core implements its built-in volumes (local drives and directories, ISO 9660 images) with it, and plugins reference only this library. See [`docs/plugin-api.md`](../../docs/plugin-api.md) for the full contracts.

## Volumes and entries

- **`IVolume`**: a browsable container with a current directory. It lists entries, changes directory (handling `Paths.Root` and `Paths.Parent`), opens entries for reading and writing, creates containers, deletes, renames and sets modified dates. Read-only volumes throw `NotSupportedException` from the write members.
- **`IEntry`**: an entry with a `Name`, `Type` (container or item), `Size` and `ModifiedDate`, and optionally:
  - `DisplayName`, the name shown in the panes, e.g. `1 - DH0 - Workbench` for a partition.
  - `Details`, sections of `EntryProperty` lines shown in the properties dialog (`EntryDetails`), e.g. the drive and volume name of an Amiga partition.
  - `IsVolume`, a container that is the root of a volume of its own, e.g. a partition.
  - `AmigaMetadata`, the Amiga protection bits and comment.
  - `Attributes`, DOS/Windows attributes (`EntryAttributes`).
- **`Paths`**: the root (`/`) and parent (`..`) path components.

### Amiga metadata and attributes

- Volumes storing Amiga names, protection bits and comments (Amiga file systems) return true from `IVolume.SupportsAmigaMetadata` and set them with `IVolume.SetAmigaMetadata`. Hst Commander keeps them when copying between such volumes and stores them as UAE metadata on other volumes.
- The properties dialog shows check boxes for the attributes and Amiga protection bits. Volumes that can change them return the attributes from `IVolume.GetChangeableAttributes` and set them with `IVolume.SetAttributes`, and return true from `IVolume.CanSetAmigaMetadata`; the check boxes are disabled otherwise.

## Plugins

A plugin implements `IPlugin`, which returns the plugin's:

| Interface | Purpose |
|---|---|
| `IVolumeProvider` | Volumes a pane can show, chosen with Alt+F1/Alt+F2 |
| `IFileVolumeProvider` | Files opened as volumes (images), detected by their magic signature with `IsMatch`; `CanWrite` for writable images |
| `IVolumeCreator` | New volumes created as files with F9, with input fields (`VolumeCreatorField`) |
| `IIconProvider` | Icons read from icon files next to entries, e.g. Amiga `.info` files, and lines for the properties dialog |
| `INavigationMode` | Directories shown as icons at positions in the GUI, e.g. the Amiga Workbench, with options shown as check boxes |
| `IImageProvider` | Images of a file format shown in the viewer (F3), e.g. IFF ILBM pictures |

`IPlugin.Initialize` gets an `IPluginHost`, which lets plugins work together without sharing types, since each plugin is loaded in a load context of its own:

- `OpenVolume` opens a stream with the file volume providers of all plugins, e.g. the Disk plugin opens a PiStorm partition with the Amiga plugin.
- `ReadImages` reads an image file with the image providers of Hst Commander and all plugins (e.g. PNG, JPEG, GIF, IFF ILBM).
- `GetPluginDirectory` gives the directory of the plugin's own files, `~/.hst-commander/plugins/<name>` (the plugin's name in lower case), e.g. the def icons of the Amiga plugin.

## Writing a plugin

1. Create a class library for .NET 10 referencing this library with `<Private>false</Private>` and `<ExcludeAssets>runtime</ExcludeAssets>`, so it isn't copied to the output.
2. Implement `IPlugin` with a public parameterless constructor and return the plugin's providers. See the Zip plugin, `Hst.Commander.Plugins.Zip`, for a small example.
3. Copy the build output to `~/.hst-commander/plugins/<Name>/` or `plugins/<Name>/` next to the Hst Commander executable, with `<Name>.dll` as the plugin assembly. Plugins are loaded at startup.

Copying, moving and deleting only use `IVolume`, so a new volume type gets them for free.
