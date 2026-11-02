# Hst.Commander.Core.Tests

xUnit unit tests for `Hst.Commander.Core` and the default plugins (Zip, LhaLzx, Disk, Amiga and PhysicalDisk) and the external Smb plugin.

## Running

```shell
dotnet test src/Hst.Commander.Core.Tests/Hst.Commander.Core.Tests.csproj
```

The tests run on Windows, macOS and Linux for every push (`build-and-test.yml`).

## Coverage

- **Volumes**: local, ISO 9660, zip, lha, lzx and nested volumes, disk images (MBR, GPT, PiStorm and hybrid disks), Amiga images (adf and RDB, Fast File System and PFS3) and physical disks (test disks and the sector-aligned disk stream).
- **Navigation** (`NavigationContext`): opening images, changing location, making directories, moving, creating volumes, selecting groups, properties, icon files and writable Amiga and disk images.
- **File operations**: copying, moving and deleting between volumes, filters and UAE metadata.
- **Viewer**: the viewer document and session, and viewing images.
- **Plugins**: loading plugins and the plugin host.
- **Icons and navigation modes**: Amiga icons, icons contained in files and the Workbench navigation mode.
- **Other**: application info, starting files, size formatting and volume provider key bindings.

## Conventions

- Test classes are named `Given<Context>`, e.g. `GivenLocalVolume`, and test methods `When_<action>_Then_<expectation>`.
- Tests are arranged, acted and asserted with `// arrange`, `// act` and `// assert` comments.

## Test helpers

- `TestVolume`, `TestVolumeProvider` and `TestEntry`: an in-memory volume and its provider and entries.
- `ZipTestData`, `AmigaTestData`, `DiskTestData`, `AmigaIconTestData` and `IlbmTestData`: create zip archives, Amiga images, disk images, Amiga icons and IFF ILBM pictures in memory.
- `ArchiveTestData`: lha and lzx archives in `TestData/Archives`.
- `SingleThreadSynchronizationContext`: runs an operation with continuations on one thread like a UI thread, to detect deadlocks.

See [`docs/testing.md`](../../docs/testing.md) for details.
