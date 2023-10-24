// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include <unknwn.h>
#include <rpc.h>
#include <dbgeng.h>
#include "debuggerservices.h"
#include "remotememoryservice.h"
#include "extensions.h"

#define VER_PLATFORM_UNIX 10 

class IMachine;

#ifdef __cplusplus
extern "C" {
#endif

class DbgEngServices : public IDebuggerServices, public IRemoteMemoryService, public IDebugEventCallbacks
{
private:
    LONG m_ref;
    PDEBUG_CLIENT         m_client;
    PDEBUG_CONTROL2       m_control;
    PDEBUG_DATA_SPACES    m_data;
    PDEBUG_SYMBOLS2       m_symbols;
    PDEBUG_SYSTEM_OBJECTS m_system;
    PDEBUG_ADVANCED       m_advanced;
    IMachine*             m_targetMachine;
    bool                  m_flushNeeded;

public:
    DbgEngServices(IDebugClient* client);
    virtual ~DbgEngServices();
    HRESULT Initialize();
    void Uninitialize();

    //----------------------------------------------------------------------------
    // Helper functions
    //----------------------------------------------------------------------------

    void FlushCheck(Extensions* extensions);

    IMachine* GetMachine();

    HRESULT SetCurrentThreadIdFromSystemId(
        ULONG32 sysId,
        PULONG originalThreadId);

    void InitializeSymbolStoreFromSymPath();

    //----------------------------------------------------------------------------
    // IUnknown
    //----------------------------------------------------------------------------

    HRESULT STDMETHODCALLTYPE QueryInterface(
        REFIID InterfaceId,
        PVOID* Interface);

    ULONG STDMETHODCALLTYPE AddRef();

    ULONG STDMETHODCALLTYPE Release();

    //----------------------------------------------------------------------------
    // IDebuggerServices
    //----------------------------------------------------------------------------

    HRESULT STDMETHODCALLTYPE GetOperatingSystem(
        OperatingSystem* operatingSystem);

    HRESULT STDMETHODCALLTYPE GetDebuggeeType(
        PULONG debugClass,
        PULONG qualifier);

    HRESULT STDMETHODCALLTYPE GetExecutingProcessorType(
        PULONG type);

    HRESULT STDMETHODCALLTYPE AddCommand(
        PCSTR command,
        PCSTR help,
        PCSTR aliases[],
        int numberOfAliases);

    void STDMETHODCALLTYPE OutputString(
        ULONG mask,
        PCSTR message);

    HRESULT STDMETHODCALLTYPE ReadVirtual(
        ULONG64 offset,
        PVOID buffer,
        ULONG bufferSize,
        PULONG bytesRead);

    HRESULT STDMETHODCALLTYPE WriteVirtual(
        ULONG64 offset,
        PVOID buffer,
        ULONG bufferSize,
        PULONG bytesWritten);

    HRESULT STDMETHODCALLTYPE GetNumberModules(
        PULONG loaded,
        PULONG unloaded);

    HRESULT STDMETHODCALLTYPE GetModuleNames(
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
        PULONG loadedImageNameSize);

    HRESULT STDMETHODCALLTYPE GetModuleInfo(
        ULONG index,
        PULONG64 moduleBase,
        PULONG64 moduleSize,
        PULONG timestamp,
        PULONG checksum);

    HRESULT STDMETHODCALLTYPE GetModuleVersionInformation(
        ULONG index,
        ULONG64 base,
        PCSTR item,
        PVOID buffer,
        ULONG bufferSize,
        PULONG versionInfoSize);
    
    HRESULT STDMETHODCALLTYPE GetNumberThreads(
        PULONG number);

    HRESULT STDMETHODCALLTYPE GetThreadIdsByIndex(
        ULONG start,
        ULONG count,
        PULONG ids,
        PULONG sysIds);

    HRESULT STDMETHODCALLTYPE GetThreadContextBySystemId(
        ULONG32 sysId,
        ULONG32 contextFlags,
        ULONG32 contextSize,
        PBYTE context);

    HRESULT STDMETHODCALLTYPE GetCurrentProcessSystemId(
        PULONG sysId);

    HRESULT STDMETHODCALLTYPE GetCurrentThreadSystemId(
        PULONG sysId);

    HRESULT STDMETHODCALLTYPE SetCurrentThreadSystemId(
        ULONG sysId);

    HRESULT STDMETHODCALLTYPE GetThreadTeb(
        ULONG sysId,
        PULONG64 pteb);

    HRESULT STDMETHODCALLTYPE VirtualUnwind(
        DWORD threadId,
        ULONG32 contextSize,
        PBYTE context);

    HRESULT STDMETHODCALLTYPE GetSymbolPath(
        PSTR buffer,
        ULONG bufferSize,
        PULONG pathSize);
 
    HRESULT STDMETHODCALLTYPE GetSymbolByOffset(
        ULONG moduleIndex,
        ULONG64 offset,
        PSTR nameBuffer,
        ULONG nameBufferSize,
        PULONG nameSize,
        PULONG64 displacement);

    HRESULT STDMETHODCALLTYPE GetOffsetBySymbol(
        ULONG moduleIndex,
        PCSTR name,
        PULONG64 offset);
        
    HRESULT STDMETHODCALLTYPE GetTypeId(
        ULONG moduleIndex,
        PCSTR typeName,
        PULONG64 typeId); 

    HRESULT STDMETHODCALLTYPE GetFieldOffset(
        ULONG moduleIndex,
        PCSTR typeName,
        ULONG64 typeId,
        PCSTR fieldName,
        PULONG offset);

    ULONG STDMETHODCALLTYPE GetOutputWidth();

    HRESULT STDMETHODCALLTYPE SupportsDml(
        PULONG supported);

    void STDMETHODCALLTYPE OutputDmlString(
        ULONG mask,
        PCSTR message);

    HRESULT STDMETHODCALLTYPE AddModuleSymbol(
        void* param,
        const char* symbolFileName);

    HRESULT STDMETHODCALLTYPE GetLastEventInformation(
        PULONG type,
        PULONG processId,
        PULONG threadId,
        PVOID extraInformation,
        ULONG extraInformationSize,
        PULONG extraInformationUsed,
        PSTR description,
        ULONG descriptionSize,
        PULONG descriptionUsed);

    //----------------------------------------------------------------------------
    // IRemoteMemoryService
    //----------------------------------------------------------------------------

    HRESULT STDMETHODCALLTYPE AllocVirtual(
        ULONG64 address,
        ULONG32 size,
        ULONG32 typeFlags,
        ULONG32 protectFlags,
        ULONG64* remoteAddress);

    HRESULT STDMETHODCALLTYPE FreeVirtual(
        ULONG64 address,
        ULONG32 size,
        ULONG32 typeFlags);

    //----------------------------------------------------------------------------
    // IDebugEventCallbacks
    //----------------------------------------------------------------------------

    HRESULT STDMETHODCALLTYPE Breakpoint(
        PDEBUG_BREAKPOINT bp);

    HRESULT STDMETHODCALLTYPE ChangeDebuggeeState(
        ULONG Flags,
        ULONG64 Argument);

    HRESULT STDMETHODCALLTYPE ChangeEngineState(
        ULONG Flags,
        ULONG64 Argument);

    HRESULT STDMETHODCALLTYPE ChangeSymbolState(
        ULONG Flags,
        ULONG64 Argument);

    HRESULT STDMETHODCALLTYPE CreateProcess(
        ULONG64 ImageFileHandle,
        ULONG64 Handle,
        ULONG64 BaseOffset,
        ULONG ModuleSize,
        PCSTR ModuleName,
        PCSTR ImageName,
        ULONG CheckSum,
        ULONG TimeDateStamp,
        ULONG64 InitialThreadHandle,
        ULONG64 ThreadDataOffset,
        ULONG64 StartOffset);

    HRESULT STDMETHODCALLTYPE CreateThread(
        ULONG64 Handle,
        ULONG64 DataOffset,
        ULONG64 StartOffset);

    HRESULT STDMETHODCALLTYPE Exception(
        PEXCEPTION_RECORD64 Exception,
        ULONG FirstChance);

    HRESULT STDMETHODCALLTYPE ExitProcess(
        ULONG ExitCode);

    HRESULT STDMETHODCALLTYPE ExitThread(
        ULONG ExitCode);

    HRESULT STDMETHODCALLTYPE GetInterestMask(
        PULONG Mask);

    HRESULT STDMETHODCALLTYPE LoadModule(
        ULONG64 ImageFileHandle,
        ULONG64 BaseOffset,
        ULONG ModuleSize,
        PCSTR ModuleName,
        PCSTR ImageName,
        ULONG CheckSum,
        ULONG TimeDateStamp);

    HRESULT STDMETHODCALLTYPE SessionStatus(
        ULONG Status);

    HRESULT STDMETHODCALLTYPE SystemError(
        ULONG Error,
        ULONG Level);

    HRESULT STDMETHODCALLTYPE UnloadModule(
        PCSTR ImageBaseName,
        ULONG64 BaseOffset);
};

#ifdef __cplusplus
};
#endif
