// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "managedanalysis.h"
#include <crosscontext.h>

_Use_decl_annotations_
ClrmaThread::ClrmaThread(ClrmaManagedAnalysis* managedAnalysis, ULONG osThreadId) :
    m_lRefs(1),
    m_managedAnalysis(managedAnalysis),
    m_osThreadId(osThreadId),
    m_lastThrownObject(0),
    m_firstNestedException(0),
    m_stackFramesInitialized(false),
    m_nestedExceptionsInitialized(false)
{
    _ASSERTE(osThreadId != 0 && osThreadId != (ULONG)-1);
    managedAnalysis->AddRef();
}

ClrmaThread::~ClrmaThread()
{
    TraceInformation("~ClrmaThread\n");
    if (m_managedAnalysis != nullptr)
    {
        m_managedAnalysis->Release();
        m_managedAnalysis = nullptr;
    }
}

/// <summary>
/// This function returns success if this thread is managed and caches some managed exception info away.
/// </summary>
HRESULT
ClrmaThread::Initialize()
{
    TraceInformation("ClrmaThread::Initialize %04x\n", m_osThreadId);
    HRESULT hr;
    DacpThreadStoreData threadStore;
    if (FAILED(hr = threadStore.Request(m_managedAnalysis->SosDacInterface())))
    {
        TraceError("ClrmaThread::Initialize GetThreadStoreData FAILED %08x\n", hr);
        return hr;
    }
    DacpThreadData thread;
    CLRDATA_ADDRESS curThread = threadStore.firstThread;
    while (curThread != 0)
    {
        if ((hr = thread.Request(m_managedAnalysis->SosDacInterface(), curThread)) != S_OK)
        {
            TraceError("ClrmaThread::Initialize GetThreadData FAILED %08x\n", hr);
            return hr;
        }
        if (thread.osThreadId == m_osThreadId)
        {
            if (thread.lastThrownObjectHandle != 0)
            {
                if (FAILED(hr = m_managedAnalysis->ReadPointer(thread.lastThrownObjectHandle, &m_lastThrownObject)))
                {
                    TraceError("ClrmaThread::Initialize ReadPointer FAILED %08x\n", hr);
                }
            }
            m_firstNestedException = thread.firstNestedException;
            return S_OK;
        }
        curThread = thread.nextThread;
    }
    TraceError("ClrmaThread::Initialize FAILED managed thread not found\n");
    return E_FAIL;
}

//
// IUnknown
//

_Use_decl_annotations_
STDMETHODIMP
ClrmaThread::QueryInterface(
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
    else if (IsEqualIID(InterfaceId, __uuidof(ICLRMAClrThread)))
    {
        *Interface = (ICLRMAClrThread*)this;
        AddRef();
        return S_OK;
    }

    return E_NOINTERFACE;
}

STDMETHODIMP_(ULONG)
ClrmaThread::AddRef()
{
    return (ULONG)InterlockedIncrement(&m_lRefs);
}

STDMETHODIMP_(ULONG)
ClrmaThread::Release()
{
    LONG lRefs = InterlockedDecrement(&m_lRefs);
    if (lRefs == 0)
    {
        delete this;
    }
    return lRefs;
}

//
// ICLRMAClrThread
//

_Use_decl_annotations_
STDMETHODIMP
ClrmaThread::get_DebuggerCommand(
    BSTR* pValue
    )
{
    if (pValue == nullptr)
    {
        return E_INVALIDARG;
    }
    *pValue = nullptr;
    return E_NOTIMPL;
}

_Use_decl_annotations_
STDMETHODIMP
ClrmaThread::get_OSThreadId(
    ULONG* pOSThreadId
    )
{
    if (pOSThreadId == nullptr)
    {
        return E_INVALIDARG;
    }
    *pOSThreadId = m_osThreadId;
    return S_OK;
}

_Use_decl_annotations_
STDMETHODIMP
ClrmaThread::get_FrameCount(
    UINT* pCount
    )
{
    TraceInformation("ClrmaThread::get_FrameCount\n");

    if (pCount == nullptr)
    {
        return E_INVALIDARG;
    }

    *pCount = 0;

    if (m_managedAnalysis == nullptr)
    {
        return E_UNEXPECTED;
    }

    if (!m_stackFramesInitialized)
    {
        m_stackFrames.clear();

        ReleaseHolder<IXCLRDataTask> pTask;
        HRESULT hr;
        if (SUCCEEDED(hr = m_managedAnalysis->ClrData()->GetTaskByOSThreadID(
            m_osThreadId,
            (IXCLRDataTask**)&pTask)))
        {
            ReleaseHolder<IXCLRDataStackWalk> pStackWalk;
            if (SUCCEEDED(hr = pTask->CreateStackWalk(
                CLRDATA_SIMPFRAME_UNRECOGNIZED |
                CLRDATA_SIMPFRAME_MANAGED_METHOD |
                CLRDATA_SIMPFRAME_RUNTIME_MANAGED_CODE |
                CLRDATA_SIMPFRAME_RUNTIME_UNMANAGED_CODE,
                (IXCLRDataStackWalk**)&pStackWalk)))
            {
                // For each managed stack frame
                int index = 0;
                int count = 0;
                do
                {
                    StackFrame frame;
                    frame.Frame = index;
                    if (FAILED(hr = GetFrameLocation(pStackWalk, &frame.IP, &frame.SP)))
                    {
                        TraceError("Unwind: GetFrameLocation() FAILED %08x\n", hr);
                        break;
                    }
                    // Only include normal frames, skipping any special frames
                    DacpFrameData frameData;
                    if (SUCCEEDED(hr = frameData.Request(pStackWalk)) && frameData.frameAddr != 0)
                    {
                        TraceInformation("Unwind: skipping special frame SP %016llx IP %016llx\n", frame.SP, frame.IP);
                        continue;
                    }
                    CLRDATA_ADDRESS methodDesc = 0;
                    if (FAILED(hr = m_managedAnalysis->SosDacInterface()->GetMethodDescPtrFromIP(frame.IP, &methodDesc)))
                    {
                        TraceInformation("Unwind: skipping frame GetMethodDescPtrFromIP(%016llx) FAILED %08x\n", frame.IP, hr);
                        continue;
                    }
                    // Get normal module and method names like MethodNameFromIP() does for !clrstack
                    if (FAILED(hr = m_managedAnalysis->GetMethodDescInfo(methodDesc, frame, /* stripFunctionParameters */ false)))
                    {
                        TraceInformation("Unwind: skipping frame GetMethodDescInfo(%016llx) FAILED %08x\n", methodDesc, hr);
                        continue;
                    }
                    m_stackFrames.push_back(frame);
                    index++;

                } while (count++ < MAX_STACK_FRAMES && pStackWalk->Next() == S_OK);
            }
            else
            {
                TraceError("Unwind: CreateStackWalk FAILED %08x\n", hr);
            }
        }
        else
        {
            TraceError("Unwind: GetTaskByOSThreadID FAILED %08x\n", hr);
        }

        m_stackFramesInitialized = true;
    }

    *pCount = (USHORT)m_stackFrames.size();

    return ((*pCount) != 0) ? S_OK : S_FALSE;
}

_Use_decl_annotations_
STDMETHODIMP
ClrmaThread::Frame(
    UINT nFrame,
    ULONG64* pAddrIP,
    ULONG64* pAddrSP,
    BSTR* bstrModule,
    BSTR* bstrFunction,
    ULONG64* pDisplacement
    )
{
    TraceInformation("ClrmaThread::Frame %d\n", nFrame);

    if (!pAddrIP || !pAddrSP || !bstrModule || !bstrFunction || !pDisplacement)
    {
        return E_INVALIDARG;
    }

    *pAddrIP = 0;
    *pAddrSP = 0;
    *bstrModule = nullptr;
    *bstrFunction = nullptr;
    *pDisplacement = 0;

    UINT nCount = 0;
    if (HRESULT hr = get_FrameCount(&nCount))
    {
        return hr;
    }

    if (nFrame >= nCount)
    {
        return E_BOUNDS;
    }

    BSTR moduleName = SysAllocString(m_stackFrames[nFrame].Module.c_str());
    if (moduleName == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    BSTR functionName = SysAllocString(m_stackFrames[nFrame].Function.c_str());
    if (functionName == nullptr)
    {
        SysFreeString(moduleName);
        return E_OUTOFMEMORY;
    }

    *pAddrIP = m_stackFrames[nFrame].IP;
    *pAddrSP = m_stackFrames[nFrame].SP;
    *bstrModule = moduleName;
    *bstrFunction = functionName;
    *pDisplacement = m_stackFrames[nFrame].Displacement;

    return S_OK;
}

_Use_decl_annotations_
STDMETHODIMP
ClrmaThread::get_CurrentException(
    ICLRMAClrException** ppClrException
    )
{
    TraceInformation("ClrmaThread::get_CurrentException\n");

    if (ppClrException == nullptr)
    {
        return E_INVALIDARG;
    }

    *ppClrException = nullptr;

    if (m_managedAnalysis == nullptr)
    {
        return E_UNEXPECTED;
    }

    if (m_lastThrownObject != 0)
    {
        ReleaseHolder<ClrmaException> exception = new (std::nothrow) ClrmaException(m_managedAnalysis, m_lastThrownObject);
        if (exception == nullptr)
        {
            return E_OUTOFMEMORY;
        }
        HRESULT hr;
        if (FAILED(hr = exception->QueryInterface(__uuidof(ICLRMAClrException), (void**)ppClrException)))
        {
            return hr;
        }
    }

    return ((*ppClrException) != nullptr) ? S_OK : S_FALSE;
}

_Use_decl_annotations_
STDMETHODIMP
ClrmaThread::get_NestedExceptionCount(
    USHORT* pCount
    )
{
    TraceInformation("ClrmaThread::get_NestedExceptionCount\n");

    if (pCount == nullptr)
    {
        return E_INVALIDARG;
    }

    *pCount = 0;

    if (m_managedAnalysis == nullptr)
    {
        return E_UNEXPECTED;
    }

    if (!m_nestedExceptionsInitialized)
    {
        m_nestedExceptions.clear();

        HRESULT hr;
        CLRDATA_ADDRESS currentNested = m_firstNestedException;
        while (currentNested != 0)
        {
            CLRDATA_ADDRESS obj = 0, next = 0;
            if (FAILED(hr = m_managedAnalysis->SosDacInterface()->GetNestedExceptionData(currentNested, &obj, &next)))
            {
                TraceError("get_NestedExceptionCount GetNestedExceptionData FAILED %08x\n", hr);
                return hr;
            }
            m_nestedExceptions.push_back(obj);
            currentNested = next;
        }

        m_nestedExceptionsInitialized = true;
    }

    *pCount = (USHORT)m_nestedExceptions.size();

    return ((*pCount) != 0) ? S_OK : S_FALSE;
}

_Use_decl_annotations_
STDMETHODIMP
ClrmaThread::NestedException(
    USHORT nIndex,
    ICLRMAClrException** ppClrException
    )
{
    TraceInformation("ClrmaThread::NestedException %d\n", nIndex);

    if (ppClrException == nullptr)
    {
        return E_INVALIDARG;
    }

    *ppClrException = nullptr;

    HRESULT hr;
    USHORT nCount = 0;
    if (hr = get_NestedExceptionCount(&nCount))
    {
        return hr;
    }

    if (nIndex >= nCount)
    {
        return E_BOUNDS;
    }

    ReleaseHolder<ClrmaException> exception = new (std::nothrow) ClrmaException(m_managedAnalysis, m_nestedExceptions[nIndex]);
    if (exception == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    if (FAILED(hr = exception->QueryInterface(__uuidof(ICLRMAClrException), (void**)ppClrException)))
    {
        return hr;
    }

    return ((*ppClrException) != nullptr) ? S_OK : S_FALSE;
}

HRESULT
ClrmaThread::GetFrameLocation(
    IXCLRDataStackWalk* pStackWalk,
    CLRDATA_ADDRESS* ip,
    CLRDATA_ADDRESS* sp)
{
    ULONG32 contextSize = 0;
    ULONG32 contextFlags = CONTEXT_ARM64_CONTROL;
    ULONG processorType = m_managedAnalysis->ProcessorType();
    switch (processorType)
    {
        case IMAGE_FILE_MACHINE_AMD64:
            contextSize = sizeof(AMD64_CONTEXT);
            contextFlags = 0x00100001;
            break;

        case IMAGE_FILE_MACHINE_ARM64:
            contextSize = sizeof(ARM64_CONTEXT);
            contextFlags = 0x00400001;
            break;

        case IMAGE_FILE_MACHINE_I386:
            contextSize = sizeof(X86_CONTEXT);
            contextFlags = 0x00010001;
            break;

        case IMAGE_FILE_MACHINE_ARM:
        case IMAGE_FILE_MACHINE_THUMB:
        case IMAGE_FILE_MACHINE_ARMNT:
            contextSize = sizeof(ARM_CONTEXT);
            contextFlags = 0x00200001;
            break;

        case IMAGE_FILE_MACHINE_RISCV64:
            contextSize = sizeof(RISCV64_CONTEXT);
            contextFlags = 0x01000001;
            break;

        case IMAGE_FILE_MACHINE_LOONGARCH64:
            contextSize = sizeof(LOONGARCH64_CONTEXT);
            contextFlags = 0x00800001;
            break;

        default:
            TraceError("GetFrameLocation: Invalid processor type %04x\n", processorType);
            return E_FAIL;
    }
    CROSS_PLATFORM_CONTEXT context;
    HRESULT hr = pStackWalk->GetContext(contextFlags, contextSize, nullptr, (BYTE *)&context);
    if (FAILED(hr))
    {
        TraceError("GetFrameLocation GetContext failed: %08x\n", hr);
        return hr;
    }
    if (hr == S_FALSE)
    {
        // GetContext returns S_FALSE if the frame iterator is invalid.  That's basically an error for us.
        TraceError("GetFrameLocation GetContext returned S_FALSE\n");
        return E_FAIL;
    }
    switch (processorType)
    {
        case IMAGE_FILE_MACHINE_AMD64:
            *ip = context.Amd64Context.Rip;
            *sp = context.Amd64Context.Rsp;
            break;

        case IMAGE_FILE_MACHINE_ARM64:
            *ip = context.Arm64Context.Pc;
            *sp = context.Arm64Context.Sp;
            break;

        case IMAGE_FILE_MACHINE_I386:
            *ip = context.X86Context.Eip;
            *sp = context.X86Context.Esp;
            break;

        case IMAGE_FILE_MACHINE_ARM:
        case IMAGE_FILE_MACHINE_THUMB:
        case IMAGE_FILE_MACHINE_ARMNT:
            *ip = context.ArmContext.Pc & ~THUMB_CODE;
            *sp = context.ArmContext.Sp;
            break;

        case IMAGE_FILE_MACHINE_RISCV64:
            *ip = context.RiscV64Context.Pc;
            *sp = context.RiscV64Context.Sp;
            break;

        case IMAGE_FILE_MACHINE_LOONGARCH64:
            *ip = context.LoongArch64Context.Pc;
            *sp = context.LoongArch64Context.Sp;
            break;
    }
    return S_OK;
}
