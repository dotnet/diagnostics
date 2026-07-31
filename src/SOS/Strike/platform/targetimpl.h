// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include "target.h"
#include "runtimeimpl.h"

extern bool IsWindowsTarget();

//----------------------------------------------------------------------------
// Local implementation of ITarget when the host doesn't provide it
//----------------------------------------------------------------------------
class Target : public ITarget
{
private:
    LONG m_ref;
    IDebuggerServices* m_debuggerServices;
    LPCSTR m_tmpPath;
#ifndef FEATURE_PAL
    Runtime* m_desktop;
#endif
    Runtime* m_netcore;

    static Target* s_target;

#ifndef FEATURE_PAL
    bool SwitchRuntimeInstance(bool desktop);
#endif
    void DisplayStatusInstance();

    Target(IDebuggerServices* debuggerServices);
    virtual ~Target();

public:
    static ITarget* GetInstance(IDebuggerServices* debuggerServices);

    HRESULT CreateInstance(IRuntime** ppRuntime);

#ifndef FEATURE_PAL
    static bool SwitchRuntime(bool desktop)
    {
        // Lazily create the local target with the session-lifetime debugger services so
        // -netfx/-netcore switching works even when no command has created a target yet.
        ITarget* target = GetInstance(GetDebuggerServices());
        bool switched = s_target != nullptr && s_target->SwitchRuntimeInstance(desktop);
        if (target != nullptr)
        {
            target->Release();
        }
        return switched;
    }
#endif

    static void DisplayStatus()
    {
        if (s_target != nullptr) 
        {
            s_target->DisplayStatusInstance();
        }
    }

    static void CleanupTarget()
    {
        if (s_target != nullptr)
        {
            s_target->Release();
        }
    }

    //----------------------------------------------------------------------------
    // IUnknown
    //----------------------------------------------------------------------------

    HRESULT STDMETHODCALLTYPE QueryInterface(
        REFIID InterfaceId,
        PVOID* Interface);

    ULONG STDMETHODCALLTYPE AddRef();

    ULONG STDMETHODCALLTYPE Release();

    //----------------------------------------------------------------------------
    // ITarget
    //----------------------------------------------------------------------------

    OperatingSystem STDMETHODCALLTYPE GetOperatingSystem();

    HRESULT STDMETHODCALLTYPE GetService(REFIID serviceId, PVOID* ppService);

    LPCSTR STDMETHODCALLTYPE GetTempDirectory();

    HRESULT STDMETHODCALLTYPE GetRuntime(IRuntime** pRuntime);

    void STDMETHODCALLTYPE Flush();
};
