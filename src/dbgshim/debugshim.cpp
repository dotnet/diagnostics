// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//*****************************************************************************
// debugshim.cpp
//

//
//*****************************************************************************

#include "debugshim.h"
#include "cordbdatatarget.h"
#include "dbgutil.h"
#include <crtdbg.h>
#include <clrinternal.h> //has the CLR_ID_V4_DESKTOP guid in it
#include <clrdata.h>      //ICLRDataTarget, PFN_CLRDataCreateInstance
#include <ntimageex.h>
#include "palclr.h"
#include "winwrap.h"
#ifndef HOST_WINDOWS
#include <dlfcn.h>
#endif

//*****************************************************************************
// CLRDebuggingImpl implementation (ICLRDebugging)
//*****************************************************************************

enum class LibraryRequest
{
    DacOnly,
    DbiAndDac,
};

static bool IsDataAccessInterface(REFIID riid);
static bool GetDbiPath(SString& dbiPath);
static bool GetCDacPath(SString& cdacPath);
static HRESULT ResolveDacPath(ClrInfo& clrInfo,
                              IUnknown* pLibraryProvider,
                              SString& dacModulePath);
static HRESULT ResolveLibraries(ClrInfo& clrInfo,
                                IUnknown* pLibraryProvider,
                                LibraryRequest request,
                                SString* pDbiModulePath,
                                SString* pDacModulePath,
                                HMODULE* phDbi,
                                HMODULE* phDac);
static HRESULT LoadResolvedLibraries(SString& dbiModulePath,
                                     SString& dacModulePath,
                                     HMODULE* phDbi,
                                     HMODULE* phDac);
static HRESULT OpenCorDebugProcessWithCDac(
    ULONG64 moduleBaseAddress,
    IUnknown* pDataTarget,
    CLR_DEBUGGING_VERSION* pMaxDebuggerSupportedVersion,
    REFIID riidProcess,
    IUnknown** ppProcess,
    CLR_DEBUGGING_PROCESS_FLAGS* pFlags);
static HRESULT OpenCorDebugProcessWithProvider(
    ULONG64 moduleBaseAddress,
    IUnknown* pDataTarget,
    ClrInfo& clrInfo,
    IUnknown* pLibraryProvider,
    CLR_DEBUGGING_VERSION* pMaxDebuggerSupportedVersion,
    REFIID riidProcess,
    IUnknown** ppProcess,
    CLR_DEBUGGING_PROCESS_FLAGS* pFlags);
static VOID RetargetDacIfNeeded(DWORD* pdwTimeStamp, DWORD* pdwSizeOfImage);
static HRESULT SelectActivationResult(
    HRESULT cdacHr,
    HRESULT fallbackHr,
    bool cdacEvaluated);

typedef HRESULT (STDAPICALLTYPE  *OpenVirtualProcessImpl2FnPtr)(ULONG64 clrInstanceId,
    IUnknown * pDataTarget,
    LPCWSTR pDacModulePath,
    CLR_DEBUGGING_VERSION * pMaxDebuggerSupportedVersion,
    REFIID riid,
    IUnknown ** ppInstance,
    CLR_DEBUGGING_PROCESS_FLAGS * pdwFlags);

typedef HRESULT (STDAPICALLTYPE  *OpenVirtualProcessImplFnPtr)(ULONG64 clrInstanceId,
    IUnknown * pDataTarget,
    HMODULE hDacDll,
    CLR_DEBUGGING_VERSION * pMaxDebuggerSupportedVersion,
    REFIID riid,
    IUnknown ** ppInstance,
    CLR_DEBUGGING_PROCESS_FLAGS * pdwFlags);

typedef HRESULT (STDAPICALLTYPE  *OpenVirtualProcess2FnPtr)(ULONG64 clrInstanceId,
    IUnknown * pDataTarget,
    HMODULE hDacDll,
    REFIID riid,
    IUnknown ** ppInstance,
    CLR_DEBUGGING_PROCESS_FLAGS * pdwFlags);

typedef HMODULE (STDAPICALLTYPE  *LoadLibraryWFnPtr)(LPCWSTR lpLibFileName);

static bool IsTargetWindows(ICorDebugDataTarget* pDataTarget)
{
    CorDebugPlatform targetPlatform;

    HRESULT result = pDataTarget->GetPlatform(&targetPlatform);
    if (FAILED(result))
    {
        _ASSERTE(!"Unexpected error");
        return false;
    }

    switch (targetPlatform)
    {
        case CORDB_PLATFORM_WINDOWS_X86:
        case CORDB_PLATFORM_WINDOWS_AMD64:
        case CORDB_PLATFORM_WINDOWS_IA64:
        case CORDB_PLATFORM_WINDOWS_ARM:
        case CORDB_PLATFORM_WINDOWS_ARM64:
            return true;
        default:
            return false;
    }
}

// Implementation of ICLRDebugging::OpenVirtualProcess
//
// Arguments:
//   moduleBaseAddress - the address of the module which might be a CLR
//   pDataTarget - the data target for inspecting the process
//   pLibraryProvider - a callback for locating DBI and DAC
//   pMaxDebuggerSupportedVersion - the max version of the CLR that this debugger will support debugging
//   riidProcess - the IID of the interface that should be passed back in ppProcess
//   ppProcess - output for the ICorDebugProcess# if this module is a CLR
//   pVersion - the CLR version if this module is a CLR
//   pFlags - output, see the CLR_DEBUGGING_PROCESS_FLAGS for more details. Right now this has only one possible
//            value which indicates this runtime had an unhandled exception
STDMETHODIMP CLRDebuggingImpl::OpenVirtualProcess(
    ULONG64 moduleBaseAddress,
    IUnknown * pDataTarget,
    ICLRDebuggingLibraryProvider * pLibraryProvider,
    CLR_DEBUGGING_VERSION * pMaxDebuggerSupportedVersion,
    REFIID riidProcess,
    IUnknown ** ppProcess,
    CLR_DEBUGGING_VERSION * pVersion,
    CLR_DEBUGGING_PROCESS_FLAGS * pFlags)
{
    // A request for a DAC interface (e.g. IXCLRDataProcess) is serviced directly from the DAC/cDAC.
    // This type of request only requires IClrDataTarget and ignores version and flags.
    if (ppProcess != NULL && IsDataAccessInterface(riidProcess))
    {
        return OpenDataAccessProcess(moduleBaseAddress, pDataTarget, pLibraryProvider, riidProcess, ppProcess);
    }
    else
    {
        return OpenCorDebugProcess(
            moduleBaseAddress,
            pDataTarget,
            pLibraryProvider,
            pMaxDebuggerSupportedVersion,
            riidProcess,
            ppProcess,
            pVersion,
            pFlags);
    }
}

HRESULT CLRDebuggingImpl::OpenCorDebugProcess(
    ULONG64 moduleBaseAddress,
    IUnknown * pDataTarget,
    ICLRDebuggingLibraryProvider * pLibraryProvider,
    CLR_DEBUGGING_VERSION * pMaxDebuggerSupportedVersion,
    REFIID riidProcess,
    IUnknown ** ppProcess,
    CLR_DEBUGGING_VERSION * pVersion,
    CLR_DEBUGGING_PROCESS_FLAGS * pFlags)
{
    HRESULT hr = S_OK;
    ClrInfo clrInfo;
    ICorDebugDataTarget * pDt = NULL;
    CLR_DEBUGGING_VERSION version = {};

    // Snapshot the policy once so a concurrent SetCDacLoadPolicy cannot change our decisions mid-call.
    CDacLoadPolicy policy = m_cdacLoadPolicy;

    // argument checking
    if ((ppProcess != NULL || pFlags != NULL) &&
        pLibraryProvider == NULL &&
        policy == CDacLoadPolicy_LegacyDacOnly)
    {
        // The library provider must be specified if either ppProcess or pFlags is non-NULL in the
        // legacy path. Under a cDAC-capable policy this guard is skipped: the cDAC needs no provider,
        // and when the cDAC path fails with no provider available the final result is still E_POINTER
        // (see the fallbackHr sentinel), so the historical no-provider error is preserved.
        hr = E_POINTER;
    }
    else if ((ppProcess != NULL || pFlags != NULL) && pMaxDebuggerSupportedVersion == NULL)
    {
        hr = E_POINTER; // the max supported version must be specified if either
                            // ppProcess or pFlags is non-NULL
    }
    else if (pVersion != NULL && pVersion->wStructVersion != 0)
    {
        hr = CORDBG_E_UNSUPPORTED_VERSION_STRUCT;
    }
    else if (FAILED(pDataTarget->QueryInterface(__uuidof(ICorDebugDataTarget), (void**)&pDt)))
    {
        hr = CORDBG_E_MISSING_DATA_TARGET_INTERFACE;
    }

    if (SUCCEEDED(hr))
    {
        // get CLR version
        // The expectation is that new versions of the CLR will continue to use the same GUID
        // (unless there's a reason to hide them from older shims), but debuggers will tell us the
        // CLR version they're designed for and mscordbi.dll can decide whether or not to accept it.
        hr = GetCLRInfo(pDt, moduleBaseAddress, &version, clrInfo);
    }

    // If we need to fetch either the process info or the flags info then we need to find
    // mscordbi and DAC and do the version specific OVP work
    if (SUCCEEDED(hr) && (ppProcess != NULL || pFlags != NULL))
    {
        if (ppProcess != NULL)
        {
            *ppProcess = NULL;
        }
        if (pFlags != NULL)
        {
            *pFlags = (CLR_DEBUGGING_PROCESS_FLAGS)0;
        }

        HRESULT cdacHr = E_FAIL;
        bool cdacEvaluated = policy != CDacLoadPolicy_LegacyDacOnly;
        if (cdacEvaluated)
        {
            cdacHr = OpenCorDebugProcessWithCDac(
                moduleBaseAddress,
                pDataTarget,
                pMaxDebuggerSupportedVersion,
                riidProcess,
                ppProcess,
                pFlags);
        }

        HRESULT fallbackHr = E_POINTER;
        bool fallbackEvaluated =
            FAILED(cdacHr) &&
            pLibraryProvider != NULL &&
            policy != CDacLoadPolicy_CDacOnly;
        if (fallbackEvaluated)
        {
            fallbackHr = OpenCorDebugProcessWithProvider(
                moduleBaseAddress,
                pDataTarget,
                clrInfo,
                pLibraryProvider,
                pMaxDebuggerSupportedVersion,
                riidProcess,
                ppProcess,
                pFlags);
        }

        hr = SelectActivationResult(cdacHr, fallbackHr, cdacEvaluated);
    }

    // version is still valid in some failure cases
    if (pVersion != NULL && (SUCCEEDED(hr) || (hr == CORDBG_E_UNSUPPORTED_DEBUGGING_MODEL) || (hr == CORDBG_E_UNSUPPORTED_FORWARD_COMPAT)))
    {
        memcpy(pVersion, &version, sizeof(CLR_DEBUGGING_VERSION));
    }

    // free the data target we QI'ed earlier
    if (pDt != NULL)
    {
        pDt->Release();
    }

    return hr;
}

static HRESULT OpenCorDebugProcessWithCDac(
    ULONG64 moduleBaseAddress,
    IUnknown* pDataTarget,
    CLR_DEBUGGING_VERSION* pMaxDebuggerSupportedVersion,
    REFIID riidProcess,
    IUnknown** ppProcess,
    CLR_DEBUGGING_PROCESS_FLAGS* pFlags)
{
    SString dbiModulePath;
    if (!GetDbiPath(dbiModulePath))
    {
        return E_FAIL;
    }

    HMODULE hDbi = LoadLibraryW(dbiModulePath);
    if (hDbi == NULL)
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    OpenVirtualProcessImpl2FnPtr ovpFn =
        (OpenVirtualProcessImpl2FnPtr)GetProcAddress(hDbi, "OpenVirtualProcessImpl2");
    if (ovpFn == NULL)
    {
        return CORDBG_E_MISSING_DEBUGGER_EXPORTS;
    }

    SString cdacModulePath;
    HRESULT hr = E_FAIL;
    if (GetCDacPath(cdacModulePath))
    {
        hr = ovpFn(
            moduleBaseAddress,
            pDataTarget,
            cdacModulePath,
            pMaxDebuggerSupportedVersion,
            riidProcess,
            ppProcess,
            pFlags);
    }

    if (SUCCEEDED(hr) && ppProcess != NULL && *ppProcess == NULL)
    {
        hr = E_FAIL;
    }
    if (FAILED(hr))
    {
        _ASSERTE(ppProcess == NULL || *ppProcess == NULL);
        _ASSERTE(pFlags == NULL || *pFlags == 0);
    }
    return hr;
}

static HRESULT OpenCorDebugProcessWithProvider(
    ULONG64 moduleBaseAddress,
    IUnknown* pDataTarget,
    ClrInfo& clrInfo,
    IUnknown* pLibraryProvider,
    CLR_DEBUGGING_VERSION* pMaxDebuggerSupportedVersion,
    REFIID riidProcess,
    IUnknown** ppProcess,
    CLR_DEBUGGING_PROCESS_FLAGS* pFlags)
{
    SString dbiModulePath;
    SString dacModulePath;
    HMODULE hDbi = NULL;
    HMODULE hDac = NULL;
    HRESULT hr = ResolveLibraries(
        clrInfo,
        pLibraryProvider,
        LibraryRequest::DbiAndDac,
        &dbiModulePath,
        &dacModulePath,
        &hDbi,
        &hDac);
    if (SUCCEEDED(hr))
    {
        hr = LoadResolvedLibraries(dbiModulePath, dacModulePath, &hDbi, &hDac);
    }
    if (FAILED(hr))
    {
        return hr;
    }

    OpenVirtualProcessImpl2FnPtr ovpImpl2Fn =
        (OpenVirtualProcessImpl2FnPtr)GetProcAddress(hDbi, "OpenVirtualProcessImpl2");
    if (ovpImpl2Fn != NULL && !dacModulePath.IsEmpty())
    {
        hr = ovpImpl2Fn(
            moduleBaseAddress,
            pDataTarget,
            dacModulePath,
            pMaxDebuggerSupportedVersion,
            riidProcess,
            ppProcess,
            pFlags);
    }

    bool useLegacyOvp = ovpImpl2Fn == NULL ||
                        dacModulePath.IsEmpty() ||
                        (ppProcess != NULL && *ppProcess == NULL);

#ifdef HOST_UNIX
    if (SUCCEEDED(hr) && useLegacyOvp && !dacModulePath.IsEmpty())
    {
        // On Linux/MacOS the DAC module handle needs to be re-created using the DAC PAL instance
        // before being passed to DBI's OpenVirtualProcess* implementation.
        LoadLibraryWFnPtr loadLibraryWFn = (LoadLibraryWFnPtr)GetProcAddress(hDac, "LoadLibraryW");
        hDac = loadLibraryWFn != NULL ? loadLibraryWFn(dacModulePath) : NULL;
        if (hDac == NULL)
        {
            hr = E_HANDLE;
        }
    }
#endif

    if (SUCCEEDED(hr) && useLegacyOvp)
    {
        OpenVirtualProcessImplFnPtr ovpImplFn =
            (OpenVirtualProcessImplFnPtr)GetProcAddress(hDbi, "OpenVirtualProcessImpl");
        if (ovpImplFn != NULL)
        {
            hr = ovpImplFn(
                moduleBaseAddress,
                pDataTarget,
                hDac,
                pMaxDebuggerSupportedVersion,
                riidProcess,
                ppProcess,
                pFlags);
        }
        else
        {
            OpenVirtualProcess2FnPtr ovp2Fn =
                (OpenVirtualProcess2FnPtr)GetProcAddress(hDbi, "OpenVirtualProcess2");
            hr = ovp2Fn != NULL
                ? ovp2Fn(moduleBaseAddress, pDataTarget, hDac, riidProcess, ppProcess, pFlags)
                : CORDBG_E_LIBRARY_PROVIDER_ERROR;
        }
    }

    if (FAILED(hr))
    {
        _ASSERTE(ppProcess == NULL || *ppProcess == NULL);
        _ASSERTE(pFlags == NULL || *pFlags == 0);
    }
    return hr;
}

// Call the library provider to get the DBI and DAC
//
// Arguments:
//   clrInfo - the runtime info
//   pLibraryProvider - a callback for locating DBI and DAC
//   pDbiModulePath - returns the DBI module path, if requested
//   pDacModulePath - returns the DAC module path
static HRESULT ResolveLibraryPathsCore(
    ClrInfo& clrInfo,
    ICLRDebuggingLibraryProvider3* pLibraryProvider,
    SString& dbiModulePath,
    SString& dacModulePath)
{
    return ResolveLibraries(
        clrInfo,
        pLibraryProvider,
        /*request*/ LibraryRequest::DbiAndDac,
        &dbiModulePath,
        &dacModulePath,
        /*phDbi*/ NULL,
        /*phDac*/ NULL);
}

HRESULT CLRDebuggingImpl::ResolveLibraryPaths(
    ClrInfo& clrInfo,
    ICLRDebuggingLibraryProvider3* pLibraryProvider,
    SString& dbiModulePath,
    SString& dacModulePath)
{
    return ResolveLibraryPathsCore(
        clrInfo,
        pLibraryProvider,
        dbiModulePath,
        dacModulePath);
}

// Call the library provider to get the DBI and DAC
//
// Arguments:
//   clrInfo - the runtime info
//   pLibraryProvider - a callback for locating DBI and DAC
//   dbiModulePath - returns the DBI module path
//   dacModulePath - returns the DAC module path
//   phDbi - returns the DBI module handle if old library provider
//   phDac - returns the DAC module handle if old library provider
static HRESULT ResolveLibraries(
    ClrInfo& clrInfo,
    IUnknown* punk,
    LibraryRequest request,
    SString* pDbiModulePathOutput,
    SString* pDacModulePathOutput,
    HMODULE* phDbi,
    HMODULE* phDac)
{
    bool resolveDbi = request == LibraryRequest::DbiAndDac;
    ReleaseHolder<ICLRDebuggingLibraryProvider3> pLibraryProvider3;
    ReleaseHolder<ICLRDebuggingLibraryProvider2> pLibraryProvider2;
    ReleaseHolder<ICLRDebuggingLibraryProvider> pLibraryProvider;
    LPWSTR pDbiModulePath = NULL;
    LPWSTR pDacModulePath = NULL;
    HRESULT hr = S_OK;

    _ASSERTE(punk != NULL);
    _ASSERTE(pDacModulePathOutput != NULL);
    _ASSERTE(!resolveDbi || pDbiModulePathOutput != NULL);
    // Validate the incoming index info
    if (resolveDbi ? !clrInfo.IsValid() : !clrInfo.IsDacValid())
    {
        hr = CORDBG_E_INCOMPATIBLE_PROTOCOL;
        goto exit;
    }

    if (SUCCEEDED(punk->QueryInterface(__uuidof(ICLRDebuggingLibraryProvider3), (void**)&pLibraryProvider3)))
    {
        const WCHAR* wszRuntimeModulePath = !clrInfo.RuntimeModulePath.IsEmpty() ? clrInfo.RuntimeModulePath.GetUnicode() : NULL;
        if (clrInfo.WindowsTarget)
        {
            if (resolveDbi &&
                (FAILED(pLibraryProvider3->ProvideWindowsLibrary(
                    clrInfo.DbiName,
                    wszRuntimeModulePath,
                    clrInfo.IndexType,
                    clrInfo.DbiTimeStamp,
                    clrInfo.DbiSizeOfImage,
                    &pDbiModulePath)) || pDbiModulePath == NULL))
            {
                hr = CORDBG_E_LIBRARY_PROVIDER_ERROR;
                goto exit;
            }
            // Ask library provider for DAC
            if (FAILED(pLibraryProvider3->ProvideWindowsLibrary(
                clrInfo.DacName,
                wszRuntimeModulePath,
                clrInfo.IndexType,
                clrInfo.DacTimeStamp,
                clrInfo.DacSizeOfImage,
                &pDacModulePath)) || pDacModulePath == NULL)
            {
                hr = CORDBG_E_LIBRARY_PROVIDER_ERROR;
                goto exit;
            }
        }
        else
        {
            BYTE* dbiBuildId = NULL;
            ULONG dbiBuildIdSize = 0;
            BYTE* dacBuildId = NULL;
            ULONG dacBuildIdSize = 0;

            // What kind of build id are we going to give the provider
            switch (clrInfo.IndexType)
            {
                case LIBRARY_PROVIDER_INDEX_TYPE::Identity:
                    if (resolveDbi && clrInfo.DbiBuildIdSize > 0)
                    {
                        dbiBuildId = clrInfo.DbiBuildId;
                        dbiBuildIdSize = clrInfo.DbiBuildIdSize;
                    }
                    if (clrInfo.DacBuildIdSize > 0)
                    {
                        dacBuildId = clrInfo.DacBuildId;
                        dacBuildIdSize = clrInfo.DacBuildIdSize;
                    }
                    break;
                case LIBRARY_PROVIDER_INDEX_TYPE::Runtime:
                    if (clrInfo.RuntimeBuildIdSize > 0)
                    {
                        if (resolveDbi)
                        {
                            dbiBuildId = clrInfo.RuntimeBuildId;
                            dbiBuildIdSize = clrInfo.RuntimeBuildIdSize;
                        }
                        dacBuildId = clrInfo.RuntimeBuildId;
                        dacBuildIdSize = clrInfo.RuntimeBuildIdSize;
                    }
                    break;
                default:
                    hr = CORDBG_E_LIBRARY_PROVIDER_ERROR;
                    goto exit;
            }
            if (resolveDbi &&
                (FAILED(pLibraryProvider3->ProvideUnixLibrary(
                    clrInfo.DbiName,
                    wszRuntimeModulePath,
                    clrInfo.IndexType,
                    dbiBuildId,
                    dbiBuildIdSize,
                    &pDbiModulePath)) || pDbiModulePath == NULL))
            {
                hr = CORDBG_E_LIBRARY_PROVIDER_ERROR;
                goto exit;
            }
            // Ask library provider for DAC
            if (FAILED(pLibraryProvider3->ProvideUnixLibrary(
                clrInfo.DacName,
                wszRuntimeModulePath,
                clrInfo.IndexType,
                dacBuildId,
                dacBuildIdSize,
                &pDacModulePath)) || pDacModulePath == NULL)
            {
                hr = CORDBG_E_LIBRARY_PROVIDER_ERROR;
                goto exit;
            }
        }
    }
    else if (SUCCEEDED(punk->QueryInterface(__uuidof(ICLRDebuggingLibraryProvider2), (void**)&pLibraryProvider2)))
    {
        if (resolveDbi &&
            (FAILED(pLibraryProvider2->ProvideLibrary2(clrInfo.DbiName, clrInfo.DbiTimeStamp, clrInfo.DbiSizeOfImage, &pDbiModulePath)) || pDbiModulePath == NULL))
        {
            hr = CORDBG_E_LIBRARY_PROVIDER_ERROR;
            goto exit;
        }

        // Adjust the timestamp and size of image if this DAC is a known buggy version and needs to be retargeted
        RetargetDacIfNeeded(&clrInfo.DacTimeStamp, &clrInfo.DacSizeOfImage);

        // Ask library provider for DAC
        if (FAILED(pLibraryProvider2->ProvideLibrary2(clrInfo.DacName, clrInfo.DacTimeStamp, clrInfo.DacSizeOfImage, &pDacModulePath)) || pDacModulePath == NULL)
        {
            hr = CORDBG_E_LIBRARY_PROVIDER_ERROR;
            goto exit;
        }
    }
    else if (SUCCEEDED(punk->QueryInterface(__uuidof(ICLRDebuggingLibraryProvider), (void**)&pLibraryProvider)))
    {
        if (phDbi == NULL || phDac == NULL)
        {
            // A v1 provider returns handles. Path-only DBI callers reject that provider version
            // as an invalid argument; direct DAC activation reports the historical provider error.
            hr = resolveDbi ? E_INVALIDARG : CORDBG_E_LIBRARY_PROVIDER_ERROR;
            goto exit;
        }

        if (FAILED(pLibraryProvider->ProvideLibrary(clrInfo.DbiName, clrInfo.DbiTimeStamp, clrInfo.DbiSizeOfImage, phDbi)) || *phDbi == NULL)
        {
            hr = CORDBG_E_LIBRARY_PROVIDER_ERROR;
            goto exit;
        }

        // Adjust the timestamp and size of image if this DAC is a known buggy version and needs to be retargeted
        RetargetDacIfNeeded(&clrInfo.DacTimeStamp, &clrInfo.DacSizeOfImage);

        // ask library provider for DAC
        if (FAILED(pLibraryProvider->ProvideLibrary(clrInfo.DacName, clrInfo.DacTimeStamp, clrInfo.DacSizeOfImage, phDac)) || *phDac == NULL)
        {
            hr = CORDBG_E_LIBRARY_PROVIDER_ERROR;
            goto exit;
        }
    }
    else
    {
        hr = CORDBG_E_LIBRARY_PROVIDER_ERROR;
        goto exit;
    }

exit:
    if (pDbiModulePath != NULL)
    {
        pDbiModulePathOutput->Set(pDbiModulePath);
#ifdef HOST_UNIX
        free(pDbiModulePath);
#else
        CoTaskMemFree(pDbiModulePath);
#endif
    }
    if (pDacModulePath != NULL)
    {
        pDacModulePathOutput->Set(pDacModulePath);
#ifdef HOST_UNIX
        free(pDacModulePath);
#else
        CoTaskMemFree(pDacModulePath);
#endif
    }
    return hr;
}

static HRESULT LoadResolvedLibraries(
    SString& dbiModulePath,
    SString& dacModulePath,
    HMODULE* phDbi,
    HMODULE* phDac)
{
    HRESULT hr = S_OK;

    // Load DAC first because DBI references exports from the matching DAC PAL.
    if (*phDac == NULL)
    {
        *phDac = LoadLibraryW(dacModulePath);
        if (*phDac == NULL)
        {
            hr = HRESULT_FROM_WIN32(GetLastError());
        }
    }

    // Load DBI after DAC (see comment above).
    if (SUCCEEDED(hr) && *phDbi == NULL)
    {
        *phDbi = LoadLibraryW(dbiModulePath);
        if (*phDbi == NULL)
        {
            hr = HRESULT_FROM_WIN32(GetLastError());
        }
    }

    return hr;
}

// Selects the final HRESULT after the cDAC and/or fallback activation attempts have run.
//   - A cDAC success wins, then a fallback success.
//   - On total failure, if the cDAC was evaluated (any policy other than LegacyDacOnly) its error
//     is surfaced so tools can log why the cDAC rejected the target.
//   - Otherwise (LegacyDacOnly) the fallback error is surfaced. Callers seed fallbackHr with
//     E_POINTER so the LegacyDacOnly no-provider case preserves its historical HRESULT.
static HRESULT SelectActivationResult(
    HRESULT cdacHr,
    HRESULT fallbackHr,
    bool cdacEvaluated)
{
    if (SUCCEEDED(cdacHr))
    {
        return cdacHr;
    }
    if (SUCCEEDED(fallbackHr))
    {
        return fallbackHr;
    }
    if (cdacEvaluated)
    {
        return cdacHr;
    }
    return fallbackHr;
}

// Checks to see if this DAC is one of a known set of old DAC builds which contains an issue.
// If so we retarget to a newer compatible version which has the bug fixed. This is done
// by changing the PE information used to lookup the DAC.
//
// Arguments
//   pdwTimeStamp - on input, the timestamp of DAC as embedded in the CLR image
//                  on output, a potentially new timestamp for an updated DAC to use
//                  instead
//   pdwSizeOfImage - on input, the sizeOfImage of DAC as embedded in the CLR image
//                  on output, a potentially new sizeOfImage for an updated DAC to use
//                  instead
static VOID RetargetDacIfNeeded(DWORD* pdwTimeStamp,
                                DWORD* pdwSizeOfImage)
{

    // This code is auto generated by the CreateRetargetTable tool
    // on 3/4/2011 6:35 PM
    // and then copy-pasted here.
    //
    //
    //
    // Retarget the GDR1 amd64 build
    if( (*pdwTimeStamp == 0x4d536868) && (*pdwSizeOfImage == 0x17b000))
    {
        *pdwTimeStamp = 0x4d71a160;
        *pdwSizeOfImage = 0x17b000;
    }
    // Retarget the GDR1 x86 build
    else if( (*pdwTimeStamp == 0x4d5368f2) && (*pdwSizeOfImage == 0x120000))
    {
        *pdwTimeStamp = 0x4d71a14f;
        *pdwSizeOfImage = 0x120000;
    }
    // Retarget the RTM amd64 build
    else if( (*pdwTimeStamp == 0x4ba21fa7) && (*pdwSizeOfImage == 0x17b000))
    {
        *pdwTimeStamp = 0x4d71a13c;
        *pdwSizeOfImage = 0x17b000;
    }
    // Retarget the RTM x86 build
    else if( (*pdwTimeStamp == 0x4ba1da25) && (*pdwSizeOfImage == 0x120000))
    {
        *pdwTimeStamp = 0x4d71a128;
        *pdwSizeOfImage = 0x120000;
    }
    // This code is auto generated by the CreateRetargetTable tool
    // on 8/17/2011 1:28 AM
    // and then copy-pasted here.
    //
    //
    //
    // Retarget the GDR2 amd64 build
    else if( (*pdwTimeStamp == 0x4da428c7) && (*pdwSizeOfImage == 0x17b000))
    {
        *pdwTimeStamp = 0x4e4b7bc2;
        *pdwSizeOfImage = 0x17b000;
    }
    // Retarget the GDR2 x86 build
    else if( (*pdwTimeStamp == 0x4da3fe52) && (*pdwSizeOfImage == 0x120000))
    {
        *pdwTimeStamp = 0x4e4b7bb1;
        *pdwSizeOfImage = 0x120000;
    }
    // End auto-generated code
}

#define PE_FIXEDFILEINFO_SIGNATURE 0xFEEF04BD

// Checks to see if a module is a CLR and if so, fetches the debug data
// from the embedded resource
//
// Arguments
//   pDataTarget - dataTarget for the process we are inspecting
//   moduleBaseAddress - base address of a module we should inspect
//   clrInfo - various info about the runtime
HRESULT CLRDebuggingImpl::GetCLRInfo(ICorDebugDataTarget * pDataTarget,
                                     ULONG64 moduleBaseAddress,
                                     CLR_DEBUGGING_VERSION* pVersion,
                                     ClrInfo& clrInfo)
{
    memset(pVersion, 0, sizeof(CLR_DEBUGGING_VERSION));
#ifdef HOST_WINDOWS
    if (IsTargetWindows(pDataTarget))
    {
        clrInfo.WindowsTarget = TRUE;

        WORD imageFileMachine = 0;
        DWORD resourceSectionRVA = 0;
        HRESULT hr = GetMachineAndResourceSectionRVA(pDataTarget, moduleBaseAddress, &imageFileMachine, &resourceSectionRVA);

        // We want the version resource which has type = RT_VERSION = 16, name = 1, language = 0x409
        DWORD versionResourceRVA = 0;
        DWORD versionResourceSize = 0;
        if (SUCCEEDED(hr))
        {
            hr = GetResourceRvaFromResourceSectionRva(pDataTarget, moduleBaseAddress, resourceSectionRVA, 16, 1, 0x409, &versionResourceRVA, &versionResourceSize);
            if (FAILED(hr))
            {
                // The single-file apps are language "neutral" (0)
                hr = GetResourceRvaFromResourceSectionRva(pDataTarget, moduleBaseAddress, resourceSectionRVA, 16, 1, 0, &versionResourceRVA, &versionResourceSize);
            }
        }

        // At last we get our version info
        VS_FIXEDFILEINFO fixedFileInfo = {0};
        if (SUCCEEDED(hr))
        {
            // The version resource has 3 words, then the unicode string "VS_VERSION_INFO"
            // (16 WCHARS including the null terminator)
            // then padding to a 32-bit boundary, then the VS_FIXEDFILEINFO struct
            DWORD fixedFileInfoRVA = ((versionResourceRVA + 3*2 + 16*2 + 3)/4)*4;
            hr = ReadFromDataTarget(pDataTarget, moduleBaseAddress + fixedFileInfoRVA, (BYTE*)&fixedFileInfo, sizeof(fixedFileInfo));
        }

        // Verify the signature on the version resource
        if (SUCCEEDED(hr) && fixedFileInfo.dwSignature != PE_FIXEDFILEINFO_SIGNATURE)
        {
            hr = CORDBG_E_NOT_CLR;
        }

        // Record the version information
        if (SUCCEEDED(hr))
        {
            pVersion->wMajor = (WORD) (fixedFileInfo.dwProductVersionMS >> 16);
            pVersion->wMinor = (WORD) (fixedFileInfo.dwProductVersionMS & 0xFFFF);
            pVersion->wBuild = (WORD) (fixedFileInfo.dwProductVersionLS >> 16);
            pVersion->wRevision = (WORD) (fixedFileInfo.dwProductVersionLS & 0xFFFF);
        }

        // Now grab the special clr debug info resource
        // We may need to scan a few different names searching though...
        // 1) CLRDEBUGINFO<host_os><host_arch> where host_os = 'WINDOWS' or 'CORESYS' and host_arch = 'X86' or 'ARM' or 'AMD64'
        // 2) For back-compat if the host os is windows and the host architecture matches the target then CLRDEBUGINFO is used with no suffix.
        DWORD debugResourceRVA = 0;
        DWORD debugResourceSize = 0;
        BOOL useCrossPlatformNaming = FALSE;
        if (SUCCEEDED(hr))
        {
            // First check for the resource which has type = RC_DATA = 10, name = "CLRDEBUGINFO<host_os><host_arch>", language = 0
            HRESULT hrGetResource = GetResourceRvaFromResourceSectionRvaByName(pDataTarget, moduleBaseAddress, resourceSectionRVA, 10, CLRDEBUGINFO_RESOURCE_NAME, 0, &debugResourceRVA, &debugResourceSize);
            useCrossPlatformNaming = SUCCEEDED(hrGetResource);

    #if defined(HOST_WINDOWS) && (defined(HOST_X86) || defined(HOST_AMD64) || defined(HOST_ARM) || defined(HOST_ARM64))
      #if defined(HOST_X86)
        #define _HOST_MACHINE_TYPE IMAGE_FILE_MACHINE_I386
      #elif defined(HOST_AMD64)
        #define _HOST_MACHINE_TYPE IMAGE_FILE_MACHINE_AMD64
      #elif defined(HOST_ARM)
        #define _HOST_MACHINE_TYPE IMAGE_FILE_MACHINE_ARMNT
      #elif defined(HOST_ARM64)
        #define _HOST_MACHINE_TYPE IMAGE_FILE_MACHINE_ARM64
      #endif

            // if this is windows, and if host_arch matches target arch then we can fallback to searching for CLRDEBUGINFO on failure
            if (FAILED(hrGetResource) && (imageFileMachine == _HOST_MACHINE_TYPE))
            {
                hrGetResource = GetResourceRvaFromResourceSectionRvaByName(pDataTarget, moduleBaseAddress, resourceSectionRVA, 10, W("CLRDEBUGINFO"), 0, &debugResourceRVA, &debugResourceSize);
            }

      #undef _HOST_MACHINE_TYPE
    #endif
            // if the search failed, we don't recognize the CLR
            if (FAILED(hrGetResource))
            {
                hr = CORDBG_E_NOT_CLR;
            }
        }

        CLR_DEBUG_RESOURCE debugResource = {};
        if (SUCCEEDED(hr) && debugResourceSize != sizeof(debugResource))
        {
            hr = CORDBG_E_NOT_CLR;
        }

        // Get the special debug resource from the image and return the results
        if (SUCCEEDED(hr))
        {
            hr = ReadFromDataTarget(pDataTarget, moduleBaseAddress + debugResourceRVA, (BYTE*)&debugResource, sizeof(debugResource));
        }
        if (SUCCEEDED(hr) && (debugResource.dwVersion != 0))
        {
            hr = CORDBG_E_NOT_CLR;
        }

        // The signature needs to match m_skuId exactly, except for m_skuId=CLR_ID_ONECORE_CLR which is
        // also compatible with the older CLR_ID_PHONE_CLR signature.
        if (SUCCEEDED(hr) && (debugResource.signature != m_skuId) && !( (debugResource.signature == CLR_ID_PHONE_CLR) && (m_skuId == CLR_ID_ONECORE_CLR) ))
        {
            hr = CORDBG_E_NOT_CLR;
        }

        if (SUCCEEDED(hr) && (debugResource.signature != CLR_ID_ONECORE_CLR) && useCrossPlatformNaming)
        {
            FormatLongDacModuleName(clrInfo.DacName, MAX_PATH_FNAME, imageFileMachine, &fixedFileInfo);
            swprintf_s(clrInfo.DbiName, MAX_PATH_FNAME, W("%s_%s.dll"), MAIN_DBI_MODULE_NAME_W, W("x86"));
        }
        else
        {
            if(m_skuId == CLR_ID_V4_DESKTOP)
                swprintf_s(clrInfo.DacName, MAX_PATH_FNAME, W("%s.dll"), CLR_DAC_MODULE_NAME_W);
            else
                swprintf_s(clrInfo.DacName, MAX_PATH_FNAME, W("%s.dll"), CORECLR_DAC_MODULE_NAME_W);
            swprintf_s(clrInfo.DbiName, MAX_PATH_FNAME, W("%s.dll"), MAIN_DBI_MODULE_NAME_W);
        }

        if (SUCCEEDED(hr))
        {
            clrInfo.IndexType = LIBRARY_PROVIDER_INDEX_TYPE::Identity;
            clrInfo.DbiTimeStamp = debugResource.dwDbiTimeStamp;
            clrInfo.DbiSizeOfImage = debugResource.dwDbiSizeOfImage;
            clrInfo.DacTimeStamp = debugResource.dwDacTimeStamp;
            clrInfo.DacSizeOfImage = debugResource.dwDacSizeOfImage;
        }

        // any failure should be interpreted as this module not being a CLR
        if (FAILED(hr))
        {
            return CORDBG_E_NOT_CLR;
        }
        else
        {
            return S_OK;
        }
    }
    else
#endif // !HOST_WINDOWS
    {
        clrInfo.WindowsTarget = FALSE;

        //
        // Check if it is a single-file app
        //
        uint64_t symbolAddress;
        if (TryGetSymbol(pDataTarget, moduleBaseAddress, RUNTIME_INFO_SIGNATURE, &symbolAddress))
        {
            RuntimeInfo runtimeInfo;
            ULONG32 bytesRead;
            if (SUCCEEDED(pDataTarget->ReadVirtual(symbolAddress, (BYTE*)&runtimeInfo, sizeof(RuntimeInfo), &bytesRead)))
            {
                if (strcmp(runtimeInfo.Signature, RUNTIME_INFO_SIGNATURE) == 0)
                {
                    // This is a single-file app
                    clrInfo.IndexType = LIBRARY_PROVIDER_INDEX_TYPE::Identity;

                    // The first byte is the number of bytes in the index
                    clrInfo.DbiBuildIdSize = runtimeInfo.DbiModuleIndex[0];
                    memcpy_s(&clrInfo.DbiBuildId, sizeof(clrInfo.DbiBuildId), &(runtimeInfo.DbiModuleIndex[1]), clrInfo.DbiBuildIdSize);

                    clrInfo.DacBuildIdSize = runtimeInfo.DacModuleIndex[0];
                    memcpy_s(&clrInfo.DacBuildId, sizeof(clrInfo.DacBuildId), &(runtimeInfo.DacModuleIndex[1]), clrInfo.DacBuildIdSize);
                }
            }
        }

        //
        // If it wasn't a single-file app, then fallback to getting the runtime module's index information
        //
        if (!clrInfo.IsValid())
        {
            if (TryGetBuildId(pDataTarget, moduleBaseAddress, clrInfo.RuntimeBuildId, MAX_BUILDID_SIZE, &clrInfo.RuntimeBuildIdSize))
            {
                // This is normal non-single-file app
                clrInfo.IndexType = LIBRARY_PROVIDER_INDEX_TYPE::Runtime;
            }
        }

        return S_OK;
    }
}

// Formats the long name for DAC
HRESULT CLRDebuggingImpl::FormatLongDacModuleName(_Inout_updates_z_(cchBuffer) WCHAR * pBuffer,
                                                  DWORD cchBuffer,
                                                  DWORD targetImageFileMachine,
                                                  VS_FIXEDFILEINFO * pVersion)
{

#ifndef HOST_WINDOWS
    _ASSERTE(!"NYI");
    return E_NOTIMPL;
#endif

#if defined(HOST_X86)
    const WCHAR* pHostArch = W("x86");
#elif defined(HOST_AMD64)
    const WCHAR* pHostArch = W("amd64");
#elif defined(HOST_ARM)
    const WCHAR* pHostArch = W("arm");
#elif defined(HOST_ARM64)
    const WCHAR* pHostArch = W("arm64");
#elif defined(HOST_RISCV64)
    const WCHAR* pHostArch = W("riscv64");
#elif defined(HOST_LOONGARCH64)
    const WCHAR* pHostArch = W("loongarch64");
#else
    _ASSERTE(!"Unknown host arch");
    return E_NOTIMPL;
#endif

    const WCHAR* pDacBaseName = NULL;
    if (m_skuId == CLR_ID_V4_DESKTOP)
        pDacBaseName = CLR_DAC_MODULE_NAME_W;
    else if (m_skuId == CLR_ID_CORECLR || m_skuId == CLR_ID_PHONE_CLR || m_skuId == CLR_ID_ONECORE_CLR)
        pDacBaseName = CORECLR_DAC_MODULE_NAME_W;
    else
    {
        _ASSERTE(!"Unknown SKU id");
        return E_UNEXPECTED;
    }

    _ASSERTE(targetImageFileMachine != IMAGE_FILE_MACHINE_ARM64X && targetImageFileMachine != IMAGE_FILE_MACHINE_ARM64EC);

    const WCHAR* pTargetArch = NULL;
    if (targetImageFileMachine == IMAGE_FILE_MACHINE_I386)
    {
        pTargetArch = W("x86");
    }
    else if (targetImageFileMachine == IMAGE_FILE_MACHINE_AMD64)
    {
        pTargetArch = W("amd64");
    }
    else if (targetImageFileMachine == IMAGE_FILE_MACHINE_ARMNT)
    {
        pTargetArch = W("arm");
    }
    else if (targetImageFileMachine == IMAGE_FILE_MACHINE_ARM64)
    {
        pTargetArch = W("arm64");
    }
    else if (targetImageFileMachine == IMAGE_FILE_MACHINE_RISCV64)
    {
        pTargetArch = W("riscv64");
    }
    else if (targetImageFileMachine == IMAGE_FILE_MACHINE_LOONGARCH64)
    {
        pTargetArch = W("loongarch64");
    }
    else
    {
        _ASSERTE(!"Unknown target image file machine type");
        return E_INVALIDARG;
    }

    const WCHAR* pBuildFlavor = W("");
    if (pVersion->dwFileFlags & VS_FF_DEBUG)
    {
        if (pVersion->dwFileFlags & VS_FF_SPECIALBUILD)
            pBuildFlavor = W(".dbg");
        else
            pBuildFlavor = W(".chk");
    }

    // WARNING: if you change the formatting make sure you recalculate the maximum
    // possible size string and verify callers pass a big enough buffer. This doesn't
    // have to be a tight estimate, just make sure its >= the biggest possible DAC name
    // and it can be calculated statically
    DWORD minCchBuffer =
        (DWORD) u16_strlen(CLR_DAC_MODULE_NAME_W) + (DWORD) u16_strlen(CORECLR_DAC_MODULE_NAME_W) + // max name
        10 + // max host arch
        10 + // max target arch
        40 + // max version
        10 + // max build flavor
        (DWORD) u16_strlen(W("name_host_target_version.flavor.dll")) + // max intermediate formatting chars
        1; // null terminator

    // validate the output buffer is larger than our estimate above
    _ASSERTE(cchBuffer >= minCchBuffer);
    if (!(cchBuffer >= minCchBuffer)) return E_INVALIDARG;

    swprintf_s(pBuffer, cchBuffer, W("%s_%s_%s_%u.%u.%u.%02u%s.dll"),
        pDacBaseName,
        pHostArch,
        pTargetArch,
        pVersion->dwProductVersionMS >> 16,
        pVersion->dwProductVersionMS & 0xFFFF,
        pVersion->dwProductVersionLS >> 16,
        pVersion->dwProductVersionLS & 0xFFFF,
        pBuildFlavor);
    return S_OK;
}

// An implementation of ICLRDebugging::CanUnloadNow
//
// Arguments:
//   hModule - a handle to a module provided earlier by ProvideLibrary
//
// Returns:
//   S_OK if the library is no longer in use and can be unloaded, S_FALSE otherwise
//
STDMETHODIMP CLRDebuggingImpl::CanUnloadNow(HMODULE hModule)
{
    // In V4 at least we don't support any unloading.
    HRESULT hr = S_FALSE;

    return hr;
}

static bool GetCurrentModulePath(SString& modulePath)
{
    modulePath.Clear();

#ifdef HOST_WINDOWS
    HMODULE hModule = NULL;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            (LPCWSTR)&GetCurrentModulePath,
            &hModule))
    {
        return false;
    }

    if (WszGetModuleFileName(hModule, modulePath) == 0)
    {
        return false;
    }
#else
    Dl_info info;
    if (dladdr((void*)&GetCurrentModulePath, &info) == 0 || info.dli_fname == NULL)
    {
        return false;
    }
    modulePath.SetUTF8((const UTF8*)info.dli_fname);
#endif

    return true;
}

// Returns an override path or a path to a library bundled next to dbgshim.
static bool GetBundledLibraryPath(
    const WCHAR* overrideName,
    const WCHAR* libraryName,
    SString& libraryPath)
{
    libraryPath.Clear();

    SString overridePath;
    if (WszGetEnvironmentVariable(overrideName, overridePath) > 0)
    {
        libraryPath.Set(overridePath);
        return true;
    }

    SString modulePath;
    if (!GetCurrentModulePath(modulePath))
    {
        return false;
    }

    SString::Iterator lastSeparator = modulePath.End();
    if (!modulePath.FindBack(lastSeparator, DIRECTORY_SEPARATOR_CHAR_W))
    {
        return false;
    }
    lastSeparator++;
    modulePath.Truncate(lastSeparator);
    modulePath.Append(libraryName);

    libraryPath.Set(modulePath);
    return true;
}

static bool GetDbiPath(SString& dbiPath)
{
    return GetBundledLibraryPath(
        W("DOTNET_DBI_PATH"),
        MAKEDLLNAME_W(MAIN_DBI_MODULE_NAME_W),
        dbiPath);
}

static bool GetCDacPath(SString& cdacPath)
{
    return GetBundledLibraryPath(
        W("DOTNET_CDAC_PATH"),
        MAKEDLLNAME_W(CORECLR_CDAC_MODULE_NAME_W),
        cdacPath);
}

// Loads the given DAC/cDAC module and creates the requested data-access interface from it by
// calling its CLRDataCreateInstance export. The module is intentionally left resident.
static HRESULT DacCreateInstance(
    const WCHAR* dacModulePath,
    IUnknown* pDataTarget,
    REFIID riid,
    IUnknown** ppInstance)
{
    HMODULE hDac = LoadLibraryW(dacModulePath);
    if (hDac == NULL)
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    PFN_CLRDataCreateInstance pfnCLRDataCreateInstance =
        (PFN_CLRDataCreateInstance)GetProcAddress(hDac, "CLRDataCreateInstance");
    if (pfnCLRDataCreateInstance == NULL)
    {
        // Leave the module resident (never unload the cDAC); report the missing export.
        return CORDBG_E_MISSING_DEBUGGER_EXPORTS;
    }

    // CLRDataCreateInstance takes an ICLRDataTarget; the cDAC additionally queries the same object
    // for ICLRContractLocator to find the target's contract descriptor.
    ICLRDataTarget* pDacDataTarget = NULL;
    HRESULT hr = pDataTarget->QueryInterface(__uuidof(ICLRDataTarget), (void**)&pDacDataTarget);
    if (FAILED(hr))
    {
        return CORDBG_E_MISSING_DATA_TARGET_INTERFACE;
    }

    hr = pfnCLRDataCreateInstance(riid, pDacDataTarget, (void**)ppInstance);
    pDacDataTarget->Release();
    return hr;
}

HRESULT CLRDebuggingImpl::OpenDataAccessProcess(
    ULONG64 moduleBaseAddress,
    IUnknown* pDataTarget,
    IUnknown* pLibraryProvider,
    REFIID riid,
    IUnknown** ppInstance)
{
    if (pDataTarget == NULL || ppInstance == NULL)
    {
        return E_INVALIDARG;
    }
    *ppInstance = NULL;

    // Snapshot the policy once so a concurrent SetCDacLoadPolicy cannot change our decisions mid-call.
    CDacLoadPolicy policy = m_cdacLoadPolicy;

    HRESULT cdacHr = E_FAIL;
    bool cdacEvaluated = policy != CDacLoadPolicy_LegacyDacOnly;
    if (cdacEvaluated)
    {
        SString cdacPath;
        if (GetCDacPath(cdacPath))
        {
            cdacHr = DacCreateInstance(cdacPath.GetUnicode(), pDataTarget, riid, ppInstance);
        }
        if (FAILED(cdacHr))
        {
            *ppInstance = NULL;
        }
    }

    HRESULT fallbackHr = E_POINTER;
    bool fallbackEvaluated =
        FAILED(cdacHr) &&
        pLibraryProvider != NULL &&
        policy != CDacLoadPolicy_CDacOnly;
    if (fallbackEvaluated)
    {
        ReleaseHolder<ICorDebugDataTarget> pDt;
        fallbackHr = pDataTarget->QueryInterface(__uuidof(ICorDebugDataTarget), (void**)&pDt);
        if (FAILED(fallbackHr))
        {
            ReleaseHolder<ICLRDataTarget> pLegacyTarget;
            fallbackHr = pDataTarget->QueryInterface(__uuidof(ICLRDataTarget), (void**)&pLegacyTarget);
            if (FAILED(fallbackHr))
            {
                fallbackHr = CORDBG_E_MISSING_DATA_TARGET_INTERFACE;
            }
            else
            {
                ICorDebugDataTarget* pAdapter = NULL;
                fallbackHr = CreateCordbDataTargetFromClrDataTarget(
                    moduleBaseAddress,
                    pLegacyTarget,
                    &pAdapter);
                pDt = pAdapter;
            }
        }

        if (SUCCEEDED(fallbackHr))
        {
            ClrInfo clrInfo;
            CLR_DEBUGGING_VERSION version = {};
            fallbackHr = GetCLRInfo(pDt, moduleBaseAddress, &version, clrInfo);
            if (SUCCEEDED(fallbackHr))
            {
                SString dacModulePath;
                fallbackHr = ResolveDacPath(clrInfo, pLibraryProvider, dacModulePath);
                if (SUCCEEDED(fallbackHr))
                {
                    fallbackHr = DacCreateInstance(
                        dacModulePath.GetUnicode(),
                        pDataTarget,
                        riid,
                        ppInstance);
                }
            }
        }

        if (FAILED(fallbackHr))
        {
            *ppInstance = NULL;
        }
    }

    return SelectActivationResult(cdacHr, fallbackHr, cdacEvaluated);
}

static HRESULT ResolveDacPath(
    ClrInfo& clrInfo,
    IUnknown* pLibraryProvider,
    SString& dacModulePath)
{
    return ResolveLibraries(
        clrInfo,
        pLibraryProvider,
        /*request*/ LibraryRequest::DacOnly,
        /*pDbiModulePath*/ NULL,
        &dacModulePath,
        /*phDbi*/ NULL,
        /*phDac*/ NULL);
}

// Data-access interfaces OpenVirtualProcess services directly from the DAC/cDAC (bypassing DBI).
static bool IsDataAccessInterface(REFIID riid)
{
    // IID_IXCLRDataProcess {5c552ab6-fc09-4cb3-8e36-22fa03c798b7}
    static const GUID IID_IXCLRDataProcess_Local =
        { 0x5c552ab6, 0xfc09, 0x4cb3, { 0x8e, 0x36, 0x22, 0xfa, 0x03, 0xc7, 0x98, 0xb7 } };

    // IID_ISOSDacInterface {436f00f2-b42a-4b9f-870c-e73db66ae930}
    static const GUID IID_ISOSDacInterface_Local =
        { 0x436f00f2, 0xb42a, 0x4b9f, { 0x87, 0x0c, 0xe7, 0x3d, 0xb6, 0x6a, 0xe9, 0x30 } };

    return riid == IID_IXCLRDataProcess_Local || riid == IID_ISOSDacInterface_Local;
}

STDMETHODIMP CLRDebuggingImpl::SetCDacLoadPolicy(DWORD policy)
{
    if (policy > CDacLoadPolicy_LegacyDacOnly)
    {
        return E_INVALIDARG;
    }
    m_cdacLoadPolicy = (CDacLoadPolicy)policy;
    return S_OK;
}

STDMETHODIMP CLRDebuggingImpl::GetCDacLoadPolicy(DWORD* pPolicy)
{
    if (pPolicy == NULL)
    {
        return E_POINTER;
    }
    *pPolicy = (DWORD)m_cdacLoadPolicy;
    return S_OK;
}


STDMETHODIMP CLRDebuggingImpl::QueryInterface(REFIID riid, void **ppvObject)
{
    HRESULT hr = S_OK;

    if (riid == __uuidof(IUnknown))
    {
        IUnknown *pItf = static_cast<IUnknown *>(static_cast<ICLRDebugging *>(this));
        pItf->AddRef();
        *ppvObject = pItf;
    }
    else if (riid == __uuidof(ICLRDebugging))
    {
        ICLRDebugging *pItf = static_cast<ICLRDebugging *>(this);
        pItf->AddRef();
        *ppvObject = pItf;
    }
    else if (riid == __uuidof(ICLRDebuggingPolicy))
    {
        ICLRDebuggingPolicy *pItf = static_cast<ICLRDebuggingPolicy *>(this);
        pItf->AddRef();
        *ppvObject = pItf;
    }
    else
        hr = E_NOINTERFACE;

    return hr;
}

// Standard AddRef implementation
ULONG CLRDebuggingImpl::AddRef()
{
    return InterlockedIncrement(&m_cRef);
}

// Standard Release implementation.
ULONG CLRDebuggingImpl::Release()
{
    _ASSERTE(m_cRef > 0);

    ULONG cRef = InterlockedDecrement(&m_cRef);

    if (cRef == 0)
        delete this; // Relies on virtual dtor to work properly.

    return cRef;
}
