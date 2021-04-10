using System.IO;

namespace ReleaseTool.Core
{
    public class SymbolPackageLayoutWorker : PassThroughLayoutWorker
    {
        public SymbolPackageLayoutWorker(string stagingPath) : base(
            shouldHandleFileFunc: ShouldHandleFile,
            getRelativePublishPathFromFileFunc: GetSymbolPackagePublishRelativePath,
            getMetadataForFileFunc: (_) => new FileMetadata(FileClass.SymbolPackage),
            stagingPath
        ) {}

        private static bool ShouldHandleFile(FileInfo file) => file.Name.EndsWith(".symbols.nupkg");
        private static string GetSymbolPackagePublishRelativePath(FileInfo file) => FileMetadata.GetDefaultCatgoryForClass(FileClass.SymbolPackage);
    }
}