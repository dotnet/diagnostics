// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//*****************************************************************************
// debugshim.h
//

//
//*****************************************************************************

#ifndef _DEBUG_SHIM_
#define _DEBUG_SHIM_

#include "cor.h"
#include "cordebug.h"
#include "sstring.h"
#include <wchar.h>
#include <metahost.h>
#include <dn-u16.h>
#include "runtimeinfo.h"

#if defined (HOST_WINDOWS) && defined(HOST_X86)
#define CLRDEBUGINFO_RESOURCE_NAME W("CLRDEBUGINFOWINDOWSX86")
#endif
#if !defined (HOST_WINDOWS) && defined(HOST_X86)
#define CLRDEBUGINFO_RESOURCE_NAME W("CLRDEBUGINFOCORESYSX86")
#endif
#if defined (HOST_WINDOWS) && defined(HOST_AMD64)
#define CLRDEBUGINFO_RESOURCE_NAME W("CLRDEBUGINFOWINDOWSAMD64")
#endif
#if !defined (HOST_WINDOWS) && defined(HOST_AMD64)
#define CLRDEBUGINFO_RESOURCE_NAME W("CLRDEBUGINFOCORESYSAMD64")
#endif
#if defined (HOST_WINDOWS) && defined(HOST_ARM64)
#define CLRDEBUGINFO_RESOURCE_NAME W("CLRDEBUGINFOWINDOWSARM64")
#endif
#if !defined (HOST_WINDOWS) && defined(HOST_ARM64)
#define CLRDEBUGINFO_RESOURCE_NAME W("CLRDEBUGINFOCORESYSARM64")
#endif
#if defined (HOST_WINDOWS) && defined(HOST_ARM)
#define CLRDEBUGINFO_RESOURCE_NAME W("CLRDEBUGINFOWINDOWSARM")
#endif
#if !defined (HOST_WINDOWS) && defined(HOST_ARM)
#define CLRDEBUGINFO_RESOURCE_NAME W("CLRDEBUGINFOCORESYSARM")
#endif

#define CORECLR_DAC_MODULE_NAME_W W("mscordaccore")
#define CLR_DAC_MODULE_NAME_W W("mscordacwks")
#define MAIN_DBI_MODULE_NAME_W W("mscordbi")

// The cDAC (contract-based data access) is bundled next to dbgshim and is never downloaded.
#define CORECLR_CDAC_MODULE_NAME_W W("mscordaccore_universal")

// Controls how OpenVirtualProcess locates the data-access layer when a data-access interface
// (for example IXCLRDataProcess) is requested. Mirrors the diagnostics CDacLoadPolicy.
enum CDacLoadPolicy
{
    // Prefer the co-located cDAC; fall back to the legacy DAC via the library provider. (default)
    CDacLoadPolicy_PreferCDac = 0,
    // Use only the cDAC; do not fall back to the legacy DAC.
    CDacLoadPolicy_CDacOnly = 1,
    // Use only the legacy DAC; do not try the cDAC.
    CDacLoadPolicy_LegacyDacOnly = 2,
};

// A small dbgshim-owned control interface, implemented by the same object as ICLRDebugging, that
// lets a consumer attach a cDAC load policy to the debugging object before requesting a data-access
// interface from OpenVirtualProcess. ICLRDebugging itself is a frozen published interface, so the
// policy is carried on this sibling interface instead of being added there.
// {2D3B4F6A-1C7E-4B2A-9E5D-7F1A6C0B8D34}
MIDL_INTERFACE("2D3B4F6A-1C7E-4B2A-9E5D-7F1A6C0B8D34")
ICLRDebuggingDataAccessControl : public IUnknown
{
public:
    // policy is a CDacLoadPolicy value.
    virtual HRESULT STDMETHODCALLTYPE SetCDacLoadPolicy(DWORD policy) = 0;
    virtual HRESULT STDMETHODCALLTYPE GetCDacLoadPolicy(DWORD* pPolicy) = 0;
};

#define MAX_BUILDID_SIZE 24

// The format of the special debugging resource we embed in CLRs starting in v4
struct CLR_DEBUG_RESOURCE
{
    DWORD dwVersion;
    GUID signature;
    DWORD dwDacTimeStamp;
    DWORD dwDacSizeOfImage;
    DWORD dwDbiTimeStamp;
    DWORD dwDbiSizeOfImage;
};

struct ClrInfo
{
    BOOL WindowsTarget;
    LIBRARY_PROVIDER_INDEX_TYPE IndexType;

    SString RuntimeModulePath;
    BYTE RuntimeBuildId[MAX_BUILDID_SIZE];
    ULONG RuntimeBuildIdSize;

    DWORD DbiTimeStamp;
    DWORD DbiSizeOfImage;
    BYTE DbiBuildId[MAX_BUILDID_SIZE];
    ULONG DbiBuildIdSize;
    WCHAR DbiName[MAX_PATH_FNAME];

    DWORD DacTimeStamp;
    DWORD DacSizeOfImage;
    BYTE DacBuildId[MAX_BUILDID_SIZE];
    ULONG DacBuildIdSize;
    WCHAR DacName[MAX_PATH_FNAME];

    ClrInfo()
    {
#ifdef HOST_UNIX 
        WindowsTarget = FALSE;
#else
        WindowsTarget = TRUE;
#endif
        IndexType = LIBRARY_PROVIDER_INDEX_TYPE::UnknownIndex; 

        memset(&RuntimeBuildId, 0, sizeof(RuntimeBuildId));
        RuntimeBuildIdSize = 0;

        DbiTimeStamp = 0;
        DbiSizeOfImage = 0;
        memset(&DbiBuildId, 0, sizeof(DbiBuildId));
        DbiBuildIdSize = 0;

        DacTimeStamp = 0;
        DacSizeOfImage = 0;;
        memset(&DacBuildId, 0, sizeof(DacBuildId));
        DacBuildIdSize = 0;

        swprintf_s(DbiName, MAX_PATH_FNAME, W("%s"), MAKEDLLNAME_W(MAIN_DBI_MODULE_NAME_W));
        swprintf_s(DacName, MAX_PATH_FNAME, W("%s"), MAKEDLLNAME_W(CORECLR_DAC_MODULE_NAME_W));
    }

    bool IsValid()
    {
        if (IndexType == LIBRARY_PROVIDER_INDEX_TYPE::Identity)
        {
            if (WindowsTarget)
            {
                return DbiTimeStamp != 0 && DbiSizeOfImage != 0 && DacTimeStamp != 0 && DacSizeOfImage;
            }
            else 
            {
                return DbiBuildIdSize > 0 && DacBuildIdSize > 0;
            }
        }
        else if (IndexType == LIBRARY_PROVIDER_INDEX_TYPE::Runtime)
        {
            // The runtime index info should never be needed or provided on Windows
            if (!WindowsTarget)
            {
                return RuntimeBuildIdSize > 0;
            }
        }
        return false;
    }

    // Like IsValid, but only requires the DAC index to be present. Used by the data-access path,
    // which resolves only the DAC and never needs the DBI index.
    bool IsDacValid()
    {
        if (IndexType == LIBRARY_PROVIDER_INDEX_TYPE::Identity)
        {
            if (WindowsTarget)
            {
                return DacTimeStamp != 0 && DacSizeOfImage != 0;
            }
            else
            {
                return DacBuildIdSize > 0;
            }
        }
        else if (IndexType == LIBRARY_PROVIDER_INDEX_TYPE::Runtime)
        {
            if (!WindowsTarget)
            {
                return RuntimeBuildIdSize > 0;
            }
        }
        return false;
    }
};

extern "C" bool TryGetSymbol(ICorDebugDataTarget* dataTarget, uint64_t baseAddress, const char* symbolName, uint64_t* symbolAddress);
extern "C" bool TryGetBuildId(ICorDebugDataTarget* dataTarget, uint64_t baseAddress, BYTE* buffer, ULONG bufferSize, PULONG pBuildIdSize);
#ifdef TARGET_UNIX
extern "C" bool TryReadSymbolFromFile(const WCHAR* modulePath, const char* symbolName, BYTE* buffer, ULONG32 size);
extern "C" bool TryGetBuildIdFromFile(const WCHAR* modulePath, BYTE* buffer, ULONG bufferSize, PULONG pBuildSize);
#endif

// forward declaration
struct ICorDebugDataTarget;

// ICLRDebugging implementation.
class CLRDebuggingImpl : public ICLRDebugging, public ICLRDebuggingDataAccessControl
{

public:
    CLRDebuggingImpl(GUID skuId) : m_cRef(0), m_skuId(skuId), m_cdacLoadPolicy(CDacLoadPolicy_PreferCDac)
    {
    }

    virtual ~CLRDebuggingImpl() {}

public:
    // ICLRDebugging methods:
    STDMETHOD(OpenVirtualProcess(
        ULONG64 moduleBaseAddress,
        IUnknown * pDataTarget,
        ICLRDebuggingLibraryProvider * pLibraryProvider,
        CLR_DEBUGGING_VERSION * pMaxDebuggerSupportedVersion,
        REFIID riidProcess,
        IUnknown ** ppProcess,
        CLR_DEBUGGING_VERSION * pVersion,
        CLR_DEBUGGING_PROCESS_FLAGS * pFlags));

    STDMETHOD(CanUnloadNow(HMODULE hModule));

    // ICLRDebuggingDataAccessControl methods:
    STDMETHOD(SetCDacLoadPolicy(DWORD policy));
    STDMETHOD(GetCDacLoadPolicy(DWORD* pPolicy));

    // IUnknown methods:
    STDMETHOD(QueryInterface(REFIID riid, void **ppvObject));

    // Standard AddRef implementation
    STDMETHOD_(ULONG, AddRef());

    // Standard Release implementation.
    STDMETHOD_(ULONG, Release());

    // Used by other dbgshim implementation classes to resolve the DBI and DAC.
    static HRESULT ProvideLibraries(ClrInfo& clrInfo,
                                    ICLRDebuggingLibraryProvider3* pLibraryProvider,
                                    SString& dbiModulePath,
                                    SString& dacModulePath);

private:
    // Locates and activates the data-access (IXCLRDataProcess) interface for the runtime module
    // at moduleBaseAddress WITHOUT loading DBI. This is the worker behind an OpenVirtualProcess call
    // that requests a data-access interface (see IsDataAccessInterface). Honors the object's cDAC
    // load policy: prefers the cDAC (mscordaccore_universal) bundled next to dbgshim, and falls back
    // to the DAC located via the library provider unless the policy forbids it. The caller owns the
    // returned interface and may hand it to other consumers (for example ClrMD).
    //
    //   moduleBaseAddress - base address of the runtime module in the target
    //   pDataTarget       - a per-runtime data target (ICLRDataTarget, and ICLRContractLocator for
    //                       the cDAC) over process/dump-wide memory
    //   pLibraryProvider  - ICLRDebuggingLibraryProvider3 used only for the DAC fallback; may be NULL
    //   riid              - the interface to create, typically IID_IXCLRDataProcess
    //   ppInstance        - out: the created interface on success
    HRESULT OpenDataAccessProcess(
        ULONG64 moduleBaseAddress,
        IUnknown* pDataTarget,
        IUnknown* pLibraryProvider,
        REFIID riid,
        IUnknown** ppInstance);

    static HRESULT ProvideLibraries(ClrInfo& clrInfo,
                                    IUnknown* pLibraryProvider,
                                    SString& dbiModulePath,
                                    SString& dacModulePath,
                                    HMODULE* phDbi,
                                    HMODULE* phDac);

    HRESULT GetCLRInfo(ICorDebugDataTarget * pDataTarget,
                       ULONG64 moduleBaseAddress,
                       CLR_DEBUGGING_VERSION* pVersion,
                       ClrInfo& clrInfo);

    HRESULT FormatLongDacModuleName(_Inout_updates_z_(cchBuffer) WCHAR * pBuffer,
                                    DWORD cchBuffer,
                                    DWORD targetImageFileMachine,
                                    VS_FIXEDFILEINFO * pVersion);

	volatile LONG m_cRef;
    GUID m_skuId;
    CDacLoadPolicy m_cdacLoadPolicy;

};  // class CLRDebuggingImpl

#endif
