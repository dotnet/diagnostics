// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;

internal static class InterpreterStackTestApp
{
    private static int Main()
    {
        InterpTestMethodRunNested("argument string");
        return 0;
    }

    // The "InterpTestMethod" prefix matches the DOTNET_Interpreter glob the SOS
    // test framework sets when SOS_TEST_INTERPRETER=true, so methods named with
    // this prefix execute on the CoreCLR interpreter.

    private static void InterpTestMethodRunNested(string argument)
    {
        Console.WriteLine("InterpTestMethodRunNested received: " + argument);
        InterpTestMethodCrash(argument);
    }

    private static void InterpTestMethodCrash(string argument)
    {
        // Force a null-pointer AV. A first-chance AV is caught by every dump
        // generator; an unhandled managed exception is not (interpreter UEF
        // doesn't escalate to second-chance today).
        Console.WriteLine("InterpTestMethodCrash about to AV: " + argument);
        Marshal.ReadIntPtr(IntPtr.Zero);
    }
}
