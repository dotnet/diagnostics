// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// Windows Header Files
#include <windows.h>
#include <stdio.h>
#include <metahost.h>
#include <objbase.h>
#include <mscoree.h>
#include <tchar.h>
#include <strsafe.h>
#include <string>
#include "releaseholder.h"
#include "arrayholder.h"
#include "extensions.h"

#define CLR_VERSION     L"v4.0.30319"

extern HMODULE g_hInstance;
extern void TraceError(PCSTR format, ...);

void UninitializeDesktopClrHost();

ICLRRuntimeHost* g_clrHost = nullptr;

/// <summary>
/// Loads and initializes the desktop CLR to host the managed SOS code.
/// </summary>
HRESULT InitializeDesktopClrHost()
{
    HRESULT hr = S_OK;
    DWORD ret = 0;

    if (g_clrHost != nullptr)
    {
        return S_OK;
    }
    ArrayHolder<WCHAR> wszSOSModulePath = new WCHAR[MAX_LONGPATH + 1];
    if (GetModuleFileNameW(g_hInstance, wszSOSModulePath, MAX_LONGPATH) == 0)
    {
        TraceError("Error: Failed to get SOS module directory\n");
        return HRESULT_FROM_WIN32(GetLastError());
    }
    ArrayHolder<WCHAR> wszManagedModulePath = new WCHAR[MAX_LONGPATH + 1];
    if (wcscpy_s(wszManagedModulePath.GetPtr(), MAX_LONGPATH, wszSOSModulePath.GetPtr()) != 0)
    {
        TraceError("Error: Failed to copy module name\n");
        return E_FAIL;
    }
    WCHAR* lastSlash = wcsrchr(wszManagedModulePath.GetPtr(), DIRECTORY_SEPARATOR_CHAR_W);
    if (lastSlash != nullptr)
    {
        *++lastSlash = L'\0';
    }
    if (wcscat_s(wszManagedModulePath.GetPtr(), MAX_LONGPATH, ExtensionsDllNameW) != 0)
    {
        TraceError("Error: Failed to append SOS module name\n");
        return E_FAIL;
    }
    hr = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    if (FAILED(hr) && hr != RPC_E_CHANGED_MODE)
    {
        TraceError("Error: CoInitializeEx failed. %08x\n", hr);
        return hr;
    }
    // Loads the CLR and then initializes the managed debugger extensions.
    ReleaseHolder<ICLRMetaHost> metaHost;
    hr = CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, (PVOID*)&metaHost);
    if (FAILED(hr) || metaHost == nullptr)
    {
        TraceError("Error: CLRCreateInstance failed %08x\n", hr);
        return hr;
    }
    ReleaseHolder<ICLRRuntimeInfo> runtimeInfo;
    hr = metaHost->GetRuntime(CLR_VERSION, IID_ICLRRuntimeInfo, (PVOID*)&runtimeInfo);
    if (FAILED(hr) || runtimeInfo == nullptr)
    {
        TraceError("Error: ICLRMetaHost::GetRuntime failed %08x\n", hr);
        return hr;
    }
    hr = runtimeInfo->GetInterface(CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (PVOID*)&g_clrHost);
    if (FAILED(hr) || g_clrHost == nullptr)
    {
        TraceError("Error: ICLRRuntimeInfo::GetInterface failed %08x\n", hr);
        return hr;
    }
    hr = g_clrHost->Start();
    if (FAILED(hr)) 
    {
        TraceError("Error: ICLRRuntimeHost::Start failed %08x\n", hr);
        g_clrHost->Release();
        g_clrHost = nullptr;
        return hr;
    }
    // Initialize the managed code
    hr = g_clrHost->ExecuteInDefaultAppDomain(wszManagedModulePath.GetPtr(), ExtensionsClassNameW, ExtensionsInitializeFunctionNameW, wszSOSModulePath.GetPtr(), (DWORD *)&ret);
    if (FAILED(hr)) 
    {
        TraceError("Error: ICLRRuntimeHost::ExecuteInDefaultAppDomain failed %08x\n", hr);
        UninitializeDesktopClrHost();
        return hr;
    }
    if (ret != 0)
    { 
        TraceError("Error: InitializeSymbolReader failed %08x\n", ret);
        UninitializeDesktopClrHost();
        return ret;
    }
    return S_OK;
}

/// <summary>
/// Uninitializes and unloads the desktop CLR
/// </summary>
void UninitializeDesktopClrHost()
{
    if (g_clrHost != nullptr)
    {
        g_clrHost->Stop();
        g_clrHost->Release();
        g_clrHost = nullptr;
    }
}
