// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

namespace SOS.TestHarness;

internal sealed class SingleFileToolLayout(string root) : PackagedToolLayout(root)
{
    protected override void ConfigureEnvironment(ProcessStartInfo startInfo)
    {
        startInfo.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(Root, "bundle-extract");
    }
}
