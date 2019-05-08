// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// ==++==
// 
 
// 
// ==--==
#ifndef __hostcoreclr_h__
#define __hostcoreclr_h__

struct SymbolModuleInfo;

typedef void (*OutputDelegate)(const char*);
typedef  int (*ReadMemoryDelegate)(ULONG64, uint8_t*, int);
typedef void (*SymbolFileCallbackDelegate)(void*, const char* moduleFileName, const char* symbolFilePath);

typedef  BOOL (*InitializeSymbolStoreDelegate)(BOOL, BOOL, BOOL, const char*, const char*, const char*);
typedef  void (*DisplaySymbolStoreDelegate)();
typedef  void (*DisableSymbolStoreDelegate)();
typedef  void (*LoadNativeSymbolsDelegate)(SymbolFileCallbackDelegate, void*, const char*, const char*, ULONG64, int, ReadMemoryDelegate);
typedef  PVOID (*LoadSymbolsForModuleDelegate)(const char*, BOOL, ULONG64, int, ULONG64, int, ReadMemoryDelegate);
typedef  void (*DisposeDelegate)(PVOID);
typedef  BOOL (*ResolveSequencePointDelegate)(PVOID, const char*, unsigned int, unsigned int*, unsigned int*);
typedef  BOOL (*GetLocalVariableNameDelegate)(PVOID, int, int, BSTR*);
typedef  BOOL (*GetLineByILOffsetDelegate)(PVOID, mdMethodDef, ULONG64, ULONG *, BSTR*);

typedef  BOOL (*GetMetadataLocatorDelegate)(
    LPCWSTR imagePath,
    unsigned int imageTimestamp,
    unsigned int imageSize,
    GUID* mvid,
    unsigned int mdRva,
    unsigned int flags,
    unsigned int bufferSize,
    PVOID pMetadata,
    unsigned int* pMetadataSize
);

struct SOSNetCoreCallbacks
{
    InitializeSymbolStoreDelegate InitializeSymbolStoreDelegate;
    DisplaySymbolStoreDelegate DisplaySymbolStoreDelegate;
    DisableSymbolStoreDelegate DisableSymbolStoreDelegate;
    LoadNativeSymbolsDelegate LoadNativeSymbolsDelegate;
    LoadSymbolsForModuleDelegate LoadSymbolsForModuleDelegate;
    DisposeDelegate DisposeDelegate;
    ResolveSequencePointDelegate ResolveSequencePointDelegate;
    GetLineByILOffsetDelegate GetLineByILOffsetDelegate;
    GetLocalVariableNameDelegate GetLocalVariableNameDelegate;
    GetMetadataLocatorDelegate GetMetadataLocatorDelegate;
};

static const char *SOSManagedDllName = "SOS.NETCore";
static const char *SymbolReaderClassName = "SOS.SymbolReader";
static const char *MetadataHelperClassName = "SOS.MetadataHelper";

extern HMODULE g_hInstance;
extern LPCSTR g_hostRuntimeDirectory;
extern SOSNetCoreCallbacks g_SOSNetCoreCallbacks;

extern LPCSTR GetDacFilePath();
extern LPCSTR GetDbiFilePath();
extern BOOL IsHostingInitialized();
extern HRESULT InitializeHosting();
extern HRESULT InitializeSymbolStore(BOOL logging, BOOL msdl, BOOL symweb, const char* symbolServer, const char* cacheDirectory);
extern void InitializeSymbolStore();
extern HRESULT LoadNativeSymbols(bool runtimeOnly = false);
extern void DisplaySymbolStore();
extern void DisableSymbolStore();

class SymbolReader
{
private:
#ifndef FEATURE_PAL
    ISymUnmanagedReader* m_pSymReader;
#endif
    PVOID m_symbolReaderHandle;

    HRESULT GetNamedLocalVariable(___in ISymUnmanagedScope* pScope, ___in ICorDebugILFrame* pILFrame, ___in mdMethodDef methodToken, ___in ULONG localIndex, 
        __out_ecount(paramNameLen) WCHAR* paramName, ___in ULONG paramNameLen, ___out ICorDebugValue** ppValue);
    HRESULT LoadSymbolsForWindowsPDB(___in IMetaDataImport* pMD, ___in ULONG64 peAddress, __in_z WCHAR* pModuleName, ___in BOOL isFileLayout);
    HRESULT LoadSymbolsForPortablePDB(__in_z WCHAR* pModuleName, ___in BOOL isInMemory, ___in BOOL isFileLayout, ___in ULONG64 peAddress, ___in ULONG64 peSize, 
        ___in ULONG64 inMemoryPdbAddress, ___in ULONG64 inMemoryPdbSize);

public:
    SymbolReader()
    {
#ifndef FEATURE_PAL
        m_pSymReader = NULL;
#endif
        m_symbolReaderHandle = 0;
    }

    ~SymbolReader()
    {
#ifndef FEATURE_PAL
        if(m_pSymReader != NULL)
        {
            m_pSymReader->Release();
            m_pSymReader = NULL;
        }
#endif
        if (m_symbolReaderHandle != 0)
        {
            g_SOSNetCoreCallbacks.DisposeDelegate(m_symbolReaderHandle);
            m_symbolReaderHandle = 0;
        }
    }

    HRESULT LoadSymbols(___in IMetaDataImport* pMD, ___in ICorDebugModule* pModule);
    HRESULT LoadSymbols(___in IMetaDataImport* pMD, ___in IXCLRDataModule* pModule);
    HRESULT GetLineByILOffset(___in mdMethodDef MethodToken, ___in ULONG64 IlOffset, ___out ULONG *pLinenum, __out_ecount(cchFileName) WCHAR* pwszFileName, ___in ULONG cchFileName);
    HRESULT GetNamedLocalVariable(___in ICorDebugFrame * pFrame, ___in ULONG localIndex, __out_ecount(paramNameLen) WCHAR* paramName, ___in ULONG paramNameLen, ___out ICorDebugValue** ppValue);
    HRESULT ResolveSequencePoint(__in_z WCHAR* pFilename, ___in ULONG32 lineNumber, ___out mdMethodDef* ___out pToken, ___out ULONG32* pIlOffset);
};

HRESULT
GetLineByOffset(
        ___in ULONG64 IP,
        ___out ULONG *pLinenum,
        __out_ecount(cchFileName) WCHAR* pwszFileName,
        ___in ULONG cchFileName);

#endif // __hostcoreclr_h__
