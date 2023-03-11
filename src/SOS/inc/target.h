// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include <stdarg.h>
#include <unknwn.h>

#ifdef __cplusplus
extern "C" {
#endif

struct IRuntime;

/// <summary>
/// ITarget - the native target interface
/// </summary>
MIDL_INTERFACE("B4640016-6CA0-468E-BA2C-1FFF28DE7B72")
ITarget : public IUnknown
{
public:
    /// <summary>
    /// Target OS values. Must match TargetWrapper.OperatingSystem
    /// </summary>
    enum OperatingSystem
    {
        Unknown         = 0,
        Windows         = 1,
        Linux           = 2,
        OSX             = 3,
    };

    /// <summary>
    /// Returns the OperatingSystem for the target
    /// </summary>
    /// <returns>target operating system</returns>
    virtual OperatingSystem STDMETHODCALLTYPE GetOperatingSystem() = 0;

    /// <summary>
    /// Returns the per-target native service for the given interface 
    /// id. There is only a limited set of services that can be queried 
    /// through this function. Adds a reference like QueryInterface.
    /// </summary>
    /// <param name="serviceId">guid of the service</param>
    /// <param name="service">pointer to return service instance</param>
    /// <returns>S_OK or E_NOINTERFACE</returns>
    virtual HRESULT STDMETHODCALLTYPE GetService(REFIID serviceId, PVOID* service) = 0;

    /// <summary>
    /// Returns the unique temporary directory for this instance of SOS
    /// </summary>
    /// <returns>temporary directory string</returns>
    virtual LPCSTR STDMETHODCALLTYPE GetTempDirectory() = 0;

    /// <summary>
    /// Returns the current runtime instance
    /// </summary>
    /// <param name="ppRuntime">pointer to return IRuntime instance</param>
    /// <returns>error code</returns>
    virtual HRESULT STDMETHODCALLTYPE GetRuntime(IRuntime** ppRuntime) = 0;

    /// <summary>
    /// Flushes any internal caching or state 
    /// </summary>
    virtual void STDMETHODCALLTYPE Flush() = 0;
};

#ifdef __cplusplus
};
#endif
