using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ReleaseTool.Core;

namespace DiagnosticsReleaseTool.Impl
{
    internal class DiagnosticsManifestGenerator : IManifestGenerator
    {
        private readonly ReleaseMetadata _productReleaseMetadata;
        private readonly JsonDocument _assetManifestManifestDom;
        private readonly ILogger _logger;

        public DiagnosticsManifestGenerator(ReleaseMetadata productReleaseMetadata, FileInfo toolManifest, ILogger logger)
        {
            _productReleaseMetadata = productReleaseMetadata;
            string manifestContent = File.ReadAllText(toolManifest.FullName);
            _assetManifestManifestDom = JsonDocument.Parse(manifestContent);
            _logger = logger;
        }

        public void Dispose()
        {
            _assetManifestManifestDom.Dispose();
        }

        public Stream GenerateManifest(IEnumerable<FileReleaseData> filesProcessed)
        {
            var stream = new MemoryStream();

            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions{ Indented = true }))
            {
                writer.WriteStartObject();

                WriteMetadata(writer);

                WriteNugetShippingPackages(writer, filesProcessed);
                WritePublishingInstructions(writer, filesProcessed);

                writer.WriteEndObject();
            }
            stream.Position = 0;
            return stream;
        }

        private void WriteNugetShippingPackages(Utf8JsonWriter writer, IEnumerable<FileReleaseData> filesProcessed)
        {
            writer.WritePropertyName(FileMetadata.GetDefaultCatgoryForClass(FileClass.Nuget));
            writer.WriteStartArray();

            IEnumerable<FileReleaseData> nugetFiles = filesProcessed.Where(file => file.FileMetadata.Class == FileClass.Nuget);

            foreach (FileReleaseData fileToRelease in nugetFiles)
            {
                writer.WriteStartObject();
                writer.WriteString("PublishRelativePath", fileToRelease.FileMap.RelativeOutputPath);
                writer.WriteString("PublishedPath", fileToRelease.PublishUri);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private void WritePublishingInstructions(Utf8JsonWriter writer, IEnumerable<FileReleaseData> filesProcessed)
        {
            writer.WritePropertyName("PublishInstructions");
            writer.WriteStartArray();

            var options = new JsonSerializerOptions
            {
                IgnoreNullValues = true,
                WriteIndented = true
            };

            foreach (FileReleaseData fileToRelease in filesProcessed)
            {
                if (fileToRelease.FileMetadata.ShouldPublishToCdn)
                {
                    FileWithCdnData fileWithCdnData = GetFileWithCdnData(fileToRelease);
                    JsonSerializer.Serialize<FileWithCdnData>(writer, fileWithCdnData, options);
                }
            }

            writer.WriteEndArray();
        }

        private FileWithCdnData GetFileWithCdnData(FileReleaseData fileToRelease)
        {
            bool categoryHasData = _assetManifestManifestDom.RootElement.TryGetProperty(
                fileToRelease.FileMetadata.AssetCategory,
                out JsonElement dataForCategory);

            string akaLink = null;
            if (categoryHasData && dataForCategory.TryGetProperty("AkaMsSchema", out JsonElement linkSchema))
            {
                akaLink = GenerateLinkFromMetadata(fileToRelease, linkSchema.GetString());
            }

            return new FileWithCdnData(
                filePath: fileToRelease.PublishUri,
                sha512: fileToRelease.FileMetadata.Sha512,
                publishUrlSubPath: null,
                akaMsLink: akaLink,
                comment: null
            );
        }

        private static readonly Regex s_akaMsMetadataMatcher = new Regex(
                $@"<(?<metadata>[a-zA-Z]\w*)>",
                RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        private string GenerateLinkFromMetadata(FileReleaseData fileToRelease, string linkSchema)
        {
            var fi = new FileInfo(fileToRelease.FileMap.LocalSourcePath);
            string link = linkSchema;
            //TODO: Revisit for perf if necessary...
            MatchCollection results = s_akaMsMetadataMatcher.Matches(linkSchema);
            foreach (Match match in results)
            {
                if(!match.Groups.TryGetValue("metadata", out Group metadataGroup))
                {
                    // Give up if the catpturing failed
                    return null;
                }

                string metadataValue = metadataGroup.Value switch {
                    "FileName" => fi.Name,
                    "Rid" => fileToRelease.FileMetadata.Rid,
                    "Sha512" => fileToRelease.FileMetadata.Sha512,
                    "AssetCategory" => fileToRelease.FileMetadata.AssetCategory,
                    _ => null
                };

                if (string.IsNullOrEmpty(metadataValue))
                {
                    _logger.LogWarning("Can't replace metadata {metadataGroup.Value} for {fileToRelease.FileMap.LocalSourcePath}",
                        metadataGroup.Value, fileToRelease.FileMap.LocalSourcePath);
                    return null;
                }
                else
                {
                    link = link.Replace($"<{metadataGroup.Value}>", metadataValue);
                }
            }

            if (Uri.TryCreate(link, UriKind.Absolute, out Uri uriResult) 
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
            {
                return link;
            }

            return null;
        }

        private void WriteMetadata(Utf8JsonWriter writer)
        {
            // There's no way to obtain the json DOM for an object...
            byte[] metadataJsonObj = JsonSerializer.SerializeToUtf8Bytes<ReleaseMetadata>(_productReleaseMetadata);
            JsonDocument metadataDoc = JsonDocument.Parse(metadataJsonObj);
            foreach(var element in metadataDoc.RootElement.EnumerateObject())
            {
                element.WriteTo(writer);
            }
        }
    }
}