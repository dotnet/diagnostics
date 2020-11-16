using System;
using System.Collections.Generic;

namespace ReleaseTool.Core
{
    public interface IManifestGenerator : IDisposable
    {
        System.IO.Stream GenerateManifest(IEnumerable<FileReleaseData> filesToRelease);
    }
}