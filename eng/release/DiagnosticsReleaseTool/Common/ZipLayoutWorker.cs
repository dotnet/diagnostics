using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ReleaseTool.Core
{
    public sealed class ZipLayoutWorker : ILayoutWorker
    {
        private readonly Func<FileInfo, bool> _shouldHandleFileFunc;
        private readonly Func<FileInfo, FileInfo, string> _getRelPathFromZipAndInnerFileFunc;
        private readonly Func<FileInfo, FileInfo, FileMetadata> _getMetadataForInnerFileFunc;

        public ZipLayoutWorker(
            Func<FileInfo, bool> shouldHandleFileFunc = null,
            Func<FileInfo, FileInfo, string> getRelPathFromZipAndInnerFileFunc = null,
            Func<FileInfo, FileInfo, FileMetadata> getMetadataForInnerFileFunc = null)
        {

            _shouldHandleFileFunc = shouldHandleFileFunc is null 
                                        ? file => file.Extension == ".zip"
                                        : shouldHandleFileFunc;

            _getRelPathFromZipAndInnerFileFunc = getRelPathFromZipAndInnerFileFunc is null 
                                                    ? (zipFile, innerFile) => Path.Combine(zipFile.Name, innerFile.Name)
                                                    : getRelPathFromZipAndInnerFileFunc;

            _getMetadataForInnerFileFunc = getMetadataForInnerFileFunc is null
                                                ? (_, _) => new FileMetadata(FileClass.Blob)
                                                : getMetadataForInnerFileFunc;
        }

        public void Dispose() { }

        public ValueTask<LayoutWorkerResult> HandleFileAsync(FileInfo file, CancellationToken ct)
        {
            if (!_shouldHandleFileFunc(file))
            {
                return new ValueTask<LayoutWorkerResult>(new LayoutWorkerResult(LayoutResultStatus.FileNotHandled));
            }

            DirectoryInfo unzipDirInfo = null;

            try {
                do {
                    string tempUnzipPath = Path.Join(Path.GetTempPath(), Path.GetRandomFileName());
                    unzipDirInfo = new DirectoryInfo(tempUnzipPath);
                } while(unzipDirInfo.Exists);

                unzipDirInfo.Create();
                // TODO: Do we really want to block because of unzipping. We could use ZipArchive.
                System.IO.Compression.ZipFile.ExtractToDirectory(file.FullName, unzipDirInfo.FullName);
            }
            catch(Exception ex) when (ex is IOException || ex is System.Security.SecurityException)
            {
                return new ValueTask<LayoutWorkerResult>(new LayoutWorkerResult(LayoutResultStatus.Error));
            }


            var filesInToolBundleToPublish = new List<(FileMapping, FileMetadata)>();

            foreach (FileInfo extractedFile in unzipDirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                if (ct.IsCancellationRequested)
                {
                    return ValueTask.FromResult(new LayoutWorkerResult(LayoutResultStatus.Error));
                }

                string relPath = _getRelPathFromZipAndInnerFileFunc(file, extractedFile);
                relPath = Path.Combine(relPath, extractedFile.Name);

                var fileMap = new FileMapping(extractedFile.FullName, relPath);
                FileMetadata metadata = _getMetadataForInnerFileFunc(file, extractedFile);
                filesInToolBundleToPublish.Add((fileMap, metadata));
            }

            return ValueTask.FromResult(new LayoutWorkerResult(LayoutResultStatus.FileHandled, filesInToolBundleToPublish));
        }
    }
}