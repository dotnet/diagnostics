// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#pragma once

#include <stdarg.h>
#include <unknwn.h>

#ifdef __cplusplus
extern "C" {
#endif

struct ITarget;

/// <summary>
/// IHost - Provides native services from the host to SOS.
/// </summary>
MIDL_INTERFACE("E0CD8534-A88B-40D7-91BA-1B4C925761E9")
IHost : public IUnknown
{
public:
    /// <summary>
    /// The type hosting the native SOS code. Must match HostType.
    /// </summary>
    enum HostType
    {
        DotnetDump,
        Lldb,
        DbgEng,
        Vs
    };

    /// <summary>
    /// Returns the host type
    /// </summary>
    virtual HostType STDMETHODCALLTYPE GetHostType() = 0;

    /// <summary>
    /// Returns the global native service for the given interface id. There
    /// is only a limited set of services that can be queried through this
    /// function. Adds a reference like QueryInterface.
    /// </summary>
    /// <param name="serviceId">guid of the service</param>
    /// <param name="service">pointer to return service instance</param>
    /// <returns>S_OK or E_NOINTERFACE</returns>
    virtual HRESULT STDMETHODCALLTYPE GetService(REFIID serviceId, PVOID* service) = 0;

    /// <summary>
    /// Returns the current target instance or null. Adds a reference.
    /// </summary>
    /// <param name="ppTarget">pointer to write current target instance</param>
    /// <returns>error code</returns>
    virtual HRESULT STDMETHODCALLTYPE GetCurrentTarget(ITarget** ppTarget) = 0;
};

#ifdef __cplusplus
};
#endif
