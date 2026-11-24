// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include <config.h>
#include <windows.h>

#ifdef FEATURE_PAL
#include <dlfcn.h>
#include <errno.h>
#endif

#include <string>

#include "arrayholder.h"
#include "extensions.h"

#ifndef FEATURE_PAL
extern HMODULE g_hInstance;
#endif

extern void TraceHostingError(PCSTR format, ...);

bool g_hostingInitialized = false;
static ExtensionsInitializeDelegate g_extensionsInitializeFunc = nullptr;

static HRESULT LoadNativeAotExtensions(std::string& sosModulePath)
{
#ifdef FEATURE_PAL
    Dl_info info;
    if (dladdr((PVOID)&LoadNativeAotExtensions, &info) == 0)
    {
        TraceHostingError("Failed to get SOS module path with dladdr()\n");
        return E_FAIL;
    }
    sosModulePath = info.dli_fname;
#else
    ArrayHolder<char> path = new char[MAX_LONGPATH + 1];
    if (GetModuleFileNameA(g_hInstance, path, MAX_LONGPATH) == 0)
    {
        TraceHostingError("Failed to get SOS module path\n");
        return HRESULT_FROM_WIN32(GetLastError());
    }
    sosModulePath = path;
#endif

    size_t lastSlash = sosModulePath.rfind(DIRECTORY_SEPARATOR_CHAR_A);
    if (lastSlash == std::string::npos)
    {
        TraceHostingError("Failed to parse SOS module path\n");
        return E_FAIL;
    }

    std::string extensionsPath = sosModulePath.substr(0, lastSlash + 1);
#ifdef HOST_OSX
    extensionsPath.append("SOS.Extensions.NativeAot.dylib");
#elif defined(FEATURE_PAL)
    extensionsPath.append("SOS.Extensions.NativeAot.so");
#else
    extensionsPath.append("SOS.Extensions.NativeAot.dll");
#endif

#ifdef HOST_OSX
    // Apple LLDB guards its Mach exception ports; the NativeAOT runtime must not replace them.
    if (setenv("PAL_MachExceptionMode", "7", 0) != 0)
    {
        TraceHostingError("Failed to set PAL_MachExceptionMode: %s\n", strerror(errno));
        return E_FAIL;
    }
#endif

#ifdef FEATURE_PAL
    void* extensionsLibrary = dlopen(extensionsPath.c_str(), RTLD_NOW | RTLD_LOCAL);
    if (extensionsLibrary == nullptr)
    {
        TraceHostingError("Failed to load NativeAOT extensions module %s: %s\n", extensionsPath.c_str(), dlerror());
        return E_FAIL;
    }
    g_extensionsInitializeFunc = (ExtensionsInitializeDelegate)dlsym(extensionsLibrary, "SOSInitializeExtensions");
#else
    HMODULE extensionsLibrary = LoadLibraryA(extensionsPath.c_str());
    if (extensionsLibrary == nullptr)
    {
        TraceHostingError("Failed to load NativeAOT extensions module %s: %u\n", extensionsPath.c_str(), GetLastError());
        return HRESULT_FROM_WIN32(GetLastError());
    }
    g_extensionsInitializeFunc = (ExtensionsInitializeDelegate)GetProcAddress(extensionsLibrary, "SOSInitializeExtensions");
#endif

    if (g_extensionsInitializeFunc == nullptr)
    {
        TraceHostingError("SOSInitializeExtensions export not found in %s\n", extensionsPath.c_str());
        return E_NOINTERFACE;
    }
    return S_OK;
}

BOOL IsHostingInitialized()
{
    return g_hostingInitialized;
}

HRESULT InitializeHosting()
{
    std::string sosModulePath;
    HRESULT hr = LoadNativeAotExtensions(sosModulePath);
    if (FAILED(hr))
        return hr;

    hr = g_extensionsInitializeFunc(sosModulePath.c_str());
    if (FAILED(hr))
    {
        TraceHostingError("NativeAOT extension host initialization failed %08x\n", hr);
        return hr;
    }

    g_hostingInitialized = true;
    return S_OK;
}
