// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ReleaseTool.Core
{
    public sealed class NugetLayoutWorker : PassThroughLayoutWorker
    {
        public NugetLayoutWorker(string stagingPath) : base(
            shouldHandleFileFunc: static file => {
                return file.Extension == ".nupkg"
                    && !file.Name.EndsWith(".symbols.nupkg", System.StringComparison.OrdinalIgnoreCase);
            },
            getRelativePublishPathFromFileFunc: static file => Helpers.GetDefaultPathForFileCategory(file, FileClass.Nuget),
            getMetadataForFileFunc: static file => Helpers.GetDefaultFileMetadata(file, FileClass.Nuget),
            stagingPath
        )
        { }
    }
}
