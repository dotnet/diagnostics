using System;
using System.IO;
using System.Text.RegularExpressions;
using ReleaseTool.Core;

namespace DiagnosticsReleaseTool.Util
{
    public static class DiagnosticsRepoHelpers
    {
        public static readonly string[] ProductNames = new []{ "diagnostics", "dotnet-diagnostics" };
        public static readonly string[] RepositoryUrls = new [] { "https://github.com/dotnet/diagnostics", "https://dev.azure.com/dnceng/internal/_git/dotnet-diagnostics" };
        public static string BundleToolsPathInDrop => System.IO.Path.Combine("diagnostics", "bundledtools");
        public const string BundledToolsPrefix = "diagnostic-tools-";
        public const string BundledToolsCategory = "ToolBundleAssets";
        public const string PdbCategory = "PdbAssets";

        private static readonly Regex s_ridBundledToolsMatcher = new Regex(
                $@"{BundledToolsPrefix}(?<rid>(\w+-)+\w+)\.zip",
                RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        private static string GetRidFromBundleZip(FileInfo zipFile)
        {
            MatchCollection matches = s_ridBundledToolsMatcher.Matches(zipFile.Name);

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

        public static FileMetadata GetMetadataForToolFile(FileInfo zipFile, FileInfo fileInZip)
        {
            string category = fileInZip.Extension switch
            {
                ".pdb" => PdbCategory,
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

        public static string GetToolPublishRelativePath(FileInfo zipFile, FileInfo fileInZip)
        {
            return FormattableString.Invariant($"{BundledToolsCategory}/{GetRidFromBundleZip(zipFile)}/{fileInZip.Name}");
        }

        public static bool IsBundledToolArchive(FileInfo file)
        {
            return file.Exists && file.Extension == ".zip"
                && file.DirectoryName.Contains(BundleToolsPathInDrop)
                && file.Name.StartsWith(BundledToolsPrefix);
        }

        public static string GetSha512(string filePath)
        {
            using FileStream stream = System.IO.File.OpenRead(filePath);
            using var sha = System.Security.Cryptography.SHA512.Create();
            byte[] checksum = sha.ComputeHash(stream);
            return Convert.ToHexString(checksum);
        }
    }
}