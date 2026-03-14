// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include "managedanalysis.h"

class ClrmaException : public ICLRMAClrException
{
public:
    ClrmaException(_In_ ClrmaManagedAnalysis* managedAnalysis, _In_ ULONG64 address);
    virtual ~ClrmaException();

public:
    // IUnknown
    STDMETHOD(QueryInterface)(_In_ REFIID InterfaceId, _Out_ PVOID* Interface);
    STDMETHOD_(ULONG, AddRef)();
    STDMETHOD_(ULONG, Release)();

    // ICLRMAClrException
    STDMETHOD(get_DebuggerCommand)(_Out_ BSTR* pValue);
    STDMETHOD(get_Address)(_Out_ ULONG64* pValue);
    STDMETHOD(get_HResult)(_Out_ HRESULT* pValue);
    STDMETHOD(get_Type)(_Out_ BSTR* pValue);
    STDMETHOD(get_Message)(_Out_ BSTR* pValue);

    STDMETHOD(get_FrameCount)(_Out_ UINT* pCount);
    STDMETHOD(Frame)(_In_ UINT nFrame, _Out_ ULONG64* pAddrIP, _Out_ ULONG64* pAddrSP, _Out_ BSTR* bstrModule, _Out_ BSTR* bstrFunction, _Out_ ULONG64* pDisplacement);

    STDMETHOD(get_InnerExceptionCount)(_Out_ USHORT* pCount);
    STDMETHOD(InnerException)(_In_ USHORT nIndex, _COM_Outptr_result_maybenull_ ICLRMAClrException** ppClrException);

private:
    HRESULT Initialize();
    HRESULT GetStackFrames();

    LONG m_lRefs;
    ClrmaManagedAnalysis* m_managedAnalysis;
    ULONG64 m_address;

    // ClrmaException::Initialize()
    DacpExceptionObjectData m_exceptionData;
    WCHAR* m_typeName;
    WCHAR* m_message;
    bool m_exceptionDataInitialized;

    // Initialized in ClrmaException::get_FrameCount
    std::vector<StackFrame> m_stackFrames;
    bool m_stackFramesInitialized;

    // Initialized in ClrmaException::get_InnerExceptionCount
    std::vector<CLRDATA_ADDRESS> m_innerExceptions;
    bool m_innerExceptionsInitialized;
};

// This struct needs to match the definition in the runtime and the target bitness.
// See: https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/clrex.h

struct StackTraceElement32
{
    ULONG32         ip;
    ULONG32         sp;
    ULONG32         pFunc;  // MethodDesc
    INT             flags;  // This is StackTraceElementFlags but it needs to always be "int" sized for backward compatibility.
};

struct StackTraceElement64
{
    ULONG64         ip;
    ULONG64         sp;
    ULONG64         pFunc;  // MethodDesc
    INT             flags;  // This is StackTraceElementFlags but it needs to always be "int" sized for backward compatibility.
};

// This is the layout of the _stackTrace pointer in an exception object. It is a managed array of bytes or if .NET 9.0 or greater
// an array of objects where the first entry is the address of stack trace element array. The layout is target bitness dependent.

#pragma warning(disable:4200)

struct StackTrace32
{
    ULONG32         m_size;             // ArrayHeader
    ULONG32         m_thread;           //
    StackTraceElement32 m_elements[0];
};

struct StackTrace64
{
    ULONG64         m_size;             // ArrayHeader
    ULONG64         m_thread;           //
    StackTraceElement64 m_elements[0];
};
