namespace ReleaseTool.Core
{
    public struct FileMapping
    {
        public FileMapping(string localSourcePath, string relativeOutputPath)
        {
            LocalSourcePath = localSourcePath;
            RelativeOutputPath = relativeOutputPath;
        }

        public string LocalSourcePath { get; }

        public string RelativeOutputPath { get; }
    }
}