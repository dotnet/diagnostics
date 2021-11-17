// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//

#include "sos.h"
#include "datatarget.h"
#include "corhdr.h"
#include "cor.h"
#include "dacprivate.h"
#include "sospriv.h"
#include "corerror.h"

#define IMAGE_FILE_MACHINE_AMD64             0x8664  // AMD64 (K8)

DataTarget::DataTarget(ULONG64 baseAddress) :
    m_ref(0),
    m_baseAddress(baseAddress)
{
}

STDMETHODIMP
DataTarget::QueryInterface(
    THIS_
    ___in REFIID InterfaceId,
    ___out PVOID* Interface
    )
{
    if (InterfaceId == IID_IUnknown ||
        InterfaceId == IID_ICLRDataTarget)
    {
        *Interface = (ICLRDataTarget*)this;
        AddRef();
        return S_OK;
    }
    else if (InterfaceId == IID_ICLRDataTarget2)
    {
        *Interface = (ICLRDataTarget2*)this;
        AddRef();
        return S_OK;
    }
    else if (InterfaceId == IID_ICorDebugDataTarget4)
    {
        *Interface = (ICorDebugDataTarget4*)this;
        AddRef();
        return S_OK;
    }
    else if (InterfaceId == IID_ICLRMetadataLocator)
    {
        *Interface = (ICLRMetadataLocator*)this;
        AddRef();
        return S_OK;
    }
    else if (InterfaceId == IID_ICLRRuntimeLocator)
    {
        *Interface = (ICLRRuntimeLocator*)this;
        AddRef();
        return S_OK;
    }
    else
    {
        *Interface = NULL;
        return E_NOINTERFACE;
    }
}

STDMETHODIMP_(ULONG)
DataTarget::AddRef(
    THIS
    )
{
    LONG ref = InterlockedIncrement(&m_ref);    
    return ref;
}

STDMETHODIMP_(ULONG)
DataTarget::Release(
    THIS
    )
{
    LONG ref = InterlockedDecrement(&m_ref);
    if (ref == 0)
    {
        delete this;
    }
    return ref;
}

HRESULT STDMETHODCALLTYPE
DataTarget::GetMachineType(
    /* [out] */ ULONG32 *machine)
{
    if (g_ExtControl == NULL)
    {
        return E_UNEXPECTED;
    }
    return g_ExtControl->GetExecutingProcessorType((PULONG)machine);
}

HRESULT STDMETHODCALLTYPE
DataTarget::GetPointerSize(
    /* [out] */ ULONG32 *size)
{
#if defined(SOS_TARGET_AMD64) || defined(SOS_TARGET_ARM64) || defined(SOS_TARGET_MIPS64)
    *size = 8;
#elif defined(SOS_TARGET_ARM) || defined(SOS_TARGET_X86)
    *size = 4;
#else
  #error Unsupported architecture
#endif

    return S_OK;
}

HRESULT STDMETHODCALLTYPE
DataTarget::GetImageBase(
    /* [string][in] */ LPCWSTR name,
    /* [out] */ CLRDATA_ADDRESS *base)
{
    if (g_ExtSymbols == NULL)
    {
        return E_UNEXPECTED;
    }
    CHAR lpstr[MAX_LONGPATH];
    int name_length = WideCharToMultiByte(CP_ACP, 0, name, -1, lpstr, MAX_LONGPATH, NULL, NULL);
    if (name_length == 0)
    {
        return E_FAIL;
    }
#ifndef FEATURE_PAL
    // Remove the extension on Windows/dbgeng.
    CHAR *lp = strrchr(lpstr, '.');
    if (lp != nullptr)
    {
        *lp = '\0';
    }
#endif
    return g_ExtSymbols->GetModuleByModuleName(lpstr, 0, NULL, base);
}

HRESULT STDMETHODCALLTYPE
DataTarget::ReadVirtual(
    /* [in] */ CLRDATA_ADDRESS address,
    /* [length_is][size_is][out] */ PBYTE buffer,
    /* [in] */ ULONG32 request,
    /* [optional][out] */ ULONG32 *done)
{
    if (g_ExtData == NULL)
    {
        return E_UNEXPECTED;
    }
    address = CONVERT_FROM_SIGN_EXTENDED(address);
#ifdef FEATURE_PAL
    if (g_sos != nullptr)
    {
        // LLDB synthesizes memory (returns 0's) for missing pages (in this case the missing metadata  pages) 
        // in core dumps. This functions creates a list of the metadata regions and caches the metadata if 
        // available from the local or downloaded assembly. If the read would be in the metadata of a loaded 
        // assembly, the metadata from the this cache will be returned.
        HRESULT hr = GetMetadataMemory(address, request, buffer);
        if (SUCCEEDED(hr)) {
            if (done != nullptr) {
                *done = request;
            }
            return hr;
        }
    }
#endif
    HRESULT hr = g_ExtData->ReadVirtual(address, (PVOID)buffer, request, (PULONG)done);
    if (FAILED(hr)) 
    {
        ExtDbgOut("DataTarget::ReadVirtual FAILED %08x address %08llx size %08x\n", hr, address, request);
    }
    return hr;
}

HRESULT STDMETHODCALLTYPE
DataTarget::WriteVirtual(
    /* [in] */ CLRDATA_ADDRESS address,
    /* [size_is][in] */ PBYTE buffer,
    /* [in] */ ULONG32 request,
    /* [optional][out] */ ULONG32 *done)
{
    if (g_ExtData == NULL)
    {
        return E_UNEXPECTED;
    }
    return g_ExtData->WriteVirtual(address, (PVOID)buffer, request, (PULONG)done);
}

HRESULT STDMETHODCALLTYPE
DataTarget::GetTLSValue(
    /* [in] */ ULONG32 threadID,
    /* [in] */ ULONG32 index,
    /* [out] */ CLRDATA_ADDRESS* value)
{
    return E_NOTIMPL;
}

HRESULT STDMETHODCALLTYPE
DataTarget::SetTLSValue(
    /* [in] */ ULONG32 threadID,
    /* [in] */ ULONG32 index,
    /* [in] */ CLRDATA_ADDRESS value)
{
    return E_NOTIMPL;
}

HRESULT STDMETHODCALLTYPE
DataTarget::GetCurrentThreadID(
    /* [out] */ ULONG32 *threadID)
{
    if (g_ExtSystem == NULL)
    {
        return E_UNEXPECTED;
    }
    return g_ExtSystem->GetCurrentThreadSystemId((PULONG)threadID);
}

HRESULT STDMETHODCALLTYPE
DataTarget::GetThreadContext(
    /* [in] */ ULONG32 threadID,
    /* [in] */ ULONG32 contextFlags,
    /* [in] */ ULONG32 contextSize,
    /* [out, size_is(contextSize)] */ PBYTE context)
{
    HRESULT hr;
#ifdef FEATURE_PAL
    if (g_ExtServices == NULL)
    {
        return E_UNEXPECTED;
    }
    hr = g_ExtServices->GetThreadContextBySystemId(threadID, contextFlags, contextSize, context);
#else
    if (g_ExtSystem == NULL || g_ExtAdvanced == NULL)
    {
        return E_UNEXPECTED;
    }
    ULONG ulThreadIDOrig;
    ULONG ulThreadIDRequested;

    hr = g_ExtSystem->GetCurrentThreadId(&ulThreadIDOrig);
    if (FAILED(hr))
    {
        return hr;
    }

    hr = g_ExtSystem->GetThreadIdBySystemId(threadID, &ulThreadIDRequested);
    if (FAILED(hr))
    {
        return hr;
    }

    hr = g_ExtSystem->SetCurrentThreadId(ulThreadIDRequested);
    if (FAILED(hr))
    {
        return hr;
    }

    // Prepare context structure
    ZeroMemory(context, contextSize);
    g_targetMachine->SetContextFlags(context, contextFlags);

    // Ok, do it!
    hr = g_ExtAdvanced->GetThreadContext((LPVOID) context, contextSize);

    // This is cleanup; failure here doesn't mean GetThreadContext should fail
    // (that's determined by hr).
    g_ExtSystem->SetCurrentThreadId(ulThreadIDOrig);
#endif

    // GetThreadContext clears ContextFlags or sets them incorrectly and DBI needs it set to know what registers to copy
    g_targetMachine->SetContextFlags(context, contextFlags);

    return hr;
}

HRESULT STDMETHODCALLTYPE
DataTarget::SetThreadContext(
    /* [in] */ ULONG32 threadID,
    /* [in] */ ULONG32 contextSize,
    /* [out, size_is(contextSize)] */ PBYTE context)
{
    return E_NOTIMPL;
}

HRESULT STDMETHODCALLTYPE
DataTarget::Request(
    /* [in] */ ULONG32 reqCode,
    /* [in] */ ULONG32 inBufferSize,
    /* [size_is][in] */ BYTE *inBuffer,
    /* [in] */ ULONG32 outBufferSize,
    /* [size_is][out] */ BYTE *outBuffer)
{
    return E_NOTIMPL;
}

// ICLRDataTarget2

HRESULT STDMETHODCALLTYPE 
DataTarget::AllocVirtual(
    /* [in] */ CLRDATA_ADDRESS addr,
    /* [in] */ ULONG32 size,
    /* [in] */ ULONG32 typeFlags,
    /* [in] */ ULONG32 protectFlags,
    /* [out] */ CLRDATA_ADDRESS* virt)
{
#ifdef FEATURE_PAL
    return E_NOTIMPL;
#else
    ULONG64 hProcess;
    HRESULT hr = g_ExtSystem->GetCurrentProcessHandle(&hProcess);
    if (FAILED(hr)) {
        return hr;
    }
    LPVOID allocation = ::VirtualAllocEx((HANDLE)hProcess, (LPVOID)addr, size, typeFlags, protectFlags);
    if (allocation == NULL) {
        return HRESULT_FROM_WIN32(::GetLastError());
    }
    *virt = (CLRDATA_ADDRESS)allocation;
    return S_OK;
#endif
}
        
HRESULT STDMETHODCALLTYPE 
DataTarget::FreeVirtual(
    /* [in] */ CLRDATA_ADDRESS addr,
    /* [in] */ ULONG32 size,
    /* [in] */ ULONG32 typeFlags)
{
#ifdef FEATURE_PAL
    return E_NOTIMPL;
#else
    ULONG64 hProcess;
    HRESULT hr = g_ExtSystem->GetCurrentProcessHandle(&hProcess);
    if (FAILED(hr)) {
        return hr;
    }
    ::VirtualFreeEx((HANDLE)hProcess, (LPVOID)addr, size, typeFlags);
    return S_OK;
#endif
}

// ICorDebugDataTarget4

HRESULT STDMETHODCALLTYPE 
DataTarget::VirtualUnwind(
    /* [in] */ DWORD threadId,
    /* [in] */ ULONG32 contextSize,
    /* [in, out, size_is(contextSize)] */ PBYTE context)
{
#ifdef FEATURE_PAL
    if (g_ExtServices == NULL)
    {
        return E_UNEXPECTED;
    }
    return g_ExtServices->VirtualUnwind(threadId, contextSize, context);
#else
    return E_NOTIMPL;
#endif
}

// ICLRMetadataLocator

HRESULT STDMETHODCALLTYPE
DataTarget::GetMetadata(
    /* [in] */ LPCWSTR imagePath,
    /* [in] */ ULONG32 imageTimestamp,
    /* [in] */ ULONG32 imageSize,
    /* [in] */ GUID* mvid,
    /* [in] */ ULONG32 mdRva,
    /* [in] */ ULONG32 flags,
    /* [in] */ ULONG32 bufferSize,
    /* [out, size_is(bufferSize), length_is(*dataSize)] */
    BYTE* buffer,
    /* [out] */ ULONG32* dataSize)
{
    return ::GetMetadataLocator(imagePath, imageTimestamp, imageSize, mvid, mdRva, flags, bufferSize, buffer, dataSize);
}

// ICLRRuntimeLocator

HRESULT STDMETHODCALLTYPE 
DataTarget::GetRuntimeBase(
    /* [out] */ CLRDATA_ADDRESS* baseAddress)
{
    *baseAddress = m_baseAddress;
    return S_OK;
}

