// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "managedanalysis.h"
#include "exts.h"

HRESULT CLRMACreateInstance(ICLRManagedAnalysis** ppCLRMA);
HRESULT CLRMAReleaseInstance();

ICLRManagedAnalysis* g_managedAnalysis = nullptr;

int g_clrmaGlobalFlags = ClrmaGlobalFlags::LoggingEnabled | ClrmaGlobalFlags::DacClrmaEnabled | ClrmaGlobalFlags::ManagedClrmaEnabled | ClrmaGlobalFlags::ModuleEnumeration_EntryPointAndDllModule;

//
// Exports
//

HRESULT
CLRMACreateInstance(ICLRManagedAnalysis** ppCLRMA)
{
    HRESULT hr = E_UNEXPECTED;

    if (ppCLRMA == nullptr)
    {
        return E_INVALIDARG;
    }

    *ppCLRMA = nullptr;

    if (g_managedAnalysis == nullptr)
    {
        g_managedAnalysis = new (std::nothrow) ClrmaManagedAnalysis();
        if (g_managedAnalysis == nullptr)
        {
           return E_OUTOFMEMORY;
        }
        OnUnloadTask::Register(([]() { CLRMAReleaseInstance(); }));
    }

    g_managedAnalysis->AddRef();
    *ppCLRMA = g_managedAnalysis;
    return S_OK;
}

HRESULT
CLRMAReleaseInstance()
{
    TraceInformation("CLRMAReleaseInstance\n");
    if (g_managedAnalysis != nullptr)
    {
        g_managedAnalysis->Release();
        g_managedAnalysis = nullptr;
    }
    return S_OK;
}

DECLARE_API(clrmaconfig)
{
    INIT_API_EXT();

    BOOL bEnable = FALSE;
    BOOL bDisable = FALSE;
    BOOL bDacClrma = FALSE;
    BOOL bManagedClrma = FALSE;
    BOOL bLogging = FALSE;
    SIZE_T moduleEnumerationScheme = -1; // None=0, EntryPointModule=1, EntryPointAndEntryPointDllModule=2, AllModules=3

    CMDOption option[] =
    {   // name, vptr, type, hasValue
        {"-enable", &bEnable, COBOOL, FALSE},
        {"-disable", &bDisable, COBOOL, FALSE},
        {"-dac", &bDacClrma, COBOOL, FALSE},
        {"-managed", &bManagedClrma, COBOOL, FALSE},
        {"-logging", &bLogging, COBOOL, FALSE},
        {"-enumScheme", &moduleEnumerationScheme, COSIZE_T, TRUE},
    };

    if (!GetCMDOption(args, option, ARRAY_SIZE(option), NULL, 0, NULL))
    {
        return E_INVALIDARG;
    }

    if (bEnable)
    {
        if (bDacClrma)
        {
            g_clrmaGlobalFlags |= ClrmaGlobalFlags::DacClrmaEnabled;
        }
        if (bManagedClrma)
        {
            g_clrmaGlobalFlags |= ClrmaGlobalFlags::ManagedClrmaEnabled;
        }
        if (bLogging)
        {
            g_clrmaGlobalFlags |= ClrmaGlobalFlags::LoggingEnabled;
        }
    }
    else if (bDisable)
    {
        if (bDacClrma)
        {
            g_clrmaGlobalFlags &= ~ClrmaGlobalFlags::DacClrmaEnabled;
        }
        if (bManagedClrma)
        {
            g_clrmaGlobalFlags &= ~ClrmaGlobalFlags::ManagedClrmaEnabled;
        }
        if (bLogging)
        {
            g_clrmaGlobalFlags &= ~ClrmaGlobalFlags::LoggingEnabled;
        }
        if (moduleEnumerationScheme != 0)
        {
            g_clrmaGlobalFlags &= ~(ClrmaGlobalFlags::ModuleEnumeration_EntryPointModule |
                                 ClrmaGlobalFlags::ModuleEnumeration_EntryPointAndDllModule |
                                 ClrmaGlobalFlags::ModuleEnumeration_AllModules);
        }
    }

    if (moduleEnumerationScheme != -1)
    {
        g_clrmaGlobalFlags &= ~(ClrmaGlobalFlags::ModuleEnumeration_EntryPointModule |
                                ClrmaGlobalFlags::ModuleEnumeration_EntryPointAndDllModule |
                                ClrmaGlobalFlags::ModuleEnumeration_AllModules);
        g_clrmaGlobalFlags |= moduleEnumerationScheme == 1 ? ClrmaGlobalFlags::ModuleEnumeration_EntryPointModule :
                                moduleEnumerationScheme == 2 ? ClrmaGlobalFlags::ModuleEnumeration_EntryPointAndDllModule :
                                moduleEnumerationScheme == 3 ? ClrmaGlobalFlags::ModuleEnumeration_AllModules :
                                0;
    }

    ExtOut("CLRMA logging:              %s\n", (g_clrmaGlobalFlags & ClrmaGlobalFlags::LoggingEnabled) ? "enabled (disable with '-disable -logging')" : "disabled (enable with '-enable -logging')");
    ExtOut("CLRMA direct DAC support:   %s\n", (g_clrmaGlobalFlags & ClrmaGlobalFlags::DacClrmaEnabled) ? "enabled (disable with '-disable -dac')" : "disabled (enable with '-enable -dac')");
    ExtOut("CLRMA managed support:      %s\n", (g_clrmaGlobalFlags & ClrmaGlobalFlags::ManagedClrmaEnabled) ? "enabled (disable with '-disable -managed')" : "disabled (enable with '-enable -managed')");
    ExtOut("CLRMA module enumeration:   %s\n", (g_clrmaGlobalFlags & ClrmaGlobalFlags::ModuleEnumeration_EntryPointModule) ? "Search for crashinfo on EntryPoint module (-enumScheme 1)" :
                                                    (g_clrmaGlobalFlags & ClrmaGlobalFlags::ModuleEnumeration_EntryPointAndDllModule) ? "Search for crash info on both EntryPoint and DLL with same name (-enumScheme 2)" :
                                                    (g_clrmaGlobalFlags & ClrmaGlobalFlags::ModuleEnumeration_AllModules) ? "Search for crash info on all modules (-enumScheme 3)" :
                                                    "Only read crashinfo from Exception if present (use -enumScheme 0)");

    return Status;
}

//
// !clrma command
//
// Drives the CLRMA (CLR Managed Analysis) interfaces the same way Watson/!analyze does so the
// managed crash bucketing path can be exercised locally with any SOS host (e.g. dotnet-dump),
// without requiring the debugger engine's built-in !clrma/!analyze commands.
//

template <typename T>
static void
PrintClrmaFrames(T* frameProvider, PCSTR indent)
{
    UINT frameCount = 0;
    if (FAILED(frameProvider->get_FrameCount(&frameCount)) || frameCount == 0)
    {
        ExtOut("%s<no managed frames>\n", indent);
        return;
    }
    for (UINT i = 0; i < frameCount; i++)
    {
        if (IsInterrupt())
        {
            return;
        }
        ULONG64 ip = 0;
        ULONG64 sp = 0;
        ULONG64 displacement = 0;
        BSTR module = nullptr;
        BSTR function = nullptr;
        if (SUCCEEDED(frameProvider->Frame(i, &ip, &sp, &module, &function, &displacement)))
        {
            ExtOut("%s%p %p %S!%S+0x%llx\n",
                indent,
                SOS_PTR(sp),
                SOS_PTR(ip),
                module != nullptr ? module : W("<unknown_module>"),
                function != nullptr ? function : W("<unknown_function>"),
                (unsigned long long)displacement);
        }
        SysFreeString(module);
        SysFreeString(function);
    }
}

static void
PrintClrmaException(ICLRMAClrException* exception, int nestingLevel)
{
    if (exception == nullptr)
    {
        return;
    }

    const char* indent = nestingLevel <= 0 ? "    " :
                         nestingLevel == 1 ? "        " :
                         nestingLevel == 2 ? "            " : "                ";

    ULONG64 address = 0;
    exception->get_Address(&address);

    HRESULT exceptionHResult = 0;
    exception->get_HResult(&exceptionHResult);

    BSTR type = nullptr;
    exception->get_Type(&type);

    BSTR message = nullptr;
    exception->get_Message(&message);

    ExtOut("%sException object: %p\n", indent, SOS_PTR(address));
    ExtOut("%sException type:   %S\n", indent, type != nullptr ? type : W("<Unknown>"));
    ExtOut("%sMessage:          %S\n", indent, message != nullptr ? message : W("<none>"));
    ExtOut("%sHResult:          %08x\n", indent, exceptionHResult);
    ExtOut("%sStackTrace (generated):\n", indent);
    PrintClrmaFrames(exception, indent);

    SysFreeString(type);
    SysFreeString(message);

    USHORT innerCount = 0;
    if (SUCCEEDED(exception->get_InnerExceptionCount(&innerCount)))
    {
        for (USHORT i = 0; i < innerCount; i++)
        {
            if (IsInterrupt())
            {
                return;
            }
            ReleaseHolder<ICLRMAClrException> inner;
            if (exception->InnerException(i, inner.GetAddr()) == S_OK && inner != nullptr)
            {
                ExtOut("%sInnerException:\n", indent);
                PrintClrmaException(inner, nestingLevel + 1);
            }
        }
    }
}

DECLARE_API(clrma)
{
    INIT_API_EXT();

    ULONG64 osThreadId = 0;

    CMDOption option[] =
    {   // name, vptr, type, hasValue
        {"-t", &osThreadId, COSIZE_T, TRUE},
    };

    if (!GetCMDOption(args, option, ARRAY_SIZE(option), NULL, 0, NULL))
    {
        return E_INVALIDARG;
    }

    ReleaseHolder<ICLRManagedAnalysis> managedAnalysis;
    if (FAILED(Status = CLRMACreateInstance(managedAnalysis.GetAddr())))
    {
        ExtErr("CLRMACreateInstance FAILED %08x\n", Status);
        return Status;
    }

    if (FAILED(Status = managedAnalysis->AssociateClient(client)))
    {
        ExtErr("No managed analysis provider available (AssociateClient FAILED %08x).\n", Status);
        ExtErr("Use 'clrmaconfig' to check the enabled CLRMA providers.\n");
        return Status;
    }

    BSTR providerName = nullptr;
    if (SUCCEEDED(managedAnalysis->get_ProviderName(&providerName)) && providerName != nullptr)
    {
        ExtOut("Managed analysis provider: %S\n", providerName);
        SysFreeString(providerName);
    }

    if (osThreadId == 0)
    {
        ULONG currentThreadId = 0;
        if (FAILED(Status = g_ExtSystem->GetCurrentThreadSystemId(&currentThreadId)))
        {
            ExtErr("GetCurrentThreadSystemId FAILED %08x\n", Status);
            return Status;
        }
        osThreadId = currentThreadId;
    }

    ReleaseHolder<ICLRMAClrThread> thread;
    Status = managedAnalysis->GetThread((ULONG)osThreadId, thread.GetAddr());
    if (FAILED(Status) || thread == nullptr)
    {
        ExtOut("Thread %04x is not a managed thread or has no managed analysis (%08x).\n", (ULONG)osThreadId, Status);
        return S_OK;
    }

    ULONG reportedThreadId = (ULONG)osThreadId;
    thread->get_OSThreadId(&reportedThreadId);
    ExtOut("OSThreadId: %04x\n", reportedThreadId);

    ExtOut("Managed stack trace:\n");
    PrintClrmaFrames(thread.GetPtr(), "    ");

    ReleaseHolder<ICLRMAClrException> currentException;
    if (thread->get_CurrentException(currentException.GetAddr()) == S_OK && currentException != nullptr)
    {
        ExtOut("Current exception:\n");
        PrintClrmaException(currentException, 0);
    }
    else
    {
        ExtOut("Current exception: <none>\n");
    }

    USHORT nestedCount = 0;
    if (SUCCEEDED(thread->get_NestedExceptionCount(&nestedCount)) && nestedCount > 0)
    {
        for (USHORT i = 0; i < nestedCount; i++)
        {
            if (IsInterrupt())
            {
                break;
            }
            ReleaseHolder<ICLRMAClrException> nested;
            if (thread->NestedException(i, nested.GetAddr()) == S_OK && nested != nullptr)
            {
                ExtOut("Nested exception #%d:\n", i);
                PrintClrmaException(nested, 0);
            }
        }
    }

    return S_OK;
}

extern void InternalOutputVaList(ULONG mask, PCSTR format, va_list args);

void
TraceInformation(PCSTR format, ...)
{
    if (g_clrmaGlobalFlags & ClrmaGlobalFlags::LoggingEnabled)
    {
        va_list args;
        va_start(args, format);
        InternalOutputVaList(DEBUG_OUTPUT_NORMAL, format, args);
        va_end(args);
    }
}

void
TraceError(PCSTR format, ...)
{
    if (g_clrmaGlobalFlags & ClrmaGlobalFlags::LoggingEnabled)
    {
        va_list args;
        va_start(args, format);
        InternalOutputVaList(DEBUG_OUTPUT_ERROR, format, args);
        va_end(args);
    }
}
