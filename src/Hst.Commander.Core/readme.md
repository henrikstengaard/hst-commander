# Hst Commander Core

## Volumes

c:\projects
c:\projects\amigaos1.3.adf
c:\projects\amigaos1.3.adf /libs/x

c:, projects, amigaos1.3.adf, libs, x


should the nested volume be in change of magic byte detecting and adding to the nested volume when entering/opening an entry.

volume methods:
- list entries
- ChangeRootDirectory(var directoryName)
- ChangeParentDirectory()
- ChangeDirectory()

what if the volumes only purpose is to list entries from a directory
- that means there is no direct control of changed directories

so how will I use the nested volume if it doesnt remember changed directories?


NavigationContext
- Has the top level volume.
- Stored a location
