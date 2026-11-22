// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DotnetDumpCommands;

internal static class Program
{
    private static readonly List<object> s_objects = new();

    private static void Main()
    {
        s_objects.Add(new DumpSampleClass[50000]);
#if NET5_0_OR_GREATER
        s_objects.Add(GC.AllocateUninitializedArray<byte>(10000, pinned: true));
#endif

        AddSamples(5);
        GC.Collect();
        AddSamples(3);
        GC.Collect();
        AddSamples(10);

        AtReady();
        Console.WriteLine($"Number of objects kept in memory = {s_objects.Count}");
    }

    private static void AddSamples(int count)
    {
        for (int i = 0; i < count; i++)
            s_objects.Add(new DumpSampleClass());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void AtReady() => TestHarness.Stop("ready");

    public sealed class DumpSampleClass
    {
    }
}
