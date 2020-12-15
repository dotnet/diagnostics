using System;
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

        internal ReleaseMetadata GetDropMetadata(string repoUrl)
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

                // TODO: This should be using Uri.Compare...
                var repoBuilds = buildList.EnumerateArray()
                                          .Where(build => build.GetProperty("repo").GetString() == repoUrl);

                if (repoBuilds.Count() != 1)
                {
                    throw new InvalidOperationException(
                        $"There's either no build for {repoUrl} or more than one. Can't retrieve metadata.");
                }

                JsonElement build = repoBuilds.ElementAt(0);

                // TODO: If any of these were to fail...
                var releaseMetadata = new ReleaseMetadata(
                    releaseVersion: releaseVersion,
                    repoUrl: repoUrl,
                    branch: build.GetProperty("branch").GetString(),
                    commit: build.GetProperty("commit").GetString(),
                    dateProduced: build.GetProperty("produced").GetString(),
                    buildNumber: build.GetProperty("buildNumber").GetString(),
                    barBuildId: build.GetProperty("barBuildId").GetInt32()
                );

                return releaseMetadata;
            }
        }

        internal DirectoryInfo GetShippingDirectoryForProject(string projectName)
        {
            using (Stream darcManifest = File.OpenRead(ReleaseFilePath))
            using (JsonDocument jsonDoc = JsonDocument.Parse(darcManifest))
            {
                // TODO: There's a lot of error validation that should go here. We are basically assuming a
                // pretty stable schema.
                JsonElement productList = jsonDoc.RootElement[0].GetProperty("products");

                var directoryList = productList.EnumerateArray()
                                               .Where(prod => prod.GetProperty("name").GetString() == projectName)
                                               .Select(prod => prod.GetProperty("fileshare"));

                if (directoryList.Count() != 1)
                {
                    throw new InvalidOperationException(
                        $"There's either no product named {projectName} or more than one in the drop.");
                }

                return new DirectoryInfo(directoryList.ElementAt(0).GetString());
            }
        }
    }
}