// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

internal class InterpreterStackTestApp
{
    private static int Main()
    {
        Console.WriteLine("Interpreter stack test starting...");
        InterpreterUserHelper helper = new();
        helper.InterpTestMethodRunNested("argument string");
        return 0;
    }
}
