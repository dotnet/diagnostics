// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#ifndef __cordebuglibraryprovider_h__
#define __cordebuglibraryprovider_h__

#ifndef FEATURE_PAL
extern HMODULE LoadLibraryAndCheck(PCWSTR filename, DWORD timestamp, DWORD filesize);
#endif

/**********************************************************************\
 * Provides a way for the public CLR debugging interface to find the 
 * appropriate mscordbi.dll, DAC, etc.
\**********************************************************************/
class CorDebugLibraryProvider : public ICLRDebuggingLibraryProvider, ICLRDebuggingLibraryProvider2
{
public:
    CorDebugLibraryProvider(Runtime* pRuntime) :
        m_ref(0),
        m_pRuntime(pRuntime)
    {
    }

    virtual ~CorDebugLibraryProvider() {}

    virtual HRESULT STDMETHODCALLTYPE QueryInterface(
        REFIID InterfaceId,
        PVOID* pInterface)
    {
        if (InterfaceId == IID_IUnknown)
        {
            *pInterface = static_cast<IUnknown *>(static_cast<ICLRDebuggingLibraryProvider*>(this));
        }
#ifndef FEATURE_PAL
        else if (InterfaceId == IID_ICLRDebuggingLibraryProvider)
        {
            *pInterface = static_cast<ICLRDebuggingLibraryProvider *>(this);
        }
#endif
        else if (InterfaceId == IID_ICLRDebuggingLibraryProvider2)
        {
            *pInterface = static_cast<ICLRDebuggingLibraryProvider2 *>(this);
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

    HRESULT ProvideLibraryInternal(
        const WCHAR* pwszFileName,
        DWORD dwTimestamp,
        DWORD dwSizeOfImage,
        HMODULE* phModule,
        LPWSTR* ppResolvedModulePath)
    {
        const char* filePath = nullptr;

        if (_wcsncmp(pwszFileName, m_pRuntime->GetDacDllNameW(), _wcslen(m_pRuntime->GetDacDllNameW())) == 0)
        {
            filePath = m_pRuntime->GetDacFilePath();
        }
        else if (_wcsncmp(pwszFileName, NET_DBI_DLL_NAME_W, _wcslen(NET_DBI_DLL_NAME_W)) == 0)
        {
            filePath = m_pRuntime->GetDbiFilePath();
        }

        ArrayHolder<WCHAR> modulePath = new WCHAR[MAX_LONGPATH + 1];
        if (filePath != nullptr)
        {
            int length = MultiByteToWideChar(CP_ACP, 0, filePath, -1, modulePath, MAX_LONGPATH);
            if (0 >= length)
            {
                ExtErr("MultiByteToWideChar(filePath) failed. Last error = 0x%x\n", GetLastError());
                return HRESULT_FROM_WIN32(GetLastError());
            }
        }
        else
        {
            LPCSTR runtimeDirectory = m_pRuntime->GetRuntimeDirectory();
            if (runtimeDirectory == nullptr)
            {
                ExtErr("Runtime not loaded\n");
                return E_FAIL;
            }
            int length = MultiByteToWideChar(CP_ACP, 0, runtimeDirectory, -1, modulePath, MAX_LONGPATH);
            if (0 >= length)
            {
                ExtErr("MultiByteToWideChar(runtimeDirectory) failed. Last error = 0x%x\n", GetLastError());
                return HRESULT_FROM_WIN32(GetLastError());
            }
            wcscat_s(modulePath, MAX_LONGPATH, pwszFileName);
        }

        ExtOut("Loaded %S\n", modulePath.GetPtr());

#ifndef FEATURE_PAL
        if (phModule != NULL)
        {
            *phModule = LoadLibraryAndCheck(modulePath.GetPtr(), dwTimestamp, dwSizeOfImage);
        }
#endif
        if (ppResolvedModulePath != NULL)
        {
            *ppResolvedModulePath = modulePath.Detach();
        }
        return S_OK;
    }

    // Called by the shim to locate and load dac and dbi
    // Parameters:
    //    pwszFileName - the name of the file to load
    //    dwTimestamp - the expected timestamp of the file
    //    dwSizeOfImage - the expected SizeOfImage (a PE header data value)
    //    phModule - a handle to loaded module
    //
    // Return Value
    //    S_OK if the file was loaded, or any error if not
    virtual HRESULT STDMETHODCALLTYPE ProvideLibrary(
        const WCHAR * pwszFileName,
        DWORD dwTimestamp,
        DWORD dwSizeOfImage,
        HMODULE* phModule)
    {
        if ((phModule == NULL) || (pwszFileName == NULL))
        {
            return E_INVALIDARG;
        }
        return ProvideLibraryInternal(pwszFileName, dwTimestamp, dwSizeOfImage, phModule, NULL);
    }

    virtual HRESULT STDMETHODCALLTYPE ProvideLibrary2(
        const WCHAR* pwszFileName,
        DWORD dwTimestamp,
        DWORD dwSizeOfImage,
        LPWSTR* ppResolvedModulePath)
    {
        if ((pwszFileName == NULL) || (ppResolvedModulePath == NULL))
        {
            return E_INVALIDARG;
        }
        return ProvideLibraryInternal(pwszFileName, dwTimestamp, dwSizeOfImage, NULL, ppResolvedModulePath);
    }

protected:
    LONG m_ref;
    Runtime* m_pRuntime;
};

#endif // __cordebuglibraryprovider_h__
