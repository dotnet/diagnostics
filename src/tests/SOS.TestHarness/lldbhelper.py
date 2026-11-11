# Licensed to the .NET Foundation under one or more agreements.
# The .NET Foundation licenses this file to you under the MIT license.
#
# lldb command framing for the SOS test harness (the LldbCliHost backend).
#
# A bare lldb REPL gives no reliable per-command "done" delimiter and no success/failure status, so the
# harness never issues commands directly: it imports this helper and runs every command as
# `runcommand <cmd>`. The helper executes the real command through the command interpreter, streams its
# output, and then prints a sentinel line the host keys on -- <END_COMMAND_OUTPUT> when the command
# succeeded, <END_COMMAND_ERROR> when it failed (so the host gets a real success bit, not a screen-scrape).

import lldb
import sys

END_OUTPUT = "<END_COMMAND_OUTPUT>"
END_ERROR = "<END_COMMAND_ERROR>"


def __lldb_init_module(debugger, internal_dict):
    debugger.HandleCommand("command script add -f lldbhelper.runcommand runcommand")
    # Emit the marker once at import time so the host can drain the startup banner up to a known point
    # and know the helper is ready before it sends the first command.
    sys.stdout.write(END_OUTPUT + "\n")
    sys.stdout.flush()


def runcommand(debugger, command, result, internal_dict):
    interpreter = debugger.GetCommandInterpreter()

    ret = lldb.SBCommandReturnObject()
    interpreter.HandleCommand(command, ret)

    # GetOutput()/GetError() already include trailing newlines; use write (not print) to avoid doubling
    # them, so the sentinel always lands on its own line.
    if ret.GetOutputSize() > 0:
        sys.stdout.write(ret.GetOutput())
    if ret.GetErrorSize() > 0:
        sys.stdout.write(ret.GetError())

    sys.stdout.write((END_OUTPUT if ret.Succeeded() else END_ERROR) + "\n")
    sys.stdout.flush()
