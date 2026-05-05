// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

public class InterpreterUserHelper
{
    // The "InterpTestMethod" prefix matches the DOTNET_Interpreter glob the SOS
    // test framework sets when SOS_TEST_INTERPRETER=true, so methods named with
    // this prefix are guaranteed to execute on the CoreCLR interpreter.

    public void InterpTestMethodRunNested(string argument)
    {
        Console.WriteLine("InterpTestMethodRunNested received: " + argument);
        InterpTestMethodThrow(argument);
    }

    private void InterpTestMethodThrow(string argument)
    {
        // Two interpreted user frames so the stack walker has at least two to walk
        // through and the test can assert both appear under the [InterpreterFrame:]
        // sentinel.
        throw new InvalidOperationException("Throwing from interpreted frame: " + argument);
    }
}
