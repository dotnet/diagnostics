// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include <stdarg.h>
#include <unknwn.h>
#include <clrma.h> // IDL

#ifdef __cplusplus
extern "C" {
#endif

/// <summary>
/// ICLRMAService
/// </summary>
MIDL_INTERFACE("1FCF4C14-60C1-44E6-84ED-20506EF3DC60")
ICLRMAService : public IUnknown
{
public:
    virtual HRESULT STDMETHODCALLTYPE AssociateClient( 
        IUnknown *pUnknown) = 0;
    
    virtual HRESULT STDMETHODCALLTYPE GetThread( 
        ULONG osThreadId,
        ICLRMAClrThread **ppClrThread) = 0;
    
    virtual HRESULT STDMETHODCALLTYPE GetException( 
        ULONG64 address,
        ICLRMAClrException **ppClrException) = 0;
    
    virtual HRESULT STDMETHODCALLTYPE GetObjectInspection( 
        ICLRMAObjectInspection **ppObjectInspection) = 0;
};

#ifdef __cplusplus
};
#endif
