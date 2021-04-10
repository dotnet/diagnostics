using System.IO;

namespace DiagnosticsReleaseTool.Impl
{
    internal class Config
    {
        public FileInfo ToolManifest { get; }
        public bool ShouldVerifyManifest { get; }
        public DirectoryInfo DropPath { get; }
        public DirectoryInfo StagingDirectory { get; }
        public string PublishPath { get; }

        public Config(FileInfo toolManifest, bool verifyToolManifest,
            DirectoryInfo inputDropPath, DirectoryInfo stagingDirectory, string publishPath)
        {
            ToolManifest = toolManifest;
            ShouldVerifyManifest = verifyToolManifest;
            DropPath = inputDropPath;
            StagingDirectory = stagingDirectory;
            PublishPath = publishPath;
        }
    }
}