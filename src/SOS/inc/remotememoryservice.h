// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include <stdarg.h>
#include <unknwn.h>

#ifdef __cplusplus
extern "C" {
#endif

/// <summary>
/// IRemoteMemoryService
/// </summary>
MIDL_INTERFACE("CD6A0F22-8BCF-4297-9366-F440C2D1C781")
IRemoteMemoryService : public IUnknown
{
public:
    virtual HRESULT STDMETHODCALLTYPE AllocVirtual(
        ULONG64 address,
        ULONG32 size,
        ULONG32 typeFlags,
        ULONG32 protectFlags,
        ULONG64* remoteAddress) = 0;

    virtual HRESULT STDMETHODCALLTYPE FreeVirtual(
        ULONG64 address,
        ULONG32 size,
        ULONG32 typeFlags) = 0;
};

#ifdef __cplusplus
};
#endif
