// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "exts.h"

// Windows host only managed command stubs

HRESULT ExecuteManagedOnlyCommand(PCSTR commandName, PCSTR args)
{
    HRESULT hr = ExecuteCommand(commandName, args);
    if (hr == E_NOTIMPL)
    {
        ExtErr("Unrecognized command '%s'\n", commandName);
    }
    return hr;
}

DECLARE_API(DumpStackObjects)
{
    INIT_API_EXT();
    MINIDUMP_NOT_SUPPORTED();
    return ExecuteManagedOnlyCommand("dumpstackobjects", args);
}

DECLARE_API(EEHeap)
{
    INIT_API_EXT();
    return ExecuteManagedOnlyCommand("eeheap", args);
}

DECLARE_API(TraverseHeap)
{
    INIT_API_EXT();
    MINIDUMP_NOT_SUPPORTED();
    return ExecuteManagedOnlyCommand("traverseheap", args);
}

DECLARE_API(DumpRuntimeTypes)
{
    INIT_API_EXT();
    MINIDUMP_NOT_SUPPORTED();
    return ExecuteManagedOnlyCommand("dumpruntimetypes", args);
}

DECLARE_API(DumpHeap)
{
    INIT_API_EXT();
    MINIDUMP_NOT_SUPPORTED();
    return ExecuteManagedOnlyCommand("dumpheap", args);
}

DECLARE_API(VerifyHeap)
{
    INIT_API_EXT();
    MINIDUMP_NOT_SUPPORTED();
    return ExecuteManagedOnlyCommand("verifyheap", args);
}

DECLARE_API(AnalyzeOOM)
{
    INIT_API_EXT();
    MINIDUMP_NOT_SUPPORTED();
    return ExecuteManagedOnlyCommand("analyzeoom", args);
}

DECLARE_API(VerifyObj)
{
    INIT_API_EXT();
    MINIDUMP_NOT_SUPPORTED();
    return ExecuteManagedOnlyCommand("verifyobj", args);
}

DECLARE_API(ListNearObj)
{
    INIT_API_EXT();
    MINIDUMP_NOT_SUPPORTED();
    return ExecuteManagedOnlyCommand("listnearobj", args);
}

DECLARE_API(GCHeapStat)
{
    INIT_API_EXT();
    MINIDUMP_NOT_SUPPORTED();
    return ExecuteManagedOnlyCommand("gcheapstat", args);
}

DECLARE_API(FinalizeQueue)
{
    INIT_API_EXT();
    MINIDUMP_NOT_SUPPORTED();
    return ExecuteManagedOnlyCommand("finalizequeue", args);
}

DECLARE_API(ThreadPool)
{
    INIT_API_EXT();
    MINIDUMP_NOT_SUPPORTED();
    return ExecuteManagedOnlyCommand("threadpool", args);
}

DECLARE_API(PathTo)
{
    INIT_API_EXT();
    MINIDUMP_NOT_SUPPORTED();
    return ExecuteManagedOnlyCommand("pathto", args);
}

DECLARE_API(GCRoot)
{
    INIT_API_EXT();
    MINIDUMP_NOT_SUPPORTED();
    return ExecuteManagedOnlyCommand("gcroot", args);
}

DECLARE_API(GCWhere)
{
    INIT_API_EXT();
    MINIDUMP_NOT_SUPPORTED();
    return ExecuteManagedOnlyCommand("gcwhere", args);
}

DECLARE_API(ObjSize)
{
    INIT_API_EXT();
    MINIDUMP_NOT_SUPPORTED();
    return ExecuteManagedOnlyCommand("objsize", args);
}

DECLARE_API(SetSymbolServer)
{
    INIT_API_EXT();
    return ExecuteManagedOnlyCommand("setsymbolserver", args);
}

DECLARE_API(assemblies)
{
    INIT_API_EXT();
    return ExecuteManagedOnlyCommand("assemblies", args);
}

DECLARE_API(crashinfo)
{
    INIT_API_EXT();
    return ExecuteManagedOnlyCommand("crashinfo", args);
}

DECLARE_API(DumpAsync)
{
    INIT_API_EXT();
    return ExecuteManagedOnlyCommand("dumpasync", args);
}

DECLARE_API(logging)
{
    INIT_API_EXT();
    return ExecuteManagedOnlyCommand("logging", args);
}

DECLARE_API(maddress)
{
    INIT_API_EXT();
    return ExecuteManagedOnlyCommand("maddress", args);
}

DECLARE_API(dumpexceptions)
{
    INIT_API_EXT();
    return ExecuteManagedOnlyCommand("dumpexceptions", args);
}

DECLARE_API(dumpgen)
{
    INIT_API_EXT();
    return ExecuteManagedOnlyCommand("dumpgen", args);
}

DECLARE_API(sizestats)
{
    INIT_API_EXT();
    return ExecuteManagedOnlyCommand("sizestats", args);
}

DECLARE_API(DumpHttp)
{
    INIT_API_EXT();
    return ExecuteManagedOnlyCommand("dumphttp", args);
}

DECLARE_API(DumpRequests)
{
    INIT_API_EXT();
    return ExecuteManagedOnlyCommand("dumprequests", args);
}

typedef HRESULT (*PFN_COMMAND)(PDEBUG_CLIENT client, PCSTR args);

//
// Executes managed extension commands (i.e. !sos)
//
DECLARE_API(ext)
{
    INIT_API_EXT();

    if (args == nullptr || strlen(args) <= 0)
    {
        args = "Help";
    }
    std::string arguments(args);
    size_t pos = arguments.find(' ');
    std::string commandName = arguments.substr(0, pos);
    if (pos != std::string::npos)
    {
        arguments = arguments.substr(pos + 1);
    }
    else
    {
        arguments.clear();
    }
    Status = ExecuteCommand(commandName.c_str(), arguments.c_str());
    if (Status == E_NOTIMPL)
    {
        PFN_COMMAND commandFunc = (PFN_COMMAND)GetProcAddress(g_hInstance, commandName.c_str());
        if (commandFunc != nullptr)
        {
            Status = (*commandFunc)(client, arguments.c_str());
        }
        else 
        {
            ExtErr("Unrecognized command '%s'\n", commandName.c_str());
        }
    }
    return Status;
}

