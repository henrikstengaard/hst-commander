# Hst.Commander.Abstractions

A class library providing volume abstractions that allows building different kinds of volumes Hst Commander can browse.

This library can be used to implement custom volumes that can be used by Hst Commander to browse different kinds of volumes.

As an example this library is used by the Hst Commander Core to implement a build-in local volume allowing Hst Commander to list local drives and browse directories and files.

Plugins reference only this library: implement `IPlugin` returning `IVolumeProvider`s (volumes a pane can show) and `IFileVolumeProvider`s (files opened as volumes, detected by their magic signature), `IVolumeCreator`s (new volumes created as files) and `IIconProvider`s (icons read from icon files next to entries, e.g. Amiga .info files), see the Zip plugin `Hst.Commander.Plugins.Zip`.

Entries can give a name shown in the panes (`IEntry.DisplayName`) and details shown in the properties dialog (`IEntry.Details`, `EntryDetails` with `EntryProperty` lines), e.g. the drive and volume name of an Amiga partition.

Volumes storing Amiga names, protection bits and comments (Amiga file systems) return true from `IVolume.SupportsAmigaMetadata`, give entries their `IEntry.AmigaMetadata` and set them with `IVolume.SetAmigaMetadata`; Hst Commander keeps them when copying between such volumes and stores them as UAE metadata on other volumes.

The properties dialog shows check boxes for `IEntry.Attributes` (DOS/Windows attributes, `EntryAttributes`) and the Amiga protection bits. Volumes that can change them return the attributes from `IVolume.GetChangeableAttributes` and set them with `IVolume.SetAttributes`, and return true from `IVolume.CanSetAmigaMetadata` when `IVolume.SetAmigaMetadata` can change the protection bits; the check boxes are disabled otherwise.

Icon providers can add lines to the properties dialog of an entry with an icon file with `IIconProvider.ReadIconProperties`, e.g. the position of an Amiga icon.

Navigation modes (`IPlugin.NavigationModes`, `INavigationMode`) show the entries of a directory in the GUI panes as icons at positions, next to the built-in list and thumbnail modes, e.g. the Amiga Workbench. `INavigationMode.Layout` returns the icons with their images and positions (icons without a position are placed by Hst Commander, which also shows a `..` button going to the parent) and `INavigationMode.SavePositions` stores the positions of icons the user dragged. `INavigationMode.Options` are check boxes shown next to the `..` button (e.g. `Icons only`), whose values are passed to `Layout`.

Plugins get an `IPluginHost` in `IPlugin.Initialize`: `OpenVolume` opens a stream with the file volume providers of all plugins, `ReadImages` reads an image file with the image providers of Hst Commander and all plugins (e.g. PNG, JPEG, GIF, IFF ILBM) and `GetPluginDirectory` gives the directory of the plugin's own files, `~/.hst-commander/plugins/<name>` (the plugin's name in lower case), e.g. the def icons of the Amiga plugin.
