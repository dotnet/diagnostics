using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ReleaseTool.Core
{
    internal class FileSharePublisher : IPublisher
    {
        private readonly string _sharePath;

        public FileSharePublisher(string sharePath)
        {
            _sharePath = sharePath;
        }

        public void Dispose() { }

        public async Task<string> PublishFileAsync(FileMapping fileMap, CancellationToken ct)
        {
            // TODO: Make sure to handle the can't access case.
            // TODO: Be resilient to "can't cancel case".
            string destinationUri = Path.Combine(_sharePath, fileMap.RelativeOutputPath);
            FileInfo fi = new FileInfo(destinationUri);
            fi.Directory.Create();

            if (fi.Exists && fi.Attributes.HasFlag(FileAttributes.Directory))
            {
                // Filestream will deal with files, but not directories
                Directory.Delete(destinationUri, true);
            }

            using (FileStream srcStream = new FileStream(fileMap.LocalSourcePath, FileMode.Open, FileAccess.Read))
            using (FileStream destStream = new FileStream(destinationUri, FileMode.Truncate, FileAccess.Write))
            {
                await srcStream.CopyToAsync(destStream, ct);
            }

            return destinationUri;
        }
    }
}