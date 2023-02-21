// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#include "strike.h"
#include "util.h"
#include "targetimpl.h"
#include "host.h"
#include <string>

Target* Target::s_target = nullptr;

//----------------------------------------------------------------------------
// Target
//----------------------------------------------------------------------------

Target::Target() :
    m_ref(1),
    m_tmpPath(nullptr),
#ifndef FEATURE_PAL
    m_desktop(nullptr),
#endif
    m_netcore(nullptr)
{ 
}

Target::~Target()
{
    // Clean up the temporary directory files and DAC symlink.
    LPCSTR tmpPath = (LPCSTR)InterlockedExchangePointer((PVOID *)&m_tmpPath, nullptr);
    if (tmpPath != nullptr)
    {
        std::string directory(tmpPath);
        directory.append("*");

        WIN32_FIND_DATAA data;
        HANDLE findHandle = FindFirstFileA(directory.c_str(), &data);

        if (findHandle != INVALID_HANDLE_VALUE) 
        {
            do
            {
                if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == 0)
                {
                    std::string file(tmpPath);
                    file.append(data.cFileName);
                    DeleteFileA(file.c_str());
                }
            } 
            while (0 != FindNextFileA(findHandle, &data));

            FindClose(findHandle);
        }

        RemoveDirectoryA(tmpPath);
        free((void*)tmpPath);
    }
    if (m_netcore != nullptr)
    {
        m_netcore->Release();
        m_netcore = nullptr;
    }
#ifdef FEATURE_PAL
    FlushMetadataRegions();
#else
    if (m_desktop != nullptr)
    {
        m_desktop->Release();
        m_desktop = nullptr;
    }
#endif
    g_pRuntime = nullptr;
    s_target = nullptr;
}

/**********************************************************************\
* Creates a local target instance or returns the existing one
\**********************************************************************/
ITarget* Target::GetInstance()
{
    if (s_target == nullptr) 
    {
        s_target = new Target();
        OnUnloadTask::Register(CleanupTarget);
    }
    s_target->AddRef();
    return s_target;
}

/**********************************************************************\
 * Creates an instance of the runtime class. First it attempts to create
 * the .NET Core instance and if that fails, it will try to create the
 * desktop CLR instance.  If both runtimes exists in the process or dump
 * this runtime only creates the .NET Core version and leaves creating
 * the desktop instance on demand in SwitchRuntime.
\**********************************************************************/
HRESULT 
Target::CreateInstance(IRuntime **ppRuntime)
{
    HRESULT hr = S_OK;
    if (*ppRuntime == nullptr)
    {
        hr = Runtime::CreateInstance(this, IRuntime::Core, &m_netcore);
#ifdef FEATURE_PAL
        * ppRuntime = m_netcore;
#else
        ITarget::OperatingSystem os = this->GetOperatingSystem();
        switch (os)
        {
            case ITarget::OperatingSystem::Linux:
            case ITarget::OperatingSystem::OSX:
                // Only attempt to create linux/OSX core runtime if above failed
                if (m_netcore == nullptr)
                {
                    hr = Runtime::CreateInstance(this, IRuntime::UnixCore, &m_netcore);
                }
                break;
            case ITarget::OperatingSystem::Windows:
                // Always attempt to create desktop clr, but only return result the if the .NET Core create failed
                HRESULT hrDesktop = Runtime::CreateInstance(this, IRuntime::WindowsDesktop, &m_desktop);
                if (m_netcore == nullptr)
                {
                    hr = hrDesktop;
                }
                break;
        }
        *ppRuntime = m_netcore != nullptr ? m_netcore : m_desktop;
#endif
    }
    return hr;
}

/**********************************************************************\
 * Switches between the .NET Core and desktop runtimes (if both 
 * loaded). Creates the desktop CLR runtime instance on demand.
\**********************************************************************/
#ifndef FEATURE_PAL
bool Target::SwitchRuntimeInstance(bool desktop)
{
    IRuntime* runtime = desktop ? m_desktop : m_netcore;
    if (runtime == nullptr) {
        return false;
    }
    g_pRuntime = runtime;
    return true;
}
#endif

/**********************************************************************\
 * Display the internal target and runtime status
\**********************************************************************/
void Target::DisplayStatusInstance()
{
    static const char* osName[] = {
        "Unknown",
        "Windows",
        "Linux",
        "MacOS"
    };
    if (g_targetMachine != nullptr) {
        ExtOut("Target OS: %s Platform: %04x Context size: %04x\n", osName[GetOperatingSystem()], g_targetMachine->GetPlatform(), g_targetMachine->GetContextSize());
    }
    if (m_tmpPath != nullptr) {
        ExtOut("Temp path: %s\n", m_tmpPath);
    }
    if (m_netcore != nullptr) {
        m_netcore->DisplayStatus();
    }
#ifndef FEATURE_PAL
    if (m_desktop != nullptr) {
        m_desktop->DisplayStatus();
    }
#endif
}

//----------------------------------------------------------------------------
// IUnknown
//----------------------------------------------------------------------------

HRESULT Target::QueryInterface(
    REFIID InterfaceId,
    PVOID* Interface
    )
{
    if (InterfaceId == __uuidof(IUnknown) ||
        InterfaceId == __uuidof(ITarget))
    {
        *Interface = (ITarget*)this;
        AddRef();
        return S_OK;
    }
    else
    {
        *Interface = NULL;
        return E_NOINTERFACE;
    }
}

ULONG Target::AddRef()
{
    LONG ref = InterlockedIncrement(&m_ref);    
    return ref;
}

ULONG Target::Release()
{
    LONG ref = InterlockedDecrement(&m_ref);
    if (ref == 0)
    {
        delete this;
    }
    return ref;
}

//----------------------------------------------------------------------------
// ITarget
//----------------------------------------------------------------------------

#define VER_PLATFORM_UNIX 10 

ITarget::OperatingSystem Target::GetOperatingSystem()
{
#ifdef FEATURE_PAL
#if defined(__APPLE__)
    return OperatingSystem::OSX;
#elif defined(__linux__)
    return OperatingSystem::Linux;
#else
    return OperatingSystem::Unknown;
#endif
#else
    ULONG platformId, major, minor, servicePack;
    if (SUCCEEDED(g_ExtControl->GetSystemVersion(&platformId, &major, &minor, nullptr, 0, nullptr, &servicePack, nullptr, 0, nullptr)))
    {
        if (platformId == VER_PLATFORM_UNIX)
        {
            return OperatingSystem::Linux;
        }
    }
    return OperatingSystem::Windows;
#endif
}

HRESULT Target::GetService(REFIID serviceId, PVOID* ppService)
{
    return E_NOINTERFACE;
}

LPCSTR Target::GetTempDirectory()
{
    if (m_tmpPath == nullptr)
    {
        char tmpPath[MAX_LONGPATH];
        if (::GetTempPathA(MAX_LONGPATH, tmpPath) == 0)
        {
            // Use current directory if can't get temp path
            strcpy_s(tmpPath, MAX_LONGPATH, ".");
            strcat_s(tmpPath, MAX_LONGPATH, DIRECTORY_SEPARATOR_STR_A);
        }
        ULONG pid = 0;
        if (g_ExtSystem->GetCurrentProcessSystemId(&pid) != 0)
        {
            // Use the SOS process id if can't get target's
            pid = ::GetCurrentProcessId();
        }
        char pidstr[128];
        sprintf_s(pidstr, ARRAY_SIZE(pidstr), "sos%d", pid);
        strcat_s(tmpPath, MAX_LONGPATH, pidstr);
        strcat_s(tmpPath, MAX_LONGPATH, DIRECTORY_SEPARATOR_STR_A);

        CreateDirectoryA(tmpPath, NULL);
        m_tmpPath = _strdup(tmpPath);
    }
    return m_tmpPath;
}

HRESULT Target::GetRuntime(IRuntime** ppRuntime)
{
    return CreateInstance(ppRuntime);
}

void Target::Flush()
{
    if (m_netcore != nullptr) {
        m_netcore->Flush();
    }
#ifdef FEATURE_PAL
    FlushMetadataRegions();
#else
    if (m_desktop != nullptr) {
        m_desktop->Flush();
    }
#endif
}

bool IsWindowsTarget()
{
    ITarget* target = GetTarget();
    if (target != nullptr)
    {
        return target->GetOperatingSystem() == ITarget::OperatingSystem::Windows;
    }
    return false;
}
