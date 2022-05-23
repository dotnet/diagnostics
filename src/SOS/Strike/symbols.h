// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#pragma once

#include "symbolservice.h"

extern HMODULE g_hInstance;

extern HRESULT GetMetadataLocator(
    LPCWSTR imagePath,
    ULONG32 imageTimestamp,
    ULONG32 imageSize,
    GUID* mvid,
    ULONG32 mdRva,
    ULONG32 flags,
    ULONG32 bufferSize,
    BYTE* buffer,
    ULONG32* dataSize);

extern HRESULT GetICorDebugMetadataLocator(
    LPCWSTR imagePath,
    ULONG32 imageTimestamp,
    ULONG32 imageSize,
    ULONG32 cchPathBuffer,
    ULONG32* pcchPathBuffer,
    WCHAR wszPathBuffer[]);

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
            GetSymbolService()->Dispose(m_symbolReaderHandle);
            m_symbolReaderHandle = 0;
        }
    }

    HRESULT LoadSymbols(___in IMetaDataImport* pMD, ___in ICorDebugModule* pModule);
    HRESULT LoadSymbols(___in IMetaDataImport* pMD, ___in IXCLRDataModule* pModule);
    HRESULT GetLineByILOffset(___in mdMethodDef MethodToken, ___in ULONG64 IlOffset, ___out ULONG *pLinenum, __out_ecount(cchFileName) WCHAR* pwszFileName, ___in ULONG cchFileName);
    HRESULT GetNamedLocalVariable(___in ICorDebugFrame * pFrame, ___in ULONG localIndex, __out_ecount(paramNameLen) WCHAR* paramName, ___in ULONG paramNameLen, ___out ICorDebugValue** ppValue);
    HRESULT ResolveSequencePoint(__in_z WCHAR* pFilename, ___in ULONG32 lineNumber, ___out mdMethodDef* pToken, ___out ULONG32* pIlOffset);
};

HRESULT
GetLineByOffset(
    ___in ULONG64 nativeOffset,
    ___out ULONG* pLinenum,
    __out_ecount(cchFileName) WCHAR* pwszFileName,
    ___in ULONG cchFileName,
    ___in BOOL bAdjustOffsetForLineNumber = FALSE);
