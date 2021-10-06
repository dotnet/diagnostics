using System.IO;

namespace ReleaseTool.Core
{
    public sealed class NugetLayoutWorker : PassThroughLayoutWorker
    {
        public NugetLayoutWorker(string stagingPath) : base(
            shouldHandleFileFunc: static file => file.Extension == ".nupkg" && !file.Name.EndsWith(".symbols.nupkg"),
            getRelativePublishPathFromFileFunc: static file => Helpers.GetDefaultPathForFileCategory(file, FileClass.Nuget),
            getMetadataForFileFunc: static file => Helpers.GetDefaultFileMetadata(file, FileClass.Nuget),
            stagingPath
        ){}
    }
}