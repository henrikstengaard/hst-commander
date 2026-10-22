# Hst.Commander.Abstractions

A class library providing volume abstractions that allows building different kinds of volumes Hst Commander can browse.

This library can be used to implement custom volumes that can be used by Hst Commander to browse different kinds of volumes.

As an example this library is used by the Hst Commander Core to implement a build-in local volume allowing Hst Commander to list local drives and browse directories and files.

Plugins reference only this library: implement `IPlugin` returning `IVolumeProvider`s (volumes a pane can show) and `IFileVolumeProvider`s (files opened as volumes, detected by their magic signature), `IVolumeCreator`s (new volumes created as files) and `IIconProvider`s (icons read from icon files next to entries, e.g. Amiga .info files), see the Zip plugin `Hst.Commander.Plugins.Zip`.

Entries can give a name shown in the panes (`IEntry.DisplayName`) and details shown in the properties dialog (`IEntry.Details`, `EntryDetails` with `EntryProperty` lines), e.g. the drive and volume name of an Amiga partition.
