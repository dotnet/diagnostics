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

            IEnumerable<FileInfo> files = dir.GetFiles("*", SearchOption.AllDirectories);
            foreach (FileInfo file in files)
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
                    string runtimeModuleDirectory = file.DirectoryName;
                    FileInfo specialFile = GetSymbolFileToAddAdditionalDebugEntry(files, clrKey);
                    if (specialFile == null)
                    {
                        tracer.Information($"Known special file '{clrKey.FullPathName}' for runtime module '{file.FullName}' does not exist in directory '{runtimeModuleDirectory}'. Skipping.");
                        continue;
                    }

                    string basedirRelativePath = specialFile.FullName.Replace(dir.FullName, string.Empty).TrimStart(Path.DirectorySeparatorChar);
                    string fileHash = CalculateSHA512(specialFile);

                    ManifestDataEntry manifestDataEntry = new()
                    {
                        BasedirRelativePath = basedirRelativePath,
                        SymbolKey = clrKey.Index,
                        Sha512 = fileHash,
                        DebugInformationLevel = Private
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

        private static FileInfo GetSymbolFileToAddAdditionalDebugEntry(IEnumerable<FileInfo> files, SymbolStoreKey clrKey)
        {
            string filePath = clrKey.FullPathName;
            string keyFileNameToMatch = Path.GetFileName(filePath);

            FileInfo matchingSymbolFileOnDisk = files.SingleOrDefault(f => f.Name.Equals(keyFileNameToMatch, StringComparison.OrdinalIgnoreCase));

            return matchingSymbolFileOnDisk;
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
        }
    }
}
