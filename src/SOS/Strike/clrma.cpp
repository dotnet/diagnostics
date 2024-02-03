#include <windows.h>
#include <unknwn.h>
#include <clrma.h> // IDL
#include "exts.h"

HRESULT CLRMACreateInstance(ICLRManagedAnalysis** ppCLRMA);
HRESULT CLRMAReleaseInstance();

ICLRManagedAnalysis* g_managedAnalysis = nullptr;

//
// Exports
//

HRESULT CLRMACreateInstance(ICLRManagedAnalysis** ppCLRMA)
{
    HRESULT hr = E_UNEXPECTED;

    if (ppCLRMA == nullptr)
    {
        return E_INVALIDARG;
    }
    *ppCLRMA = nullptr;

    if (g_managedAnalysis == nullptr)
    {
        Extensions* extensions = Extensions::GetInstance();
        if (extensions == nullptr || extensions->GetDebuggerServices() == nullptr)
        {
            return E_FAIL;
        }
        ITarget* target = extensions->GetTarget();
        if (target == nullptr)
        {
            return E_FAIL;
        }
        hr = target->GetService(__uuidof(ICLRManagedAnalysis), (void**)&g_managedAnalysis);
        if (FAILED(hr))
        {
            return hr;
        }
    }
    g_managedAnalysis->AddRef();
    *ppCLRMA = g_managedAnalysis;
    return S_OK;
}

HRESULT CLRMAReleaseInstance()
{
    if (g_managedAnalysis != nullptr)
    {
        g_managedAnalysis->Release();
        g_managedAnalysis = nullptr;
    }
    return S_OK;
}
