using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ReleaseTool.Core;

namespace DiagnosticsReleaseTool.Util
{
    internal class DarcHelpers
    {
        private readonly DirectoryInfo _dropPath;

        public string ReleaseFilePath { get; }

        public string ManifestFilePath { get; }

        public DarcHelpers(DirectoryInfo dropPath)
        {
            _dropPath = dropPath;
            ReleaseFilePath = Path.Join(_dropPath.FullName, "release.json");
            ManifestFilePath = Path.Join(_dropPath.FullName, "manifest.json");

            if (!dropPath.Exists
                || !File.Exists(ReleaseFilePath)
                || !File.Exists(ManifestFilePath))
            {
                throw new InvalidOperationException($"{_dropPath.FullName} in not a valid darc drop");
            }
        }

        internal ReleaseMetadata GetDropMetadataForSingleRepoVariants(IEnumerable<string> repoUrls)
        {
            string releaseVersion;
            using (Stream darcReleaseFile = File.OpenRead(ReleaseFilePath))
            using (JsonDocument jsonDoc = JsonDocument.Parse(darcReleaseFile))
            {
                JsonElement releaseVersionElement = jsonDoc.RootElement[0].GetProperty("release");
                releaseVersion = releaseVersionElement.GetString();
            }

            using (Stream darcManifest = File.OpenRead(ManifestFilePath))
            using (JsonDocument jsonDoc = JsonDocument.Parse(darcManifest))
            {
                // TODO: Schema validation.
                JsonElement buildList = jsonDoc.RootElement.GetProperty("builds");

                // This iteration is necessary due to the public/private nature repos.
                var repoBuilds = buildList.EnumerateArray()
                                          .Where(build => 
                                          {
                                            var buildUri = new Uri(build.GetProperty("repo").GetString());
                                            return repoUrls.Any(repoUrl => buildUri == new Uri(repoUrl));
                                          });

                if (repoBuilds.Count() != 1)
                {
                    throw new InvalidOperationException(
                        $"There's either no build for requested repos or more than one. Can't retrieve metadata.");
                }

                JsonElement build = repoBuilds.First();

                var releaseMetadata = new ReleaseMetadata(
                    releaseVersion: releaseVersion,
                    repoUrl: build.GetProperty("repo").GetString(),
                    branch: build.GetProperty("branch").GetString(),
                    commit: build.GetProperty("commit").GetString(),
                    dateProduced: build.GetProperty("produced").GetString(),
                    buildNumber: build.GetProperty("buildNumber").GetString(),
                    barBuildId: build.GetProperty("barBuildId").GetInt32()
                );

                return releaseMetadata;
            }
        }

        internal DirectoryInfo GetShippingDirectoryForSingleProjectVariants(IEnumerable<string> projectNames)
        {
            using (Stream darcManifest = File.OpenRead(ReleaseFilePath))
            using (JsonDocument jsonDoc = JsonDocument.Parse(darcManifest))
            {
                // TODO: There's a lot of error validation that should go here. We are basically assuming a
                // pretty stable schema.
                JsonElement productList = jsonDoc.RootElement[0].GetProperty("products");

                var matchingProducts = productList.EnumerateArray()
                                               .Where(prod => projectNames.Contains(prod.GetProperty("name").GetString()));

                if (matchingProducts.Count() != 1)
                {
                    throw new InvalidOperationException(
                        $"There's either no product under the provided names or more than one in the drop.");
                }

                return new DirectoryInfo(matchingProducts.First().GetProperty("fileshare").GetString());
            }
        }
    }
}