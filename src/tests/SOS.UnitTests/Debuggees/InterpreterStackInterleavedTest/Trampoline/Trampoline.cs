// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;

namespace InterpreterStackInterleavedTest.Trampoline;

// Always JIT'd. Methods in this assembly are not in g_interpModule even if
// they happen to match the DOTNET_Interpreter glob.
public static class JitTrampoline
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Bounce(Action callback) => callback();
}
