// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.SymbolStore;
using Microsoft.SymbolStore.KeyGenerators;

namespace Microsoft.SymbolManifestGenerator
{
    public static class SymbolManifestGenerator
    {
        private const int Private = 340;

        public static void GenerateManifest(ITracer tracer, DirectoryInfo dir, string manifestFileName)
        {
            ManifestDataV1 manifestData = new();

            FileInfo[] allFiles = dir.GetFiles("*", SearchOption.AllDirectories);
            foreach (FileInfo file in allFiles)
            {
                using FileStream fileStream = file.OpenRead();
                SymbolStoreFile symbolStoreFile = new(fileStream, file.FullName);
                FileKeyGenerator generator = new(tracer, symbolStoreFile);
                if (!generator.IsValid())
                {
                    tracer.Information($"Could not generate a valid FileKeyGenerator from file '{file.FullName}'. Skipping.");
                    continue;
                }

                foreach (SymbolStoreKey clrKey in generator.GetKeys(KeyTypeFlags.ClrKeys))
                {
                    FileInfo specialFile = ResolveClrKeyToUniqueFileFromAllFiles(allFiles, clrKey);
                    if (specialFile == null)
                    {
                        tracer.Information($"Known special file '{clrKey.FullPathName}' for runtime module '{file.FullName}' does not exist in directory '{file.DirectoryName}'. Skipping.");
                        continue;
                    }

                    string basedirRelativePath = specialFile.FullName.Replace(dir.FullName, string.Empty).TrimStart(Path.DirectorySeparatorChar);
                    string fileHash = CalculateSHA512(specialFile);

                    ManifestDataEntry manifestDataEntry = new()
                    {
                        BasedirRelativePath = basedirRelativePath,
                        SymbolKey = clrKey.Index,
                        Sha512 = fileHash,
                        DebugInformationLevel = Private,
                        LegacyDebugInformationLevel = Private
                    };

                    manifestData.Entries.Add(manifestDataEntry);
                }
            }

            JsonSerializerOptions serializeOptions = new()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
            string manifestDataContent = JsonSerializer.Serialize(manifestData, serializeOptions);
            File.WriteAllText(manifestFileName, manifestDataContent);
        }

        // Special files associated with a particular runtime module have not been guaranteed to be in the same directory as the runtime module.
        // As such, the directory for which a manifest is being generated must guarantee that at most one file exists with the same name as the special file.
        private static FileInfo ResolveClrKeyToUniqueFileFromAllFiles(FileInfo[] allFiles, SymbolStoreKey clrKey)
        {
            string clrKeyFileName = Path.GetFileName(clrKey.FullPathName);

            FileInfo matchingSymbolFileOnDisk = allFiles.SingleOrDefault(file => FileHasClrKeyFileName(file, clrKeyFileName));

            return matchingSymbolFileOnDisk;

            static bool FileHasClrKeyFileName(FileInfo file, string clrKeyFileName)
            {
                return file.Name.Equals(clrKeyFileName, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string CalculateSHA512(FileInfo file)
        {
            using FileStream fileReadStream = file.OpenRead();
            using System.Security.Cryptography.SHA512 sha = System.Security.Cryptography.SHA512.Create();
            byte[] hashValueBytes = sha.ComputeHash(fileReadStream);
            return BitConverter.ToString(hashValueBytes).Replace("-", "");
        }

        private class ManifestFileVersion
        {
            public string Version { get; set; }
        }

        private class ManifestDataV1 : ManifestFileVersion
        {
            public List<ManifestDataEntry> Entries { get; set; }

            public ManifestDataV1()
            {
                Version = "1";
                Entries = new List<ManifestDataEntry>();
            }
        }

        private class ManifestDataEntry
        {
            public string BasedirRelativePath { get; set; }
            public string SymbolKey { get; set; }
            public string Sha512 { get; set; }
            public int DebugInformationLevel { get; set; }
            [JsonPropertyName("DebugInformationLevel")]
            public int LegacyDebugInformationLevel { get; set; }
        }
    }
}
