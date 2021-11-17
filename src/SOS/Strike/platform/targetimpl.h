// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//

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

    Target();
    virtual ~Target();

public:
    static ITarget* GetInstance();

    HRESULT CreateInstance(IRuntime** ppRuntime);

#ifndef FEATURE_PAL
    static bool SwitchRuntime(bool desktop)
    {
        GetInstance();
        _ASSERTE(s_target != nullptr);
        return s_target->SwitchRuntimeInstance(desktop);
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

    LPCSTR STDMETHODCALLTYPE GetTempDirectory();

    HRESULT STDMETHODCALLTYPE GetRuntime(IRuntime** pRuntime);

    void STDMETHODCALLTYPE Flush();
};

