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
#include "runtimeinfo.h"

#define CORECLR_DAC_MODULE_NAME_W W("mscordaccore")
#define CLR_DAC_MODULE_NAME_W W("mscordacwks")
#define MAIN_DBI_MODULE_NAME_W W("mscordbi")

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
        IndexType = LIBRARY_PROVIDER_INDEX_TYPE::Unknown; 

        memset(&RuntimeBuildId, 0, MAX_BUILDID_SIZE);
        RuntimeBuildIdSize = 0;

        DbiTimeStamp = 0;
        DbiSizeOfImage = 0;
        memset(&DbiBuildId, 0, MAX_BUILDID_SIZE);
        DbiBuildIdSize = 0;

        DacTimeStamp = 0;
        DacSizeOfImage = 0;;
        memset(&DacBuildId, 0, MAX_BUILDID_SIZE);
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
class CLRDebuggingImpl : public ICLRDebugging
{

public:
    CLRDebuggingImpl(GUID skuId) : m_cRef(0), m_skuId(skuId)
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

	// IUnknown methods:
	STDMETHOD(QueryInterface(REFIID riid, void **ppvObject));

	// Standard AddRef implementation
	STDMETHOD_(ULONG, AddRef());

	// Standard Release implementation.
	STDMETHOD_(ULONG, Release());

    static HRESULT ProvideLibraries(ClrInfo& clrInfo,
                                    ICLRDebuggingLibraryProvider3* pLibraryProvider,
                                    SString& dbiModulePath,
                                    SString& dacModulePath);

private:
    static HRESULT ProvideLibraries(ClrInfo& clrInfo,
                                    IUnknown* pLibraryProvider,
                                    SString& dbiModulePath,
                                    SString& dacModulePath,
                                    HMODULE* phDbi,
                                    HMODULE* phDac);

    static VOID RetargetDacIfNeeded(DWORD* pdwTimeStamp,
                                    DWORD* pdwSizeOfImage);

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

};  // class CLRDebuggingImpl

#endif
