// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "cordbdatatarget.h"
#include <new>
#include "ntimageex.h"
#include "palclr.h"

namespace
{
class CLRDataTargetAdapter final : public ICorDebugDataTarget
{
public:
    CLRDataTargetAdapter(ICLRDataTarget* target, bool windowsTarget)
        : m_refCount(1),
          m_target(target),
          m_windowsTarget(windowsTarget)
    {
        m_target->AddRef();
    }

    ~CLRDataTargetAdapter()
    {
        m_target->Release();
    }

    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID riid, void** ppInterface) override
    {
        if (ppInterface == NULL)
        {
            return E_POINTER;
        }
        *ppInterface = NULL;

        if (riid == __uuidof(IUnknown) || riid == __uuidof(ICorDebugDataTarget))
        {
            *ppInterface = static_cast<ICorDebugDataTarget*>(this);
            AddRef();
            return S_OK;
        }

        return E_NOINTERFACE;
    }

    ULONG STDMETHODCALLTYPE AddRef() override
    {
        return InterlockedIncrement(&m_refCount);
    }

    ULONG STDMETHODCALLTYPE Release() override
    {
        LONG refCount = InterlockedDecrement(&m_refCount);
        if (refCount == 0)
        {
            delete this;
        }
        return refCount;
    }

    HRESULT STDMETHODCALLTYPE GetPlatform(CorDebugPlatform* pPlatform) override
    {
        ULONG32 machineType;
        HRESULT hr = m_target->GetMachineType(&machineType);
        if (FAILED(hr))
        {
            return hr;
        }

        ULONG32 expectedPointerSize;
        CorDebugPlatform platform;
        switch (machineType)
        {
            case IMAGE_FILE_MACHINE_I386:
                expectedPointerSize = 4;
                platform = m_windowsTarget ? CORDB_PLATFORM_WINDOWS_X86 : CORDB_PLATFORM_POSIX_X86;
                break;
            case IMAGE_FILE_MACHINE_AMD64:
                expectedPointerSize = 8;
                platform = m_windowsTarget ? CORDB_PLATFORM_WINDOWS_AMD64 : CORDB_PLATFORM_POSIX_AMD64;
                break;
            case IMAGE_FILE_MACHINE_ARMNT:
                expectedPointerSize = 4;
                platform = m_windowsTarget ? CORDB_PLATFORM_WINDOWS_ARM : CORDB_PLATFORM_POSIX_ARM;
                break;
            case IMAGE_FILE_MACHINE_ARM64:
                expectedPointerSize = 8;
                platform = m_windowsTarget ? CORDB_PLATFORM_WINDOWS_ARM64 : CORDB_PLATFORM_POSIX_ARM64;
                break;
            case IMAGE_FILE_MACHINE_LOONGARCH64:
                if (m_windowsTarget)
                {
                    return E_NOTIMPL;
                }
                expectedPointerSize = 8;
                platform = CORDB_PLATFORM_POSIX_LOONGARCH64;
                break;
            case IMAGE_FILE_MACHINE_RISCV64:
                if (m_windowsTarget)
                {
                    return E_NOTIMPL;
                }
                expectedPointerSize = 8;
                platform = CORDB_PLATFORM_POSIX_RISCV64;
                break;
            default:
                return E_NOTIMPL;
        }

        ULONG32 pointerSize;
        hr = m_target->GetPointerSize(&pointerSize);
        if (FAILED(hr))
        {
            return hr;
        }
        if (pointerSize != expectedPointerSize)
        {
            return E_UNEXPECTED;
        }

        *pPlatform = platform;
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE ReadVirtual(
        CORDB_ADDRESS address,
        BYTE* pBuffer,
        ULONG32 bytesRequested,
        ULONG32* pBytesRead) override
    {
        return m_target->ReadVirtual(address, pBuffer, bytesRequested, pBytesRead);
    }

    HRESULT STDMETHODCALLTYPE GetThreadContext(
        DWORD threadID,
        ULONG32 contextFlags,
        ULONG32 contextSize,
        BYTE* pContext) override
    {
        return m_target->GetThreadContext(threadID, contextFlags, contextSize, pContext);
    }

private:
    LONG m_refCount;
    ICLRDataTarget* m_target;
    bool m_windowsTarget;
};
}

HRESULT CreateCordbDataTargetFromClrDataTarget(
    ULONG64 moduleBaseAddress,
    ICLRDataTarget* pClrDataTarget,
    ICorDebugDataTarget** ppCorDebugDataTarget)
{
    if (pClrDataTarget == NULL || ppCorDebugDataTarget == NULL)
    {
        return E_INVALIDARG;
    }
    *ppCorDebugDataTarget = NULL;

    USHORT imageSignature;
    ULONG32 bytesRead;
    HRESULT hr = pClrDataTarget->ReadVirtual(
        moduleBaseAddress,
        reinterpret_cast<BYTE*>(&imageSignature),
        sizeof(imageSignature),
        &bytesRead);
    if (FAILED(hr))
    {
        return hr;
    }
    if (bytesRead != sizeof(imageSignature))
    {
        return HRESULT_FROM_WIN32(ERROR_PARTIAL_COPY);
    }

    bool windowsTarget = imageSignature == IMAGE_DOS_SIGNATURE;
    CLRDataTargetAdapter* adapter = new (std::nothrow) CLRDataTargetAdapter(pClrDataTarget, windowsTarget);
    if (adapter == NULL)
    {
        return E_OUTOFMEMORY;
    }

    *ppCorDebugDataTarget = adapter;
    return S_OK;
}
