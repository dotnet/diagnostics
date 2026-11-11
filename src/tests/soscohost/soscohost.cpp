// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include <windows.h>
#include <dbgeng.h>
#include <mscoree.h>
#include <coreclrhost.h>
#include <string>
#include <vector>

static ICLRRuntimeHost* s_host = nullptr;
static coreclr_shutdown_2_ptr s_shutdownRuntime = nullptr;
static unsigned int s_domainId = 0;
static std::wstring s_assemblyPath;
static std::wstring s_dependencyPath;

static std::wstring ToWide(const std::string& value)
{
    std::wstring result(value.size() + 1, L'\0');
    int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.c_str(), -1, result.data(), static_cast<int>(result.size()));
    if (length == 0) {
        return {};
    }
    result.resize(length - 1);
    return result;
}

static HRESULT Check(IDebugClient* client, const WCHAR* method)
{
    if (s_host == nullptr) {
        return E_UNEXPECTED;
    }
    DWORD version = 0;
    HRESULT hr = s_host->ExecuteInDefaultAppDomain(s_assemblyPath.c_str(), L"SOS.Cohost.Host", method,
        s_dependencyPath.c_str(), &version);
    IDebugControl* control = nullptr;
    if (SUCCEEDED(client->QueryInterface(__uuidof(IDebugControl), reinterpret_cast<void**>(&control)))) {
        control->Output(DEBUG_OUTPUT_NORMAL, "COHOST dependency version=%u, HRESULT=%08x\n", version, hr);
        control->Release();
    }
    return hr;
}

extern "C" __declspec(dllexport) HRESULT __cdecl DebugExtensionInitialize(PULONG version, PULONG flags)
{
    *version = DEBUG_EXTENSION_VERSION(1, 0);
    *flags = 0;
    return S_OK;
}

extern "C" __declspec(dllexport) HRESULT CALLBACK initialize(IDebugClient* client, PCSTR args)
{
    if (s_host != nullptr) {
        return E_UNEXPECTED;
    }
    std::string arguments(args);
    size_t first = arguments.find('|');
    size_t second = arguments.find('|', first == std::string::npos ? first : first + 1);
    if (first == std::string::npos || second == std::string::npos) {
        return E_INVALIDARG;
    }
    std::string runtimeDirectory = arguments.substr(0, first);
    s_assemblyPath = ToWide(arguments.substr(first + 1, second - first - 1));
    s_dependencyPath = ToWide(arguments.substr(second + 1));
    if (s_assemblyPath.empty() || s_dependencyPath.empty()) {
        return E_INVALIDARG;
    }
    HMODULE runtime = LoadLibraryW(ToWide(runtimeDirectory + "\\coreclr.dll").c_str());
    if (runtime == nullptr) {
        return HRESULT_FROM_WIN32(GetLastError());
    }
    coreclr_initialize_ptr initializeRuntime = reinterpret_cast<coreclr_initialize_ptr>(GetProcAddress(runtime, "coreclr_initialize"));
    if (initializeRuntime == nullptr) {
        return HRESULT_FROM_WIN32(GetLastError());
    }
    s_shutdownRuntime = reinterpret_cast<coreclr_shutdown_2_ptr>(GetProcAddress(runtime, "coreclr_shutdown_2"));
    if (s_shutdownRuntime == nullptr) {
        return HRESULT_FROM_WIN32(GetLastError());
    }
    std::string tpa;
    WIN32_FIND_DATAW data;
    HANDLE find = FindFirstFileW(ToWide(runtimeDirectory + "\\*.dll").c_str(), &data);
    if (find == INVALID_HANDLE_VALUE) {
        return HRESULT_FROM_WIN32(GetLastError());
    }
    do {
        int length = WideCharToMultiByte(CP_UTF8, 0, data.cFileName, -1, nullptr, 0, nullptr, nullptr);
        std::vector<char> name(length);
        if (length == 0 || WideCharToMultiByte(CP_UTF8, 0, data.cFileName, -1, name.data(), length, nullptr, nullptr) == 0) {
            FindClose(find);
            return HRESULT_FROM_WIN32(GetLastError());
        }
        tpa.append(runtimeDirectory).append("\\").append(name.data()).append(";");
    } while (FindNextFileW(find, &data));
    DWORD error = GetLastError();
    FindClose(find);
    if (error != ERROR_NO_MORE_FILES) {
        return HRESULT_FROM_WIN32(error);
    }
    const char* keys[] = { "TRUSTED_PLATFORM_ASSEMBLIES" };
    const char* values[] = { tpa.c_str() };
    void* host = nullptr;
    HRESULT hr = initializeRuntime("soscohost", "CompetingDebuggerExtension", 1, keys, values, &host, &s_domainId);
    if (FAILED(hr)) {
        return hr;
    }
    s_host = static_cast<ICLRRuntimeHost*>(host);
    return Check(client, L"Initialize");
}

extern "C" __declspec(dllexport) HRESULT CALLBACK check(IDebugClient* client, PCSTR)
{
    return Check(client, L"VerifyHostedSOS");
}

extern "C" __declspec(dllexport) HRESULT CALLBACK shutdown(IDebugClient* client, PCSTR)
{
    if (s_host == nullptr) {
        return E_UNEXPECTED;
    }
    IDebugControl* control = nullptr;
    HRESULT hr = client->QueryInterface(__uuidof(IDebugControl), reinterpret_cast<void**>(&control));
    if (FAILED(hr)) {
        return hr;
    }
    ICLRRuntimeHost* host = s_host;
    s_host = nullptr;
    int exitCode = 0;
    hr = s_shutdownRuntime(host, s_domainId, &exitCode);
    control->Output(DEBUG_OUTPUT_NORMAL, "COHOST shutdown HRESULT=%08x, exit=%d\n", hr, exitCode);
    control->Release();
    return hr == S_FALSE ? S_OK : E_FAIL;
}
