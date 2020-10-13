using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ReleaseTool.Core
{
    public class PassThroughLayoutWorker : ILayoutWorker
    {
        private readonly Func<FileInfo, bool> _shouldHandleFileFunc;
        private readonly Func<FileInfo, string> _getRelPublishPathFromFileFunc;
        private readonly Func<FileInfo, FileMetadata> _getMetadataForFileFunc;

        public PassThroughLayoutWorker(
            Func<FileInfo, bool> shouldHandleFileFunc = null,
            Func<FileInfo, string> getRelPublishPathFromFileFunc = null,
            Func<FileInfo, FileMetadata> getMetadataForFileFunc = null)
        {

            _shouldHandleFileFunc = shouldHandleFileFunc is null 
                                        ? _ => true
                                        : shouldHandleFileFunc;

            _getRelPublishPathFromFileFunc = getRelPublishPathFromFileFunc is null 
                                                ? (file) => Path.Combine(FileMetadata.GetDefaultCatgoryForClass(FileClass.Unknown), file.Name)
                                                : getRelPublishPathFromFileFunc;

            _getMetadataForFileFunc = getMetadataForFileFunc is null
                                        ? (_) => new FileMetadata(FileClass.Unknown)
                                        : getMetadataForFileFunc;
        }

        public void Dispose() {}

        public ValueTask<LayoutWorkerResult> HandleFileAsync(FileInfo file, CancellationToken ct)
        {
            if (!_shouldHandleFileFunc(file))
            {
                return new ValueTask<LayoutWorkerResult>(new LayoutWorkerResult(LayoutResultStatus.FileNotHandled));
            }

            string publishRelPath = Path.Combine(_getRelPublishPathFromFileFunc(file), file.Name);

            var fileMap = new FileMapping(file.FullName, publishRelPath);
            var metadata = _getMetadataForFileFunc(file);

            return ValueTask.FromResult(
                new LayoutWorkerResult(
                    LayoutResultStatus.FileHandled,
                    new SingleFileResult(fileMap, metadata)
                ));
        }
    }
}