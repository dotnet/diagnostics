# Licensed to the .NET Foundation under one or more agreements.
# The .NET Foundation licenses this file to you under the MIT license.
#

import lldb
import re
import testutils as test

# bpmd -clear


def runScenario(assembly, debugger, target):
    process = target.GetProcess()
    res = lldb.SBCommandReturnObject()
    ci = debugger.GetCommandInterpreter()

    # Run debugger, wait until libcoreclr is loaded,
    # set breakpoint at Test.Main and stop there
    test.stop_in_main(debugger, assembly)

    # Set breakpoint

    ci.HandleCommand("bpmd " + assembly + " Test.UnlikelyInlined", res)
    out_msg = res.GetOutput()
    err_msg = res.GetError()
    print(out_msg)
    print(err_msg)
    # Interpreter must have this command and able to run it
    test.assertTrue(res.Succeeded())

    # Output is not empty
    # Should be at least 'Adding pending breakpoints...'
    test.assertTrue(res.GetOutputSize() > 0)

    # Error message is empty
    test.assertTrue(res.GetErrorSize() == 0)

    # Delete the first breakpoint

    ci.HandleCommand("bpmd -clear 1", res)
    out_msg = res.GetOutput()
    err_msg = res.GetError()
    print(out_msg)
    print(err_msg)
    # Interpreter must have this command and able to run it
    test.assertTrue(res.Succeeded())

    match = re.search('Cleared', out_msg)
    # Check for specific output
    test.assertTrue(match)

    # Error message is empty
    test.assertTrue(res.GetErrorSize() == 0)

    process.Continue()
    # Process must be exited
    test.assertEqual(process.GetState(), lldb.eStateStopped)

    # The reason of this stop must be a breakpoint
    test.assertEqual(process.GetSelectedThread().GetStopReason(),
                     lldb.eStopReasonBreakpoint)

    #

    # Delete all breakpoints, continue current process and checks its exit code
    test.exit_lldb(debugger, assembly)
