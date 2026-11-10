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
EXTERN_GUID(IID_IRemoteMemoryService, 0xCD6A0F22, 0x8BCF, 0x4297, 0x93, 0x66, 0xF4, 0x40, 0xC2, 0xD1, 0xC7, 0x81);
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
