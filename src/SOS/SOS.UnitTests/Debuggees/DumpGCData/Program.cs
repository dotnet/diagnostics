// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DumpGCData;

internal class Program
{
    private static void Main()
    {
        Debugger.Break();

        byte[] data = new byte[1024 * 1024];

        GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        GC.Collect();
        Debugger.Break();
        Console.WriteLine(handle.ToString());
    }
}
