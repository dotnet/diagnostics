// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//

#pragma once

#include <corhdr.h>
#include <string>
#include "host.h"
#include "hostservices.h"
#include "debuggerservices.h"
#include "symbolservice.h"

interface IRuntime;

enum HostRuntimeFlavor
{
    None,
    NetCore,
    NetFx
};

extern BOOL IsHostingInitialized();
extern HRESULT InitializeHosting();
extern LPCSTR GetHostRuntimeDirectory();
extern bool SetHostRuntimeDirectory(LPCSTR hostRuntimeDirectory);
extern HostRuntimeFlavor GetHostRuntimeFlavor();
extern bool SetHostRuntimeFlavor(HostRuntimeFlavor flavor);
extern bool GetAbsolutePath(const char* path, std::string& absolutePath);

#ifdef __cplusplus
extern "C" {
#endif

class Extensions
{
protected:
    static Extensions* s_extensions;

    IHost* m_pHost;
    ITarget* m_pTarget;
    IDebuggerServices* m_pDebuggerServices;
    IHostServices* m_pHostServices;
    ISymbolService* m_pSymbolService;

public:
    Extensions(IDebuggerServices* pDebuggerServices);
    virtual ~Extensions();

    /// <summary>
    /// Return the singleton extensions instance
    /// </summary>
    /// <returns></returns>
    static Extensions* GetInstance()
    {
        return s_extensions;
    }

    /// <summary>
    /// The extension host initialize callback function
    /// </summary>
    /// <param name="punk">IUnknown</param>
    /// <returns>error code</returns>
    HRESULT InitializeHostServices(IUnknown* punk);

    /// <summary>
    /// Returns the debugger services instance
    /// </summary>
    IDebuggerServices* GetDebuggerServices() 
    { 
        return m_pDebuggerServices;
    }

    /// <summary>
    /// Returns the host service provider or null
    /// </summary>
    virtual IHost* GetHost() = 0;

    /// <summary>
    /// Returns the extension service interface or null
    /// </summary>
    IHostServices* GetHostServices();

    /// <summary>
    /// Returns the symbol service instance
    /// </summary>
    ISymbolService* GetSymbolService();

    /// <summary>
    /// Create a new target with the extension services for  
    /// </summary>
    /// <returns>error result</returns>
    HRESULT CreateTarget();

    /// <summary>
    /// Creates and/or destroys the target based on the processId.
    /// </summary>
    /// <param name="processId">process id or 0 if none</param>
    /// <returns>error result</returns>
    HRESULT UpdateTarget(ULONG processId);

    /// <summary>
    /// Create a new target with the extension services for  
    /// </summary>
    void DestroyTarget();

    /// <summary>
    /// Returns the target instance
    /// </summary>
    ITarget* GetTarget();

    /// <summary>
    /// Flush the target
    /// </summary>
    void FlushTarget();

    /// <summary>
    /// Releases and clears the target 
    /// </summary>
    void ReleaseTarget();
};

inline IHost* GetHost()
{
    return Extensions::GetInstance()->GetHost();
}

inline IDebuggerServices* GetDebuggerServices()
{
    return Extensions::GetInstance()->GetDebuggerServices();
}

inline ITarget* GetTarget()
{
    return Extensions::GetInstance()->GetTarget();
} 

inline void ReleaseTarget()
{
    Extensions::GetInstance()->ReleaseTarget();
}

inline IHostServices* GetHostServices()
{
    return Extensions::GetInstance()->GetHostServices();
}

inline ISymbolService* GetSymbolService()
{
    return Extensions::GetInstance()->GetSymbolService();
}

extern HRESULT GetRuntime(IRuntime** ppRuntime);

#ifdef __cplusplus
}
#endif
