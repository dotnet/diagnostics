// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;

namespace DiagnosticsReleaseTool.Impl
{
    internal class Config
    {
        public FileInfo ToolManifest { get; }
        public bool ShouldVerifyManifest { get; }
        public DirectoryInfo DropPath { get; }
        public DirectoryInfo StagingDirectory { get; }
        public string ReleaseName { get; }
        public string AccountName { get; }
        public string AccountKey { get; }
        public string ContainerName { get; }
        public int SasValidDays { get; }

        public Config(
            FileInfo toolManifest,
            bool verifyToolManifest,
            DirectoryInfo inputDropPath,
            DirectoryInfo stagingDirectory,
            string releaseName,
            string accountName,
            string accountKey,
            string containerName,
            int sasValidDays)
        {
            ToolManifest = toolManifest;
            ShouldVerifyManifest = verifyToolManifest;
            DropPath = inputDropPath;
            StagingDirectory = stagingDirectory;
            ReleaseName = releaseName;
            AccountName = accountName;
            AccountKey = accountKey;
            ContainerName = containerName;
            SasValidDays = sasValidDays;
        }
    }
}
