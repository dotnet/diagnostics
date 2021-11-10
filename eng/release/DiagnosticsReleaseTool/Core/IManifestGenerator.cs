// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace ReleaseTool.Core
{
    public interface IManifestGenerator : IDisposable
    {
        System.IO.Stream GenerateManifest(IEnumerable<FileReleaseData> filesToRelease);
    }
}