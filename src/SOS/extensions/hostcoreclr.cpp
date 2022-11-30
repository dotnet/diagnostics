// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#include <config.h>
#include <windows.h>
#include <psapi.h>
#include <tchar.h>
#include <limits.h>

#ifdef FEATURE_PAL
#include <sys/stat.h>
#include <dirent.h>
#include <dlfcn.h>
#include <unistd.h>
#endif // FEATURE_PAL

#include <functional>
#include <set>
#include <string>
#include <vector>

#include "palclr.h"
#include "arrayholder.h"
#include "coreclrhost.h"
#include "extensions.h"

#include <minipal/getexepath.h>
#include <minipal/utils.h>

#ifndef IfFailRet
#define IfFailRet(EXPR) do { Status = (EXPR); if(FAILED(Status)) { return (Status); } } while (0)
#endif

#ifdef FEATURE_PAL
#define TPALIST_SEPARATOR_STR_A ":"
#else
#define TPALIST_SEPARATOR_STR_A ";"
#endif

#if defined(FEATURE_PAL) && !HAVE_DIRENT_D_TYPE
#define DT_UNKNOWN 0
#define DT_DIR 4
#define DT_REG 8
#define DT_LNK 10
#endif

#if !defined(FEATURE_PAL) && !defined(HOST_ARM64) && !defined(HOST_ARM)
extern HRESULT InitializeDesktopClrHost();
#endif

#ifndef FEATURE_PAL
extern HMODULE g_hInstance;
#endif

extern void TraceError(PCSTR format, ...);

static HostRuntimeFlavor g_hostRuntimeFlavor = HostRuntimeFlavor::NetCore;
bool g_hostingInitialized = false;
static LPCSTR g_hostRuntimeDirectory = nullptr;
static ExtensionsInitializeDelegate g_extensionsInitializeFunc = nullptr;

struct RuntimeVersion
{
    uint32_t Major;
    uint32_t Minor;
};

namespace RuntimeHostingConstants
{
    // This list is in probing order.
    constexpr RuntimeVersion SupportedHostRuntimeVersions[] = {
        {6, 0},
#if !(defined(HOST_OSX) && defined(HOST_ARM64))
        {3, 1},
        {5, 0},
#endif
        {7, 0}
    };

    constexpr char DotnetRootEnvVar[] = "DOTNET_ROOT";

    constexpr char DotnetRootArchSpecificEnvVar[] =
#if defined(HOST_X86)
        "DOTNET_ROOT_X86";
#elif defined(HOST_AMD64)
        "DOTNET_ROOT_X64";
#elif defined(HOST_ARM) || defined(HOST_ARMV6)
        "DOTNET_ROOT_ARM";
#elif defined(HOST_ARM64)
        "DOTNET_ROOT_ARM64";
#else
        "Error";
#error Hosting layer doesn't support target arch
#endif

#ifdef HOST_WINDOWS
    constexpr char RuntimeSubDir[] = "\\shared\\Microsoft.NETCore.App";
#else
    constexpr char RuntimeSubDir[] = "/shared/Microsoft.NETCore.App";

    constexpr char RuntimeInstallMarkerFile[] = "/etc/dotnet/install_location";
    constexpr char RuntimeArchSpecificInstallMarkerFile[] =
#if defined(HOST_X86)
        "/etc/dotnet/install_location_x86";
#elif defined(HOST_AMD64)
        "/etc/dotnet/install_location_x64";
#elif defined(HOST_ARM) || defined(HOST_ARMV6)
        "/etc/dotnet/install_location_arm";
#elif defined(HOST_ARM64)
        "/etc/dotnet/install_location_arm64";
#else
        "ERROR";
#error Hosting layer doesn't support target arch
#endif

    constexpr char const * UnixInstallPaths[] = {
#if defined(HOST_OSX)
#if defined(HOST_AMD64)
        "/usr/local/share/dotnet/x64",
#endif
        "/usr/local/share/dotnet"
#else
        "/rh-dotnet60/root/usr/bin/dotnet",
        "/rh-dotnet31/root/usr/bin/dotnet",
        "/rh-dotnet50/root/usr/bin/dotnet",
        "/rh-dotnet70/root/usr/bin/dotnet",
        "/usr/share/dotnet",
#endif
    };
#endif
};

struct FileFind
{
#ifdef FEATURE_PAL
private:
    DIR* m_dir;
    struct dirent* m_entry;
    const char* m_directory;

public:
    bool Open(const char* directory)
    { 
        m_directory = directory;
        m_dir = opendir(directory);
        if (m_dir == nullptr) {
            return false;
        }
        return Next();
    }

    bool Next()
    {
        if (m_dir == nullptr) {
            return false;
        }
        while ((m_entry = readdir(m_dir)) != nullptr)
        {
#if HAVE_DIRENT_D_TYPE
            int dirEntryType = m_entry->d_type;
#else
            int dirEntryType = DT_UNKNOWN;
#endif
            switch (dirEntryType)
            {
                case DT_REG:
                case DT_DIR:
                    return true;

                // Handle symlinks and file systems that do not support d_type
                case DT_LNK:
                case DT_UNKNOWN:
                {
                    std::string fullFilename;

                    fullFilename.append(m_directory);
                    fullFilename.append(DIRECTORY_SEPARATOR_STR_A);
                    fullFilename.append(m_entry->d_name);

                    struct stat sb;
                    if (stat(fullFilename.c_str(), &sb) == 0) 
                    {
                        if (S_ISREG(sb.st_mode) || S_ISDIR(sb.st_mode)) {
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }

    void Close()
    {
        if (m_dir != nullptr)
        {
            closedir(m_dir);
            m_dir = nullptr;
        }
    }

    bool IsDirectory() 
    {
        return m_entry->d_type == DT_DIR;
    }

    const char* FileName()
    {
        return m_entry->d_name;
    }
#else
private:
    HANDLE m_handle;
    WIN32_FIND_DATAA m_data;

public:
    bool Open(const char* directory)
    {
        std::string dir(directory);
        dir.append(DIRECTORY_SEPARATOR_STR_A);
        dir.append("*");
        m_handle = FindFirstFileA(dir.c_str(), &m_data);
        return m_handle != INVALID_HANDLE_VALUE;
    }

    bool Next()
    {
        if (m_handle == INVALID_HANDLE_VALUE) {
            return false;
        }
        return FindNextFileA(m_handle, &m_data);
    }

    void Close()
    {
        if (m_handle != INVALID_HANDLE_VALUE)
        {
            FindClose(m_handle);
            m_handle = INVALID_HANDLE_VALUE;
        }
    }

    bool IsDirectory() 
    {
        return (m_data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0;
    }

    const char* FileName()
    {
        return m_data.cFileName;
    }
#endif

    ~FileFind()
    {
        Close();
    }
};

static void AddFileToTpaList(const char* directory, const char* filename, std::string & tpaList)
{
    tpaList.append(directory);
    tpaList.append(DIRECTORY_SEPARATOR_STR_A);
    tpaList.append(filename);
    tpaList.append(TPALIST_SEPARATOR_STR_A);
}

//
// Build the TPA list of assemblies for the runtime hosting api.
//
static void AddFilesFromDirectoryToTpaList(const char* directory, std::string& tpaList)
{
    std::set<std::string> addedAssemblies;

    FileFind find;
    if (find.Open(directory))
    {
        // For all entries in the directory
        do
        {
            if (!find.IsDirectory())
            {
                const char extension[] = ".dll";
                std::string filename(find.FileName());
                size_t extLen = sizeof(extension) - 1;
                size_t extPos = filename.length();
                if (extPos > extLen)
                {
                    extPos -= extLen;

                    // Check if the extension matches the one we are looking for
                    if (filename.compare(extPos, extLen, extension) == 0)
                    {
                        std::string filenameWithoutExt(filename.substr(0, extPos));

                        // Make sure if we have an assembly with multiple extensions present,
                        // we insert only one version of it.
                        if (addedAssemblies.find(filenameWithoutExt) == addedAssemblies.end())
                        {
                            addedAssemblies.insert(filenameWithoutExt);
                            AddFileToTpaList(directory, filename.c_str(), tpaList);
                        }
                    }
                }
            }
        }
        while (find.Next());
    }
}

static std::string GetTpaListForRuntimeVersion(
    const std::string& sosModuleDirectory,
    const std::string& hostRuntimeDirectory,
    const RuntimeVersion& hostRuntimeVersion)
{
    std::string tpaList;
    const char* directory = sosModuleDirectory.c_str();

    // TODO: This is a little brittle. At the very least we should make sure that versions
    //       of managed assemblies used by SOS other than the framework ones aren't of a greater
    //       assembly version than the ones in the ones in the framework. The test could just
    //       have a list of assemblies we pack with the versions, and if we end up using a newer assembly
    //       fail the test and point to update this list.
    if (hostRuntimeVersion.Major < 5)
    {
        AddFileToTpaList(directory, "System.Collections.Immutable.dll", tpaList);
        AddFileToTpaList(directory, "System.Reflection.Metadata.dll", tpaList);
        AddFileToTpaList(directory, "System.Runtime.CompilerServices.Unsafe.dll", tpaList);
    }

    // Trust the runtime assemblies that are newer than the ones needed and provided by SOS's managed
    // components.
    AddFilesFromDirectoryToTpaList(hostRuntimeDirectory.c_str(), tpaList);
    return tpaList;
}

//
// Searches the runtime directory for a .NET Core runtime version
//
static bool FindDotNetVersion(const RuntimeVersion& runtimeVersion, std::string& hostRuntimeDirectory)
{
    std::string versionFound;

    FileFind find;
    if (find.Open(hostRuntimeDirectory.c_str()))
    {
        uint32_t highestRevision = 0;
        do
        {
            if (find.IsDirectory())
            {
                uint32_t major = 0;
                uint32_t minor = 0;
                uint32_t revision = 0;
                if (sscanf(find.FileName(), "%d.%d.%d", &major, &minor, &revision) == 3)
                {
                    if (major == runtimeVersion.Major && minor == runtimeVersion.Minor)
                    {
                        if (revision >= highestRevision)
                        {
                            highestRevision = revision;
                            versionFound.assign(find.FileName());
                        }
                    }
                }
            }
        } 
        while (find.Next());
    }

    if (versionFound.length() > 0)
    {
        hostRuntimeDirectory.append(versionFound);
        return true;
    }

    return false;
}

#ifdef HOST_UNIX

static HRESULT ProbeInstallationMarkerFile(const char* const markerName, std::string &hostRuntimeDirectory)
{
    char* line = nullptr;
    size_t lineLen = 0;
    FILE* locationFile = fopen(markerName, "r");
    if (locationFile == nullptr)
    {
        return S_FALSE;
    }

    if (getline(&line, &lineLen, locationFile) == -1)
    {
        TraceError("Unable to read .NET installation marker at %s\n", markerName);
        free(line);
        return E_FAIL;
    }

    hostRuntimeDirectory.assign(line);

    size_t newLinePostion = hostRuntimeDirectory.rfind('\n');
    if (newLinePostion != std::string::npos) {
        hostRuntimeDirectory.erase(newLinePostion);
    }

    hostRuntimeDirectory.append(RuntimeHostingConstants::RuntimeSubDir);
    free(line);

    return hostRuntimeDirectory.empty() ? S_FALSE : S_OK;
}

#endif // HOST_UNIX

static HRESULT ProbeInstallationDir(const char* const installPath, std::string& hostRuntimeDirectory)
{
    hostRuntimeDirectory.assign(installPath);
    hostRuntimeDirectory.append(RuntimeHostingConstants::RuntimeSubDir);
#ifdef HOST_UNIX
    if (access(hostRuntimeDirectory.c_str(), F_OK) != 0)
#else
    if (GetFileAttributesA(hostRuntimeDirectory.c_str()) == INVALID_FILE_ATTRIBUTES)
#endif
    {
        return S_FALSE;
    }
    return S_OK;
}

static HRESULT ProbeEnvVarInstallationHint(const char* const varName, std::string &hostRuntimeDirectory)
{
    char* dotnetRoot = getenv(varName);
    if (dotnetRoot == nullptr)
    {
        return S_FALSE;
    }

    HRESULT Status = ProbeInstallationDir(dotnetRoot, hostRuntimeDirectory);

    return Status == S_OK ? S_OK : E_FAIL;
}

struct ProbingStrategy
{
    std::function<HRESULT(const char* const, std::string&)> strategyDelegate;
    const char* const strategyHint;

    HRESULT Execute(std::string& hostRutimeDirectory) const
    {
        return strategyDelegate(strategyHint, hostRutimeDirectory);
    }
};

/**********************************************************************\
 * Returns the path to the coreclr to use for hosting and it's
 * directory. Attempts to use the best installed version of the 
 * runtime, otherwise it defaults to the target's runtime version.
\**********************************************************************/
static HRESULT GetHostRuntime(std::string& coreClrPath, std::string& hostRuntimeDirectory, RuntimeVersion& hostRuntimeVersion)
{
    // If the hosting runtime isn't already set, use the runtime we are debugging
    if (g_hostRuntimeDirectory == nullptr)
    {
#if defined(HOST_FREEBSD)
        TraceError("Hosting on NetBSD not supported\n");
        return E_FAIL;
#else

        HRESULT Status = E_FAIL;
        std::vector<ProbingStrategy> strategyList = {
             { ProbeEnvVarInstallationHint, RuntimeHostingConstants::DotnetRootArchSpecificEnvVar }
            ,{ ProbeEnvVarInstallationHint, RuntimeHostingConstants::DotnetRootEnvVar }
#if defined(HOST_UNIX)
            ,{ ProbeInstallationMarkerFile, RuntimeHostingConstants::RuntimeArchSpecificInstallMarkerFile }
            ,{ ProbeInstallationMarkerFile, RuntimeHostingConstants::RuntimeInstallMarkerFile }
#endif
        };

#if defined(HOST_UNIX)
        for (int i = 0; i < ARRAY_SIZE(RuntimeHostingConstants::UnixInstallPaths); i++)
        {
            strategyList.push_back({ ProbeInstallationDir, RuntimeHostingConstants::UnixInstallPaths[i] });
        }
#else
        ArrayHolder<CHAR> programFiles = new CHAR[MAX_LONGPATH];
        if (GetEnvironmentVariableA("PROGRAMFILES", programFiles, MAX_LONGPATH) == 0)
        {
            TraceError("PROGRAMFILES environment variable not found\n");
            return E_FAIL;
        }
        std::string windowsInstallPath(programFiles);
        windowsInstallPath.append("\\dotnet");
        strategyList.push_back({ ProbeInstallationDir, windowsInstallPath.c_str() });
#endif
        for (auto it = strategyList.cbegin(); it != strategyList.cend() && Status != S_OK; it++)
        {
            IfFailRet(it->Execute(hostRuntimeDirectory));
        }

        if (Status != S_OK)
        {
            TraceError("Error: Failed to find runtime directory\n");
            return E_FAIL;
        }

        hostRuntimeDirectory.append(DIRECTORY_SEPARATOR_STR_A);

        for (const RuntimeVersion& version: RuntimeHostingConstants::SupportedHostRuntimeVersions)
        {
            if (FindDotNetVersion(version, hostRuntimeDirectory))
            {
                hostRuntimeVersion = version;
                break;
            }
        }

        if (hostRuntimeVersion.Major == 0)
        {
            TraceError("Error: Failed to find runtime directory within %s\n", hostRuntimeDirectory.c_str());
            return E_FAIL;
        }

        // Save away the runtime version we are going to use to host the SOS managed code
        g_hostRuntimeDirectory = _strdup(hostRuntimeDirectory.c_str());
    }
    hostRuntimeDirectory.assign(g_hostRuntimeDirectory);
    coreClrPath.assign(g_hostRuntimeDirectory);
    coreClrPath.append(DIRECTORY_SEPARATOR_STR_A);
    coreClrPath.append(MAKEDLLNAME_A("coreclr"));
    return S_OK;
#endif
}

/**********************************************************************\
 * Initializes the host coreclr runtime
\**********************************************************************/
static HRESULT InitializeNetCoreHost()
{
    std::string sosModulePath;
    HRESULT hr = S_OK;

#ifdef FEATURE_PAL
    Dl_info info;
    if (dladdr((PVOID)&InitializeNetCoreHost, &info) == 0)
    {
        TraceError("Error: dladdr() failed getting current module directory\n");
        return E_FAIL;
    }
    sosModulePath = info.dli_fname;
#else
    ArrayHolder<char> szSOSModulePath = new char[MAX_LONGPATH + 1];
    if (GetModuleFileNameA(g_hInstance, szSOSModulePath, MAX_LONGPATH) == 0)
    {
        TraceError("Error: Failed to get SOS module directory\n");
        return HRESULT_FROM_WIN32(GetLastError());
    }
    sosModulePath = szSOSModulePath;
#endif // FEATURE_PAL

    if (g_extensionsInitializeFunc == nullptr)
    {
        coreclr_initialize_ptr initializeCoreCLR = nullptr;
        coreclr_create_delegate_ptr createDelegate = nullptr;
        std::string sosModuleDirectory;
        std::string hostRuntimeDirectory;
        std::string coreClrPath;
        RuntimeVersion hostRuntimeVersion = {};

        hr = GetHostRuntime(coreClrPath, hostRuntimeDirectory, hostRuntimeVersion);
        if (FAILED(hr))
        {
            return hr;
        }
#ifdef FEATURE_PAL
        void* coreclrLib = dlopen(coreClrPath.c_str(), RTLD_NOW | RTLD_LOCAL);
        if (coreclrLib == nullptr)
        {
            TraceError("Error: Failed to load %s\n", coreClrPath.c_str());
            return E_FAIL;
        }
        initializeCoreCLR = (coreclr_initialize_ptr)dlsym(coreclrLib, "coreclr_initialize");
        createDelegate = (coreclr_create_delegate_ptr)dlsym(coreclrLib, "coreclr_create_delegate");
#else
        HMODULE coreclrLib = LoadLibraryA(coreClrPath.c_str());
        if (coreclrLib == nullptr)
        {
            TraceError("Error: Failed to load %s\n", coreClrPath.c_str());
            return E_FAIL;
        }
        initializeCoreCLR = (coreclr_initialize_ptr)GetProcAddress(coreclrLib, "coreclr_initialize");
        createDelegate = (coreclr_create_delegate_ptr)GetProcAddress(coreclrLib, "coreclr_create_delegate");
#endif // FEATURE_PAL

        if (initializeCoreCLR == nullptr || createDelegate == nullptr)
        {
            TraceError("Error: coreclr_initialize or coreclr_create_delegate not found\n");
            return E_FAIL;
        }

        // Get just the sos module directory
        sosModuleDirectory = sosModulePath;
        size_t lastSlash = sosModuleDirectory.rfind(DIRECTORY_SEPARATOR_CHAR_A);
        if (lastSlash == std::string::npos)
        {
            TraceError("Error: Failed to parse sos module name\n");
            return E_FAIL;
        }
        sosModuleDirectory.erase(lastSlash);

        // Trust The SOS managed and dependent assemblies from the sos directory
        std::string tpaList = GetTpaListForRuntimeVersion(sosModuleDirectory, hostRuntimeDirectory, hostRuntimeVersion);

        std::string appPaths;
        appPaths.append(sosModuleDirectory);

        const char* propertyKeys[] = {
            "TRUSTED_PLATFORM_ASSEMBLIES",
            "APP_PATHS",
            "APP_NI_PATHS",
            "NATIVE_DLL_SEARCH_DIRECTORIES",
            "AppDomainCompatSwitch"
        };

        const char* propertyValues[] = {
            // TRUSTED_PLATFORM_ASSEMBLIES
            tpaList.c_str(),
            // APP_PATHS
            appPaths.c_str(),
            // APP_NI_PATHS
            hostRuntimeDirectory.c_str(),
            // NATIVE_DLL_SEARCH_DIRECTORIES
            appPaths.c_str(),
            // AppDomainCompatSwitch
            "UseLatestBehaviorWhenTFMNotSpecified"
        };

        char* exePath = minipal_getexepath();
        if (!exePath)
        {
            TraceError("Could not get full path to current executable");
            return E_FAIL;
        }

        void* hostHandle;
        unsigned int domainId;
        hr = initializeCoreCLR(exePath, "sos", ARRAY_SIZE(propertyKeys), propertyKeys, propertyValues, &hostHandle, &domainId);

        free(exePath);
        if (FAILED(hr))
        {
            TraceError("Error: Fail to initialize coreclr %08x\n", hr);
            return hr;
        }

        hr = createDelegate(hostHandle, domainId, ExtensionsDllName, ExtensionsClassName, ExtensionsInitializeFunctionName, (void**)&g_extensionsInitializeFunc);
        if (FAILED(hr))
        {
            TraceError("Error: Fail to create host ldelegate %08x\n", hr);
            return hr;
        }
    }
    try 
    {
        hr = g_extensionsInitializeFunc(sosModulePath.c_str());
    }
    catch (...)
    {
        hr = E_ACCESSDENIED;
    }
    if (FAILED(hr))
    {
        TraceError("Extension host initialization FAILED %08x\n", hr);
        return hr;
    }
    return hr;
}

/**********************************************************************\
 * Gets the host runtime flavor
\**********************************************************************/
HostRuntimeFlavor GetHostRuntimeFlavor()
{
    return g_hostRuntimeFlavor;
}

/**********************************************************************\
 * Sets the host runtime flavor
\**********************************************************************/
bool SetHostRuntimeFlavor(HostRuntimeFlavor flavor)
{
    g_hostRuntimeFlavor = flavor;
    return true;
}

/**********************************************************************\
 * Sets the host runtime directory path
\**********************************************************************/
bool SetHostRuntimeDirectory(LPCSTR hostRuntimeDirectory)
{
    if (hostRuntimeDirectory != nullptr)
    {
        std::string fullPath;
        if (!GetAbsolutePath(hostRuntimeDirectory, fullPath))
        {
            return false;
        }
        hostRuntimeDirectory = _strdup(fullPath.c_str());
    }
    if (g_hostRuntimeDirectory != nullptr)
    {
        free((void*)g_hostRuntimeDirectory);
    }
    g_hostRuntimeDirectory = hostRuntimeDirectory;
    g_hostRuntimeFlavor = HostRuntimeFlavor::NetCore;
    return true;
}

/**********************************************************************\
 * Gets the current host runtime directory path or null if not set
\**********************************************************************/
LPCSTR GetHostRuntimeDirectory()
{
    return g_hostRuntimeDirectory;
}

/**********************************************************************\
 * Returns true if the host runtime has already been initialized.
\**********************************************************************/
BOOL IsHostingInitialized()
{
    return g_hostingInitialized;
}

/**********************************************************************\
 * Initializes the host runtime
\**********************************************************************/
HRESULT InitializeHosting()
{
    if (g_hostRuntimeFlavor == HostRuntimeFlavor::None)
    {
        return E_FAIL;
    }
    HRESULT hr = S_OK;
    if (g_hostRuntimeFlavor == HostRuntimeFlavor::NetCore)
    {
        hr = InitializeNetCoreHost();
        if (SUCCEEDED(hr))
        {
            g_hostRuntimeFlavor = HostRuntimeFlavor::NetCore;
            g_hostingInitialized = true;
            return hr;
        }
    }
#if !defined(FEATURE_PAL) && !defined(HOST_ARM64) && !defined(HOST_ARM)
    hr = InitializeDesktopClrHost();
    if (SUCCEEDED(hr))
    {
        g_hostRuntimeFlavor = HostRuntimeFlavor::NetFx;
        g_hostingInitialized = true;
        return hr;
    }
#endif
    g_hostRuntimeFlavor = HostRuntimeFlavor::None;
    return hr;
}
