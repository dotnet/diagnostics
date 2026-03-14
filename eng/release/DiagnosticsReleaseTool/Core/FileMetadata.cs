// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace ReleaseTool.Core
{
    public enum FileClass
    {
        Blob,
        Nuget,
        SymbolPackage,
        Unknown
    }

    public struct FileMetadata
    {
        public readonly FileClass Class { get; }

        public readonly string AssetCategory { get; }

        public readonly bool ShouldPublishToCdn { get; }

        public readonly string Rid { get; }

        public readonly string Sha512 { get; }

        // TODO: Add a metadata bag for Key,Value pairs.

        public FileMetadata(FileClass fileClass, string assetCategory, string sha512)
            : this(fileClass, assetCategory, shouldPublishToCdn: false, rid: "any", sha512: sha512) { }

        public FileMetadata(FileClass fileClass, string assetCategory, bool shouldPublishToCdn, string rid, string sha512)
        {
            if (string.IsNullOrEmpty(assetCategory))
            {
                throw new ArgumentException("AssetCategory for file can't be null or empty");
            }

            if (sha512 is not null)
            {
                bool validSha = sha512.Length == 128;
                for (int idx = 0; idx < sha512.Length && validSha; idx++)
                {
                    char x = char.ToLower(sha512[idx]);
                    validSha |= (char.IsDigit(x) || (x >= 'a' && x <= 'f'));
                }

                if (!validSha)
                {
                    throw new ArgumentException("SHA512 is invalid");
                }
            }

            if (sha512 is null && shouldPublishToCdn)
            {
                throw new InvalidOperationException("SHA512 can't be null if file needs CDN publishing");
            }

            Class = fileClass;
            AssetCategory = assetCategory;
            ShouldPublishToCdn = shouldPublishToCdn;
            Sha512 = sha512;
            Rid = rid;
        }

        public static string GetDefaultCatgoryForClass(FileClass fileClass) => fileClass switch
        {
            FileClass.Blob => "BlobAssets",
            FileClass.Nuget => "NugetAssets",
            FileClass.SymbolPackage => "SymbolNugetAssets",
            FileClass.Unknown => "UnknownAssets",
            _ => "UnknownAssets"
        };

        public override string ToString()
        {
            return $"Class: {Class}, Category: {AssetCategory}, CDN: {ShouldPublishToCdn}, RID: {Rid}";
        }
    }
}
