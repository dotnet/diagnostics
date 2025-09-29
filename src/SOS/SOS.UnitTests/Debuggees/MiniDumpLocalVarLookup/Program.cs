// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;

namespace MiniDumpLocalVarLookup;

internal class Program
{
    static public void Main()
    {
        int intValue = 42;
        string stringValue = "Hello, World!";

        PrintValues(intValue, stringValue);
    }

    static void PrintValues(int intValue, string stringValue)
    {
        int length = stringValue.Length;
        Debugger.Break();
        Console.WriteLine($"intValue: {intValue}");
        Console.WriteLine($"stringValue: {stringValue} (length = {length})");
    }
}
