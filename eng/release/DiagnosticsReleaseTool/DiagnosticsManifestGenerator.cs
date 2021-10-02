using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DiagnosticsReleaseTool.Util;
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

            var jro = new JsonWriterOptions
            {
                Indented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            using (var writer = new Utf8JsonWriter(stream, jro))
            {
                writer.WriteStartObject();

                WriteMetadata(writer);

                WritePublishingInstructions(writer, filesProcessed);
                WriteBundledTools(writer, filesProcessed);
                WriteNugetShippingPackages(writer, filesProcessed);

                writer.WriteEndObject();
            }
            stream.Position = 0;
            return stream;
        }

        private void WriteBundledTools(Utf8JsonWriter writer, IEnumerable<FileReleaseData> filesProcessed)
        {
            writer.WritePropertyName(DiagnosticsRepoHelpers.BundledToolsCategory);
            writer.WriteStartArray();

            IEnumerable<FileReleaseData> bundledTools = 
                filesProcessed.Where(
                    file => file.FileMetadata.AssetCategory == DiagnosticsRepoHelpers.BundledToolsCategory);

            foreach (FileReleaseData fileToRelease in bundledTools)
            {
                writer.WriteStartObject();
                writer.WriteString("ToolName", Path.GetFileNameWithoutExtension(fileToRelease.FileMap.LocalSourcePath));
                writer.WriteString("Rid", fileToRelease.FileMetadata.Rid);
                writer.WriteString("PublishRelativePath", fileToRelease.FileMap.RelativeOutputPath);
                writer.WriteString("PublishedPath", fileToRelease.PublishUri);
                writer.WriteString("Sha512", fileToRelease.FileMetadata.Sha512);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
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
                writer.WriteString("Sha512", fileToRelease.FileMetadata.Sha512);
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
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
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

            string subPath = GenerateSubpath(fileToRelease);

            return new FileWithCdnData(
                filePath: fileToRelease.PublishUri,
                sha512: fileToRelease.FileMetadata.Sha512,
                publishUrlSubPath: subPath,
                akaMsLink: akaLink,
                comment: null
            );
        }

        private string GenerateSubpath(FileReleaseData fileToRelease)
        {
            var fi = new FileInfo(fileToRelease.FileMap.LocalSourcePath);
            using var hash = System.Security.Cryptography.SHA256.Create();
            var enc = System.Text.Encoding.UTF8;
            byte[] hashResult = hash.ComputeHash(enc.GetBytes(fileToRelease.FileMap.RelativeOutputPath));
            string pathHash = BitConverter.ToString(hashResult).Replace("-", String.Empty);

            return $"{_productReleaseMetadata.ReleaseVersion}/{pathHash}/{fi.Name}";
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
                    // Give up if the capturing failed
                    return null;
                }

                string metadataValue = metadataGroup.Value switch {
                    "FileName" => fi.Name,
                    "FileNameNoExt" => Path.GetFileNameWithoutExtension(fi.Name),
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