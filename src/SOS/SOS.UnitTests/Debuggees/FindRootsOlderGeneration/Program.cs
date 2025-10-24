// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime;

namespace FindRootsOlderGeneration;

internal class Program
{
    private static void Main()
    {
        // Allocate a large object to ensure it goes on the LOH
        Thing[] things = new Thing[1024 * 100];

        Debugger.Break();

        // On CI runs, in server GC mode, these collects have sometimes triggered
        // a gen 2 collection that is not expected and causes the test to fail.
        // Adding "SustainedLowLatency" mode to try to prevent that.
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

        PopulateArray(things);
        Thing lastThing = things[things.Length - 1];

        Console.WriteLine("Enable CLRN notifications: SXE CLRN");
        Debugger.Break();

        int dataGen = GC.GetGeneration(things);
        int thingGen = GC.GetGeneration(lastThing);
        Console.WriteLine($"Before GC - Array Gen: {dataGen}, Thing Gen: {thingGen}");

        Console.WriteLine("Forcing GC...");

        GC.Collect(0, GCCollectionMode.Forced, blocking: true);

        Console.WriteLine("GC complete.");
        Console.WriteLine("Disable CLRN notifications: SXN CLRN");

        dataGen = GC.GetGeneration(things);
        thingGen = GC.GetGeneration(lastThing);
        Console.WriteLine($"After GC - Array Gen: {dataGen}, Thing Gen: {thingGen}");

        Debugger.Break();

        // Keep data alive
        PrintSumArray(things);
    }

    private static void PopulateArray(Thing[] things)
    {
        for (uint i = 0; i < things.Length; i++)
        {
            things[i] = new Thing() { Id = i };
        }
    }

    private static void PrintSumArray(Thing[] things)
    {
        ulong acc = 0;
        for (int i = 0; i < things.Length; i++)
        {
            acc += things[i].Id;
        }
        Console.WriteLine($"Thing Array sum: {acc}");
    }
}

internal class Thing
{
    public uint Id { get; set; }
}
