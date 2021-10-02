using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using DiagnosticsReleaseTool.Util;
using ReleaseTool.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DiagnosticsReleaseTool.Impl
{
    internal class DiagnosticsReleaseRunner
    {
        internal const string ManifestName = "publishManifest.json";

        internal async static Task<int> PrepareRelease(Config releaseConfig, bool verbose, CancellationToken ct)
        {
            // TODO: This will throw if invalid drop path is given.
            var darcLayoutHelper = new DarcHelpers(releaseConfig.DropPath);

            ILogger logger = GetDiagLogger(verbose);

            var layoutWorkerList = new List<ILayoutWorker>
            {
                // TODO: We may want to inject a logger.
                new NugetLayoutWorker(stagingPath: releaseConfig.StagingDirectory.FullName),
                new SymbolPackageLayoutWorker(stagingPath: releaseConfig.StagingDirectory.FullName),
                new ZipLayoutWorker(
                    shouldHandleFileFunc: DiagnosticsRepoHelpers.IsBundledToolArchive,
                    getRelativePathFromZipAndInnerFileFunc: DiagnosticsRepoHelpers.GetToolPublishRelativePath,
                    getMetadataForInnerFileFunc: DiagnosticsRepoHelpers.GetMetadataForToolFile,
                    stagingPath: releaseConfig.StagingDirectory.FullName
                )
            };

            var verifierList = new List<IReleaseVerifier> { };

            if (releaseConfig.ShouldVerifyManifest)
            {
                // TODO: add verifier.
                // verifierList.Add();
            }

            // TODO: Probably should use BAR ID instead as an identifier for the metadata to gather.
            ReleaseMetadata releaseMetadata = darcLayoutHelper.GetDropMetadata(DiagnosticsRepoHelpers.RepositoryName);
            DirectoryInfo basePublishDirectory = darcLayoutHelper.GetShippingDirectoryForProject(DiagnosticsRepoHelpers.ProductName);
            string publishManifestPath = Path.Combine(releaseConfig.StagingDirectory.FullName, ManifestName);

            IPublisher releasePublisher = new AzureBlobBublisher(releaseConfig.AccountName, releaseConfig.AccountKey, releaseConfig.ContainerName, releaseConfig.ReleaseName, releaseConfig.SasValidDays, logger);
            IManifestGenerator manifestGenerator = new DiagnosticsManifestGenerator(releaseMetadata, releaseConfig.ToolManifest, logger);

            using var diagnosticsRelease = new Release(
                productBuildPath: basePublishDirectory,
                layoutWorkers: layoutWorkerList,
                verifiers: verifierList,
                publisher: releasePublisher,
                manifestGenerator: manifestGenerator,
                manifestSavePath: publishManifestPath
            );

            diagnosticsRelease.UseLogger(logger);

            return await diagnosticsRelease.RunAsync(ct);
        }

        private static ILogger GetDiagLogger(bool verbose)
        {
            var loggingConfiguration = new ConfigurationBuilder()
                .AddJsonFile("logging.json", optional: false, reloadOnChange: false)
                .Build();

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConfiguration(loggingConfiguration.GetSection("Logging"))
                    .AddConsole();

                if (verbose)
                {
                    builder.AddFilter("DiagnosticsReleaseTool.Impl.DiagnosticsReleaseRunner", LogLevel.Trace);
                }
            });

            return loggerFactory.CreateLogger<DiagnosticsReleaseRunner>();
        }
    }
}