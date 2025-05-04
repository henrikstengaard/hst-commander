// namespace Hst.Commander.Plugins.Amiga;
//
// using Hst.Amiga.FileSystems.FastFileSystem;
//
// public class AmigaAdfContainerProvider : IContainerProvider
// {
//     private FastFileSystemVolume volume;
//     
//     public async Task Init(string path)
//     {
//         volume = FastFileSystemHelper.MountAdf(System.IO.File.Open(path, FileMode.Open, FileAccess.ReadWrite));
//     }
//     
//     public Task<IEnumerable<IEntry>> GetEntries(string path)
//     {
//         throw new NotImplementedException();
//     }
//
//     public string CombinePath(string path1, string path2)
//     {
//         throw new NotImplementedException();
//     }
//
//     public string GetParentPath(string path)
//     {
//         throw new NotImplementedException();
//     }
//
//     public Task NewContainer(string path)
//     {
//         throw new NotImplementedException();
//     }
// }