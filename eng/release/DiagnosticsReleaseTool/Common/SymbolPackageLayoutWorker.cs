using System.IO;

namespace ReleaseTool.Core
{
    public class SymbolPackageLayoutWorker : PassThroughLayoutWorker
    {
        public SymbolPackageLayoutWorker(string stagingPath) : base(
            shouldHandleFileFunc: static file => file.Name.EndsWith(".symbols.nupkg"),
            getRelativePublishPathFromFileFunc: static file => Helpers.GetDefaultPathForFileCategory(file, FileClass.SymbolPackage),
            getMetadataForFileFunc: static file => Helpers.GetDefaultFileMetadata(file, FileClass.SymbolPackage),
            stagingPath
        ) {}
    }
}