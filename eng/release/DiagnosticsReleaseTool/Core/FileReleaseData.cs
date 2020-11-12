namespace ReleaseTool.Core
{
    public class FileReleaseData
    {
        public FileReleaseData(FileMapping fileMap, FileMetadata fileMetadata)
            : this(fileMap, fileMetadata, null) {}

        private FileReleaseData(FileMapping fileMap, FileMetadata fileMetadata, string publishUri)
        {
            FileMap = fileMap;
            FileMetadata = fileMetadata;
            PublishUri = publishUri;
        }

        public FileMapping FileMap { get; }
        public FileMetadata FileMetadata { get; }
        public string PublishUri { get; internal set; }
    }
}