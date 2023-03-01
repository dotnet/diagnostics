// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "strike.h"
#include "util.h"
#include "targetimpl.h"
#include "hostimpl.h"

Host* Host::s_host = nullptr;

//----------------------------------------------------------------------------
// Host
//----------------------------------------------------------------------------

Host::Host() :
    m_ref(1)
{ 
}

Host::~Host()
{
    s_host = nullptr;
}

/// <summary>
/// Creates a local service provider instance or returns the existing one 
/// </summary>
/// <returns></returns>
IHost* Host::GetInstance()
{
    if (s_host == nullptr) 
    {
        s_host = new Host();
    }
    s_host->AddRef();
    return s_host;
}

//----------------------------------------------------------------------------
// IUnknown
//----------------------------------------------------------------------------

HRESULT Host::QueryInterface(
    REFIID InterfaceId,
    PVOID* Interface
    )
{
    if (InterfaceId == __uuidof(IUnknown) ||
        InterfaceId == __uuidof(IHost))
    {
        *Interface = (IHost*)this;
        AddRef();
        return S_OK;
    }
    else
    {
        *Interface = NULL;
        return E_NOINTERFACE;
    }
}

ULONG Host::AddRef()
{
    LONG ref = InterlockedIncrement(&m_ref);    
    return ref;
}

ULONG Host::Release()
{
    LONG ref = InterlockedDecrement(&m_ref);
    if (ref == 0)
    {
        delete this;
    }
    return ref;
}

//----------------------------------------------------------------------------
// IHost
//----------------------------------------------------------------------------

IHost::HostType Host::GetHostType()
{
#ifdef FEATURE_PAL
    return HostType::Lldb;
#else
    return HostType::DbgEng;
#endif
}

HRESULT Host::GetService(REFIID serviceId, PVOID* ppService)
{
    return E_NOINTERFACE;
}

HRESULT Host::GetCurrentTarget(ITarget** ppTarget)
{
    if (ppTarget == nullptr)
    {
        return E_INVALIDARG;
    }
    *ppTarget = Target::GetInstance();
    return S_OK;
}
