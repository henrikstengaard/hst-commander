# Hst.Commander.Abstractions

A class library providing volume abstractions that allows building different kinds of volumes Hst Commander can browse.

This library can be used to implement custom volumes that can be used by Hst Commander to browse different kinds of volumes.

As an example this library is used by the Hst Commander Core to implement a build-in local volume allowing Hst Commander to list local drives and browse directories and files.

Plugins reference only this library: implement `IPlugin` returning `IVolumeProvider`s (volumes a pane can show) and `IFileVolumeProvider`s (files opened as volumes, detected by their magic signature), see the Zip plugin `Hst.Commander.Plugins.Zip`.
