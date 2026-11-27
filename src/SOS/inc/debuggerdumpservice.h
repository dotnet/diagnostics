// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include <unknwn.h>

MIDL_INTERFACE("7b52c66b-b09d-45d0-b871-9c113db3c088")
IDebuggerDumpService : public IUnknown
{
public:
    // Returns the SOS setting regardless of the current target's dump format.
    virtual HRESULT STDMETHODCALLTYPE IsMinidumpSafeModeEnabled() = 0;
};
