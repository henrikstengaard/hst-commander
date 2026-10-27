# Testing

Test helpers in `Hst.Commander.Core.Tests`.

Paths are relative to `src/`. Part of the agent docs, see [`src/agents.md`](../src/agents.md).

## Test Helpers

- `TestVolume` (`Hst.Commander.Core.Tests/TestVolume.cs`): in-memory `IVolume` backed by a `Dictionary<string, List<TestEntry>>`. Supports `..` in `ChangeDirectory`, reading/writing `TestEntry.Data`, creating, deleting and renaming entries; `GetEntry(pathComponents)` looks up an entry for assertions.
- `TestVolumeProvider` (`Hst.Commander.Core.Tests/TestVolumeProvider.cs`): `IVolumeProvider` creating a new `TestVolume` filled by a setup action on each `CreateVolume`.
- `TestEntry` (`Hst.Commander.Core.Tests/TestEntry.cs`): in-memory `IEntry` with optional `byte[]? Data`.
- Use `AddEntry(pathComponents, entry)` to build the in-memory tree.
- `ZipTestData` (`Hst.Commander.Core.Tests/ZipTestData.cs`): creates zip archives in memory (`CreateZip((name, content)...)`, names ending with `/` are directories).
- `AmigaTestData` (`Hst.Commander.Core.Tests/AmigaTestData.cs`): creates a formatted adf (`CreateAdf`), raw and dynamic vhd RDB images with Fast File System (`Dos3DosType`) and PFS3 (`Pfs3DosType`) partitions (`CreateRdbImage`, `CreateVhdRdbImage`) and lists an adf mounted again (`ListAdfNames`).
- `SingleThreadSynchronizationContext` (`Hst.Commander.Core.Tests/SingleThreadSynchronizationContext.cs`): `Run(operation, timeout)` runs an operation with continuations on one thread like a UI thread, throwing `TimeoutException` on a deadlock.
- `AmigaIconTestData` (`Hst.Commander.Core.Tests/AmigaIconTestData.cs`): creates Amiga .info icons with a planar image (`CreatePlanarIcon`, optionally with a selected image and a Workbench position), an 8 color planar image (`CreateEightColorPlanarIcon`) or a NewIcon image (`CreateNewIcon`).
- `DiskTestData` (`Hst.Commander.Core.Tests/DiskTestData.cs`): creates an MBR disk with a FAT partition (`CreateMbrDisk`), a GPT disk with FAT and NTFS partitions (`CreateGptDisk`), GPT and MBR disks with 4096 byte sectors (`CreateLargeSectorDisk`), an MBR disk with a small partition that would also fit with 4096 byte sectors (`CreateSmallPartitionMbrDisk`), PiStorm MBR and GPT disks with a `0x76` or `3F82EEBC-...` partition holding a rigid disk block (`CreatePiStormDisk`, `CreateGptPiStormDisk`, using `AmigaTestData.WriteRdbToStream`) and a hybrid disk with the rigid disk block in block 2 and an MBR in block 0 (`CreateHybridDisk`).
- `IlbmTestData` (`Hst.Commander.Core.Tests/IlbmTestData.cs`): creates IFF ILBM pictures from color indices (`CreateIlbm`: bitplanes, optionally ByteRun1 compressed, a CMAP, CAMG view modes and a mask plane) and a LIST of them (`CreateList`).
- `ArchiveTestData` (`Hst.Commander.Core.Tests/ArchiveTestData.cs`): lha and lzx archives in `TestData/Archives` (from the Hst.Compression tests), e.g. `dirs-files.lha`/`.lzx` (`ArchiveTestData.DirsFiles`), and `special_chars.lha`/`.lzx` (from the Hst Imager tests) with Amiga names, protection bits and comments.

