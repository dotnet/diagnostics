using System.IO;

namespace ReleaseTool.Core
{
    public class SymbolPackageLayoutWorker : PassThroughLayoutWorker
    {
        public SymbolPackageLayoutWorker(string stagingPath) : base(
            shouldHandleFileFunc: ShouldHandleFile,
            getRelativePublishPathFromFileFunc: GetSymbolPackagePublishRelPath,
            getMetadataForFileFunc: (_) => new FileMetadata(FileClass.SymbolPackage),
            stagingPath
        ) {}

        private static bool ShouldHandleFile(FileInfo file) => file.Name.EndsWith(".symbols.nupkg");
        private static string GetSymbolPackagePublishRelPath(FileInfo file) => FileMetadata.GetDefaultCatgoryForClass(FileClass.SymbolPackage);
    }
}