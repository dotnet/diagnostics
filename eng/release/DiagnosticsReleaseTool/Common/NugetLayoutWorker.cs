using System.IO;

namespace ReleaseTool.Core
{
    public sealed class NugetLayoutWorker : PassThroughLayoutWorker
    {
        public NugetLayoutWorker(string stagingPath) : base(
            shouldHandleFileFunc: ShouldHandleFile,
            getRelativePublishPathFromFileFunc: GetNugetPublishRelativePath,
            getMetadataForFileFunc: (_) => new FileMetadata(FileClass.Nuget),
            stagingPath
        ) {}

        private static bool ShouldHandleFile(FileInfo file) => file.Extension == ".nupkg" && !file.Name.EndsWith(".symbols.nupkg");
        private static string GetNugetPublishRelativePath(FileInfo file) => FileMetadata.GetDefaultCatgoryForClass(FileClass.Nuget);
    }
}