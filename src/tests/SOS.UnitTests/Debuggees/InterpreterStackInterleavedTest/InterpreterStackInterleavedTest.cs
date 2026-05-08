// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using InterpreterStackInterleavedTest.Trampoline;

internal static class InterpreterStackInterleavedTestApp
{
    private static int Main()
    {
        InterpTestMethodOuter();
        return 0;
    }

    // The "InterpTestMethod" prefix matches the DOTNET_Interpreter glob the SOS
    // test framework sets when SOS_TEST_INTERPRETER=true, so methods named with
    // this prefix execute on the CoreCLR interpreter. JitTrampoline.Bounce
    // lives in a separate assembly so it stays JIT'd, producing two
    // InterpreterFrame regions separated by the JIT'd bounce.

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InterpTestMethodOuter()
    {
        JitTrampoline.Bounce(InterpTestMethodInner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InterpTestMethodInner()
    {
        // Force a null-pointer AV. A first-chance AV is caught by every dump
        // generator; an unhandled managed exception is not (interpreter UEF
        // doesn't escalate to second-chance today).
        Marshal.ReadIntPtr(IntPtr.Zero);
    }
}
