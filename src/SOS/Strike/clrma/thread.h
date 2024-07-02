// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include "managedanalysis.h"

class ClrmaThread : public ICLRMAClrThread
{
public:
    ClrmaThread(_In_ ClrmaManagedAnalysis* managedAnalysis, _In_ ULONG osThreadId);
    virtual ~ClrmaThread();

public:
    // IUnknown
    STDMETHOD(QueryInterface)(_In_ REFIID InterfaceId, _Out_ PVOID* Interface);
    STDMETHOD_(ULONG, AddRef)();
    STDMETHOD_(ULONG, Release)();

    // ICLRMAClrThread
    STDMETHOD(get_DebuggerCommand)(_Out_ BSTR* pValue);
    STDMETHOD(get_OSThreadId)(_Out_ ULONG* pValue);

    STDMETHOD(get_FrameCount)(_Out_ UINT* pCount);
    STDMETHOD(Frame)(_In_ UINT nFrame, _Out_ ULONG64* pAddrIP, _Out_ ULONG64* pAddrSP, _Out_ BSTR* bstrModule, _Out_ BSTR* bstrFunction, _Out_ ULONG64* pDisplacement);

    STDMETHOD(get_CurrentException)(_COM_Outptr_result_maybenull_ ICLRMAClrException** ppClrException);

    STDMETHOD(get_NestedExceptionCount)(_Out_ USHORT* pCount);
    STDMETHOD(NestedException)(_In_ USHORT nIndex, _COM_Outptr_ ICLRMAClrException** ppClrException);

    HRESULT Initialize();

private:
    LONG m_lRefs;
    ClrmaManagedAnalysis* m_managedAnalysis;
    ULONG m_osThreadId;

    // ClrmaThread::Initialize()
    CLRDATA_ADDRESS m_lastThrownObject;
    CLRDATA_ADDRESS m_firstNestedException;

    // Initialized in ClrmaThread::get_FrameCount
    std::vector<StackFrame> m_stackFrames;
    bool m_stackFramesInitialized;

    // Initialized in ClrmaThread::get_NestedExceptionCount
    std::vector<CLRDATA_ADDRESS> m_nestedExceptions;
    bool m_nestedExceptionsInitialized;

    HRESULT GetFrameLocation(IXCLRDataStackWalk* pStackWalk, CLRDATA_ADDRESS* ip, CLRDATA_ADDRESS* sp);
};
