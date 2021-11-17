// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//

class DataTarget : public ICLRDataTarget2, ICorDebugDataTarget4, ICLRMetadataLocator, ICLRRuntimeLocator
{
private:
    LONG m_ref;                         // Reference count.
    ULONG64 m_baseAddress;              // Runtime base address

public:
    DataTarget(ULONG64 baseAddress);
    virtual ~DataTarget() {}
    
    // IUnknown.
    STDMETHOD(QueryInterface)(
        THIS_
        ___in REFIID InterfaceId,
        ___out PVOID* Interface
        );
    STDMETHOD_(ULONG, AddRef)(
        THIS
        );
    STDMETHOD_(ULONG, Release)(
        THIS
        );

    //
    // ICLRDataTarget.
    //
    
    virtual HRESULT STDMETHODCALLTYPE GetMachineType( 
        /* [out] */ ULONG32 *machine);

    virtual HRESULT STDMETHODCALLTYPE GetPointerSize( 
        /* [out] */ ULONG32 *size);

    virtual HRESULT STDMETHODCALLTYPE GetImageBase( 
        /* [string][in] */ LPCWSTR name,
        /* [out] */ CLRDATA_ADDRESS *base);

    virtual HRESULT STDMETHODCALLTYPE ReadVirtual( 
        /* [in] */ CLRDATA_ADDRESS address,
        /* [length_is][size_is][out] */ PBYTE buffer,
        /* [in] */ ULONG32 request,
        /* [optional][out] */ ULONG32 *done);

    virtual HRESULT STDMETHODCALLTYPE WriteVirtual( 
        /* [in] */ CLRDATA_ADDRESS address,
        /* [size_is][in] */ PBYTE buffer,
        /* [in] */ ULONG32 request,
        /* [optional][out] */ ULONG32 *done);

    virtual HRESULT STDMETHODCALLTYPE GetTLSValue(
        /* [in] */ ULONG32 threadID,
        /* [in] */ ULONG32 index,
        /* [out] */ CLRDATA_ADDRESS* value);

    virtual HRESULT STDMETHODCALLTYPE SetTLSValue(
        /* [in] */ ULONG32 threadID,
        /* [in] */ ULONG32 index,
        /* [in] */ CLRDATA_ADDRESS value);

    virtual HRESULT STDMETHODCALLTYPE GetCurrentThreadID(
        /* [out] */ ULONG32* threadID);

    virtual HRESULT STDMETHODCALLTYPE GetThreadContext(
        /* [in] */ ULONG32 threadID,
        /* [in] */ ULONG32 contextFlags,
        /* [in] */ ULONG32 contextSize,
        /* [out, size_is(contextSize)] */ PBYTE context);

    virtual HRESULT STDMETHODCALLTYPE SetThreadContext(
        /* [in] */ ULONG32 threadID,
        /* [in] */ ULONG32 contextSize,
        /* [in, size_is(contextSize)] */ PBYTE context);

    virtual HRESULT STDMETHODCALLTYPE Request( 
        /* [in] */ ULONG32 reqCode,
        /* [in] */ ULONG32 inBufferSize,
        /* [size_is][in] */ BYTE *inBuffer,
        /* [in] */ ULONG32 outBufferSize,
        /* [size_is][out] */ BYTE *outBuffer);

    // ICLRDataTarget2

    virtual HRESULT STDMETHODCALLTYPE AllocVirtual( 
            /* [in] */ CLRDATA_ADDRESS addr,
            /* [in] */ ULONG32 size,
            /* [in] */ ULONG32 typeFlags,
            /* [in] */ ULONG32 protectFlags,
            /* [out] */ CLRDATA_ADDRESS *virt);
        
    virtual HRESULT STDMETHODCALLTYPE FreeVirtual( 
            /* [in] */ CLRDATA_ADDRESS addr,
            /* [in] */ ULONG32 size,
            /* [in] */ ULONG32 typeFlags);

    // ICorDebugDataTarget4

    virtual HRESULT STDMETHODCALLTYPE VirtualUnwind(
        /* [in] */ DWORD threadId,
        /* [in] */ ULONG32 contextSize,
        /* [in, out, size_is(contextSize)] */ PBYTE context);

    // ICLRMetadataLocator

    virtual HRESULT STDMETHODCALLTYPE GetMetadata(
        /* [in] */ LPCWSTR imagePath,
        /* [in] */ ULONG32 imageTimestamp,
        /* [in] */ ULONG32 imageSize,
        /* [in] */ GUID* mvid,
        /* [in] */ ULONG32 mdRva,
        /* [in] */ ULONG32 flags,
        /* [in] */ ULONG32 bufferSize,
        /* [out, size_is(bufferSize), length_is(*dataSize)] */
        BYTE* buffer,
        /* [out] */ ULONG32* dataSize);

    // ICLRRuntimeLocator

    virtual HRESULT STDMETHODCALLTYPE GetRuntimeBase(
        /* [out] */ CLRDATA_ADDRESS* baseAddress);
};