using System;
using System.Collections.Generic;

namespace ReleaseTool.Core
{
    public interface IReleaseVerifier : IDisposable
    {
        bool VerifyFiles(IEnumerable<FileReleaseData> filestoRelease);
    }
}