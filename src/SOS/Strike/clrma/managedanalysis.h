// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include <windows.h>
#include <unknwn.h>
#include <dbgeng.h>
#include <clrma.h> // IDL
#include <clrmaservice.h>
#include <dbgtargetcontext.h>
#include <corhdr.h>
#include <cordebug.h>
#include <xclrdata.h>
#include <sospriv.h>
#include <releaseholder.h>
#include <arrayholder.h>
#include <dacprivate.h>
#include <extensions.h>
#include <target.h>
#include <runtime.h>
#include <vector>

#ifndef IMAGE_FILE_MACHINE_RISCV64
#define IMAGE_FILE_MACHINE_RISCV64          0x5064  // RISCV64
#endif

#ifndef IMAGE_FILE_MACHINE_LOONGARCH64
#define IMAGE_FILE_MACHINE_LOONGARCH64      0x6264  // LOONGARCH64
#endif

enum ClrmaGlobalFlags
{
    LoggingEnabled = 0x01,                  // CLRMA logging enabled
    DacClrmaEnabled = 0x02,                 // Direct DAC CLRMA code enabled
    ManagedClrmaEnabled = 0x04,             // Native AOT managed support enabled
};

#define MAX_STACK_FRAMES    1000            // Max number of stack frames returned from thread stackwalk

typedef struct StackFrame
{
    ULONG Frame = 0;
    ULONG64 SP = 0;
    ULONG64 IP = 0;
    ULONG64 Displacement = 0;
    std::wstring Module;
    std::wstring Function;
} StackFrame;

extern int g_clrmaGlobalFlags;

extern void TraceInformation(PCSTR format, ...);
extern void TraceError(PCSTR format, ...);

class ClrmaManagedAnalysis : public ICLRManagedAnalysis
{
public:
    ClrmaManagedAnalysis();
    virtual ~ClrmaManagedAnalysis();

public:
    // IUnknown
    STDMETHOD(QueryInterface)(_In_ REFIID InterfaceId, _Out_ PVOID* Interface);
    STDMETHOD_(ULONG, AddRef)();
    STDMETHOD_(ULONG, Release)();

    // ICLRManagedAnalysis
    STDMETHOD(AssociateClient)(_In_ IUnknown* pUnknown);

    STDMETHOD(get_ProviderName)(_Out_ BSTR* bstrProvider);

    STDMETHOD(GetThread)(_In_ ULONG osThreadId, _COM_Outptr_ ICLRMAClrThread** ppClrThread);

    STDMETHOD(GetException)(_In_ ULONG64 address, _COM_Outptr_ ICLRMAClrException** ppClrException);

    STDMETHOD(get_ObjectInspection)(_COM_Outptr_ ICLRMAObjectInspection** ppObjectInspection);

    // Helper functions
    inline IXCLRDataProcess* ClrData() { return m_clrData; }
    inline ISOSDacInterface* SosDacInterface() { return m_sosDac; }
    inline int PointerSize() { return m_pointerSize; }
    inline ULONG ProcessorType() { return m_processorType; }
    inline CLRDATA_ADDRESS ObjectMethodTable() { return m_usefulGlobals.ObjectMethodTable; }

    /// <summary>
    /// Fills in the frame.Module and frame.Function from the MethodDesc.
    /// </summary>
    HRESULT GetMethodDescInfo(CLRDATA_ADDRESS methodDesc, StackFrame& frame, bool stripFunctionParameters);

    /// <summary>
    /// Returns base Exception MT address if exception derived MT
    /// </summary>
    CLRDATA_ADDRESS IsExceptionObj(CLRDATA_ADDRESS mtObj);

    /// <summary>
    /// Return the string object contents
    /// </summary>
    WCHAR* GetStringObject(CLRDATA_ADDRESS stringObject);

    /// <summary>
    /// Reads a target size pointer.
    /// </summary>
    HRESULT ReadPointer(CLRDATA_ADDRESS address, CLRDATA_ADDRESS* pointer);

    /// <summary>
    /// Read memory
    /// </summary>
    inline HRESULT ReadMemory(CLRDATA_ADDRESS address, PVOID buffer, ULONG cb) { return m_debugData->ReadVirtual(address, buffer, cb, nullptr); }

private:
    HRESULT QueryDebugClient(IUnknown* pUnknown);
    void ReleaseDebugClient();

    LONG m_lRefs;
    int m_pointerSize;
    WCHAR m_fileSeparator;
    ULONG m_processorType;
    IDebugClient* m_debugClient;
    IDebugDataSpaces*  m_debugData;
    IDebugSystemObjects*  m_debugSystem;
    IDebugControl*  m_debugControl;
    IDebugSymbols3*  m_debugSymbols;

    // CLRMA service from managed code
    ICLRMAService* m_clrmaService;

    // DAC interface instances
    IXCLRDataProcess* m_clrData;
    ISOSDacInterface* m_sosDac;

    DacpUsefulGlobalsData m_usefulGlobals;
};

#include "thread.h"
#include "exception.h"

