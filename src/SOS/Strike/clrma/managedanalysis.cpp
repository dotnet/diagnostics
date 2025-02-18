// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "managedanalysis.h"

extern bool IsWindowsTarget();
extern "C" IXCLRDataProcess * GetClrDataFromDbgEng();

_Use_decl_annotations_
ClrmaManagedAnalysis::ClrmaManagedAnalysis() : 
    m_lRefs(1), 
    m_pointerSize(0),
    m_fileSeparator(0),
    m_processorType(0),
    m_debugClient(nullptr),
    m_debugData(nullptr),
    m_debugSystem(nullptr),
    m_debugControl(nullptr),
    m_debugSymbols(nullptr),
    m_clrmaService(nullptr),
    m_clrData(nullptr),
    m_sosDac(nullptr)
{
}

ClrmaManagedAnalysis::~ClrmaManagedAnalysis()
{
    TraceInformation("~ClrmaManagedAnalysis\n");
    ReleaseDebugClient();
}

HRESULT
ClrmaManagedAnalysis::QueryDebugClient(IUnknown* pUnknown)
{
    HRESULT hr;
    ReleaseHolder<IDebugClient> debugClient;
    if (FAILED(hr = pUnknown->QueryInterface(__uuidof(IDebugClient), (void**)&debugClient)))
    {
        return hr;
    }
    ReleaseHolder<IDebugDataSpaces> debugData;
    if (FAILED(hr = debugClient->QueryInterface(__uuidof(IDebugDataSpaces), (void**)&debugData)))
    {
        return hr;
    }
    ReleaseHolder<IDebugSystemObjects> debugSystem;
    if (FAILED(hr = debugClient->QueryInterface(__uuidof(IDebugSystemObjects), (void**)&debugSystem)))
    {
        return hr;
    }
    ReleaseHolder<IDebugControl> debugControl;
    if (FAILED(hr = debugClient->QueryInterface(__uuidof(IDebugControl), (void**)&debugControl)))
    {
        return hr;
    }
    ReleaseHolder<IDebugSymbols3> debugSymbols;
    if (FAILED(hr = debugClient->QueryInterface(__uuidof(IDebugSymbols3), (void**)&debugSymbols)))
    {
        return hr;
    }
    m_debugClient = debugClient.Detach();
    m_debugData = debugData.Detach();
    m_debugSystem = debugSystem.Detach();
    m_debugControl = debugControl.Detach();
    m_debugSymbols = debugSymbols.Detach();

    if (FAILED(hr = m_debugControl->GetExecutingProcessorType(&m_processorType)))
    {
        return hr;
    }
    switch (m_processorType)
    {
        case IMAGE_FILE_MACHINE_AMD64:
        case IMAGE_FILE_MACHINE_ARM64:
        case IMAGE_FILE_MACHINE_ARM64X:
        case IMAGE_FILE_MACHINE_ARM64EC:
        case IMAGE_FILE_MACHINE_LOONGARCH64:
        case IMAGE_FILE_MACHINE_RISCV64:
            m_pointerSize = 8;
            break;

        case IMAGE_FILE_MACHINE_I386:
        case IMAGE_FILE_MACHINE_ARM:
        case IMAGE_FILE_MACHINE_THUMB:
        case IMAGE_FILE_MACHINE_ARMNT:
            m_pointerSize = 4;
            break;

        default:
            return E_INVALIDARG;
    }
    if (IsWindowsTarget())
    {
        m_fileSeparator = L'\\';
    }
    else
    {
        m_fileSeparator = L'/';
    }
    return S_OK;
}

void
ClrmaManagedAnalysis::ReleaseDebugClient()
{
    if (m_clrData != nullptr)
    {
        m_clrData->Release();
        m_clrData = nullptr;
    }
    if (m_sosDac != nullptr)
    {
        m_sosDac->Release();
        m_sosDac = nullptr;
    }
    if (m_clrmaService != nullptr)
    {
        m_clrmaService->Release();
        m_clrmaService = nullptr;
    }
    if (m_debugSymbols != nullptr)
    {
        m_debugSymbols->Release();
        m_debugSymbols= nullptr;
    }
    if (m_debugControl != nullptr)
    {
        m_debugControl->Release();
        m_debugControl = nullptr;
    }
    if (m_debugSystem != nullptr)
    {
        m_debugSystem->Release();
        m_debugSystem = nullptr;
    }
    if (m_debugData != nullptr)
    {
        m_debugData->Release();
        m_debugData = nullptr;
    }
    if (m_debugClient != nullptr)
    {
        m_debugClient->Release();
        m_debugClient = nullptr;
    }
}

//
// IUnknown
//

_Use_decl_annotations_
STDMETHODIMP
ClrmaManagedAnalysis::QueryInterface(
    REFIID InterfaceId,
    PVOID* Interface
    )
{
    if (Interface == nullptr)
    {
        return E_INVALIDARG;
    }

    *Interface = nullptr;

    if (IsEqualIID(InterfaceId, IID_IUnknown))
    {
        *Interface = (IUnknown*)this;
        AddRef();
        return S_OK;
    }
    else if (IsEqualIID(InterfaceId, __uuidof(ICLRManagedAnalysis)))
    {
        *Interface = (ICLRManagedAnalysis*)this;
        AddRef();
        return S_OK;
    }

    return E_NOINTERFACE;
}

STDMETHODIMP_(ULONG)
ClrmaManagedAnalysis::AddRef()
{
    return (ULONG)InterlockedIncrement(&m_lRefs);
}

STDMETHODIMP_(ULONG)
ClrmaManagedAnalysis::Release()
{
    LONG lRefs = InterlockedDecrement(&m_lRefs);
    if (lRefs == 0)
    {
        delete this;
    }
    return lRefs;
}

//
// ICLRManagedAnalysis
//

_Use_decl_annotations_
STDMETHODIMP
ClrmaManagedAnalysis::AssociateClient(
    IUnknown* pUnknown
    )
{
    TraceInformation("ClrmaManagedAnalysis::AssociateClient\n");

    if (pUnknown == nullptr)
    {
        return E_INVALIDARG;
    }

    // Release previous client and DAC interfaces
    ReleaseDebugClient();

    // Setup the debugger client interfaces
    HRESULT hr;
    if (FAILED(hr = QueryDebugClient(pUnknown)))
    {
        TraceError("AssociateClient QueryDebugClient FAILED %08x\n", hr);
        return hr;
    }

    Extensions* extensions = Extensions::GetInstance();
    if (extensions != nullptr && extensions->GetDebuggerServices() != nullptr)
    {
        extensions->FlushCheck();

        ITarget* target = extensions->GetTarget();
        if (target != nullptr)
        {
            //
            // First try getting the managed CLRMA service instance
            //
            if (g_clrmaGlobalFlags & ClrmaGlobalFlags::ManagedClrmaEnabled)
            {
                TraceInformation("AssociateClient trying managed CLRMA\n");
                ReleaseHolder<ICLRMAService> clrmaService;
                if (SUCCEEDED(hr = target->GetService(__uuidof(ICLRMAService), (void**)&clrmaService)))
                {
                    if (SUCCEEDED(hr = clrmaService->AssociateClient(m_debugClient)))
                    {
                        m_clrmaService = clrmaService.Detach();
                        return S_OK;
                    }
                }
            }
            //
            // If there isn't a managed CLRMA service, use the DAC CLRMA implementation
            //
            if (g_clrmaGlobalFlags & ClrmaGlobalFlags::DacClrmaEnabled)
            {
                TraceInformation("AssociateClient trying DAC CLRMA\n");
                IRuntime* runtime = nullptr;
                if (FAILED(hr = target->GetRuntime(&runtime)))
                {
                    TraceError("AssociateClient GetRuntime FAILED %08x\n", hr);
                    return hr;
                }
                if (FAILED(hr = runtime->GetClrDataProcess(&m_clrData)))
                {
                    m_clrData = GetClrDataFromDbgEng();
                    if (m_clrData == nullptr)
                    {
                        TraceError("AssociateClient GetClrDataProcess FAILED %08x\n", hr);
                        return hr;
                    }
                }
                else
                {
                    m_clrData->AddRef();
                    m_clrData->Flush();
                }
                if (FAILED(hr = m_clrData->QueryInterface(__uuidof(ISOSDacInterface), (void**)&m_sosDac)))
                {
                    TraceError("AssociateClient QueryInterface ISOSDacInterface FAILED %08x\n", hr);
                    return hr;
                }
                // Ignore error getting the global objects method tables like ResetGlobals does. This can only
                // happen because the runtime globals containing them are not in the dump and we don't want to
                // fail this CLRMA API causing !analyze to fallback to the unstructured provider. We only
                // use m_usefulGlobals.ExceptionMethodTable and it will only slightly degrade the exception
                // stack unwinding experience.
                m_sosDac->GetUsefulGlobals(&m_usefulGlobals);
                return S_OK;
            }
        }
    }

    return E_NOINTERFACE;
}

_Use_decl_annotations_
STDMETHODIMP
ClrmaManagedAnalysis::get_ProviderName(
    BSTR* bstrProvider
    )
{
    TraceInformation("ClrmaManagedAnalysis::get_ProviderName\n");

    if (bstrProvider == nullptr)
    {
        return E_INVALIDARG;
    }

    *bstrProvider = SysAllocString(L"SOSCLRMA");

    if ((*bstrProvider) == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    return S_OK;
}

_Use_decl_annotations_
STDMETHODIMP
ClrmaManagedAnalysis::GetThread(
    ULONG osThreadId,
    ICLRMAClrThread** ppClrThread
)
{
    TraceInformation("ClrmaManagedAnalysis::GetThread %04x\n", osThreadId);

    if (ppClrThread == nullptr)
    {
        return E_INVALIDARG;
    }

    *ppClrThread = nullptr;

    if (m_debugClient == nullptr)
    {
        return E_UNEXPECTED;
    }

    // Current thread?
    HRESULT hr;
    if (osThreadId == 0)
    {
        ULONG tid;
        if (FAILED(hr = m_debugSystem->GetCurrentThreadSystemId(&tid)))
        {
            TraceError("GetThread GetCurrentThreadSystemId FAILED %08x\n", hr);
            return hr;
        }
        osThreadId = tid;
    }
    // Last event thread?
    else if (osThreadId == (ULONG)-1)
    {
        ULONG lastEventType = 0;
        ULONG lastEventProcessId = 0;
        ULONG lastEventThreadIdIndex = DEBUG_ANY_ID;
        if (FAILED(hr = m_debugControl->GetLastEventInformation(&lastEventType, &lastEventProcessId, &lastEventThreadIdIndex, NULL, 0, NULL, NULL, 0, NULL)))
        {
            TraceError("GetThread GetLastEventInformation FAILED %08x\n", hr);
            return hr;
        }
        if (lastEventThreadIdIndex == DEBUG_ANY_ID)
        {
            TraceError("GetThread lastEventThreadIdIndex == DEBUG_ANY_ID\n");
            return E_INVALIDARG;
        }
        ULONG ids = 0;
        ULONG sysIds = 0;
        if (FAILED(hr = m_debugSystem->GetThreadIdsByIndex(lastEventThreadIdIndex, 1, &ids, &sysIds)))
        {
            TraceError("GetThread GetThreadIdsByIndex FAILED %08x\n", hr);
            return hr;
        }
        osThreadId = sysIds;
    }

    if (m_clrmaService != nullptr)
    {
        if (FAILED(hr = m_clrmaService->GetThread(osThreadId, ppClrThread)))
        {
            TraceError("GetThread ICLRMAService::GetThread FAILED %08x\n", hr);
            return hr;
        }
    }
    else
    {
        ReleaseHolder<ClrmaThread> thread = new (std::nothrow) ClrmaThread(this, osThreadId);
        if (thread == nullptr)
        {
            return E_OUTOFMEMORY;
        }
        if (FAILED(hr = thread->Initialize()))
        {
            return hr;
        }
        if (FAILED(hr = thread->QueryInterface(__uuidof(ICLRMAClrThread), (void**)ppClrThread)))
        {
            TraceError("GetThread QueryInterface ICLRMAClrThread 1 FAILED %08x\n", hr);
            return hr;
        }
    }

    return S_OK;
}

_Use_decl_annotations_
STDMETHODIMP
ClrmaManagedAnalysis::GetException(
    ULONG64 address,
    ICLRMAClrException** ppClrException
    )
{
    TraceInformation("ClrmaManagedAnalysis::GetException %016llx\n", address);

    if (ppClrException == nullptr)
    {
        return E_INVALIDARG;
    }

    *ppClrException = nullptr;

    if (m_debugClient == nullptr)
    {
        return E_UNEXPECTED;
    }

    HRESULT hr;
    if (m_clrmaService != nullptr)
    {
        if (FAILED(hr = m_clrmaService->GetException(address, ppClrException)))
        {
            TraceError("GetException ICLRMAService::GetException FAILED %08x\n", hr);
            return hr;
        }
    }
    else
    {
        if (address == 0)
        {
            ReleaseHolder<ICLRMAClrThread> thread;
            if (FAILED(hr = GetThread(0, (ICLRMAClrThread**)&thread)))
            {
                TraceError("GetException GetThread FAILED %08x\n", hr);
                return hr;
            }
            if (FAILED(hr = thread->get_CurrentException(ppClrException)))
            {
                TraceError("GetException get_CurrentException FAILED %08x\n", hr);
                return hr;
            }
        }
        else
        {
            ReleaseHolder<ClrmaException> exception = new (std::nothrow) ClrmaException(this, address);
            if (exception == nullptr)
            {
                TraceError("GetException new ClrmaException FAILED\n");
                return E_OUTOFMEMORY;
            }
            if (FAILED(hr = exception->QueryInterface(__uuidof(ICLRMAClrException), (void**)ppClrException)))
            {
                TraceError("GetException QueryInterface ICLRMAClrException 1 FAILED %08x\n", hr);
                return hr;
            }
        }
    }

    return S_OK;
}

_Use_decl_annotations_
STDMETHODIMP
ClrmaManagedAnalysis::get_ObjectInspection(
    ICLRMAObjectInspection** ppObjectInspection)
{
    TraceInformation("ClrmaManagedAnalysis::get_ObjectInspection\n");

    if (ppObjectInspection == nullptr)
    {
        return E_INVALIDARG;
    }

    *ppObjectInspection = nullptr;

    if (m_debugClient == nullptr)
    {
        return E_UNEXPECTED;
    }

    if (m_clrmaService != nullptr)
    {
        return m_clrmaService->GetObjectInspection(ppObjectInspection);
    }

    return E_NOTIMPL;
}

HRESULT
ClrmaManagedAnalysis::GetMethodDescInfo(CLRDATA_ADDRESS methodDesc, StackFrame& frame, bool stripFunctionParameters)
{
    HRESULT hr;
    DacpMethodDescData methodDescData;
    if (SUCCEEDED(hr = methodDescData.Request(SosDacInterface(), methodDesc)))
    {
        // Don't compute the method displacement if IP is 0
        if (frame.IP > 0)
        {
            frame.Displacement = (frame.IP - methodDescData.NativeCodeAddr);
        }

        DacpModuleData moduleData;
        if (SUCCEEDED(hr = moduleData.Request(SosDacInterface(), methodDescData.ModulePtr)))
        {
            CLRDATA_ADDRESS baseAddress = 0;
            ULONG index = DEBUG_ANY_ID;
            if (FAILED(hr = SosDacInterface()->GetPEFileBase(moduleData.PEAssembly, &baseAddress)) || baseAddress == 0)
            {
                TraceInformation("GetMethodDescInfo(%016llx) GetPEFileBase %016llx FAILED %08x\n", methodDesc, moduleData.PEAssembly, hr);
                if (FAILED(hr = m_debugSymbols->GetModuleByOffset(frame.IP, 0, &index, &baseAddress)))
                {
                    TraceError("GetMethodDescInfo GetModuleByOffset FAILED %08x\n", hr);
                    baseAddress = 0;
                    index = DEBUG_ANY_ID;
                }
            }

            // Attempt to get the module name from the debugger
            ArrayHolder<WCHAR> wszModuleName = new WCHAR[MAX_LONGPATH + 1];
            if (baseAddress != 0 || index != DEBUG_ANY_ID)
            {
                if (SUCCEEDED(hr = m_debugSymbols->GetModuleNameStringWide(DEBUG_MODNAME_MODULE, index, baseAddress, wszModuleName, MAX_LONGPATH, nullptr)))
                {
                    frame.Module = wszModuleName;
                }
                else
                {
                    TraceError("GetMethodDescInfo(%016llx) GetModuleNameStringWide(%d, %016llx) FAILED %08x\n", methodDesc, index, baseAddress, hr);
                }
            }

            // Fallback if we can't get it from the debugger
            if (frame.Module.empty())
            {
                wszModuleName[0] = L'\0';
                if (FAILED(hr = SosDacInterface()->GetPEFileName(moduleData.PEAssembly, MAX_LONGPATH, wszModuleName, nullptr)))
                {
                    TraceInformation("GetMethodDescInfo(%016llx) GetPEFileName(%016llx) FAILED %08x\n", methodDesc, moduleData.PEAssembly, hr);
                    ReleaseHolder<IXCLRDataModule> pModule;
                    if (SUCCEEDED(hr = SosDacInterface()->GetModule(moduleData.Address, (IXCLRDataModule**)&pModule)))
                    {
                        ULONG32 nameLen = 0;
                        if (FAILED(hr = pModule->GetFileName(MAX_LONGPATH, &nameLen, wszModuleName)))
                        {
                            TraceError("GetMethodDescInfo IXCLRDataModule::GetFileName FAILED %08x\n", hr);
                        }
                    }
                    else
                    {
                        TraceError("GetMethodDescInfo GetModule FAILED %08x\n", hr);
                    }
                }
                if (wszModuleName[0] != L'\0')
                {
                    frame.Module = wszModuleName;
                    _ASSERTE(m_fileSeparator != 0);
                    size_t nameStart = frame.Module.find_last_of(m_fileSeparator);
                    if (nameStart != -1)
                    {
                        frame.Module = frame.Module.substr(nameStart + 1);
                    }
                }
            }
        }
        else
        {
            TraceError("GetMethodDescInfo(%016llx) ISOSDacInterface::GetModuleData FAILED %08x\n", methodDesc, hr);
        }

        ArrayHolder<WCHAR> wszNameBuffer = new WCHAR[MAX_LONGPATH + 1];
        if (SUCCEEDED(hr = SosDacInterface()->GetMethodDescName(methodDesc, MAX_LONGPATH, wszNameBuffer, NULL)))
        {
            frame.Function = wszNameBuffer;

            // Under certain circumstances DacpMethodDescData::GetMethodDescName() returns a module qualified method name
            size_t nameStart = frame.Function.find_first_of(L'!');
            if (nameStart != -1)
            {
                // Fallback to using the module name from the function name
                if (frame.Module.empty())
                {
                    frame.Module = frame.Function.substr(0, nameStart);
                }
                // Now strip the module name from the function name. Need to do this after the module name fallback
                frame.Function = frame.Function.substr(nameStart + 1);
            }

            // Strip off the function parameters
            if (stripFunctionParameters)
            {
                size_t parameterStart = frame.Function.find_first_of(L'(');
                if (parameterStart != -1)
                {
                    frame.Function = frame.Function.substr(0, parameterStart);
                }
            }
        }
        else
        {
            TraceError("GetMethodDescInfo(%016llx) ISOSDacInterface::GetMethodDescName FAILED %08x\n", methodDesc, hr);
        }
    }
    else
    {
        TraceError("GetMethodDescInfo(%016llx) ISOSDacInterface::GetMethodDescData FAILED %08x\n", methodDesc, hr);
    }
    if (frame.Module.empty())
    {
        frame.Module = L"UNKNOWN";
    }
    if (frame.Function.empty())
    {
        frame.Function = L"UNKNOWN";
    }
    return S_OK;
}

CLRDATA_ADDRESS
ClrmaManagedAnalysis::IsExceptionObj(CLRDATA_ADDRESS mtObj)
{
    CLRDATA_ADDRESS walkMT = mtObj;
    DacpMethodTableData dmtd;
    HRESULT hr;

    // We want to follow back until we get the mt for System.Exception
    while (walkMT != NULL)
    {
        if (FAILED(hr = dmtd.Request(SosDacInterface(), walkMT)))
        {
            TraceError("IsExceptionObj ISOSDacInterface::GetMethodDescData FAILED %08x\n", hr);
            break;
        }
        if (walkMT == m_usefulGlobals.ExceptionMethodTable)
        {
            return walkMT;
        }
        walkMT = dmtd.ParentMethodTable;
    }

    return 0;
}

WCHAR*
ClrmaManagedAnalysis::GetStringObject(CLRDATA_ADDRESS stringObject)
{
    if (stringObject == 0)
    {
        return nullptr;
    }
    HRESULT hr;
    DacpObjectData objData;
    if (FAILED(hr = objData.Request(SosDacInterface(), stringObject)))
    {
        TraceError("GetStringObject ISOSDacInterface::GetObjectData FAILED %08x\n", hr);
        return nullptr;
    }
    if (objData.Size > 0x200000)
    {
        TraceError("GetStringObject object size (%08llx) > 0x200000\n", objData.Size);
        return nullptr;
    }
    // Ignore the HRESULT because this function fails with E_INVALIDARG but still returns cbNeeded.
    UINT32 cbNeeded = 0;
    SosDacInterface()->GetObjectStringData(stringObject, 0, nullptr, &cbNeeded);
    if (cbNeeded <= 0 || cbNeeded > 0x200000)
    {
        TraceError("GetStringObject needed (%08x) > 0x200000\n", cbNeeded);
        return nullptr;
    }
    ArrayHolder<WCHAR> stringBuffer = new (std::nothrow) WCHAR[cbNeeded];
    if (stringBuffer == nullptr)
    {
        TraceError("GetStringObject out of memory\n");
        return nullptr;
    }
    if (FAILED(hr = SosDacInterface()->GetObjectStringData(stringObject, cbNeeded, stringBuffer, nullptr)))
    {
        TraceError("GetStringObject ISOSDacInterface::GetObjectStringData FAILED %08x\n", hr);
        return nullptr;
    }
    return stringBuffer.Detach();
}

HRESULT
ClrmaManagedAnalysis::ReadPointer(CLRDATA_ADDRESS address, CLRDATA_ADDRESS* pointer)
{
    _ASSERTE(pointer != nullptr);
    _ASSERTE(m_pointerSize == 4 || m_pointerSize == 8);
    *pointer = 0;
    return m_debugData->ReadVirtual(address, pointer, m_pointerSize, nullptr);
}
