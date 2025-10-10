// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VarargPInvokeInteropMD;

internal class Program
{
    private static void Main()
    {
        Debugger.Break();

        Interop.printf("Number: %d, Float: %.2f\n", __arglist(42, 3.14159));
    }
}

internal class Interop
{
    [DllImport("msvcrt")]
    public static extern int printf(string format, __arglist);
}
