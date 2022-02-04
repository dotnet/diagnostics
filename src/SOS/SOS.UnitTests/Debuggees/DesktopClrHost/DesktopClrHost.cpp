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

#define CLR_VERSION L"v4.0.30319"

EXTERN_GUID(CLSID_CLRRuntimeHost, 0x90F1A06E, 0x7712, 0x4762, 0x86, 0xB5, 0x7A, 0x5E, 0xBA, 0x6B, 0xDB, 0x02);

EXTERN_C __declspec(dllexport) HRESULT __cdecl 
InitializeDesktopClrHost(
    LPCWSTR assemblyPath,
    LPCWSTR className,
    LPCWSTR functionName,
    LPCWSTR argument)
{
    ICLRRuntimeHost* clrHost = NULL;
    HRESULT hr = S_OK;
    DWORD ret;

    hr = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    if (SUCCEEDED(hr) || hr == RPC_E_CHANGED_MODE) {

        // Loads the CLR and then initializes the managed debugger extensions.
        ICLRMetaHost* metaHost;
        hr = CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, (PVOID*)&metaHost);
        if (SUCCEEDED(hr) && metaHost != NULL) {

            ICLRRuntimeInfo* runtimeInfo;
            hr = metaHost->GetRuntime(CLR_VERSION, IID_ICLRRuntimeInfo, (PVOID*)&runtimeInfo);
            if (SUCCEEDED(hr) && runtimeInfo != NULL) {

                hr = runtimeInfo->GetInterface(CLSID_CLRRuntimeHost, IID_ICLRRuntimeHost, (PVOID*)&clrHost);
                if (SUCCEEDED(hr) && clrHost != NULL) {

                    hr = clrHost->Start();
                    if (SUCCEEDED(hr)) {

                        // Initialize the managed code
                        hr = clrHost->ExecuteInDefaultAppDomain(assemblyPath, className, functionName, argument, (DWORD *)&ret);
                        if (FAILED(hr)) {
                            printf("InitializeDesktopClrHost: InitializeExtensions failed 0x%X\r\n", hr);
                        }
                    }
                    else {
                        printf("InitializeDesktopClrHost: ICLRRuntimeHost::Start failed 0x%X\r\n", hr);
                    }
                }
                else {
                    printf("InitializeDesktopClrHost: ICLRRuntimeInfo::GetInterface failed 0x%X\r\n", hr);
                }
            }
            else {
                printf("InitializeDesktopClrHost: ICLRMetaHost::GetRuntime failed 0x%X\r\n", hr);
            }
        }
        else {
            printf("InitializeDesktopClrHost: CLRCreateInstance failed 0x%X\r\n", hr);
        }
    }
    else {
        printf("InitializeDesktopClrHost: CoInitializeEx failed. 0x%X\r\n", hr);
    }

    return hr;
}

