using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ReleaseTool.Core
{
    public class Release : IDisposable
    {
        // TODO: there might be a need to expose this for multiple product roots.
        private readonly DirectoryInfo _productBuildPath;
        private readonly List<ILayoutWorker> _layoutWorkers;
        private readonly List<IReleaseVerifier> _verifiers;
        private readonly IPublisher _publisher;
        private readonly IManifestGenerator _manifestGenerator;
        private readonly string _manifestSavePath;

        private readonly List<FileReleaseData> _filesToRelease;
        private ILogger _logger;

        public Release(DirectoryInfo productBuildPath, 
            List<ILayoutWorker> layoutWorkers, List<IReleaseVerifier> verifiers,
            IPublisher publisher, IManifestGenerator manifestGenerator, string manifestSavePath)
        {
            _productBuildPath = productBuildPath;
            _layoutWorkers = layoutWorkers;
            _verifiers = verifiers;
            _publisher = publisher;
            _manifestGenerator = manifestGenerator;
            _manifestSavePath = manifestSavePath;
            _filesToRelease = new List<FileReleaseData>();
            _logger = null;
            // TODO: Validate drop to publish exists.
        }

        public void UseLogger(ILogger logger)
        {
            _logger = logger;
        }

        internal async Task<int> RunAsync(CancellationToken ct)
        {
            EnsureLoggerAvailable();

            int unusedFiles = 0;
            try
            {
                unusedFiles = await LayoutFilesAsync(ct);

                // TODO: Implement switch to ignore files that are not used as option.
                if (unusedFiles != 0)
                {
                    return unusedFiles;
                }

                // TODO: Verification

                unusedFiles = await PublishFiles(ct);
                if (unusedFiles != 0)
                {
                    return unusedFiles;
                }

                return await GenerateAndPublishManifestAsync(ct);
            }
            catch (TaskCanceledException)
            {
               _logger.LogError("Cancellation issued.");
                return -1;
            }
            catch (AggregateException agEx)
            {
               _logger.LogError("Aggregate Exception");

                foreach (var ex in agEx.InnerExceptions)
                   _logger.LogError(ex, "Inner Exception");

                return -1;
            }
            catch (Exception ex)
            {
               _logger.LogError(ex, "Exception");
                return -1;
            }
        }

        private void EnsureLoggerAvailable()
        {
            if (_logger is not null)
            {
                return;
            }

            _logger = NullLogger.Instance;
        }

        private async Task<int> GenerateAndPublishManifestAsync(CancellationToken ct)
        {
            // Manifest
            using var scope = _logger.BeginScope("Manifest Generation");
            Stream manifestStream = _manifestGenerator.GenerateManifest(_filesToRelease);
            FileInfo fi = new FileInfo(_manifestSavePath);
            fi.Directory.Create();

            using (FileStream fs = fi.Open(FileMode.Truncate, FileAccess.Write))
            {
                await manifestStream.CopyToAsync(fs, ct);
            }

           _logger.LogInformation("Manifest saved to {_manifestSavePath}", _manifestSavePath);

            // We save the manifest at the root.
            string manifestPublishPath = await _publisher.PublishFileAsync(
                new FileMapping(_manifestSavePath, fi.Name),
                ct
            );

            if (manifestPublishPath is null)
            {
                _logger.LogError("Couldn't publish manifest");
                return -1;
            }

            _logger.LogInformation("Published manifest to {manifestPublishPath}", manifestPublishPath);

            return 0;
        }

        private async Task<int> PublishFiles(CancellationToken ct)
        {
            int unpublishedFiles = 0;

           using var scope = _logger.BeginScope("Publishing files");

            foreach (FileReleaseData releaseData in _filesToRelease)
            {
                if (ct.IsCancellationRequested)
                {
                   _logger.LogError("Cancellation issued.");
                    return -1;
                }

                string sourcePath = releaseData.FileMap.LocalSourcePath;
                string relOutputPath = releaseData.FileMap.RelativeOutputPath;
                string publishUri = await _publisher.PublishFileAsync(releaseData.FileMap, ct);
                if (publishUri is null)
                {
                    _logger.LogWarning("Failed to publish {sourcePath}");
                    unpublishedFiles++;
                }
                else
                {
                    _logger.LogTrace("Published {sourcePath} to relative path {relOutputPath} at {publishUri}", sourcePath, relOutputPath, publishUri);
                    releaseData.PublishUri = publishUri;
                }
            }

            return unpublishedFiles;
        }

        private async Task<int> LayoutFilesAsync(CancellationToken ct)
        {
            int unhandledFiles = 0;
            var relPublishPathsUsed = new HashSet<string>();

            using var scope = _logger.BeginScope("Laying out files");

            _logger.LogInformation("Laying out files from {_productBuildPath}", _productBuildPath);

            // TODO: Make this parallel using Task.Run + semaphore to batch process files. Need to make collections concurrent or have single
            //       queue to aggregate results.
            // TODO: The file enumeration should have the posibility to inject a custom enumerator. Useful in case there's only subsets of files.
            //       For example, shipping only files. 
            foreach (FileInfo file in _productBuildPath.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                bool isProcessed = false;
                foreach (ILayoutWorker worker in _layoutWorkers)
                {
                    if (ct.IsCancellationRequested)
                    {
                       _logger.LogError($"Cancellation issued.");
                        return -1;
                    }

                    // Do we want to parallelize the number of workers?
                    LayoutWorkerResult layoutResult = await worker.HandleFileAsync(file, ct);

                    if (layoutResult.Status == LayoutResultStatus.Error)
                    {
                       _logger.LogError($"Error handling file.");
                        return -1;
                    }

                    if (layoutResult.Status == LayoutResultStatus.FileHandled)
                    {
                        if (isProcessed)
                        {
                            // TODO: Might be worth to relax this limitation. It just needs to turn the
                            //      source -> fileData relationship to something like source -> List<FileData>).
                           _logger.LogError("File {file} is getting handled by several workers.", file);
                            return -1;
                        }

                        isProcessed = true;

                        foreach ((FileMapping fileMap, FileMetadata fileMetadata) in layoutResult.LayoutDataEnumerable)
                        {
                            string srcPath = fileMap.LocalSourcePath;
                            string dstPath = fileMap.RelativeOutputPath;
                            if (relPublishPathsUsed.Contains(dstPath))
                            {
                               _logger.LogError("File {srcPath} is getting published to relative path {dstPath} which is already in use.", srcPath, dstPath);
                                return -1;
                            }
                            relPublishPathsUsed.Add(dstPath);
                            _logger.LogTrace("{srcPath} -> {dstPath} [{fileMetadata}]", srcPath, dstPath, fileMetadata);
                            _filesToRelease.Add(new FileReleaseData(fileMap, fileMetadata));
                        }
                    }
                }

                if (!isProcessed)
                {
                    _logger.LogWarning("File not handled {file}", file);
                    unhandledFiles++;
                }
            }

            return unhandledFiles;
        }

        public void Dispose()
        {
            foreach(ILayoutWorker lw in _layoutWorkers)
                lw.Dispose();

            foreach(IReleaseVerifier rv in _verifiers)
                rv.Dispose();

            _publisher.Dispose();
            _manifestGenerator.Dispose();
        }
    }
}