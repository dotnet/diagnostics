// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

internal class GCPOH
{
    private static int Main()
    {
        // Use reflection to get the API because the test is compiled against an older version of netstandard
        // that doesn't have the POH APIs
        Assembly corlib = typeof(Object).Assembly;
        Type gc = corlib.GetType("System.GC");
        MethodInfo method = gc.GetMethod("AllocateArray", BindingFlags.Public | BindingFlags.Static);
        MethodInfo generic = method.MakeGenericMethod(typeof(byte));
        // Calls GC.AllocateArray(size: 100, pinned: true)
        byte[] myArray = (byte[])generic.Invoke(null, new object[] { 100, true });
        Debugger.Break();
        GC.KeepAlive(myArray);
        return 0;
    }
}
