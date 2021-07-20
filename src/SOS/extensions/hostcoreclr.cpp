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

#include <set>
#include <string>

#if defined(__APPLE__)
#include <mach-o/dyld.h>
#endif

#if defined(__FreeBSD__)
#include <sys/types.h>
#include <sys/sysctl.h>
#endif

#include "palclr.h"
#include "arrayholder.h"
#include "coreclrhost.h"
#include "extensions.h"

#ifndef _countof
#define _countof(x) (sizeof(x)/sizeof(x[0]))
#endif

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

#if !defined(FEATURE_PAL) && !defined(_TARGET_ARM64_)
extern HRESULT InitializeDesktopClrHost();
#endif

#ifndef FEATURE_PAL
extern HMODULE g_hInstance;
#endif

extern void TraceError(PCSTR format, ...);

static HostRuntimeFlavor g_hostRuntimeFlavor = HostRuntimeFlavor::NetCore;
static bool g_hostingInitialized = false;
static LPCSTR g_hostRuntimeDirectory = nullptr;

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
                size_t extPos = filename.length() - extLen;

                // Check if the extension matches the one we are looking for
                if ((extPos > 0) && (filename.compare(extPos, extLen, extension) == 0))
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
        while (find.Next());
    }
}

//
// Searches the runtime directory for a .NET Core runtime version
//
static bool FindDotNetVersion(int majorFilter, int minorFilter, std::string& hostRuntimeDirectory)
{
    std::string versionFound;

    FileFind find;
    if (find.Open(hostRuntimeDirectory.c_str()))
    {
        int highestRevision = 0;
        do
        {
            if (find.IsDirectory())
            {
                int major = 0;
                int minor = 0;
                int revision = 0;
                if (sscanf(find.FileName(), "%d.%d.%d", &major, &minor, &revision) == 3)
                {
                    if (major == majorFilter && minor == minorFilter)
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

#ifndef FEATURE_PAL

static bool GetEntrypointExecutableAbsolutePath(std::string& entrypointExecutable)
{
    ArrayHolder<char> hostPath = new char[MAX_LONGPATH+1];
    if (::GetModuleFileName(NULL, hostPath, MAX_LONGPATH) == 0)
    {
        return false;
    }
    entrypointExecutable.clear();
    entrypointExecutable.append(hostPath);
    return true;
}

#else // FEATURE_PAL

#if defined(__linux__)
#define symlinkEntrypointExecutable "/proc/self/exe"
#elif !defined(__APPLE__)
#define symlinkEntrypointExecutable "/proc/curproc/exe"
#endif

static bool GetEntrypointExecutableAbsolutePath(std::string& entrypointExecutable)
{
    bool result = false;
    
    entrypointExecutable.clear();

    // Get path to the executable for the current process using
    // platform specific means.
#if defined(__APPLE__)
    // On Mac, we ask the OS for the absolute path to the entrypoint executable
    uint32_t lenActualPath = 0;
    if (_NSGetExecutablePath(nullptr, &lenActualPath) == -1)
    {
        // OSX has placed the actual path length in lenActualPath,
        // so re-attempt the operation
        std::string resizedPath(lenActualPath, '\0');
        char *pResizedPath = const_cast<char *>(resizedPath.c_str());
        if (_NSGetExecutablePath(pResizedPath, &lenActualPath) == 0)
        {
            entrypointExecutable.assign(pResizedPath);
            result = true;
        }
    }
#elif defined (__FreeBSD__)
    static const int name[] = {
        CTL_KERN, KERN_PROC, KERN_PROC_PATHNAME, -1
    };
    char path[PATH_MAX];
    size_t len;

    len = sizeof(path);
    if (sysctl(name, 4, path, &len, nullptr, 0) == 0)
    {
        entrypointExecutable.assign(path);
        result = true;
    }
    else
    {
        // ENOMEM
        result = false;
    }
#elif defined(__NetBSD__) && defined(KERN_PROC_PATHNAME)
    static const int name[] = {
        CTL_KERN, KERN_PROC_ARGS, -1, KERN_PROC_PATHNAME,
    };
    char path[MAXPATHLEN];
    size_t len;

    len = sizeof(path);
    if (sysctl(name, __arraycount(name), path, &len, NULL, 0) != -1)
    {
        entrypointExecutable.assign(path);
        result = true;
    }
    else
    {
        result = false;
    }
#else
    // On other OSs, return the symlink that will be resolved by GetAbsolutePath
    // to fetch the entrypoint EXE absolute path, inclusive of filename.
    result = GetAbsolutePath(symlinkEntrypointExecutable, entrypointExecutable);
#endif 

    return result;
}

const char *g_linuxPaths[] = {
#if defined(__APPLE__)
    "/usr/local/share/dotnet/shared/Microsoft.NETCore.App"
#else
    "/rh-dotnet31/root/usr/bin/dotnet/shared/Microsoft.NETCore.App",
    "/rh-dotnet30/root/usr/bin/dotnet/shared/Microsoft.NETCore.App",
    "/rh-dotnet21/root/usr/bin/dotnet/shared/Microsoft.NETCore.App",
    "/usr/share/dotnet/shared/Microsoft.NETCore.App",
#endif
};

#endif // FEATURE_PAL

/**********************************************************************\
 * Returns the path to the coreclr to use for hosting and it's
 * directory. Attempts to use the best installed version of the 
 * runtime, otherwise it defaults to the target's runtime version.
\**********************************************************************/
static HRESULT GetHostRuntime(std::string& coreClrPath, std::string& hostRuntimeDirectory)
{
    // If the hosting runtime isn't already set, use the runtime we are debugging
    if (g_hostRuntimeDirectory == nullptr)
    {
        char* dotnetRoot = getenv("DOTNET_ROOT");
        if (dotnetRoot != nullptr)
        {
            hostRuntimeDirectory.assign(dotnetRoot);
            hostRuntimeDirectory.append(DIRECTORY_SEPARATOR_STR_A);
            hostRuntimeDirectory.append("shared");
            hostRuntimeDirectory.append(DIRECTORY_SEPARATOR_STR_A);
            hostRuntimeDirectory.append("Microsoft.NETCore.App");
#ifdef FEATURE_PAL
            if (access(hostRuntimeDirectory.c_str(), F_OK) != 0)
#else
            if (GetFileAttributesA(hostRuntimeDirectory.c_str()) == INVALID_FILE_ATTRIBUTES)
#endif
            {
                TraceError("DOTNET_ROOT (%s) path doesn't exist\n", hostRuntimeDirectory.c_str());
                return E_FAIL;
            }
        }
        else 
        {
#ifdef FEATURE_PAL
#if defined(__NetBSD__)
            TraceError("Hosting on NetBSD not supported\n");
            return E_FAIL;
#else
            char* line = nullptr;
            size_t lineLen = 0;

            // Start with Linux location file if exists
            FILE* locationFile = fopen("/etc/dotnet/install_location", "r");
            if (locationFile != nullptr)
            {
                if (getline(&line, &lineLen, locationFile) != -1)
                {
                    hostRuntimeDirectory.assign(line);
                    size_t newLinePostion = hostRuntimeDirectory.rfind('\n');
                    if (newLinePostion != std::string::npos) {
                        hostRuntimeDirectory.erase(newLinePostion);
                        hostRuntimeDirectory.append("/shared/Microsoft.NETCore.App");
                    }
                    free(line);
                }
            }
            if (hostRuntimeDirectory.empty())
            {
                // Now try the possible runtime locations
                for (int i = 0; i < _countof(g_linuxPaths); i++)
                {
                    hostRuntimeDirectory.assign(g_linuxPaths[i]);
                    if (access(hostRuntimeDirectory.c_str(), F_OK) == 0)
                    {
                        break;
                    }
                }
            }
#endif // defined(__NetBSD__)
#else
            ArrayHolder<CHAR> programFiles = new CHAR[MAX_LONGPATH];
            if (GetEnvironmentVariableA("PROGRAMFILES", programFiles, MAX_LONGPATH) == 0)
            {
                TraceError("PROGRAMFILES environment variable not found\n");
                return E_FAIL;
            }
            hostRuntimeDirectory.assign(programFiles);
            hostRuntimeDirectory.append("\\dotnet\\shared\\Microsoft.NETCore.App");
#endif // FEATURE_PAL
        }
        hostRuntimeDirectory.append(DIRECTORY_SEPARATOR_STR_A);

        // Start with the latest released version and then the LTS's.
        if (!FindDotNetVersion(5, 0, hostRuntimeDirectory))
        {
            // Find highest 3.1.x LTS version
            if (!FindDotNetVersion(3, 1, hostRuntimeDirectory))
            {
                // Find highest 2.1.x LTS version
                if (!FindDotNetVersion(2, 1, hostRuntimeDirectory))
                {
                    // Find highest 6.0.x version
                    if (!FindDotNetVersion(6, 0, hostRuntimeDirectory))
                    {
                        TraceError("Error: Failed to find runtime directory\n");
                        return E_FAIL;
                    }
                }
            }
        }

        // Save away the runtime version we are going to use to host the SOS managed code
        g_hostRuntimeDirectory = _strdup(hostRuntimeDirectory.c_str());
    }
    hostRuntimeDirectory.assign(g_hostRuntimeDirectory);
    coreClrPath.assign(g_hostRuntimeDirectory);
    coreClrPath.append(DIRECTORY_SEPARATOR_STR_A);
    coreClrPath.append(MAKEDLLNAME_A("coreclr"));
    return S_OK;
}

/**********************************************************************\
 * Initializes the host coreclr runtime
\**********************************************************************/
static HRESULT InitializeNetCoreHost()
{
    coreclr_initialize_ptr initializeCoreCLR = nullptr;
    coreclr_create_delegate_ptr createDelegate = nullptr;
    std::string hostRuntimeDirectory;
    std::string sosModuleDirectory;
    std::string extensionPath;
    std::string coreClrPath;

    HRESULT hr = GetHostRuntime(coreClrPath, hostRuntimeDirectory);
    if (FAILED(hr))
    {
        return hr;
    }
#ifdef FEATURE_PAL
    Dl_info info;
    if (dladdr((PVOID)&InitializeNetCoreHost, &info) == 0)
    {
        TraceError("Error: dladdr() failed getting current module directory\n");
        return E_FAIL;
    }
    sosModuleDirectory = info.dli_fname;
    extensionPath = info.dli_fname;

    void* coreclrLib = dlopen(coreClrPath.c_str(), RTLD_NOW | RTLD_LOCAL);
    if (coreclrLib == nullptr)
    {
        TraceError("Error: Failed to load %s\n", coreClrPath.c_str());
        return E_FAIL;
    }
    initializeCoreCLR = (coreclr_initialize_ptr)dlsym(coreclrLib, "coreclr_initialize");
    createDelegate = (coreclr_create_delegate_ptr)dlsym(coreclrLib, "coreclr_create_delegate");
#else
    ArrayHolder<char> szSOSModulePath = new char[MAX_LONGPATH + 1];
    if (GetModuleFileNameA(g_hInstance, szSOSModulePath, MAX_LONGPATH) == 0)
    {
        TraceError("Error: Failed to get SOS module directory\n");
        return HRESULT_FROM_WIN32(GetLastError());
    }
    sosModuleDirectory = szSOSModulePath;
    extensionPath = szSOSModulePath;

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
    size_t lastSlash = sosModuleDirectory.rfind(DIRECTORY_SEPARATOR_CHAR_A);
    if (lastSlash == std::string::npos)
    {
        TraceError("Error: Failed to parse sos module name\n");
        return E_FAIL;
    }
    sosModuleDirectory.erase(lastSlash);

    // Trust The SOS managed and dependent assemblies from the sos directory
    std::string tpaList;
    const char* directory = sosModuleDirectory.c_str();
    AddFileToTpaList(directory, "System.Reflection.Metadata.dll", tpaList);
    AddFileToTpaList(directory, "System.Collections.Immutable.dll", tpaList);
    AddFileToTpaList(directory, "Microsoft.FileFormats.dll", tpaList);
    AddFileToTpaList(directory, "Microsoft.SymbolStore.dll", tpaList);

    // Trust the runtime assemblies
    AddFilesFromDirectoryToTpaList(hostRuntimeDirectory.c_str(), tpaList);

    std::string appPaths;
    appPaths.append(sosModuleDirectory);

    const char *propertyKeys[] = {
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

    std::string entryPointExecutablePath;
    if (!GetEntrypointExecutableAbsolutePath(entryPointExecutablePath))
    {
        TraceError("Could not get full path to current executable");
        return E_FAIL;
    }

    void *hostHandle;
    unsigned int domainId;
    hr = initializeCoreCLR(entryPointExecutablePath.c_str(), "sos", 
        sizeof(propertyKeys) / sizeof(propertyKeys[0]), propertyKeys, propertyValues, &hostHandle, &domainId);

    if (FAILED(hr))
    {
        TraceError("Error: Fail to initialize coreclr %08x\n", hr);
        return hr;
    }

    ExtensionsInitializeDelegate extensionsInitializeFunc = nullptr;
    hr = createDelegate(hostHandle, domainId, ExtensionsDllName, ExtensionsClassName, ExtensionsInitializeFunctionName, (void**)&extensionsInitializeFunc);
    if (SUCCEEDED(hr)) 
    {
        try 
        {
            hr = extensionsInitializeFunc(extensionPath.c_str());
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
    if (g_hostingInitialized)
    {
        return S_OK;
    }
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
#if !defined(FEATURE_PAL) && !defined(_TARGET_ARM64_)
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
