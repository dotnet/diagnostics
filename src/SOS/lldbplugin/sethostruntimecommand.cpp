// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//

#include "sosplugin.h"
#include <dlfcn.h>
#include <string.h>
#include <string>
#include <stdlib.h>
#include <limits.h>

class sethostruntimeCommand : public lldb::SBCommandPluginInterface
{
public:
    sethostruntimeCommand()
    {
    }

    virtual bool
    DoExecute (lldb::SBDebugger debugger,
               char** arguments,
               lldb::SBCommandReturnObject &result)
    {
        result.SetStatus(lldb::eReturnStatusSuccessFinishResult);

        if (arguments != nullptr && arguments[0] != nullptr)
        {
            if (IsHostingInitialized())
            {
                result.Printf("Runtime hosting already initialized\n");
                result.SetStatus(lldb::eReturnStatusFailed);
                return result.Succeeded();
            }
            else 
            {
                if (strcmp(arguments[0], "-none") == 0)
                {
                    SetHostRuntimeFlavor(HostRuntimeFlavor::None); 
                }
                else if (strcmp(arguments[0], "-netcore") == 0)
                {
                    SetHostRuntimeFlavor(HostRuntimeFlavor::NetCore); 
                }
                else if (!SetHostRuntimeDirectory(arguments[0])) 
                {
                    result.Printf("Invalid host runtime path: %s\n", arguments[0]);
                    result.SetStatus(lldb::eReturnStatusFailed);
                    return result.Succeeded();
                }
            }
        }
        const char* flavor = "<unknown>";
        switch (GetHostRuntimeFlavor())
        {
            case HostRuntimeFlavor::None:
                flavor = "no";
                break;
            case HostRuntimeFlavor::NetCore:
                flavor = ".NET Core";
                break;
            default:
                break;
        }
        result.Printf("Using %s runtime to host the managed SOS code\n", flavor);
        const char* directory = GetHostRuntimeDirectory();
        if (directory != nullptr)
        {
            result.Printf("Host runtime path: %s\n", directory);
        }
        return result.Succeeded();
    }
};

bool
sethostruntimeCommandInitialize(lldb::SBDebugger debugger)
{
    g_services->AddCommand("sethostruntime", new sethostruntimeCommand(), "Sets the path to the .NET Core runtime to use to host the managed code that runs as part of SOS");
    return true;
}
