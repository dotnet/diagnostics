// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

//----------------------------------------------------------------------------
//
// LLDB debugger services for sos
//
//----------------------------------------------------------------------------

#ifndef __SOSHOSTSERVICES_H__
#define __SOSHOSTSERVICES_H__

#include <stdarg.h>
#include <unknwn.h>

#ifdef __cplusplus
extern "C" {
#endif

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

#define SOSNetCoreCallbacksVersion 2

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

MIDL_INTERFACE("D13608FB-AD14-4B49-990A-80284F934C41")
ISOSHostServices : public IUnknown
{
public:
    //----------------------------------------------------------------------------
    // ISOSHostServices
    //----------------------------------------------------------------------------

    virtual HRESULT GetSOSNETCoreCallbacks(
        int version,
        SOSNetCoreCallbacks* callbacks) = 0;
};

#ifdef __cplusplus
};
#endif

#endif // #ifndef __SOSHOSTSERVICES_H__
