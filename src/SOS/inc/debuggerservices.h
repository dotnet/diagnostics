// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#pragma once

#include <stdarg.h>
#include <unknwn.h>

#ifdef __cplusplus
extern "C" {
#endif

/// <summary>
/// IDebuggerServices 
/// 
/// The interface that the native debuggers (dbgeng/lldb) provide the managed extension infrastructure 
/// service (SOS.Extensions). Isn't used when SOS is hosted by SOS.Hosting (i.e. dotnet-dump).
/// </summary>
MIDL_INTERFACE("B4640016-6CA0-468E-BA2C-1FFF28DE7B72")
IDebuggerServices : public IUnknown
{
public:
    enum OperatingSystem
    {
        Unknown         = 0,
        Windows         = 1,
        Linux           = 2,
        OSX             = 3,
    };

    virtual HRESULT STDMETHODCALLTYPE GetOperatingSystem(
        OperatingSystem* operatingSystem) = 0;

    virtual HRESULT STDMETHODCALLTYPE GetDebuggeeType(
        PULONG debugClass,
        PULONG qualifier) = 0;

    virtual HRESULT STDMETHODCALLTYPE GetExecutingProcessorType(
        PULONG type) = 0;

    virtual HRESULT STDMETHODCALLTYPE AddCommand(
        PCSTR command,
        PCSTR help,
        PCSTR aliases[],
        int numberOfAliases) = 0;

    virtual void STDMETHODCALLTYPE OutputString(
        ULONG mask,
        PCSTR message) = 0;

    virtual HRESULT STDMETHODCALLTYPE ReadVirtual(
        ULONG64 offset,
        PVOID buffer,
        ULONG bufferSize,
        PULONG bytesRead) = 0;

    virtual HRESULT STDMETHODCALLTYPE WriteVirtual(
        ULONG64 offset,
        PVOID buffer,
        ULONG bufferSize,
        PULONG bytesWritten) = 0;

    virtual HRESULT STDMETHODCALLTYPE GetNumberModules(
        PULONG loaded,
        PULONG unloaded) = 0;

    virtual HRESULT STDMETHODCALLTYPE GetModuleNames(
        ULONG index,
        ULONG64 base,
        PSTR imageNameBuffer,
        ULONG imageNameBufferSize,
        PULONG imageNameSize,
        PSTR moduleNameBuffer,
        ULONG moduleNameBufferSize,
        PULONG moduleNameSize,
        PSTR loadedImageNameBuffer,
        ULONG loadedImageNameBufferSize,
        PULONG loadedImageNameSize) = 0;

    virtual HRESULT STDMETHODCALLTYPE GetModuleInfo(
        ULONG index,
        PULONG64 moduleBase,
        PULONG64 moduleSize,
        PULONG timestamp,
        PULONG checksum) = 0;

    virtual HRESULT STDMETHODCALLTYPE GetModuleVersionInformation(
        ULONG index,
        ULONG64 base,
        PCSTR item,
        PVOID buffer,
        ULONG bufferSize,
        PULONG versionInfoSize) = 0;

    virtual HRESULT STDMETHODCALLTYPE GetNumberThreads(
        PULONG number) = 0;

    virtual HRESULT STDMETHODCALLTYPE GetThreadIdsByIndex(
        ULONG start,
        ULONG count,
        PULONG ids,
        PULONG sysIds) = 0;

    virtual HRESULT STDMETHODCALLTYPE GetThreadContextBySystemId(
        ULONG32 sysId,
        ULONG32 contextFlags,
        ULONG32 contextSize,
        PBYTE context) = 0;
 
    virtual HRESULT STDMETHODCALLTYPE GetCurrentProcessSystemId(
        PULONG sysId) = 0;

    virtual HRESULT STDMETHODCALLTYPE GetCurrentThreadSystemId(
        PULONG sysId) = 0;

    virtual HRESULT STDMETHODCALLTYPE SetCurrentThreadSystemId(
        ULONG sysId) = 0;

    virtual HRESULT STDMETHODCALLTYPE GetThreadTeb(
        ULONG sysId,
        PULONG64 pteb) = 0;

    virtual HRESULT STDMETHODCALLTYPE VirtualUnwind(
        DWORD threadId,
        ULONG32 contextSize,
        PBYTE context) = 0;

    virtual HRESULT STDMETHODCALLTYPE GetSymbolPath(
        PSTR buffer,
        ULONG bufferSize,
        PULONG pathSize) = 0;

    virtual HRESULT STDMETHODCALLTYPE GetSymbolByOffset(
        ULONG moduleIndex,
        ULONG64 offset,
        PSTR nameBuffer,
        ULONG nameBufferSize,
        PULONG nameSize,
        PULONG64 displacement) = 0;

    virtual HRESULT STDMETHODCALLTYPE GetOffsetBySymbol(
        ULONG moduleIndex,
        PCSTR name,
        PULONG64 offset) = 0;

    virtual ULONG STDMETHODCALLTYPE GetOutputWidth() = 0;

    virtual HRESULT STDMETHODCALLTYPE SupportsDml(PULONG supported) = 0;

    virtual void STDMETHODCALLTYPE OutputDmlString(
        ULONG mask,
        PCSTR message) = 0;
};

#ifdef __cplusplus
};
#endif
