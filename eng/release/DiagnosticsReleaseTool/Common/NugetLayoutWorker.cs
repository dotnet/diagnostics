using System.IO;

namespace ReleaseTool.Core
{
    public sealed class NugetLayoutWorker : PassThroughLayoutWorker
    {
        public NugetLayoutWorker(string stagingPath) : base(
            shouldHandleFileFunc: ShouldHandleFile,
            getRelativePublishPathFromFileFunc: GetNugetPublishRelPath,
            getMetadataForFileFunc: (_) => new FileMetadata(FileClass.Nuget),
            stagingPath
        ) {}

        private static bool ShouldHandleFile(FileInfo file) => file.Extension == ".nupkg" && !file.Name.EndsWith(".symbols.nupkg");
        private static string GetNugetPublishRelPath(FileInfo file) => FileMetadata.GetDefaultCatgoryForClass(FileClass.Nuget);
    }
}