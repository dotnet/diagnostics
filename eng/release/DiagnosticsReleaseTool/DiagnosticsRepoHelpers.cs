// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using ReleaseTool.Core;

namespace DiagnosticsReleaseTool.Util
{
    public sealed partial class DiagnosticsRepoHelpers
    {
        private readonly string _publicBundleToolsPathInDrop;
        private readonly string _internalBundleToolsPathInDrop;
        private readonly string _bundledToolsPrefix;
        private readonly string _pdbCategory;

        public string BundledToolsCategory { get; }

        public DiagnosticsRepoHelpers(FileInfo toolManifest)
        {
            using FileStream manifestStream = File.OpenRead(toolManifest.FullName);
            using JsonDocument manifest = JsonDocument.Parse(manifestStream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            JsonElement constants = manifest.RootElement.GetProperty("ReleaseToolConstants");
            _publicBundleToolsPathInDrop = NormalizePath(constants.GetProperty("PublicBundleToolsPathInDrop").GetString());
            _internalBundleToolsPathInDrop = NormalizePath(constants.GetProperty("InternalBundleToolsPathInDrop").GetString());
            _bundledToolsPrefix = constants.GetProperty("BundledToolsPrefix").GetString();
            BundledToolsCategory = constants.GetProperty("BundledToolsCategory").GetString();
            _pdbCategory = constants.GetProperty("PdbCategory").GetString();
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        }

        private static string GetRidFromBundleZip(FileInfo zipFile)
        {
            MatchCollection matches = RidBundledToolsRegex().Matches(zipFile.Name);

            if (matches.Count != 1)
            {
                throw new Exception($"Unexpected file name for tool bundle: {zipFile}.");
            }

            foreach (Match match in matches)
            {
                if (!match.Groups.TryGetValue("rid", out Group ridGroup))
                {
                    throw new Exception($"Can't extract a RID from {zipFile}.");
                }

                return ridGroup.Value;
            }

            throw new Exception($"Unexpected failure in RID extraction from {zipFile}.");
        }

        public FileMetadata GetMetadataForToolFile(FileInfo zipFile, FileInfo fileInZip)
        {
            string category = fileInZip.Extension switch
            {
                ".pdb" => _pdbCategory,
                ".exe" => BundledToolsCategory,
                "" => BundledToolsCategory,
                _ => "UnknownAssets"
            };

            string sha512 = GetSha512(fileInZip.FullName);
            string rid = GetRidFromBundleZip(zipFile);

            return new FileMetadata(
                    FileClass.Blob,
                    assetCategory: category,
                    shouldPublishToCdn: category == BundledToolsCategory,
                    rid: rid,
                    sha512: sha512);
        }

        public string GetToolPublishRelativePath(FileInfo zipFile, FileInfo fileInZip)
        {
            return FormattableString.Invariant($"{BundledToolsCategory}/{GetRidFromBundleZip(zipFile)}/{fileInZip.Name}");
        }

        public bool IsBundledToolArchive(FileInfo file)
        {
            return file.Exists && file.Extension == ".zip"
                && (file.DirectoryName.Contains(_internalBundleToolsPathInDrop) || file.DirectoryName.Contains(_publicBundleToolsPathInDrop))
                && file.Name.StartsWith(_bundledToolsPrefix);
        }

        public static string GetSha512(string filePath)
        {
            using FileStream stream = System.IO.File.OpenRead(filePath);
            using System.Security.Cryptography.SHA512 sha = System.Security.Cryptography.SHA512.Create();
            byte[] checksum = sha.ComputeHash(stream);
            return Convert.ToHexString(checksum);
        }

        [GeneratedRegex(@"diagnostic-tools-(?<rid>(\w+-)+\w+)\.zip", RegexOptions.ExplicitCapture | RegexOptions.Compiled)]
        private static partial Regex RidBundledToolsRegex();
    }
}
