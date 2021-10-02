using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ReleaseTool.Core
{
    public sealed class ZipLayoutWorker : ILayoutWorker
    {
        private Func<FileInfo, bool> _shouldHandleFileFunc;
        private Func<FileInfo, FileInfo, string> _getRelativePathFromZipAndInnerFileFunc;
        private Func<FileInfo, FileInfo, FileMetadata> _getMetadataForInnerFileFunc;
        private readonly string _stagingPath;

        public ZipLayoutWorker(
            Func<FileInfo, bool> shouldHandleFileFunc,
            Func<FileInfo, FileInfo, string> getRelativePathFromZipAndInnerFileFunc,
            Func<FileInfo, FileInfo, FileMetadata> getMetadataForInnerFileFunc,
            string stagingPath)
        {

            _shouldHandleFileFunc = shouldHandleFileFunc ?? (file => file.Extension == ".zip");

            Func<FileInfo, FileInfo, string> defaultgetRelPathFunc = static (zipFile, innerFile) =>
                                    FormattableString.Invariant($"{Path.GetFileNameWithoutExtension(zipFile.Name)}/{innerFile.Name}");

            _getRelativePathFromZipAndInnerFileFunc = getRelativePathFromZipAndInnerFileFunc ?? defaultgetRelPathFunc;

            _getMetadataForInnerFileFunc = getMetadataForInnerFileFunc ?? (static (_, innerFile) => Helpers.GetDefaultFileMetadata(innerFile, FileClass.Blob));

            _stagingPath = stagingPath;
        }

        public void Dispose() { }

        public async ValueTask<LayoutWorkerResult> HandleFileAsync(FileInfo file, CancellationToken ct)
        {
            if (!_shouldHandleFileFunc(file))
            {
                return new LayoutWorkerResult(LayoutResultStatus.FileNotHandled);
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
                return new LayoutWorkerResult(LayoutResultStatus.Error);
            }


            var filesInToolBundleToPublish = new List<(FileMapping, FileMetadata)>();

            foreach (FileInfo extractedFile in unzipDirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                if (ct.IsCancellationRequested)
                {
                    return new LayoutWorkerResult(LayoutResultStatus.Error);
                }

                string relativePath = _getRelativePathFromZipAndInnerFileFunc(file, extractedFile);
                string localPath = extractedFile.FullName;

                if (_stagingPath is not null)
                {
                    localPath = Path.Combine(_stagingPath, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(localPath));
                    using (FileStream srcStream = new FileStream(extractedFile.FullName, FileMode.Open, FileAccess.Read))
                    using (FileStream destStream = new FileStream(localPath, FileMode.Create, FileAccess.Write))
                    {
                        await srcStream.CopyToAsync(destStream, ct);
                    }
                }

                var fileMap = new FileMapping(localPath, relativePath);
                FileMetadata metadata = _getMetadataForInnerFileFunc(file, extractedFile);
                filesInToolBundleToPublish.Add((fileMap, metadata));
            }

            return new LayoutWorkerResult(LayoutResultStatus.FileHandled, filesInToolBundleToPublish);
        }
    }
}