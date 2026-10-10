// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DumpGCData;

internal class Program
{
    private static void Main()
    {
        AtUnpinned();

        byte[] data = new byte[1024 * 1024];

        GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        GC.Collect();
        AtPinned();
        Console.WriteLine(handle.ToString());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AtUnpinned() => TestHarness.Stop("unpinned");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AtPinned() => TestHarness.Stop("pinned");
}
