// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
        HostRuntimeFlavor flavor = HostRuntimeFlavor::NetCore;
        LPCSTR hostRuntimeDirectory = nullptr;
        int major = 0, minor = 0;

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
                if (*arguments != nullptr && strcmp(*arguments, "-clear") == 0)
                {
                    SetHostRuntime(HostRuntimeFlavor::NetCore, 0, 0, nullptr);
                    arguments++;
                }
                if (*arguments != nullptr && strcmp(*arguments, "-none") == 0)
                {
                    flavor = HostRuntimeFlavor::None;
                    arguments++;
                }
                else if (*arguments != nullptr && strcmp(*arguments, "-netcore") == 0)
                {
                    flavor = HostRuntimeFlavor::NetCore; 
                    arguments++;
                }
                if (*arguments != nullptr && strcmp(*arguments, "-major") == 0)
                {
                    arguments++;
                    if (*arguments != nullptr)
                    {
                        major = atoi(*arguments);
                        arguments++;
                    }
                }
                if (*arguments != nullptr)
                {
                    hostRuntimeDirectory = *arguments;
                    arguments++;
                }
                if (!SetHostRuntime(flavor, major, minor, hostRuntimeDirectory))
                {
                    result.Printf("Invalid host runtime path: %s\n", hostRuntimeDirectory);
                    result.SetStatus(lldb::eReturnStatusFailed);
                    return result.Succeeded();
                }
            }
        }
        GetHostRuntime(flavor, major, minor, hostRuntimeDirectory);
        switch (flavor)
        {
            case HostRuntimeFlavor::None:
                result.Printf("Using no runtime to host the managed SOS code\n");
                break;
            case HostRuntimeFlavor::NetCore:
                result.Printf("Using .NET Core runtime (version %d.%d) to host the managed SOS code\n", major, minor);
                break;
            default:
                break;
        }
        if (hostRuntimeDirectory != nullptr)
        {
            result.Printf("Host runtime path: %s\n", hostRuntimeDirectory);
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
