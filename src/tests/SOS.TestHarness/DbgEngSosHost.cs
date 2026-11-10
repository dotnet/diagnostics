// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.Hosting.Loader;

namespace SOS.TestHarness;

internal static class DbgEngSosHost
{
    public static void Initialize()
    {
        int hr = SOSLoader.Initialize(ToolPaths.SosPath);
        if (hr != 0)
        {
            throw new InvalidOperationException($"SOSLoader.Initialize('{ToolPaths.SosPath}') failed: 0x{hr:x8}.");
        }
    }
}
