// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#include "sos.h"
#include "disasm.h"
#include "dbghelp.h"

#include "corhdr.h"
#include "cor.h"
#include "dacprivate.h"
#include "sospriv.h"
#include "corerror.h"
#include "safemath.h"

#include <psapi.h>
#include <tchar.h>
#include <limits.h>

#ifdef FEATURE_PAL
#include <sys/stat.h>
#include <dlfcn.h>
#include <unistd.h>
#endif // FEATURE_PAL

#include "coreclrhost.h"
#include <set>

#if defined(__APPLE__)
#include <mach-o/dyld.h>
#endif

#ifndef IfFailRet
#define IfFailRet(EXPR) do { Status = (EXPR); if(FAILED(Status)) { return (Status); } } while (0)
#endif

#ifndef FEATURE_PAL
HMODULE g_hmoduleSymBinder = nullptr;
ISymUnmanagedBinder3 *g_pSymBinder = nullptr;
#endif

/**********************************************************************\
 * Called when the managed host or plug-in loads/initializes SOS.
\**********************************************************************/
extern "C" HRESULT STDMETHODCALLTYPE SOSInitializeByHost(IUnknown* punk, IDebuggerServices* debuggerServices)
{
    IHost* host = nullptr;
    HRESULT hr;

    if (punk != nullptr)
    {
        hr = punk->QueryInterface(__uuidof(IHost), (void**)&host);
        if (FAILED(hr)) {
            return hr;
        }
    }
    hr = SOSExtensions::Initialize(host, debuggerServices);
    if (FAILED(hr)) {
        return hr;
    }
#ifndef FEATURE_PAL
    // When SOS is hosted on dotnet-dump on Windows, the ExtensionApis are not set so 
    // the expression evaluation function needs to be supplied.
    if (GetExpression == nullptr)
    {
        GetExpression = ([](const char* message) {
		    ISymbolService* symbolService = GetSymbolService();
		    if (symbolService == nullptr)
		    {
		        return (ULONG64)0;
		    }
            return symbolService->GetExpressionValue(message);
        });
    }
#endif
    return S_OK;
}

/**********************************************************************\
 * Called when the managed host or plug-in exits.
\**********************************************************************/
extern "C" void STDMETHODCALLTYPE SOSUninitializeByHost()
{
    OnUnloadTask::Run();
}

/**********************************************************************\
 * Returns the metadata from a local or downloaded assembly
\**********************************************************************/
HRESULT GetMetadataLocator(
    LPCWSTR imagePath,
    ULONG32 imageTimestamp,
    ULONG32 imageSize,
    GUID* mvid,
    ULONG32 mdRva,
    ULONG32 flags,
    ULONG32 bufferSize,
    BYTE* buffer,
    ULONG32* dataSize)
{
    ISymbolService* symbolService = GetSymbolService();
    if (symbolService == nullptr)
    {
        return E_NOINTERFACE;
    }
    return symbolService->GetMetadataLocator(imagePath, imageTimestamp, imageSize, mvid, mdRva, flags, bufferSize, buffer, dataSize);
}

/**********************************************************************\
 * Returns the metadata from a local or downloaded assembly
\**********************************************************************/
HRESULT GetICorDebugMetadataLocator(
    LPCWSTR imagePath,
    ULONG32 imageTimestamp,
    ULONG32 imageSize,
    ULONG32 cchPathBuffer,
    ULONG32 *pcchPathBuffer,
    WCHAR wszPathBuffer[])
{
    ISymbolService* symbolService = GetSymbolService();
    if (symbolService == nullptr)
    {
        return E_NOINTERFACE;
    }
    return symbolService->GetICorDebugMetadataLocator(imagePath, imageTimestamp, imageSize, cchPathBuffer, pcchPathBuffer, wszPathBuffer);
}

#ifndef FEATURE_PAL

/**********************************************************************\
* A typesafe version of GetProcAddress
\**********************************************************************/
template <typename T>
BOOL GetProcAddressT(PCSTR FunctionName, PCSTR DllName, T* OutFunctionPointer, HMODULE* InOutDllHandle)
{
    _ASSERTE(InOutDllHandle != NULL);
    _ASSERTE(OutFunctionPointer != NULL);

    T FunctionPointer = NULL;
    HMODULE DllHandle = *InOutDllHandle;
    if (DllHandle == NULL)
    {
        DllHandle = LoadLibraryExA(DllName, NULL, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (DllHandle != NULL)
            *InOutDllHandle = DllHandle;
    }
    if (DllHandle != NULL)
    {
        FunctionPointer = (T) GetProcAddress(DllHandle, FunctionName);
    }
    *OutFunctionPointer = FunctionPointer;
    return FunctionPointer != NULL;
}

/**********************************************************************\
* CreateInstanceFromPath() instantiates a COM object using a passed in *
* fully-qualified path and a CLSID.                                    *
\**********************************************************************/
HRESULT CreateInstanceFromPath(REFCLSID clsid, REFIID iid, LPCSTR path, HMODULE* pModuleHandle, void** ppItf)
{
    HRESULT (__stdcall *pfnDllGetClassObject)(REFCLSID rclsid, REFIID riid, LPVOID *ppv) = NULL;
    HRESULT hr = S_OK;

    if (!GetProcAddressT("DllGetClassObject", path, &pfnDllGetClassObject, pModuleHandle)) {
        return REGDB_E_CLASSNOTREG;
    }
    ToRelease<IClassFactory> pFactory;
    if (SUCCEEDED(hr = pfnDllGetClassObject(clsid, IID_IClassFactory, (void**)&pFactory))) {
        if (SUCCEEDED(hr = pFactory->CreateInstance(NULL, iid, ppItf))) {
            return S_OK;
        }
    }
    if (*pModuleHandle != NULL) {
        FreeLibrary(*pModuleHandle);
        *pModuleHandle = NULL;
    }
    return hr;
}

#define PORTABLE_PDB_MINOR_VERSION              20557
#define IMAGE_DEBUG_TYPE_EMBEDDED_PORTABLE_PDB  17

/**********************************************************************\
 * Returns true if the PE image debug directory has a portable PDB
\**********************************************************************/
bool HasPortablePDB(ULONG64 baseAddress)
{
    IMAGE_DOS_HEADER dosHeader;
    if (g_ExtData->ReadVirtual(baseAddress, &dosHeader, sizeof(dosHeader), nullptr) != S_OK) {
        return false;
    }
    WORD magic;
    if (g_ExtData->ReadVirtual((baseAddress + dosHeader.e_lfanew + offsetof(IMAGE_NT_HEADERS, OptionalHeader.Magic)), &magic, sizeof(magic), nullptr) != S_OK) {
        return false;
    }
    ULONG64 debugDirAddr = 0;
    DWORD nSize = 0;
    if (magic == IMAGE_NT_OPTIONAL_HDR32_MAGIC)
    {
        IMAGE_NT_HEADERS32 header32;
        if (g_ExtData->ReadVirtual(baseAddress + dosHeader.e_lfanew, &header32, sizeof(header32), nullptr) != S_OK) {
            return false;
        }
        // If there is no comheader, this can not be managed code so no portable PDB
        if (header32.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_COMHEADER].VirtualAddress == 0) {
            return false;
        }
        // If there is no debug directory, don't know if it is a portable PDB
        if (header32.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG].VirtualAddress == 0) {
            return false;
        }
        debugDirAddr = baseAddress + header32.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG].VirtualAddress;
        nSize = header32.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG].Size;
    }
    else if (magic == IMAGE_NT_OPTIONAL_HDR64_MAGIC)
    {
        IMAGE_NT_HEADERS64 header64;
        if (g_ExtData->ReadVirtual(baseAddress + dosHeader.e_lfanew, &header64, sizeof(header64), nullptr) != S_OK) {
            return false;
        }
        // If there is no comheader, this can not be managed code so no portable PDB
        if (header64.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_COMHEADER].VirtualAddress == 0) {
            return false;
        }
        // If there is no debug directory, don't know if it is a portable PDB
        if (header64.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG].VirtualAddress == 0) {
            return false;
        }
        debugDirAddr = baseAddress + header64.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG].VirtualAddress;
        nSize = header64.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG].Size;
    }
    else {
        return false;
    }
    IMAGE_DEBUG_DIRECTORY debugDir;
    size_t nbytes = 0;
    while (nbytes < nSize) {
        if (g_ExtData->ReadVirtual(debugDirAddr + nbytes, &debugDir, sizeof(debugDir), NULL) != S_OK) {
            return false;
        }
        if ((debugDir.Type == IMAGE_DEBUG_TYPE_CODEVIEW && debugDir.MinorVersion == PORTABLE_PDB_MINOR_VERSION ) || (debugDir.Type == IMAGE_DEBUG_TYPE_EMBEDDED_PORTABLE_PDB)) {
            return true;
        }
        nbytes += sizeof(debugDir);
    }
    return false;
}

#endif // FEATURE_PAL

/**********************************************************************\
 * Load symbols for an ICorDebugModule. Used by "clrstack -i".
\**********************************************************************/
HRESULT SymbolReader::LoadSymbols(___in IMetaDataImport* pMD, ___in ICorDebugModule* pModule)
{
    HRESULT Status = S_OK;

    BOOL isDynamic = FALSE;
    IfFailRet(pModule->IsDynamic(&isDynamic));
    if (isDynamic)
    {
        // Dynamic and in memory assemblies are a special case which we will ignore for now
        ExtWarn("SOS Warning: Loading symbols for dynamic assemblies is not yet supported\n");
        return E_FAIL;
    }

    ULONG64 peAddress = 0;
    IfFailRet(pModule->GetBaseAddress(&peAddress));

    ToRelease<IXCLRDataModule> pClrModule;
    IfFailRet(GetModuleFromAddress(peAddress, &pClrModule));

    return LoadSymbols(pMD, pClrModule);
}

/**********************************************************************\
 * Load symbols for a module.
\**********************************************************************/
HRESULT SymbolReader::LoadSymbols(___in IMetaDataImport* pMD, ___in IXCLRDataModule* pModule)
{
    ULONG32 flags;
    HRESULT hr = pModule->GetFlags(&flags);
    if (FAILED(hr)) 
    {
        ExtOut("LoadSymbols IXCLRDataModule->GetFlags FAILED 0x%08x\n", hr);
        return hr;
    }

    if (flags & CLRDATA_MODULE_IS_DYNAMIC)
    {
        ExtWarn("SOS Warning: Loading symbols for dynamic assemblies is not yet supported\n");
        return E_FAIL;
    }

    ArrayHolder<WCHAR> pModuleName = new WCHAR[MAX_LONGPATH + 1];
    ULONG32 nameLen = 0;
    hr = pModule->GetFileName(MAX_LONGPATH, &nameLen, pModuleName);
    if (FAILED(hr))
    {
        ExtOut("LoadSymbols: IXCLRDataModule->GetFileName FAILED 0x%08x\n", hr);
        return hr;
    }

    DacpGetModuleData moduleData;
    hr = moduleData.Request(pModule);
    if (FAILED(hr))
    {
#ifdef FEATURE_PAL
        ExtOut("LoadSymbols moduleData.Request FAILED 0x%08x\n", hr);
        return hr;
#else
        ULONG64 moduleBase;
        ULONG64 moduleSize;
        hr = GetClrModuleImages(pModule, CLRDATA_MODULE_PE_FILE, &moduleBase, &moduleSize);
        if (FAILED(hr))
        {
            ExtOut("LoadSymbols GetClrModuleImages FAILED 0x%08x\n", hr);
            return hr;
        }
        if (GetSymbolService() == nullptr || !HasPortablePDB(moduleBase))
        {
            hr = LoadSymbolsForWindowsPDB(pMD, moduleBase, pModuleName, FALSE);
            if (SUCCEEDED(hr))
            {
                return hr;
            }
        }
        moduleData.LoadedPEAddress = moduleBase;
        moduleData.LoadedPESize = moduleSize;
        moduleData.IsFileLayout = TRUE;
#endif
    }

#ifndef FEATURE_PAL
    if (GetSymbolService() == nullptr || !HasPortablePDB(moduleData.LoadedPEAddress))
    {
	    // TODO: in-memory windows PDB not supported
        hr = LoadSymbolsForWindowsPDB(pMD, moduleData.LoadedPEAddress, pModuleName, moduleData.IsFileLayout);
        if (SUCCEEDED(hr))
        {
            return hr;
        }
    }
#endif // FEATURE_PAL

    return LoadSymbolsForPortablePDB(
        pModuleName, 
        moduleData.IsInMemory,
        moduleData.IsFileLayout,
        moduleData.LoadedPEAddress,
        moduleData.LoadedPESize, 
        moduleData.InMemoryPdbAddress,
        moduleData.InMemoryPdbSize);
}

#ifndef FEATURE_PAL

static void CleanupSymBinder()
{
    if (g_pSymBinder != nullptr)
    {
        g_pSymBinder->Release();
        g_pSymBinder = nullptr;
    }
    if (g_hmoduleSymBinder != nullptr)
    {
        FreeLibrary(g_hmoduleSymBinder);
        g_hmoduleSymBinder = nullptr;
    }
}

/**********************************************************************\
 * Attempts to load Windows PDBs on Windows.
\**********************************************************************/
HRESULT SymbolReader::LoadSymbolsForWindowsPDB(___in IMetaDataImport* pMD, ___in ULONG64 peAddress, __in_z WCHAR* pModuleName, ___in BOOL isFileLayout)
{
    HRESULT Status = S_OK;

    if (m_pSymReader != NULL) 
        return S_OK;

    if (pMD == nullptr)
        return E_INVALIDARG;

    if (g_pSymBinder == nullptr)
    {
        // Ignore errors to be able to run under a managed host (dotnet-dump).
        CoInitialize(NULL);

        std::string diasymreaderPath;
        ArrayHolder<char> szSOSModulePath = new char[MAX_LONGPATH + 1];
        if (GetModuleFileNameA(g_hInstance, szSOSModulePath, MAX_LONGPATH) == 0)
        {
            ExtErr("Error: Failed to get SOS module directory\n");
            return HRESULT_FROM_WIN32(GetLastError());
        }
        diasymreaderPath = szSOSModulePath;

        // Get just the sos module directory
        size_t lastSlash = diasymreaderPath.rfind(DIRECTORY_SEPARATOR_CHAR_A);
        if (lastSlash == std::string::npos)
        {
            ExtErr("Error: Failed to parse SOS module name\n");
            return E_FAIL;
        }
        diasymreaderPath.erase(lastSlash + 1);
        diasymreaderPath.append(NATIVE_SYMBOL_READER_DLL);

        // We now need a binder object that will take the module and return a 
        if (FAILED(Status = CreateInstanceFromPath(CLSID_CorSymBinder_SxS, IID_ISymUnmanagedBinder3, diasymreaderPath.c_str(), &g_hmoduleSymBinder, (void**)&g_pSymBinder)))
        {
            ExtDbgOut("SOS error: Unable to find the diasymreader module/interface %08x at %s\n", Status, diasymreaderPath.c_str());
            return Status;
        }
        OnUnloadTask::Register(CleanupSymBinder);
    }
    ToRelease<IDebugSymbols3> spSym3(NULL);
    Status = g_ExtSymbols->QueryInterface(__uuidof(IDebugSymbols3), (void**)&spSym3);
    if (FAILED(Status))
    {
        ExtOut("SOS Error: Unable to query IDebugSymbols3 HRESULT=0x%x.\n", Status);
        return Status;
    }

    ULONG pathSize = 0;
    Status = spSym3->GetSymbolPathWide(NULL, 0, &pathSize);
    if (FAILED(Status)) //S_FALSE if the path doesn't fit, but if the path was size 0 perhaps we would get S_OK?
    {
        ExtOut("SOS Error: Unable to get symbol path length. IDebugSymbols3::GetSymbolPathWide HRESULT=0x%x.\n", Status);
        return Status;
    }

    ArrayHolder<WCHAR> symbolPath = new WCHAR[pathSize];
    Status = spSym3->GetSymbolPathWide(symbolPath, pathSize, NULL);
    if (S_OK != Status)
    {
        ExtOut("SOS Error: Unable to get symbol path. IDebugSymbols3::GetSymbolPathWide HRESULT=0x%x.\n", Status);
        return Status;
    }

    ToRelease<IUnknown> pCallback = NULL;
    if (isFileLayout)
    {
        pCallback = (IUnknown*) new PEOffsetMemoryReader(TO_TADDR(peAddress));
    }
    else
    {
        pCallback = (IUnknown*) new PERvaMemoryReader(TO_TADDR(peAddress));
    }

    // TODO: this should be better integrated with windbg's symbol lookup
    Status = g_pSymBinder->GetReaderFromCallback(pMD, pModuleName, symbolPath, 
        AllowRegistryAccess | AllowSymbolServerAccess | AllowOriginalPathAccess | AllowReferencePathAccess, pCallback, &m_pSymReader);

    if (FAILED(Status) && m_pSymReader != NULL)
    {
        m_pSymReader->Release();
        m_pSymReader = NULL;
    }
    return Status;
}

#endif // FEATURE_PAL

/**********************************************************************\
 * Attempts to load a portable or embeded PDB. Both Windows and xplat.
\**********************************************************************/
HRESULT SymbolReader::LoadSymbolsForPortablePDB(__in_z WCHAR* pModuleName, ___in BOOL isInMemory, ___in BOOL isFileLayout,
    ___in ULONG64 peAddress, ___in ULONG64 peSize, ___in ULONG64 inMemoryPdbAddress, ___in ULONG64 inMemoryPdbSize)
{
    ISymbolService* symbolService = GetSymbolService();
    if (symbolService == nullptr)
    {
        return E_NOINTERFACE;
    }
    m_symbolReaderHandle = GetSymbolService()->LoadSymbolsForModule(
        pModuleName, isFileLayout, peAddress, (int)peSize, inMemoryPdbAddress, (int)inMemoryPdbSize);

    if (m_symbolReaderHandle == 0)
    {
        return E_FAIL;
    }
    return S_OK;
}

/**********************************************************************\
 * Return the source/line number info for method/il offset.
\**********************************************************************/
HRESULT SymbolReader::GetLineByILOffset(___in mdMethodDef methodToken, ___in ULONG64 ilOffset,
    ___out ULONG *pLinenum, __out_ecount(cchFileName) WCHAR* pwszFileName, ___in ULONG cchFileName)
{
    HRESULT Status = S_OK;

    if (m_symbolReaderHandle != 0)
    {
        BSTR bstrFileName = SysAllocStringLen(0, MAX_LONGPATH);
        if (bstrFileName == nullptr)
        {
            return E_OUTOFMEMORY;
        }
        // Source lines with 0xFEEFEE markers are filtered out on the managed side.
        if ((GetSymbolService()->GetLineByILOffset(m_symbolReaderHandle, methodToken, ilOffset, pLinenum, &bstrFileName) == FALSE) || (*pLinenum == 0))
        {
            SysFreeString(bstrFileName);
            return E_FAIL;
        }
        wcscpy_s(pwszFileName, cchFileName, bstrFileName);
        SysFreeString(bstrFileName);
        return S_OK;
    }

#ifndef FEATURE_PAL
    if (m_pSymReader == NULL)
        return E_FAIL;

    ToRelease<ISymUnmanagedMethod> pSymMethod(NULL);
    IfFailRet(m_pSymReader->GetMethod(methodToken, &pSymMethod));

    ULONG32 seqPointCount = 0;
    IfFailRet(pSymMethod->GetSequencePointCount(&seqPointCount));

    if (seqPointCount == 0)
        return E_FAIL;

    // allocate memory for the objects to be fetched
    ArrayHolder<ULONG32> offsets(new ULONG32[seqPointCount]);
    ArrayHolder<ULONG32> lines(new ULONG32[seqPointCount]);
    ArrayHolder<ULONG32> columns(new ULONG32[seqPointCount]);
    ArrayHolder<ULONG32> endlines(new ULONG32[seqPointCount]);
    ArrayHolder<ULONG32> endcolumns(new ULONG32[seqPointCount]);
    ArrayHolder<ToRelease<ISymUnmanagedDocument>> documents(new ToRelease<ISymUnmanagedDocument>[seqPointCount]);

    ULONG32 realSeqPointCount = 0;
    IfFailRet(pSymMethod->GetSequencePoints(seqPointCount, &realSeqPointCount, offsets, &(documents[0]), lines, columns, endlines, endcolumns));

    const ULONG32 HiddenLine = 0x00feefee;
    int bestSoFar = -1;

    for (int i = 0; i < (int)realSeqPointCount; i++)
    {
        if (offsets[i] > ilOffset)
            break;

        if (lines[i] != HiddenLine)
            bestSoFar = i;
    }

    if (bestSoFar != -1)
    {
        ULONG32 cchNeeded = 0;
        IfFailRet(documents[bestSoFar]->GetURL(cchFileName, &cchNeeded, pwszFileName));

        *pLinenum = lines[bestSoFar];
        return S_OK;
    }
#endif // FEATURE_PAL

    return E_FAIL;
}

/**********************************************************************\
 * Returns the name of the local variable from a PDB. 
\**********************************************************************/
HRESULT SymbolReader::GetNamedLocalVariable(___in ISymUnmanagedScope * pScope, ___in ICorDebugILFrame * pILFrame, ___in mdMethodDef methodToken, 
    ___in ULONG localIndex, __out_ecount(paramNameLen) WCHAR* paramName, ___in ULONG paramNameLen, ICorDebugValue** ppValue)
{
    HRESULT Status = S_OK;

    if (m_symbolReaderHandle != 0)
    {
        BSTR wszParamName = SysAllocStringLen(0, mdNameLen);
        if (wszParamName == NULL)
        {
            return E_OUTOFMEMORY;
        }

        if (GetSymbolService()->GetLocalVariableName(m_symbolReaderHandle, methodToken, localIndex, &wszParamName) == FALSE)
        {
            SysFreeString(wszParamName);
            return E_FAIL;
        }

        wcscpy_s(paramName, paramNameLen, wszParamName);
        SysFreeString(wszParamName);

        if (FAILED(pILFrame->GetLocalVariable(localIndex, ppValue)) || (*ppValue == NULL))
        {
            *ppValue = NULL;
            return E_FAIL;
        }
        return S_OK;
    }

#ifndef FEATURE_PAL
    if (m_pSymReader == NULL)
        return E_FAIL;

    if (pScope == NULL)
    {
        ToRelease<ISymUnmanagedMethod> pSymMethod;
        IfFailRet(m_pSymReader->GetMethod(methodToken, &pSymMethod));

        ToRelease<ISymUnmanagedScope> pScope;
        IfFailRet(pSymMethod->GetRootScope(&pScope));

        return GetNamedLocalVariable(pScope, pILFrame, methodToken, localIndex, paramName, paramNameLen, ppValue);
    }
    else
    {
        ULONG32 numVars = 0;
        IfFailRet(pScope->GetLocals(0, &numVars, NULL));

        ArrayHolder<ISymUnmanagedVariable*> pLocals = new ISymUnmanagedVariable*[numVars];
        IfFailRet(pScope->GetLocals(numVars, &numVars, pLocals));

        for (ULONG i = 0; i < numVars; i++)
        {
            ULONG32 varIndexInMethod = 0;
            if (SUCCEEDED(pLocals[i]->GetAddressField1(&varIndexInMethod)))
            {
                if (varIndexInMethod != localIndex)
                    continue;

                ULONG32 nameLen = 0;
                if (FAILED(pLocals[i]->GetName(paramNameLen, &nameLen, paramName)))
                        swprintf_s(paramName, paramNameLen, W("local_%d\0"), localIndex);

                if (SUCCEEDED(pILFrame->GetLocalVariable(varIndexInMethod, ppValue)) && (*ppValue != NULL))
                {
                    for(ULONG j = 0; j < numVars; j++) pLocals[j]->Release();
                    return S_OK;
                }
                else
                {
                    *ppValue = NULL;
                    for(ULONG j = 0; j < numVars; j++) pLocals[j]->Release();
                    return E_FAIL;
                }
            }
        }

        ULONG32 numChildren = 0;
        IfFailRet(pScope->GetChildren(0, &numChildren, NULL));

        ArrayHolder<ISymUnmanagedScope*> pChildren = new ISymUnmanagedScope*[numChildren];
        IfFailRet(pScope->GetChildren(numChildren, &numChildren, pChildren));

        for (ULONG i = 0; i < numChildren; i++)
        {
            if (SUCCEEDED(GetNamedLocalVariable(pChildren[i], pILFrame, methodToken, localIndex, paramName, paramNameLen, ppValue)))
            {
                for (ULONG j = 0; j < numChildren; j++) pChildren[j]->Release();
                return S_OK;
            }
        }

        for (ULONG j = 0; j < numChildren; j++) pChildren[j]->Release();
    }
#endif // FEATURE_PAL

    return E_FAIL;
}

/**********************************************************************\
 * Returns the name of the local variable from a PDB. 
\**********************************************************************/
HRESULT SymbolReader::GetNamedLocalVariable(___in ICorDebugFrame * pFrame, ___in ULONG localIndex, __out_ecount(paramNameLen) WCHAR* paramName, 
    ___in ULONG paramNameLen, ___out ICorDebugValue** ppValue)
{
    HRESULT Status = S_OK;

    *ppValue = NULL;
    paramName[0] = L'\0';

    ToRelease<ICorDebugILFrame> pILFrame;
    IfFailRet(pFrame->QueryInterface(IID_ICorDebugILFrame, (LPVOID*) &pILFrame));

    ToRelease<ICorDebugFunction> pFunction;
    IfFailRet(pFrame->GetFunction(&pFunction));

    mdMethodDef methodDef;
    ToRelease<ICorDebugClass> pClass;
    ToRelease<ICorDebugModule> pModule;
    IfFailRet(pFunction->GetClass(&pClass));
    IfFailRet(pFunction->GetModule(&pModule));
    IfFailRet(pFunction->GetToken(&methodDef));

    return GetNamedLocalVariable(NULL, pILFrame, methodDef, localIndex, paramName, paramNameLen, ppValue);
}

/**********************************************************************\
 * Returns the sequence point to bind breakpoints.
\**********************************************************************/
HRESULT SymbolReader::ResolveSequencePoint(__in_z WCHAR* pFilename, ___in ULONG32 lineNumber, ___out mdMethodDef* pToken, ___out ULONG32* pIlOffset)
{
    HRESULT Status = S_OK;

    if (m_symbolReaderHandle != 0)
    {
        char szName[mdNameLen];
        if (WideCharToMultiByte(CP_ACP, 0, pFilename, (int)(_wcslen(pFilename) + 1), szName, mdNameLen, NULL, NULL) == 0)
        { 
            return E_FAIL;
        }
        if (GetSymbolService()->ResolveSequencePoint(m_symbolReaderHandle, szName, lineNumber, pToken, pIlOffset) == FALSE)
        {
            return E_FAIL;
        }
        return S_OK;
    }

#ifndef FEATURE_PAL
    if (m_pSymReader == NULL)
        return E_FAIL;

    ULONG32 cDocs = 0;
    ULONG32 cDocsNeeded = 0;
    ArrayHolder<ToRelease<ISymUnmanagedDocument>> pDocs = NULL;

    IfFailRet(m_pSymReader->GetDocuments(cDocs, &cDocsNeeded, NULL));
    pDocs = new ToRelease<ISymUnmanagedDocument>[cDocsNeeded];
    cDocs = cDocsNeeded;
    IfFailRet(m_pSymReader->GetDocuments(cDocs, &cDocsNeeded, &(pDocs[0])));

    ULONG32 filenameLen = (ULONG32) _wcslen(pFilename);

    for (ULONG32 i = 0; i < cDocs; i++)
    {
        ULONG32 cchUrl = 0;
        ULONG32 cchUrlNeeded = 0;
        ArrayHolder<WCHAR> pUrl = NULL;
        IfFailRet(pDocs[i]->GetURL(cchUrl, &cchUrlNeeded, pUrl));
        pUrl = new WCHAR[cchUrlNeeded];
        cchUrl = cchUrlNeeded;
        IfFailRet(pDocs[i]->GetURL(cchUrl, &cchUrlNeeded, pUrl));

        // If the URL is exactly as long as the filename then compare the two names directly
        if (cchUrl-1 == filenameLen)
        {
            if (0!=_wcsicmp(pUrl, pFilename))
                continue;
        }
        // does the URL suffix match [back]slash + filename?
        else if (cchUrl-1 > filenameLen)
        {
            WCHAR* slashLocation = pUrl + (cchUrl - filenameLen - 2);
            if (*slashLocation != L'\\' && *slashLocation != L'/')
                continue;
            if (0 != _wcsicmp(slashLocation+1, pFilename))
                continue;
        }
        // URL is too short to match
        else
            continue;

        ULONG32 closestLine = 0;
        if (FAILED(pDocs[i]->FindClosestLine(lineNumber, &closestLine)))
            continue;

        ToRelease<ISymUnmanagedMethod> pSymUnmanagedMethod;
        IfFailRet(m_pSymReader->GetMethodFromDocumentPosition(pDocs[i], closestLine, 0, &pSymUnmanagedMethod));
        IfFailRet(pSymUnmanagedMethod->GetToken(pToken));
        IfFailRet(pSymUnmanagedMethod->GetOffset(pDocs[i], closestLine, 0, pIlOffset));

        // If this IL 
        if (*pIlOffset == -1)
        {
            return E_FAIL;
        }
        return S_OK;
    }
#endif // FEATURE_PAL

    return E_FAIL;
}
