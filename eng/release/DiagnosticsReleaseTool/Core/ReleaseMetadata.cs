// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ReleaseTool.Core
{
    public class ReleaseMetadata
    {
        public string ReleaseVersion { get; }
        public string RepoUrl { get; }
        public string Branch { get; }
        public string Commit { get; }
        public string DateProduced { get; }
        public string BuildNumber { get; }
        public int BarBuildId { get; }

        public ReleaseMetadata(string releaseVersion, string repoUrl, string branch, string commit, string dateProduced, string buildNumber, int barBuildId)
        {
            ReleaseVersion = releaseVersion;
            RepoUrl = repoUrl;
            Branch = branch;
            Commit = commit;
            DateProduced = dateProduced;
            BuildNumber = buildNumber;
            BarBuildId = barBuildId;
        }
    }
}
