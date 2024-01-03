// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef __cordebugdatatarget_h__
#define __cordebugdatatarget_h__

/**********************************************************************\
* Data target for the debugged process. Provided to OpenVirtualProcess 
* in order to get an ICorDebugProcess back.
\**********************************************************************/
class CorDebugDataTarget : public ICorDebugMutableDataTarget, public ICorDebugMetaDataLocator, public ICorDebugDataTarget4
{
public:
    CorDebugDataTarget() : m_ref(1)
    {
    }

    virtual ~CorDebugDataTarget() {}

    virtual HRESULT STDMETHODCALLTYPE QueryInterface(
        REFIID InterfaceId,
        PVOID* pInterface)
    {
        if (InterfaceId == IID_IUnknown)
        {
            *pInterface = static_cast<IUnknown *>(static_cast<ICorDebugDataTarget *>(this));
        }
        else if (InterfaceId == IID_ICorDebugDataTarget)
        {
            *pInterface = static_cast<ICorDebugDataTarget *>(this);
        }
        else if (InterfaceId == IID_ICorDebugMutableDataTarget)
        {
            *pInterface = static_cast<ICorDebugMutableDataTarget *>(this);
        }
        else if (InterfaceId == IID_ICorDebugMetaDataLocator)
        {
            *pInterface = static_cast<ICorDebugMetaDataLocator *>(this);
        }
        else if (InterfaceId == IID_ICorDebugDataTarget4)
        {
            *pInterface = static_cast<ICorDebugDataTarget4 *>(this);
        }
        else
        {
            *pInterface = NULL;
            return E_NOINTERFACE;
        }

        AddRef();
        return S_OK;
    }
    
    virtual ULONG STDMETHODCALLTYPE AddRef()
    {
        return InterlockedIncrement(&m_ref);    
    }

    virtual ULONG STDMETHODCALLTYPE Release()
    {
        LONG ref = InterlockedDecrement(&m_ref);
        if (ref == 0)
        {
            delete this;
        }
        return ref;
    }

    //
    // ICorDebugDataTarget.
    //

    virtual HRESULT STDMETHODCALLTYPE GetPlatform(CorDebugPlatform * pPlatform)
    {
        ULONG platformKind = g_targetMachine->GetPlatform();
        if (IsWindowsTarget())
        {
            if (platformKind == IMAGE_FILE_MACHINE_I386)
                *pPlatform = CORDB_PLATFORM_WINDOWS_X86;
            else if (platformKind == IMAGE_FILE_MACHINE_AMD64)
                *pPlatform = CORDB_PLATFORM_WINDOWS_AMD64;
            else if (platformKind == IMAGE_FILE_MACHINE_ARMNT)
                *pPlatform = CORDB_PLATFORM_WINDOWS_ARM;
            else if (platformKind == IMAGE_FILE_MACHINE_ARM64)
                *pPlatform = CORDB_PLATFORM_WINDOWS_ARM64;
            else
                return E_FAIL;
        }
        else
        {
            if (platformKind == IMAGE_FILE_MACHINE_I386)
                *pPlatform = CORDB_PLATFORM_POSIX_X86;
            else if (platformKind == IMAGE_FILE_MACHINE_AMD64)
                *pPlatform = CORDB_PLATFORM_POSIX_AMD64;
            else if (platformKind == IMAGE_FILE_MACHINE_ARMNT)
                *pPlatform = CORDB_PLATFORM_POSIX_ARM;
            else if (platformKind == IMAGE_FILE_MACHINE_ARM64)
                *pPlatform = CORDB_PLATFORM_POSIX_ARM64;
            else if (platformKind == IMAGE_FILE_MACHINE_RISCV64)
                *pPlatform = CORDB_PLATFORM_POSIX_RISCV64;
            else
                return E_FAIL;
        }
    
        return S_OK;
    }

    virtual HRESULT STDMETHODCALLTYPE ReadVirtual( 
        CORDB_ADDRESS address,
        BYTE * pBuffer,
        ULONG32 request,
        ULONG32 * pcbRead)
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
            HRESULT hr = GetMetadataMemory(address, request, pBuffer);
            if (SUCCEEDED(hr)) {
                if (pcbRead != nullptr) {
                    *pcbRead = request;
                }
                return hr;
            }
        }
#endif
        HRESULT hr = g_ExtData->ReadVirtual(address, pBuffer, request, (PULONG) pcbRead);
        if (FAILED(hr)) 
        {
            ExtDbgOut("CorDebugDataTarget::ReadVirtual FAILED %08x address %p size %08x\n", hr, address, request);
        }
        return hr;
    }

    virtual HRESULT STDMETHODCALLTYPE GetThreadContext(
        DWORD dwThreadOSID,
        ULONG32 contextFlags,
        ULONG32 contextSize,
        BYTE * context)
    {
        HRESULT hr;
#ifdef FEATURE_PAL
        if (g_ExtServices == NULL)
        {
            return E_UNEXPECTED;
        }
        hr = g_ExtServices->GetThreadContextBySystemId(dwThreadOSID, contextFlags, contextSize, context);
#else
        ULONG ulThreadIDOrig;
        ULONG ulThreadIDRequested;

        hr = g_ExtSystem->GetCurrentThreadId(&ulThreadIDOrig);
        if (FAILED(hr))
        {
            return hr;
        }

        hr = g_ExtSystem->GetThreadIdBySystemId(dwThreadOSID, &ulThreadIDRequested);
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
#endif // FEATURE_PAL

        // GetThreadContext clears ContextFlags or sets them incorrectly and DBI needs it set to know what registers to copy
        g_targetMachine->SetContextFlags(context, contextFlags);

        return hr;
    }

    //
    // ICorDebugMutableDataTarget.
    //

    virtual HRESULT STDMETHODCALLTYPE WriteVirtual(CORDB_ADDRESS address,
                                                   const BYTE * pBuffer,
                                                   ULONG32 bytesRequested)
    {
        if (g_ExtData == NULL)
        {
            return E_UNEXPECTED;
        }
        return g_ExtData->WriteVirtual(address, (PVOID)pBuffer, bytesRequested, NULL);
    }

    virtual HRESULT STDMETHODCALLTYPE SetThreadContext(DWORD dwThreadID,
                                                       ULONG32 contextSize,
                                                       const BYTE * pContext)
    {
        return E_NOTIMPL;
    }

    virtual HRESULT STDMETHODCALLTYPE ContinueStatusChanged(DWORD dwThreadId,
                                                            CORDB_CONTINUE_STATUS continueStatus)
    {
        return E_NOTIMPL;
    }

    //
    // ICorDebugMetaDataLocator.
    //

    virtual HRESULT STDMETHODCALLTYPE GetMetaData(
        /* [in] */ LPCWSTR wszImagePath,
        /* [in] */ DWORD dwImageTimeStamp,
        /* [in] */ DWORD dwImageSize,
        /* [in] */ ULONG32 cchPathBuffer,
        /* [annotation][out] */ 
        _Out_ ULONG32 *pcchPathBuffer,
        /* [annotation][length_is][size_is][out] */ 
        _Out_writes_to_(cchPathBuffer, *pcchPathBuffer) WCHAR wszPathBuffer[])
    {
        return ::GetICorDebugMetadataLocator(wszImagePath, dwImageTimeStamp, dwImageSize, cchPathBuffer, pcchPathBuffer, wszPathBuffer);
    }

    //
    // ICorDebugDataTarget4
    //
    virtual HRESULT STDMETHODCALLTYPE VirtualUnwind(DWORD threadId, ULONG32 contextSize, PBYTE context)
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

protected:
    LONG m_ref;
};

#endif // __cordebugdatatarget_h__
