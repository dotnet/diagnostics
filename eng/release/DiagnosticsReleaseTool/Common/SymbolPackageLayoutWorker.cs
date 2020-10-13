using System.IO;

namespace ReleaseTool.Core
{
    public class SymbolPackageLayoutWorker : PassThroughLayoutWorker
    {
        public SymbolPackageLayoutWorker() : base(
            shouldHandleFileFunc: ShouldHandleFile,
            getRelPublishPathFromFileFunc: GetSymbolPackagePublishRelPath,
            getMetadataForFileFunc: (_) => new FileMetadata(FileClass.SymbolPackage)
        ) {}

        private static bool ShouldHandleFile(FileInfo file) => file.Name.EndsWith(".symbols.nupkg");
        private static string GetSymbolPackagePublishRelPath(FileInfo file) => FileMetadata.GetDefaultCatgoryForClass(FileClass.SymbolPackage);
    }
}