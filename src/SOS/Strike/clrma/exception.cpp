// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "managedanalysis.h"

extern BOOL IsAsyncException(const DacpExceptionObjectData& excData);

_Use_decl_annotations_
ClrmaException::ClrmaException(ClrmaManagedAnalysis* managedAnalysis, ULONG64 address) :
    m_lRefs(1),
    m_managedAnalysis(managedAnalysis),
    m_address(address),
    m_typeName(nullptr),
    m_message(nullptr),
    m_exceptionDataInitialized(false),
    m_stackFramesInitialized(false),
    m_innerExceptionsInitialized(false)
{
    _ASSERTE(address != 0);
    managedAnalysis->AddRef();
}

ClrmaException::~ClrmaException()
{
    TraceInformation("~ClrmaException\n");
    if (m_typeName != nullptr)
    {
        delete [] m_typeName;
        m_typeName = nullptr;
    }
    if (m_message != nullptr)
    {
        delete [] m_message;
        m_message = nullptr;
    }
    if (m_managedAnalysis != nullptr)
    {
        m_managedAnalysis->Release();
        m_managedAnalysis = nullptr;
    }
}

/// <summary>
/// This called by each clrma exception methods to initialize and cache the exception data.
/// </summary>
HRESULT
ClrmaException::Initialize()
{
    if (m_managedAnalysis == nullptr)
    {
        return E_UNEXPECTED;
    }
    if (!m_exceptionDataInitialized)
    {
        TraceInformation("ClrmaException::Initialize %016llx\n", m_address);

        HRESULT hr;
        DacpObjectData objData;
        if (FAILED(hr = objData.Request(m_managedAnalysis->SosDacInterface(), m_address)))
        {
            TraceError("ClrmaException::Initialize GetObjectData FAILED %08x\n", hr);
            return hr;
        }

        if (m_managedAnalysis->IsExceptionObj(objData.MethodTable) != 0)
        {
            if (FAILED(hr = m_exceptionData.Request(m_managedAnalysis->SosDacInterface(), m_address)))
            {
                TraceError("ClrmaException::Initialize GetObjectExceptionData FAILED %08x\n", hr);
                return hr;
            }
            UINT cbTypeName = 0;
            if (SUCCEEDED(hr = m_managedAnalysis->SosDacInterface()->GetMethodTableName(objData.MethodTable, 0, nullptr, &cbTypeName)))
            {
                ArrayHolder<WCHAR> typeName = new (std::nothrow)WCHAR[cbTypeName];
                if (typeName != nullptr)
                {
                    if (SUCCEEDED(hr = m_managedAnalysis->SosDacInterface()->GetMethodTableName(objData.MethodTable, cbTypeName, typeName, nullptr)))
                    {
                        m_typeName = typeName.Detach();
                    }
                    else
                    {
                        TraceError("ClrmaException::Initialize GetMethodTableName(%016x) 2 FAILED %08x\n", objData.MethodTable, hr);
                    }
                }
            }
            else
            {
                TraceError("ClrmaException::Initialize GetMethodTableName(%016x) 1 FAILED %08x\n", objData.MethodTable, hr);
            }
            if (m_exceptionData.Message == 0)
            {
                // To match the built-in SOS provider that scrapes !pe output.
                const WCHAR* none = L"<none>";
                m_message = new (std::nothrow) WCHAR[wcslen(none) + 1];
                wcscpy(m_message, none);
            }
            else
            {
                m_message = m_managedAnalysis->GetStringObject(m_exceptionData.Message);
            }
        }
        m_exceptionDataInitialized = true;
    }
    return S_OK;
}

// IUnknown
_Use_decl_annotations_
STDMETHODIMP
ClrmaException::QueryInterface(
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
    else if (IsEqualIID(InterfaceId, __uuidof(ICLRMAClrException)))
    {
        *Interface = (ICLRMAClrException*)this;
        AddRef();
        return S_OK;
    }

    return E_NOINTERFACE;
}

STDMETHODIMP_(ULONG)
ClrmaException::AddRef()
{
    return (ULONG)InterlockedIncrement(&m_lRefs);
}

STDMETHODIMP_(ULONG)
ClrmaException::Release()
{
    LONG lRefs = InterlockedDecrement(&m_lRefs);
    if (lRefs == 0)
    {
        delete this;
    }
    return lRefs;
}

//
// ICLRMAClrException
//

_Use_decl_annotations_
STDMETHODIMP
ClrmaException::get_DebuggerCommand(
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
ClrmaException::get_Address(
    ULONG64* pValue
    )
{
    if (pValue == nullptr)
    {
        return E_INVALIDARG;
    }
    *pValue = m_address;
    return S_OK;
}

_Use_decl_annotations_
STDMETHODIMP
ClrmaException::get_HResult(
    HRESULT* pValue
    )
{
    if (pValue == nullptr)
    {
        return E_INVALIDARG;
    }

    *pValue = 0;

    HRESULT hr;
    if (FAILED(hr = Initialize()))
    {
        return hr;
    }

    *pValue = m_exceptionData.HResult;
    return S_OK;
}

_Use_decl_annotations_
STDMETHODIMP
ClrmaException::get_Type(
    BSTR* pValue
    )
{
    if (pValue == nullptr)
    {
        return E_INVALIDARG;
    }

    *pValue = nullptr;

    HRESULT hr;
    if (FAILED(hr = Initialize()))
    {
        return hr;
    }

    const WCHAR* typeName = m_typeName;
    if (typeName == nullptr)
    {
        // To match the built-in SOS provider that scrapes !pe output
        typeName = L"<Unknown>";
    }

    *pValue = SysAllocString(typeName);

    return ((*pValue) != nullptr) ? S_OK : S_FALSE;
}

_Use_decl_annotations_
STDMETHODIMP
ClrmaException::get_Message(
    BSTR* pValue
    )
{
    if (pValue == nullptr)
    {
        return E_INVALIDARG;
    }

    *pValue = nullptr;

    HRESULT hr;
    if (FAILED(hr = Initialize()))
    {
        return hr;
    }

    if (m_message != nullptr)
    {
        *pValue = SysAllocString(m_message);
    }

    return ((*pValue) != nullptr) ? S_OK : S_FALSE;
}

_Use_decl_annotations_
STDMETHODIMP
ClrmaException::get_FrameCount(
    UINT* pCount
    )
{
    TraceInformation("ClrmaException::get_FrameCount\n");

    if (pCount == nullptr)
    {
        return E_INVALIDARG;
    }

    *pCount = 0;

    HRESULT hr;
    if (FAILED(hr = Initialize()))
    {
        return hr;
    }

    if (!m_stackFramesInitialized)
    {
        GetStackFrames();
        m_stackFramesInitialized = true;
    }

    *pCount = (USHORT)m_stackFrames.size();

    return ((*pCount) != 0) ? S_OK : S_FALSE;
}

_Use_decl_annotations_
STDMETHODIMP
ClrmaException::Frame(
    UINT nFrame,
    ULONG64* pAddrIP,
    ULONG64* pAddrSP,
    BSTR* bstrModule,
    BSTR* bstrFunction,
    ULONG64* pDisplacement
    )
{
    TraceInformation("ClrmaException::Frame %d\n", nFrame);

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
ClrmaException::get_InnerExceptionCount(
    USHORT* pCount
    )
{
    TraceInformation("ClrmaException::get_InnerExceptionCount\n");

    if (pCount == nullptr)
    {
        return E_INVALIDARG;
    }

    *pCount = 0;

    HRESULT hr;
    if (FAILED(hr = Initialize()))
    {
        return hr;
    }

    if (!m_innerExceptionsInitialized)
    {
        m_innerExceptions.clear();

        if (m_exceptionData.InnerException != 0)
        {
            m_innerExceptions.push_back(m_exceptionData.InnerException);
        }

        m_innerExceptionsInitialized = true;
    }

    *pCount = (USHORT)m_innerExceptions.size();

    return ((*pCount) != 0) ? S_OK : S_FALSE;
}

_Use_decl_annotations_
STDMETHODIMP
ClrmaException::InnerException(
    USHORT nIndex,
    ICLRMAClrException** ppClrException)
{
    TraceInformation("ClrmaException::InnerException %d\n", nIndex);

    if (ppClrException == nullptr)
    {
        return E_INVALIDARG;
    }

    *ppClrException = nullptr;

    HRESULT hr;
    USHORT nCount = 0;
    if (hr = get_InnerExceptionCount(&nCount))
    {
        return hr;
    }

    if (nIndex >= nCount)
    {
        return E_BOUNDS;
    }

    ReleaseHolder<ClrmaException> exception = new (std::nothrow) ClrmaException(m_managedAnalysis, m_innerExceptions[nIndex]);
    if (exception == nullptr)
    {
        return E_OUTOFMEMORY;
    }

    if (FAILED(hr = exception->QueryInterface(__uuidof(ICLRMAClrException), (void**)ppClrException)))
    {
        return hr;
    }

    return S_OK;
}

HRESULT
ClrmaException::GetStackFrames()
{
    HRESULT hr;

    m_stackFrames.clear();

    if (m_exceptionData.StackTrace == 0)
    {
        return S_OK;
    }

    DacpObjectData arrayObjData;
    if (FAILED(hr = arrayObjData.Request(m_managedAnalysis->SosDacInterface(), m_exceptionData.StackTrace)))
    {
        TraceError("ClrmaException::GetStackFrames GetObjectData(%016llx) FAILED %08x\n", m_exceptionData.StackTrace, hr);
        return hr;
    }
        
    if (arrayObjData.ObjectType != OBJ_ARRAY || arrayObjData.dwNumComponents == 0)
    {
        TraceError("ClrmaException::GetStackFrames StackTrace not array or empty\n");
        return E_FAIL;
    }
    CLRDATA_ADDRESS arrayDataPtr = arrayObjData.ArrayDataPtr;

    // If the stack trace is object[] (.NET 9 or greater), the StackTraceElement array is referenced by the first entry
    if (arrayObjData.ElementTypeHandle == m_managedAnalysis->ObjectMethodTable())
    {
        if (FAILED(hr = m_managedAnalysis->ReadPointer(arrayDataPtr, &arrayDataPtr)))
        {
            TraceError("ClrmaException::GetStackFrames ReadPointer(%016llx) FAILED %08x\n", arrayDataPtr, hr);
            return hr;
        }
    }

    bool bAsync = IsAsyncException(m_exceptionData);

    if (m_managedAnalysis->PointerSize() == 8)
    {
        StackTrace64 stackTrace;
        if (FAILED(hr = m_managedAnalysis->ReadMemory(arrayDataPtr, &stackTrace, sizeof(StackTrace64))))
        {
            TraceError("ClrmaException::GetStackFrames ReadMemory(%016llx) StackTrace64 FAILED %08x\n", arrayDataPtr, hr);
            return hr;
        }
        if (stackTrace.m_size > 0)
        {
            CLRDATA_ADDRESS elementPtr = arrayDataPtr + offsetof(StackTrace64, m_elements);
            for (ULONG i = 0; i < MAX_STACK_FRAMES && i < stackTrace.m_size; i++)
            {
                StackTraceElement64 stackTraceElement;
                if (SUCCEEDED(hr = m_managedAnalysis->ReadMemory(elementPtr, &stackTraceElement, sizeof(StackTraceElement64))))
                {
                    StackFrame frame;
                    frame.Frame = i;
                    frame.SP = stackTraceElement.sp;
                    frame.IP = stackTraceElement.ip;
                    if ((m_managedAnalysis->ProcessorType() == IMAGE_FILE_MACHINE_AMD64) && bAsync)
                    {
                        frame.IP++;
                    }
                    if (SUCCEEDED(hr = m_managedAnalysis->GetMethodDescInfo(stackTraceElement.pFunc, frame, /* stripFunctionParameters */ true)))
                    {
                        m_stackFrames.push_back(frame);
                    }
                }
                else
                {
                    TraceError("ClrmaException::GetStackFrames ReadMemory(%016llx) StackTraceElement64 FAILED %08x\n", elementPtr, hr);
                }
                elementPtr += sizeof(StackTraceElement64);
            }
        }
    }
    else
    {
        StackTrace32 stackTrace;
        if (FAILED(hr = m_managedAnalysis->ReadMemory(arrayDataPtr, &stackTrace, sizeof(StackTrace32))))
        {
            TraceError("ClrmaException::GetStackFrames ReadMemory(%016llx) StackTrace32 FAILED %08x\n", arrayDataPtr, hr);
            return hr;
        }
        if (stackTrace.m_size > 0)
        {
            CLRDATA_ADDRESS elementPtr = arrayDataPtr + offsetof(StackTrace32, m_elements);
            for (ULONG i = 0; i < MAX_STACK_FRAMES && i < stackTrace.m_size; i++)
            {
                StackTraceElement32 stackTraceElement;
                if (SUCCEEDED(hr = m_managedAnalysis->ReadMemory(elementPtr, &stackTraceElement, sizeof(StackTraceElement32))))
                {
                    StackFrame frame;
                    frame.Frame = i;
                    frame.SP = stackTraceElement.sp;
                    frame.IP = stackTraceElement.ip;
                    if ((m_managedAnalysis->ProcessorType() == IMAGE_FILE_MACHINE_I386) && (!bAsync || i != 0))
                    {
                        frame.IP++;
                    }
                    if (SUCCEEDED(hr = m_managedAnalysis->GetMethodDescInfo(stackTraceElement.pFunc, frame, /* stripFunctionParameters */ true)))
                    {
                        m_stackFrames.push_back(frame);
                    }
                }
                else
                {
                    TraceError("ClrmaException::GetStackFrames ReadMemory(%016llx) StackTraceElement32 FAILED %08x\n", elementPtr, hr);
                }
                elementPtr += sizeof(StackTraceElement32);
            }
        }
    }

    return S_OK;
}
