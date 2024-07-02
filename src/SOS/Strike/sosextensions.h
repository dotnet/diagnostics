// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include <extensions.h>

//-----------------------------------------------------------------------------------------
// Extension helper class
//-----------------------------------------------------------------------------------------
class SOSExtensions : public Extensions
{
    SOSExtensions(IDebuggerServices* debuggerServices, IHost* host);
#ifndef FEATURE_PAL
    ~SOSExtensions();
#endif

public:
#ifndef FEATURE_PAL
    static HRESULT Initialize(IDebugClient* client);
#endif
    static HRESULT Initialize(IHost* host, IDebuggerServices* debuggerServices);
    static void Uninitialize();
    IHost* GetHost();
};

extern HRESULT GetRuntime(IRuntime** ppRuntime);
