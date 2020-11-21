using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ReleaseTool.Core
{
    public class PassThroughLayoutWorker : ILayoutWorker
    {
        private readonly Func<FileInfo, bool> _shouldHandleFileFunc;
        private readonly Func<FileInfo, string> _getRelativePublishPathFromFileFunc;
        private readonly Func<FileInfo, FileMetadata> _getMetadataForFileFunc;
        private readonly string _stagingPath;

        public PassThroughLayoutWorker(
            Func<FileInfo, bool> shouldHandleFileFunc,
            Func<FileInfo, string> getRelativePublishPathFromFileFunc,
            Func<FileInfo, FileMetadata> getMetadataForFileFunc,
            string stagingPath)
        {

            _shouldHandleFileFunc = shouldHandleFileFunc ?? (_ => true);

            _getRelativePublishPathFromFileFunc = getRelativePublishPathFromFileFunc ?? (file => Path.Combine(FileMetadata.GetDefaultCatgoryForClass(FileClass.Unknown), file.Name));

            _getMetadataForFileFunc = getMetadataForFileFunc ?? (_ => new FileMetadata(FileClass.Unknown));

            _stagingPath = stagingPath;
        }

        public void Dispose() {}

        public async ValueTask<LayoutWorkerResult> HandleFileAsync(FileInfo file, CancellationToken ct)
        {
            if (!_shouldHandleFileFunc(file))
            {
                return new LayoutWorkerResult(LayoutResultStatus.FileNotHandled);
            }

            string publishReleasePath = Path.Combine(_getRelativePublishPathFromFileFunc(file), file.Name);

            string localPath = file.FullName;

            if (_stagingPath is not null)
            {
                localPath = Path.Combine(_stagingPath, publishReleasePath);
                Directory.CreateDirectory(Path.GetDirectoryName(localPath));
                using (FileStream srcStream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read))
                using (FileStream destStream = new FileStream(localPath, FileMode.Create, FileAccess.Write))
                {
                    await srcStream.CopyToAsync(destStream, ct);
                }
            }

            var fileMap = new FileMapping(localPath, publishReleasePath);
            var metadata = _getMetadataForFileFunc(file);

            return new LayoutWorkerResult(
                    LayoutResultStatus.FileHandled,
                    new SingleFileResult(fileMap, metadata));
        }
    }
}