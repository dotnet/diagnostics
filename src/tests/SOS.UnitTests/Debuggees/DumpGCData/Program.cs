// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;

namespace DumpGCData;

internal class Program
{
    private static void Main()
    {
        BeforeCollection();

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        AfterCollection();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BeforeCollection() => TestHarness.Stop("beforeCollection");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AfterCollection() => TestHarness.Stop("afterCollection");
}
