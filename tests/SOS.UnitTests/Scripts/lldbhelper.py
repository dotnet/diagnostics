import lldb

def __lldb_init_module(debugger, internal_dict):    
    try:
        debugger.HandleCommand('command script add -f lldbhelper.runcommand runcommand')
    except ImportError:
        print("Failed to initialize the python lldb module - ImportError")
        import os
        os._exit(1)
    print("<END_COMMAND_OUTPUT>")

def runcommand(debugger, command, result, internal_dict):
    interpreter = debugger.GetCommandInterpreter()

    commandResult = lldb.SBCommandReturnObject()
    interpreter.HandleCommand(command, commandResult)

    if commandResult.GetOutputSize() > 0:
        print(commandResult.GetOutput())

    if commandResult.GetErrorSize() > 0:
        print(commandResult.GetError())

    if commandResult.Succeeded():
        print("<END_COMMAND_OUTPUT>")
    else:
        print("<END_COMMAND_ERROR>")
