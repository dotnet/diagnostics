

/* this ALWAYS GENERATED file contains the definitions for the interfaces */


 /* File created by MIDL compiler version 8.01.0628 */
/* at Mon Jan 18 19:14:07 2038
 */
/* Compiler settings for C:\ssd\reliability.banganalyze\src\CLRMA\Interface\idl\CLRMA.idl:
    Oicf, W1, Zp8, env=Win64 (32b run), target_arch=AMD64 8.01.0628 
    protocol : dce , ms_ext, c_ext, robust
    error checks: allocation ref bounds_check enum stub_data 
    VC __declspec() decoration level: 
         __declspec(uuid()), __declspec(selectany), __declspec(novtable)
         DECLSPEC_UUID(), MIDL_INTERFACE()
*/
/* @@MIDL_FILE_HEADING(  ) */

#pragma warning( disable: 4049 )  /* more than 64k source lines */


/* verify that the <rpcndr.h> version is high enough to compile this file*/
#ifndef __REQUIRED_RPCNDR_H_VERSION__
#define __REQUIRED_RPCNDR_H_VERSION__ 475
#endif

#include "rpc.h"
#include "rpcndr.h"

#ifndef __RPCNDR_H_VERSION__
#error this stub requires an updated version of <rpcndr.h>
#endif /* __RPCNDR_H_VERSION__ */


#ifndef __CLRMA_h__
#define __CLRMA_h__

#if defined(_MSC_VER) && (_MSC_VER >= 1020)
#pragma once
#endif

#ifndef DECLSPEC_XFGVIRT
#if defined(_CONTROL_FLOW_GUARD_XFG)
#define DECLSPEC_XFGVIRT(base, func) __declspec(xfg_virtual(base, func))
#else
#define DECLSPEC_XFGVIRT(base, func)
#endif
#endif

/* Forward Declarations */ 

#ifndef __ICLRManagedAnalysis_FWD_DEFINED__
#define __ICLRManagedAnalysis_FWD_DEFINED__
typedef interface ICLRManagedAnalysis ICLRManagedAnalysis;

#endif 	/* __ICLRManagedAnalysis_FWD_DEFINED__ */


#ifndef __ICLRMAClrThread_FWD_DEFINED__
#define __ICLRMAClrThread_FWD_DEFINED__
typedef interface ICLRMAClrThread ICLRMAClrThread;

#endif 	/* __ICLRMAClrThread_FWD_DEFINED__ */


#ifndef __ICLRMAClrException_FWD_DEFINED__
#define __ICLRMAClrException_FWD_DEFINED__
typedef interface ICLRMAClrException ICLRMAClrException;

#endif 	/* __ICLRMAClrException_FWD_DEFINED__ */


#ifndef __ICLRMAObjectInspection_FWD_DEFINED__
#define __ICLRMAObjectInspection_FWD_DEFINED__
typedef interface ICLRMAObjectInspection ICLRMAObjectInspection;

#endif 	/* __ICLRMAObjectInspection_FWD_DEFINED__ */


/* header files for imported files */
#include "unknwn.h"

#ifdef __cplusplus
extern "C"{
#endif 



#ifndef __CLRMA_LIBRARY_DEFINED__
#define __CLRMA_LIBRARY_DEFINED__

/* library CLRMA */
/* [helpstring][version][uuid] */ 






EXTERN_C const IID LIBID_CLRMA;

#ifndef __ICLRManagedAnalysis_INTERFACE_DEFINED__
#define __ICLRManagedAnalysis_INTERFACE_DEFINED__

/* interface ICLRManagedAnalysis */
/* [object][helpstring][uuid] */ 


EXTERN_C const IID IID_ICLRManagedAnalysis;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("8CA73A16-C017-4c8f-AD51-B758727478CA")
    ICLRManagedAnalysis : public IUnknown
    {
    public:
        virtual /* [propget] */ HRESULT STDMETHODCALLTYPE get_ProviderName( 
            /* [retval][out] */ BSTR *bstrProvider) = 0;
        
        virtual /* [helpstring] */ HRESULT STDMETHODCALLTYPE AssociateClient( 
            /* [in] */ IUnknown *pUnknown) = 0;
        
        virtual /* [helpstring] */ HRESULT STDMETHODCALLTYPE GetThread( 
            /* [in] */ ULONG osThreadID,
            /* [retval][out] */ ICLRMAClrThread **ppClrThread) = 0;
        
        virtual /* [helpstring] */ HRESULT STDMETHODCALLTYPE GetException( 
            /* [in] */ ULONG64 addr,
            /* [retval][out] */ ICLRMAClrException **ppClrException) = 0;
        
        virtual /* [propget][helpstring] */ HRESULT STDMETHODCALLTYPE get_ObjectInspection( 
            /* [retval][out] */ ICLRMAObjectInspection **ppObjectInspection) = 0;
        
    };
    
    
#else 	/* C style interface */

    typedef struct ICLRManagedAnalysisVtbl
    {
        BEGIN_INTERFACE
        
        DECLSPEC_XFGVIRT(IUnknown, QueryInterface)
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            ICLRManagedAnalysis * This,
            /* [in] */ REFIID riid,
            /* [annotation][iid_is][out] */ 
            _COM_Outptr_  void **ppvObject);
        
        DECLSPEC_XFGVIRT(IUnknown, AddRef)
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            ICLRManagedAnalysis * This);
        
        DECLSPEC_XFGVIRT(IUnknown, Release)
        ULONG ( STDMETHODCALLTYPE *Release )( 
            ICLRManagedAnalysis * This);
        
        DECLSPEC_XFGVIRT(ICLRManagedAnalysis, get_ProviderName)
        /* [propget] */ HRESULT ( STDMETHODCALLTYPE *get_ProviderName )( 
            ICLRManagedAnalysis * This,
            /* [retval][out] */ BSTR *bstrProvider);
        
        DECLSPEC_XFGVIRT(ICLRManagedAnalysis, AssociateClient)
        /* [helpstring] */ HRESULT ( STDMETHODCALLTYPE *AssociateClient )( 
            ICLRManagedAnalysis * This,
            /* [in] */ IUnknown *pUnknown);
        
        DECLSPEC_XFGVIRT(ICLRManagedAnalysis, GetThread)
        /* [helpstring] */ HRESULT ( STDMETHODCALLTYPE *GetThread )( 
            ICLRManagedAnalysis * This,
            /* [in] */ ULONG osThreadID,
            /* [retval][out] */ ICLRMAClrThread **ppClrThread);
        
        DECLSPEC_XFGVIRT(ICLRManagedAnalysis, GetException)
        /* [helpstring] */ HRESULT ( STDMETHODCALLTYPE *GetException )( 
            ICLRManagedAnalysis * This,
            /* [in] */ ULONG64 addr,
            /* [retval][out] */ ICLRMAClrException **ppClrException);
        
        DECLSPEC_XFGVIRT(ICLRManagedAnalysis, get_ObjectInspection)
        /* [propget][helpstring] */ HRESULT ( STDMETHODCALLTYPE *get_ObjectInspection )( 
            ICLRManagedAnalysis * This,
            /* [retval][out] */ ICLRMAObjectInspection **ppObjectInspection);
        
        END_INTERFACE
    } ICLRManagedAnalysisVtbl;

    interface ICLRManagedAnalysis
    {
        CONST_VTBL struct ICLRManagedAnalysisVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define ICLRManagedAnalysis_QueryInterface(This,riid,ppvObject)	\
    ( (This)->lpVtbl -> QueryInterface(This,riid,ppvObject) ) 

#define ICLRManagedAnalysis_AddRef(This)	\
    ( (This)->lpVtbl -> AddRef(This) ) 

#define ICLRManagedAnalysis_Release(This)	\
    ( (This)->lpVtbl -> Release(This) ) 


#define ICLRManagedAnalysis_get_ProviderName(This,bstrProvider)	\
    ( (This)->lpVtbl -> get_ProviderName(This,bstrProvider) ) 

#define ICLRManagedAnalysis_AssociateClient(This,pUnknown)	\
    ( (This)->lpVtbl -> AssociateClient(This,pUnknown) ) 

#define ICLRManagedAnalysis_GetThread(This,osThreadID,ppClrThread)	\
    ( (This)->lpVtbl -> GetThread(This,osThreadID,ppClrThread) ) 

#define ICLRManagedAnalysis_GetException(This,addr,ppClrException)	\
    ( (This)->lpVtbl -> GetException(This,addr,ppClrException) ) 

#define ICLRManagedAnalysis_get_ObjectInspection(This,ppObjectInspection)	\
    ( (This)->lpVtbl -> get_ObjectInspection(This,ppObjectInspection) ) 

#endif /* COBJMACROS */


#endif 	/* C style interface */




#endif 	/* __ICLRManagedAnalysis_INTERFACE_DEFINED__ */


#ifndef __ICLRMAClrThread_INTERFACE_DEFINED__
#define __ICLRMAClrThread_INTERFACE_DEFINED__

/* interface ICLRMAClrThread */
/* [object][helpstring][uuid] */ 


EXTERN_C const IID IID_ICLRMAClrThread;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("9849CFC9-0868-406e-9059-6B04E9ADBBB8")
    ICLRMAClrThread : public IUnknown
    {
    public:
        virtual /* [propget] */ HRESULT STDMETHODCALLTYPE get_DebuggerCommand( 
            /* [retval][out] */ BSTR *pValue) = 0;
        
        virtual /* [propget] */ HRESULT STDMETHODCALLTYPE get_OSThreadId( 
            /* [retval][out] */ ULONG *pValue) = 0;
        
        virtual /* [propget] */ HRESULT STDMETHODCALLTYPE get_FrameCount( 
            /* [retval][out] */ UINT *pCount) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Frame( 
            /* [in] */ UINT nFrame,
            /* [out] */ ULONG64 *pAddrIP,
            /* [out] */ ULONG64 *pAddrSP,
            /* [out] */ BSTR *bstrModule,
            /* [out] */ BSTR *bstrFunction,
            /* [out] */ ULONG64 *pDisplacement) = 0;
        
        virtual /* [propget] */ HRESULT STDMETHODCALLTYPE get_CurrentException( 
            /* [retval][out] */ ICLRMAClrException **ppClrException) = 0;
        
        virtual /* [propget] */ HRESULT STDMETHODCALLTYPE get_NestedExceptionCount( 
            /* [retval][out] */ USHORT *pCount) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE NestedException( 
            /* [in] */ USHORT nIndex,
            /* [retval][out] */ ICLRMAClrException **ppClrException) = 0;
        
    };
    
    
#else 	/* C style interface */

    typedef struct ICLRMAClrThreadVtbl
    {
        BEGIN_INTERFACE
        
        DECLSPEC_XFGVIRT(IUnknown, QueryInterface)
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            ICLRMAClrThread * This,
            /* [in] */ REFIID riid,
            /* [annotation][iid_is][out] */ 
            _COM_Outptr_  void **ppvObject);
        
        DECLSPEC_XFGVIRT(IUnknown, AddRef)
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            ICLRMAClrThread * This);
        
        DECLSPEC_XFGVIRT(IUnknown, Release)
        ULONG ( STDMETHODCALLTYPE *Release )( 
            ICLRMAClrThread * This);
        
        DECLSPEC_XFGVIRT(ICLRMAClrThread, get_DebuggerCommand)
        /* [propget] */ HRESULT ( STDMETHODCALLTYPE *get_DebuggerCommand )( 
            ICLRMAClrThread * This,
            /* [retval][out] */ BSTR *pValue);
        
        DECLSPEC_XFGVIRT(ICLRMAClrThread, get_OSThreadId)
        /* [propget] */ HRESULT ( STDMETHODCALLTYPE *get_OSThreadId )( 
            ICLRMAClrThread * This,
            /* [retval][out] */ ULONG *pValue);
        
        DECLSPEC_XFGVIRT(ICLRMAClrThread, get_FrameCount)
        /* [propget] */ HRESULT ( STDMETHODCALLTYPE *get_FrameCount )( 
            ICLRMAClrThread * This,
            /* [retval][out] */ UINT *pCount);
        
        DECLSPEC_XFGVIRT(ICLRMAClrThread, Frame)
        HRESULT ( STDMETHODCALLTYPE *Frame )( 
            ICLRMAClrThread * This,
            /* [in] */ UINT nFrame,
            /* [out] */ ULONG64 *pAddrIP,
            /* [out] */ ULONG64 *pAddrSP,
            /* [out] */ BSTR *bstrModule,
            /* [out] */ BSTR *bstrFunction,
            /* [out] */ ULONG64 *pDisplacement);
        
        DECLSPEC_XFGVIRT(ICLRMAClrThread, get_CurrentException)
        /* [propget] */ HRESULT ( STDMETHODCALLTYPE *get_CurrentException )( 
            ICLRMAClrThread * This,
            /* [retval][out] */ ICLRMAClrException **ppClrException);
        
        DECLSPEC_XFGVIRT(ICLRMAClrThread, get_NestedExceptionCount)
        /* [propget] */ HRESULT ( STDMETHODCALLTYPE *get_NestedExceptionCount )( 
            ICLRMAClrThread * This,
            /* [retval][out] */ USHORT *pCount);
        
        DECLSPEC_XFGVIRT(ICLRMAClrThread, NestedException)
        HRESULT ( STDMETHODCALLTYPE *NestedException )( 
            ICLRMAClrThread * This,
            /* [in] */ USHORT nIndex,
            /* [retval][out] */ ICLRMAClrException **ppClrException);
        
        END_INTERFACE
    } ICLRMAClrThreadVtbl;

    interface ICLRMAClrThread
    {
        CONST_VTBL struct ICLRMAClrThreadVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define ICLRMAClrThread_QueryInterface(This,riid,ppvObject)	\
    ( (This)->lpVtbl -> QueryInterface(This,riid,ppvObject) ) 

#define ICLRMAClrThread_AddRef(This)	\
    ( (This)->lpVtbl -> AddRef(This) ) 

#define ICLRMAClrThread_Release(This)	\
    ( (This)->lpVtbl -> Release(This) ) 


#define ICLRMAClrThread_get_DebuggerCommand(This,pValue)	\
    ( (This)->lpVtbl -> get_DebuggerCommand(This,pValue) ) 

#define ICLRMAClrThread_get_OSThreadId(This,pValue)	\
    ( (This)->lpVtbl -> get_OSThreadId(This,pValue) ) 

#define ICLRMAClrThread_get_FrameCount(This,pCount)	\
    ( (This)->lpVtbl -> get_FrameCount(This,pCount) ) 

#define ICLRMAClrThread_Frame(This,nFrame,pAddrIP,pAddrSP,bstrModule,bstrFunction,pDisplacement)	\
    ( (This)->lpVtbl -> Frame(This,nFrame,pAddrIP,pAddrSP,bstrModule,bstrFunction,pDisplacement) ) 

#define ICLRMAClrThread_get_CurrentException(This,ppClrException)	\
    ( (This)->lpVtbl -> get_CurrentException(This,ppClrException) ) 

#define ICLRMAClrThread_get_NestedExceptionCount(This,pCount)	\
    ( (This)->lpVtbl -> get_NestedExceptionCount(This,pCount) ) 

#define ICLRMAClrThread_NestedException(This,nIndex,ppClrException)	\
    ( (This)->lpVtbl -> NestedException(This,nIndex,ppClrException) ) 

#endif /* COBJMACROS */


#endif 	/* C style interface */




#endif 	/* __ICLRMAClrThread_INTERFACE_DEFINED__ */


#ifndef __ICLRMAClrException_INTERFACE_DEFINED__
#define __ICLRMAClrException_INTERFACE_DEFINED__

/* interface ICLRMAClrException */
/* [object][helpstring][uuid] */ 


EXTERN_C const IID IID_ICLRMAClrException;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("7C165652-D539-472e-A6CF-F657FFF31751")
    ICLRMAClrException : public IUnknown
    {
    public:
        virtual /* [propget] */ HRESULT STDMETHODCALLTYPE get_DebuggerCommand( 
            /* [retval][out] */ BSTR *pValue) = 0;
        
        virtual /* [propget] */ HRESULT STDMETHODCALLTYPE get_Address( 
            /* [retval][out] */ ULONG64 *pValue) = 0;
        
        virtual /* [propget] */ HRESULT STDMETHODCALLTYPE get_HResult( 
            /* [retval][out] */ HRESULT *pValue) = 0;
        
        virtual /* [propget] */ HRESULT STDMETHODCALLTYPE get_Type( 
            /* [retval][out] */ BSTR *pValue) = 0;
        
        virtual /* [propget] */ HRESULT STDMETHODCALLTYPE get_Message( 
            /* [retval][out] */ BSTR *pValue) = 0;
        
        virtual /* [propget] */ HRESULT STDMETHODCALLTYPE get_FrameCount( 
            /* [retval][out] */ UINT *pCount) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE Frame( 
            /* [in] */ UINT nFrame,
            /* [out] */ ULONG64 *pAddrIP,
            /* [out] */ ULONG64 *pAddrSP,
            /* [out] */ BSTR *pModule,
            /* [out] */ BSTR *pFunction,
            /* [out] */ ULONG64 *pDisplacement) = 0;
        
        virtual /* [propget] */ HRESULT STDMETHODCALLTYPE get_InnerExceptionCount( 
            /* [retval][out] */ USHORT *pCount) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE InnerException( 
            /* [in] */ USHORT nIndex,
            /* [retval][out] */ ICLRMAClrException **ppClrException) = 0;
        
    };
    
    
#else 	/* C style interface */

    typedef struct ICLRMAClrExceptionVtbl
    {
        BEGIN_INTERFACE
        
        DECLSPEC_XFGVIRT(IUnknown, QueryInterface)
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            ICLRMAClrException * This,
            /* [in] */ REFIID riid,
            /* [annotation][iid_is][out] */ 
            _COM_Outptr_  void **ppvObject);
        
        DECLSPEC_XFGVIRT(IUnknown, AddRef)
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            ICLRMAClrException * This);
        
        DECLSPEC_XFGVIRT(IUnknown, Release)
        ULONG ( STDMETHODCALLTYPE *Release )( 
            ICLRMAClrException * This);
        
        DECLSPEC_XFGVIRT(ICLRMAClrException, get_DebuggerCommand)
        /* [propget] */ HRESULT ( STDMETHODCALLTYPE *get_DebuggerCommand )( 
            ICLRMAClrException * This,
            /* [retval][out] */ BSTR *pValue);
        
        DECLSPEC_XFGVIRT(ICLRMAClrException, get_Address)
        /* [propget] */ HRESULT ( STDMETHODCALLTYPE *get_Address )( 
            ICLRMAClrException * This,
            /* [retval][out] */ ULONG64 *pValue);
        
        DECLSPEC_XFGVIRT(ICLRMAClrException, get_HResult)
        /* [propget] */ HRESULT ( STDMETHODCALLTYPE *get_HResult )( 
            ICLRMAClrException * This,
            /* [retval][out] */ HRESULT *pValue);
        
        DECLSPEC_XFGVIRT(ICLRMAClrException, get_Type)
        /* [propget] */ HRESULT ( STDMETHODCALLTYPE *get_Type )( 
            ICLRMAClrException * This,
            /* [retval][out] */ BSTR *pValue);
        
        DECLSPEC_XFGVIRT(ICLRMAClrException, get_Message)
        /* [propget] */ HRESULT ( STDMETHODCALLTYPE *get_Message )( 
            ICLRMAClrException * This,
            /* [retval][out] */ BSTR *pValue);
        
        DECLSPEC_XFGVIRT(ICLRMAClrException, get_FrameCount)
        /* [propget] */ HRESULT ( STDMETHODCALLTYPE *get_FrameCount )( 
            ICLRMAClrException * This,
            /* [retval][out] */ UINT *pCount);
        
        DECLSPEC_XFGVIRT(ICLRMAClrException, Frame)
        HRESULT ( STDMETHODCALLTYPE *Frame )( 
            ICLRMAClrException * This,
            /* [in] */ UINT nFrame,
            /* [out] */ ULONG64 *pAddrIP,
            /* [out] */ ULONG64 *pAddrSP,
            /* [out] */ BSTR *pModule,
            /* [out] */ BSTR *pFunction,
            /* [out] */ ULONG64 *pDisplacement);
        
        DECLSPEC_XFGVIRT(ICLRMAClrException, get_InnerExceptionCount)
        /* [propget] */ HRESULT ( STDMETHODCALLTYPE *get_InnerExceptionCount )( 
            ICLRMAClrException * This,
            /* [retval][out] */ USHORT *pCount);
        
        DECLSPEC_XFGVIRT(ICLRMAClrException, InnerException)
        HRESULT ( STDMETHODCALLTYPE *InnerException )( 
            ICLRMAClrException * This,
            /* [in] */ USHORT nIndex,
            /* [retval][out] */ ICLRMAClrException **ppClrException);
        
        END_INTERFACE
    } ICLRMAClrExceptionVtbl;

    interface ICLRMAClrException
    {
        CONST_VTBL struct ICLRMAClrExceptionVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define ICLRMAClrException_QueryInterface(This,riid,ppvObject)	\
    ( (This)->lpVtbl -> QueryInterface(This,riid,ppvObject) ) 

#define ICLRMAClrException_AddRef(This)	\
    ( (This)->lpVtbl -> AddRef(This) ) 

#define ICLRMAClrException_Release(This)	\
    ( (This)->lpVtbl -> Release(This) ) 


#define ICLRMAClrException_get_DebuggerCommand(This,pValue)	\
    ( (This)->lpVtbl -> get_DebuggerCommand(This,pValue) ) 

#define ICLRMAClrException_get_Address(This,pValue)	\
    ( (This)->lpVtbl -> get_Address(This,pValue) ) 

#define ICLRMAClrException_get_HResult(This,pValue)	\
    ( (This)->lpVtbl -> get_HResult(This,pValue) ) 

#define ICLRMAClrException_get_Type(This,pValue)	\
    ( (This)->lpVtbl -> get_Type(This,pValue) ) 

#define ICLRMAClrException_get_Message(This,pValue)	\
    ( (This)->lpVtbl -> get_Message(This,pValue) ) 

#define ICLRMAClrException_get_FrameCount(This,pCount)	\
    ( (This)->lpVtbl -> get_FrameCount(This,pCount) ) 

#define ICLRMAClrException_Frame(This,nFrame,pAddrIP,pAddrSP,pModule,pFunction,pDisplacement)	\
    ( (This)->lpVtbl -> Frame(This,nFrame,pAddrIP,pAddrSP,pModule,pFunction,pDisplacement) ) 

#define ICLRMAClrException_get_InnerExceptionCount(This,pCount)	\
    ( (This)->lpVtbl -> get_InnerExceptionCount(This,pCount) ) 

#define ICLRMAClrException_InnerException(This,nIndex,ppClrException)	\
    ( (This)->lpVtbl -> InnerException(This,nIndex,ppClrException) ) 

#endif /* COBJMACROS */


#endif 	/* C style interface */




#endif 	/* __ICLRMAClrException_INTERFACE_DEFINED__ */


#ifndef __ICLRMAObjectInspection_INTERFACE_DEFINED__
#define __ICLRMAObjectInspection_INTERFACE_DEFINED__

/* interface ICLRMAObjectInspection */
/* [object][helpstring][uuid] */ 


EXTERN_C const IID IID_ICLRMAObjectInspection;

#if defined(__cplusplus) && !defined(CINTERFACE)
    
    MIDL_INTERFACE("836259DB-7452-4b2b-95C4-4BC52CB9ABC7")
    ICLRMAObjectInspection : public IUnknown
    {
    public:
        virtual HRESULT STDMETHODCALLTYPE GetType( 
            /* [in] */ ULONG64 addr,
            /* [retval][out] */ BSTR *pValue) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetAddressFromCcwAddress( 
            /* [in] */ ULONG64 addr,
            /* [retval][out] */ ULONG64 *pAddress) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetField_SystemString( 
            /* [in] */ ULONG64 addr,
            /* [in] */ BSTR bstrField,
            /* [retval][out] */ BSTR *pValue) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetField_SystemUInt32( 
            /* [in] */ ULONG64 addr,
            /* [in] */ BSTR bstrField,
            /* [retval][out] */ ULONG *pValue) = 0;
        
        virtual HRESULT STDMETHODCALLTYPE GetField_SystemInt32( 
            /* [in] */ ULONG64 addr,
            /* [in] */ BSTR bstrField,
            /* [retval][out] */ LONG *pValue) = 0;
        
    };
    
    
#else 	/* C style interface */

    typedef struct ICLRMAObjectInspectionVtbl
    {
        BEGIN_INTERFACE
        
        DECLSPEC_XFGVIRT(IUnknown, QueryInterface)
        HRESULT ( STDMETHODCALLTYPE *QueryInterface )( 
            ICLRMAObjectInspection * This,
            /* [in] */ REFIID riid,
            /* [annotation][iid_is][out] */ 
            _COM_Outptr_  void **ppvObject);
        
        DECLSPEC_XFGVIRT(IUnknown, AddRef)
        ULONG ( STDMETHODCALLTYPE *AddRef )( 
            ICLRMAObjectInspection * This);
        
        DECLSPEC_XFGVIRT(IUnknown, Release)
        ULONG ( STDMETHODCALLTYPE *Release )( 
            ICLRMAObjectInspection * This);
        
        DECLSPEC_XFGVIRT(ICLRMAObjectInspection, GetType)
        HRESULT ( STDMETHODCALLTYPE *GetType )( 
            ICLRMAObjectInspection * This,
            /* [in] */ ULONG64 addr,
            /* [retval][out] */ BSTR *pValue);
        
        DECLSPEC_XFGVIRT(ICLRMAObjectInspection, GetAddressFromCcwAddress)
        HRESULT ( STDMETHODCALLTYPE *GetAddressFromCcwAddress )( 
            ICLRMAObjectInspection * This,
            /* [in] */ ULONG64 addr,
            /* [retval][out] */ ULONG64 *pAddress);
        
        DECLSPEC_XFGVIRT(ICLRMAObjectInspection, GetField_SystemString)
        HRESULT ( STDMETHODCALLTYPE *GetField_SystemString )( 
            ICLRMAObjectInspection * This,
            /* [in] */ ULONG64 addr,
            /* [in] */ BSTR bstrField,
            /* [retval][out] */ BSTR *pValue);
        
        DECLSPEC_XFGVIRT(ICLRMAObjectInspection, GetField_SystemUInt32)
        HRESULT ( STDMETHODCALLTYPE *GetField_SystemUInt32 )( 
            ICLRMAObjectInspection * This,
            /* [in] */ ULONG64 addr,
            /* [in] */ BSTR bstrField,
            /* [retval][out] */ ULONG *pValue);
        
        DECLSPEC_XFGVIRT(ICLRMAObjectInspection, GetField_SystemInt32)
        HRESULT ( STDMETHODCALLTYPE *GetField_SystemInt32 )( 
            ICLRMAObjectInspection * This,
            /* [in] */ ULONG64 addr,
            /* [in] */ BSTR bstrField,
            /* [retval][out] */ LONG *pValue);
        
        END_INTERFACE
    } ICLRMAObjectInspectionVtbl;

    interface ICLRMAObjectInspection
    {
        CONST_VTBL struct ICLRMAObjectInspectionVtbl *lpVtbl;
    };

    

#ifdef COBJMACROS


#define ICLRMAObjectInspection_QueryInterface(This,riid,ppvObject)	\
    ( (This)->lpVtbl -> QueryInterface(This,riid,ppvObject) ) 

#define ICLRMAObjectInspection_AddRef(This)	\
    ( (This)->lpVtbl -> AddRef(This) ) 

#define ICLRMAObjectInspection_Release(This)	\
    ( (This)->lpVtbl -> Release(This) ) 


#define ICLRMAObjectInspection_GetType(This,addr,pValue)	\
    ( (This)->lpVtbl -> GetType(This,addr,pValue) ) 

#define ICLRMAObjectInspection_GetAddressFromCcwAddress(This,addr,pAddress)	\
    ( (This)->lpVtbl -> GetAddressFromCcwAddress(This,addr,pAddress) ) 

#define ICLRMAObjectInspection_GetField_SystemString(This,addr,bstrField,pValue)	\
    ( (This)->lpVtbl -> GetField_SystemString(This,addr,bstrField,pValue) ) 

#define ICLRMAObjectInspection_GetField_SystemUInt32(This,addr,bstrField,pValue)	\
    ( (This)->lpVtbl -> GetField_SystemUInt32(This,addr,bstrField,pValue) ) 

#define ICLRMAObjectInspection_GetField_SystemInt32(This,addr,bstrField,pValue)	\
    ( (This)->lpVtbl -> GetField_SystemInt32(This,addr,bstrField,pValue) ) 

#endif /* COBJMACROS */


#endif 	/* C style interface */




#endif 	/* __ICLRMAObjectInspection_INTERFACE_DEFINED__ */

#endif /* __CLRMA_LIBRARY_DEFINED__ */

/* Additional Prototypes for ALL interfaces */

/* end of Additional Prototypes */

#ifdef __cplusplus
}
#endif

#endif


