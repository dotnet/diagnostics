// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//

#include "sosplugin.h"
#include <dlfcn.h>
#include <string.h>
#include <string>
#include <stdlib.h>
#include <limits.h>

class setsostidCommand : public lldb::SBCommandPluginInterface
{
public:
    setsostidCommand()
    {
    }

    virtual bool
        DoExecute(lldb::SBDebugger debugger,
        char** arguments,
        lldb::SBCommandReturnObject &result)
    {
        result.SetStatus(lldb::eReturnStatusSuccessFinishResult);

        if (arguments == nullptr || arguments[0] == nullptr)
        {
            int index = 1;
            result.Printf("OS TID -> lldb index\n");
            for (const SpecialThreadInfoEntry& entry: g_services->ThreadInfos())
            {
                if (entry.tid != 0)
                {
                    result.Printf("0x%08x -> %d\n", entry.tid, index);
                }
                index++;
            }
        }   
        else if (arguments[1] == nullptr)
        {
            result.Printf("Need thread index parameter that maps to the OS tid. setsostid <tid> <index>\n");
        }
        else
        {
            ULONG tid = 0;
            if (strcmp(arguments[0], "-c") != 0 && strcmp(arguments[0], "--clear") != 0) 
            {
                tid = strtoul(arguments[0], nullptr, 16);
            }
            ULONG index = strtoul(arguments[1], nullptr, 10);
            if (index <= 0)
            {
                result.Printf("Invalid thread index parameter\n");
            }
            else
            {
                g_services->AddThreadInfoEntry(tid, index);
                if (tid == 0)
                {
                    result.Printf("Cleared lldb thread index %d\n", index);
                }
                else {
                    result.Printf("Mapped SOS OS tid 0x%x to lldb thread index %d\n", tid, index);
                }
            }
        }
        return result.Succeeded();
    }
};

bool
setsostidCommandInitialize(lldb::SBDebugger debugger)
{
    g_services->AddCommand("setsostid", new setsostidCommand(), "Set the current os tid/thread index instead of using the one lldb provides. setsostid <tid> <index>");
    return true;
}
