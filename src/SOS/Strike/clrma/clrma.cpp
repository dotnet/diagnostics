// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "exts.h"
#include "managedanalysis.h"

HRESULT CLRMACreateInstance(ICLRManagedAnalysis** ppCLRMA);
HRESULT CLRMAReleaseInstance();

ICLRManagedAnalysis* g_managedAnalysis = nullptr;

int g_clrmaGlobalFlags = ClrmaGlobalFlags::LoggingEnabled | ClrmaGlobalFlags::DacClrmaEnabled | ClrmaGlobalFlags::ManagedClrmaEnabled;

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

    CMDOption option[] =
    {   // name, vptr, type, hasValue
        {"-enable", &bEnable, COBOOL, FALSE},
        {"-disable", &bDisable, COBOOL, FALSE},
        {"-dac", &bDacClrma, COBOOL, FALSE},
        {"-managed", &bManagedClrma, COBOOL, FALSE},
        {"-logging", &bLogging, COBOOL, FALSE},
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
    }

    ExtOut("CLRMA logging:              %s\n", (g_clrmaGlobalFlags & ClrmaGlobalFlags::LoggingEnabled) ? "enabled (disable with '-disable -logging')" : "disabled (enable with '-enable -logging')");
    ExtOut("CLRMA direct DAC support:   %s\n", (g_clrmaGlobalFlags & ClrmaGlobalFlags::DacClrmaEnabled) ? "enabled (disable with '-disable -dac')" : "disabled (enable with '-enable -dac')");
    ExtOut("CLRMA managed support:      %s\n", (g_clrmaGlobalFlags & ClrmaGlobalFlags::ManagedClrmaEnabled) ? "enabled (disable with '-disable -managed')" : "disabled (enable with '-enable -managed')");

    return Status;
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
