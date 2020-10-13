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
            for (int i = 0; i < 100; i++) System.GC.Collect();

            var layoutWorkerList = new List<ILayoutWorker>
            {
                // TODO: We may want to inject a logger.
                new NugetLayoutWorker(),
                new SymbolPackageLayoutWorker(),
                new ZipLayoutWorker(
                    shouldHandleFileFunc: DiagnosticsRepoHelpers.IsBundledToolArchive,
                    getRelPathFromZipAndInnerFileFunc: DiagnosticsRepoHelpers.GetToolPublishRelativePath,
                    getMetadataForInnerFileFunc: DiagnosticsRepoHelpers.GetMetadataForToolFile
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

            IPublisher releasePublisher = new FileSharePublisher(releaseConfig.PublishPath);
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
                .SetBasePath(Directory.GetCurrentDirectory())
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