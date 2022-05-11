// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include "hostservices.h"
#include "dbgengservices.h"
#include "exts.h"

extern IMachine* GetTargetMachine(ULONG processorType);

DbgEngServices::DbgEngServices(IDebugClient* client) :
    m_ref(1),
    m_client(client),
    m_control(nullptr),
    m_data(nullptr),
    m_symbols(nullptr),
    m_system(nullptr),
    m_advanced(nullptr),
    m_targetMachine(nullptr)
{
    client->AddRef();
}

DbgEngServices::~DbgEngServices()
{
    if (m_control != nullptr)
    {
        m_control->Release();
        m_control = nullptr;
    }
    if (m_data != nullptr)
    {
        m_data->Release();
        m_data = nullptr;
    } 
    if (m_symbols != nullptr)
    {
        m_symbols->Release();
        m_symbols = nullptr;
    } 
    if (m_system != nullptr)
    {
        m_system->Release();
        m_system = nullptr;
    } 
    if (m_advanced != nullptr)
    {
        m_advanced->Release();
        m_advanced = nullptr;
    } 
    if (m_client != nullptr)
    {
        m_client->Release();
        m_client = nullptr;
    }
}

HRESULT 
DbgEngServices::Initialize()
{
    HRESULT hr;

    if (FAILED(hr = m_client->QueryInterface(__uuidof(IDebugControl2), (void **)&m_control)))
    {
        return hr;
    }
    if (FAILED(hr = m_client->QueryInterface(__uuidof(IDebugDataSpaces), (void **)&m_data)))
    {
        return hr;
    }
    if (FAILED(hr = m_client->QueryInterface(__uuidof(IDebugSymbols2), (void **)&m_symbols)))
    {
        return hr;
    }
    if (FAILED(hr = m_client->QueryInterface(__uuidof(IDebugSystemObjects), (void **)&m_system)))
    {
        return hr;
    }
    if (FAILED(hr = m_client->QueryInterface(__uuidof(IDebugAdvanced), (void **)&m_advanced)))
    {
        return hr;
    }
    ReleaseHolder<IDebugEventCallbacks> pCallbacks = nullptr;
    hr = QueryInterface(__uuidof(IDebugEventCallbacks), (void**)&pCallbacks);
    _ASSERTE(SUCCEEDED(hr));

    if (FAILED(hr = m_client->SetEventCallbacks(pCallbacks)))
    {
        return hr;
    }
    return S_OK;
}

void
DbgEngServices::Uninitialize()
{
    if (m_client != nullptr)
    {
        m_client->SetEventCallbacks(nullptr);
    }
}

//----------------------------------------------------------------------------
// IUnknown
//----------------------------------------------------------------------------

HRESULT
DbgEngServices::QueryInterface(
    REFIID InterfaceId,
    PVOID* Interface
    )
{
    if (InterfaceId == __uuidof(IUnknown) ||
        InterfaceId == __uuidof(IDebuggerServices))
    {
        *Interface = static_cast<IDebuggerServices*>(this);
        AddRef();
        return S_OK;
    }
    else if (InterfaceId == __uuidof(IRemoteMemoryService))
    {
        *Interface = static_cast<IRemoteMemoryService*>(this);
        AddRef();
        return S_OK;
    }
    if (InterfaceId == __uuidof(IDebugEventCallbacks))
    {
        *Interface = static_cast<IDebugEventCallbacks*>(this);
        AddRef();
        return S_OK;
    }
    else
    {
        *Interface = nullptr;
        return E_NOINTERFACE;
    }
}

ULONG
DbgEngServices::AddRef()
{
    LONG ref = InterlockedIncrement(&m_ref);    
    return ref;
}

ULONG
DbgEngServices::Release()
{
    LONG ref = InterlockedDecrement(&m_ref);
    if (ref == 0)
    {
        delete this;
    }
    return ref;
}

//----------------------------------------------------------------------------
// IDebuggerServices
//----------------------------------------------------------------------------

HRESULT 
DbgEngServices::GetOperatingSystem(
    OperatingSystem* operatingSystem)
{
    *operatingSystem = OperatingSystem::Windows;

    ULONG platformId, major, minor, servicePack;
    if (SUCCEEDED(m_control->GetSystemVersion(&platformId, &major, &minor, nullptr, 0, nullptr, &servicePack, nullptr, 0, nullptr)))
    {
        if (platformId == VER_PLATFORM_UNIX)
        {
            *operatingSystem = OperatingSystem::Linux;
        }
    }
    return S_OK;
}

HRESULT 
DbgEngServices::GetDebuggeeType(
    PULONG debugClass,
    PULONG qualifier)
{
    return m_control->GetDebuggeeType(debugClass, qualifier);
}

HRESULT
DbgEngServices::GetExecutingProcessorType(
    PULONG type)
{
    return m_control->GetExecutingProcessorType(type);
}

HRESULT
DbgEngServices::AddCommand(
    PCSTR command,
    PCSTR help,
    PCSTR aliases[],
    int numberOfAliases)
{
    return S_OK;
}

void
DbgEngServices::OutputString(
    ULONG mask,
    PCSTR message)
{
    m_control->Output(mask, "%s", message);
}

HRESULT
DbgEngServices::ReadVirtual(
    ULONG64 offset,
    PVOID buffer,
    ULONG bufferSize,
    PULONG bytesRead)
{
    return m_data->ReadVirtual(offset, buffer, bufferSize, bytesRead);
}

HRESULT 
DbgEngServices::WriteVirtual(
    ULONG64 offset,
    PVOID buffer,
    ULONG bufferSize,
    PULONG bytesWritten)
{
    return m_data->WriteVirtual(offset, buffer, bufferSize, bytesWritten);
}

HRESULT 
DbgEngServices::GetNumberModules(
    PULONG loaded,
    PULONG unloaded)
{
    return m_symbols->GetNumberModules(loaded, unloaded);
}

HRESULT 
DbgEngServices::GetModuleNames(
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
    PULONG loadedImageNameSize)
{
    return m_symbols->GetModuleNames(index, base, imageNameBuffer, imageNameBufferSize, imageNameSize, moduleNameBuffer,
        moduleNameBufferSize, moduleNameSize, loadedImageNameBuffer, loadedImageNameBufferSize, loadedImageNameSize);
}

HRESULT 
DbgEngServices::GetModuleInfo(
    ULONG index,
    PULONG64 moduleBase,
    PULONG64 moduleSize,
    PULONG timestamp,
    PULONG checksum)
{
    HRESULT hr = m_symbols->GetModuleByIndex(index, moduleBase);
    if (FAILED(hr)) {
        return hr;
    }
    DEBUG_MODULE_PARAMETERS params;
    hr = m_symbols->GetModuleParameters(1, moduleBase, 0, &params);
    if (FAILED(hr)) {
        return hr;
    }
    if (moduleSize) {
        *moduleSize = params.Size;
    }
    if (timestamp) {
        *timestamp = params.TimeDateStamp;
    }
    if (checksum) {
        *checksum = params.Checksum;
    }
    return S_OK;
}

HRESULT 
DbgEngServices::GetModuleVersionInformation(
    ULONG index,
    ULONG64 base,
    PCSTR item,
    PVOID buffer,
    ULONG bufferSize,
    PULONG versionInfoSize)
{
    return m_symbols->GetModuleVersionInformation(index, base, item, buffer, bufferSize, versionInfoSize);
}

HRESULT 
DbgEngServices::GetNumberThreads(
    PULONG number)
{
    return m_system->GetNumberThreads(number);
}

HRESULT 
DbgEngServices::GetThreadIdsByIndex(
    ULONG start,
    ULONG count,
    PULONG ids,
    PULONG sysIds)
{
    return m_system->GetThreadIdsByIndex(start, count, ids, sysIds);
}

HRESULT 
DbgEngServices::GetThreadContextBySystemId(
    ULONG32 sysId,
    ULONG32 contextFlags,
    ULONG32 contextSize,
    PBYTE context)
{
    ULONG originalThreadId;
    HRESULT hr = SetCurrentThreadIdFromSystemId(sysId, &originalThreadId);
    if (SUCCEEDED(hr))
    {
        // Prepare context structure
        ZeroMemory(context, contextSize);
        GetMachine()->SetContextFlags(context, contextFlags);

        // Ok, do it!
        hr = m_advanced->GetThreadContext((LPVOID)context, contextSize);

        // This is cleanup; failure here doesn't mean GetThreadContext should fail (that's determined by hr).
        m_system->SetCurrentThreadId(originalThreadId);

        // GetThreadContext clears ContextFlags or sets them incorrectly and DBI needs it set to know what registers to copy
        GetMachine()->SetContextFlags(context, contextFlags);
    }
    return hr;
}

HRESULT
DbgEngServices::GetCurrentProcessSystemId(
    PULONG sysId)
{
    return m_system->GetCurrentProcessSystemId(sysId);
}

HRESULT
DbgEngServices::GetCurrentThreadSystemId(
    PULONG sysId)
{
    return m_system->GetCurrentThreadSystemId(sysId);
}

HRESULT
DbgEngServices::SetCurrentThreadSystemId(
    ULONG sysId)
{
    ULONG id = 0;
    HRESULT hr = m_system->GetThreadIdBySystemId(sysId, &id);
    if (FAILED(hr))
    {
        return hr;
    }
    return m_system->SetCurrentThreadId(id);
}

HRESULT 
DbgEngServices::GetThreadTeb(
    ULONG sysId,
    PULONG64 pteb)
{
    ULONG originalThreadId;
    HRESULT hr = SetCurrentThreadIdFromSystemId(sysId, &originalThreadId);
    if (SUCCEEDED(hr))
    {
        hr = m_system->GetCurrentThreadTeb(pteb);

        // This is cleanup; failure here doesn't mean GetThreadContext should fail (that's determined by hr).
        m_system->SetCurrentThreadId(originalThreadId);
    }
    return hr;
}

HRESULT 
DbgEngServices::VirtualUnwind(
    DWORD threadId,
    ULONG32 contextSize,
    PBYTE context)
{
    return E_NOTIMPL;
}

HRESULT 
DbgEngServices::GetSymbolPath(
    PSTR buffer,
    ULONG bufferSize,
    PULONG pathSize)
{
    return m_symbols->GetSymbolPath(buffer, bufferSize, pathSize);
}
 
HRESULT 
DbgEngServices::GetSymbolByOffset(
    ULONG moduleIndex,
    ULONG64 offset,
    PSTR nameBuffer,
    ULONG nameBufferSize,
    PULONG nameSize,
    PULONG64 displacement)
{
    return m_symbols->GetNameByOffset(offset, nameBuffer, nameBufferSize, nameSize, displacement);
}

HRESULT 
DbgEngServices::GetOffsetBySymbol(
    ULONG moduleIndex,
    PCSTR name,
    PULONG64 offset)
{
    ULONG cch = 0;
    HRESULT hr = m_symbols->GetModuleNameString(DEBUG_MODNAME_MODULE, moduleIndex, 0, nullptr, 0, &cch);
    if (FAILED(hr)) {
        return hr;
    }
    ArrayHolder<char> moduleName = new char[cch];
    hr = m_symbols->GetModuleNameString(DEBUG_MODNAME_MODULE, moduleIndex, 0, moduleName, cch, nullptr);
    if (FAILED(hr)) {
        return hr;
    }
    std::string symbolName;
    symbolName.append(moduleName);
    symbolName.append("!");
    symbolName.append(name);
    return m_symbols->GetOffsetByName(symbolName.c_str(), offset);
}

ULONG
DbgEngServices::GetOutputWidth()
{
    // m_client->GetOutputWidth() always returns 80 as the width under windbg, windbgx and cdb so just return the max.
    return INT_MAX;
}

HRESULT
DbgEngServices::SupportsDml(PULONG supported)
{
    ULONG opts = 0;
    HRESULT hr = m_control->GetEngineOptions(&opts);
    *supported = (SUCCEEDED(hr) && (opts & DEBUG_ENGOPT_PREFER_DML) == DEBUG_ENGOPT_PREFER_DML) ? 1 : 0;
    return hr;
}

void
DbgEngServices::OutputDmlString(
    ULONG mask,
    PCSTR message)
{
    m_control->ControlledOutput(DEBUG_OUTCTL_AMBIENT_DML, mask, "%s", message);
}

HRESULT 
DbgEngServices::AddModuleSymbol(
    void* param,
    const char* symbolFileName)
{
    return S_OK;
}

//----------------------------------------------------------------------------
// IRemoteMemoryService
//----------------------------------------------------------------------------

HRESULT DbgEngServices::AllocVirtual(
    ULONG64 address,
    ULONG32 size,
    ULONG32 typeFlags,
    ULONG32 protectFlags,
    ULONG64* remoteAddress)
{
    ULONG64 hProcess;
    HRESULT hr = m_system->GetCurrentProcessHandle(&hProcess);
    if (FAILED(hr)) {
        return hr;
    }
    LPVOID allocation = ::VirtualAllocEx((HANDLE)hProcess, (LPVOID)address, size, typeFlags, protectFlags);
    if (allocation == NULL) {
        return HRESULT_FROM_WIN32(::GetLastError());
    }
    *remoteAddress = (CLRDATA_ADDRESS)allocation;
    return S_OK;
}

HRESULT DbgEngServices::FreeVirtual(
    ULONG64 address,
    ULONG32 size,
    ULONG32 typeFlags)
{
    ULONG64 hProcess;
    HRESULT hr = m_system->GetCurrentProcessHandle(&hProcess);
    if (FAILED(hr)) {
        return hr;
    }
    ::VirtualFreeEx((HANDLE)hProcess, (LPVOID)address, size, typeFlags);
    return S_OK;
}

//----------------------------------------------------------------------------
// IDebugEventCallbacks
//----------------------------------------------------------------------------

HRESULT DbgEngServices::Breakpoint(
    PDEBUG_BREAKPOINT bp)
{
    return DEBUG_STATUS_NO_CHANGE;
}

HRESULT DbgEngServices::ChangeDebuggeeState(
    ULONG Flags,
    ULONG64 Argument)
{
    return DEBUG_STATUS_NO_CHANGE;
}

HRESULT DbgEngServices::ChangeEngineState(
    ULONG Flags,
    ULONG64 Argument)
{
    if (Flags == DEBUG_CES_EXECUTION_STATUS)
    {
        if (((Argument & DEBUG_STATUS_MASK) == DEBUG_STATUS_BREAK) && ((Argument & DEBUG_STATUS_INSIDE_WAIT) == 0))
        {
            // Flush the target when the debugger target breaks
            Extensions::GetInstance()->FlushTarget();
        }
    }
    return DEBUG_STATUS_NO_CHANGE;
}

HRESULT DbgEngServices::ChangeSymbolState(
    ULONG Flags,
    ULONG64 Argument)
{
    if (Flags == DEBUG_CSS_PATHS)
    {
        InitializeSymbolStoreFromSymPath();
    }
    return DEBUG_STATUS_NO_CHANGE;
}

HRESULT DbgEngServices::CreateProcess(
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
    ULONG64 StartOffset)
{
    Extensions::GetInstance()->CreateTarget();
    return DEBUG_STATUS_NO_CHANGE;
}

HRESULT DbgEngServices::CreateThread(
    ULONG64 Handle,
    ULONG64 DataOffset,
    ULONG64 StartOffset)
{
    return DEBUG_STATUS_NO_CHANGE;
}

HRESULT DbgEngServices::Exception(
    PEXCEPTION_RECORD64 Exception, 
    ULONG FirstChance)
{
    return DEBUG_STATUS_NO_CHANGE;
}

HRESULT DbgEngServices::ExitProcess(
    ULONG ExitCode)
{
    m_targetMachine = nullptr;
    Extensions::GetInstance()->DestroyTarget();
    return DEBUG_STATUS_NO_CHANGE;
}

HRESULT DbgEngServices::ExitThread(
    ULONG ExitCode)
{
    return DEBUG_STATUS_NO_CHANGE;
}

HRESULT DbgEngServices::GetInterestMask(
    PULONG Mask)
{
    *Mask = DEBUG_EVENT_CREATE_PROCESS | DEBUG_EVENT_EXIT_PROCESS | DEBUG_EVENT_LOAD_MODULE | DEBUG_EVENT_CHANGE_ENGINE_STATE | DEBUG_EVENT_CHANGE_SYMBOL_STATE;
    return S_OK;
}

extern HRESULT LoadModuleEvent(IDebugClient* client, PCSTR moduleName);

HRESULT DbgEngServices::LoadModule(
    ULONG64 ImageFileHandle,
    ULONG64 BaseOffset,
    ULONG ModuleSize,
    PCSTR ModuleName,
    PCSTR ImageName,
    ULONG CheckSum,
    ULONG TimeDateStamp)
{ 
    return LoadModuleEvent(m_client, ModuleName);
}

HRESULT DbgEngServices::SessionStatus(
    ULONG Status)
{
    return DEBUG_STATUS_NO_CHANGE;
}

HRESULT DbgEngServices::SystemError(
    ULONG Error,
    ULONG Level)
{
    return DEBUG_STATUS_NO_CHANGE;
}

HRESULT DbgEngServices::UnloadModule(
    PCSTR ImageBaseName,
    ULONG64 BaseOffset)
{
    return DEBUG_STATUS_NO_CHANGE;
}

//----------------------------------------------------------------------------
// Helper Functions
//----------------------------------------------------------------------------

IMachine*
DbgEngServices::GetMachine()
{
    if (m_targetMachine == nullptr)
    {
        ULONG processorType = 0;
        m_control->GetExecutingProcessorType(&processorType);
        m_targetMachine = ::GetTargetMachine(processorType);
    }
    return m_targetMachine;
}

HRESULT
DbgEngServices::SetCurrentThreadIdFromSystemId(
    ULONG32 sysId,
    PULONG originalThreadId)
{
    ULONG requestedThreadId;
    HRESULT hr;

    hr = m_system->GetCurrentThreadId(originalThreadId);
    if (FAILED(hr))
    {
        return hr;
    }
    hr = m_system->GetThreadIdBySystemId(sysId, &requestedThreadId);
    if (FAILED(hr))
    {
        return hr;
    }
    return m_system->SetCurrentThreadId(requestedThreadId);
}

void 
DbgEngServices::InitializeSymbolStoreFromSymPath()
{
    ISymbolService* symbolService = GetSymbolService();
    if (symbolService != nullptr)
    {
        ULONG cchLength = 0;
        if (SUCCEEDED(m_symbols->GetSymbolPath(nullptr, 0, &cchLength)))
        {
            ArrayHolder<char> symbolPath = new char[cchLength];
            if (SUCCEEDED(m_symbols->GetSymbolPath(symbolPath, cchLength, nullptr)))
            {
                if (strlen(symbolPath) > 0)
                {
                    if (!symbolService->ParseSymbolPath(symbolPath))
                    {
                        m_control->Output(DEBUG_OUTPUT_ERROR, "Windows symbol path parsing FAILED %s\n", symbolPath.GetPtr());
                    }
                }
            }
        }
    }
}
