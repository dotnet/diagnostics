// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using SOS.Extensions;

namespace SOS.TestHarness;

internal static class DbgEngSosHost
{
    public static void Initialize()
    {
        // DbgEng is already inside a managed process: connect SOS to that CoreCLR instead of starting another runtime.
        int hr = HostServices.Initialize(ToolPaths.SosPath);
        if (hr != 0)
        {
            throw new InvalidOperationException($"HostServices.Initialize('{ToolPaths.SosPath}') failed: 0x{hr:x8}.");
        }
    }
}
