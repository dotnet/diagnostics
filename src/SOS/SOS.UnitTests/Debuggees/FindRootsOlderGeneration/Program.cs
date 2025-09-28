// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace FindRootsOlderGeneration;

internal class Program
{
    private static void Main()
    {
        Debugger.Break();

        // Allocate a large object to ensure it goes on the LOH
        Thing[] data = new Thing[1024 * 1024 * 3];
        int dataGen = GC.GetGeneration(data);
        data[0] = new Thing() { Name = "First" };
        int thingGen = GC.GetGeneration(data[0]);

        Console.WriteLine("Enable CLRN notifications: SXE CLRN");
        Debugger.Break();

        Console.WriteLine("Forcing GC...");
        GC.Collect(0, GCCollectionMode.Forced, true);
        GC.Collect(0, GCCollectionMode.Forced, true);
        Console.WriteLine("GC complete.");
        Console.WriteLine("Disable CLRN notifications: SXN CLRN");
        Debugger.Break();

        Console.WriteLine(data[0].Name);
        Console.WriteLine($"Array Gen: {dataGen}, Thing Gen: {thingGen}");
    }
}

internal class Thing
{
    public string Name { get; set; }
}
