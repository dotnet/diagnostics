// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal class GCWhere
{
    private string _string;
    private static ulong _static = 52704621242434;

    public GCWhere(string inputString)
    {
        _string = inputString;
    }

    private string TempString
    {
        get { return _string; }
    }
    public ulong TempStatic
    {
        get { return _static; }
    }

    // Create an object, ensure that it is kept alive and force
    // several GC collections to happen which will cause the
    // object to move from Gen0 to Gen1 to Gen2 where it should
    // stay
    private static int Main() 
    {
        GCWhere temp = new GCWhere("This is a string!!");
        StringWriter textWriter = new StringWriter();
        ulong staticValue = temp.TempStatic;
        int genFirstTime = GC.GetGeneration(temp);
        Debugger.Break();   // GCWhere should temp in Gen0        
        GC.Collect();
        int genSecondTime = GC.GetGeneration(temp);
        Debugger.Break();   // GCWhere should temp in Gen1                
        GC.Collect();
        int genThirdTime = GC.GetGeneration(temp);
        Debugger.Break();   // GCWhere should temp in Gen2                
        GC.Collect();
        int genFourthTime = GC.GetGeneration(temp);
        Console.WriteLine("1st: {0} 2nd: {1}, 3rd: {2} 4th: {3}", genFirstTime, genSecondTime, genThirdTime, genFourthTime);
        Debugger.Break();   // GCWhere should temp in Gen2                
        PrintIt(temp);
        GC.KeepAlive(temp);
        return 0;
    }

    // This is here because without calling something with the object as an argument it'll get optimized away
    private static void PrintIt(GCWhere temp)
    {
        Console.WriteLine(temp.TempString);
    }
}
