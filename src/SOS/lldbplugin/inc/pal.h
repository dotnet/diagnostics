// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

// Contains some definitions duplicated from pal.h, palrt.h, rpc.h, 
// etc. because they have various conflicits with the linux standard
// runtime h files like wchar_t, memcpy, etc.

#include <stdarg.h>
#include <string.h>
#include <pal_mstypes.h>

#define MAX_PATH                         260 
#define MAX_LONGPATH                    1024  /* max. length of full pathname */

// Platform-specific library naming
// 
#ifdef __APPLE__
#define MAKEDLLNAME_W(name) u"lib" name u".dylib"
#define MAKEDLLNAME_A(name)  "lib" name  ".dylib"
#elif defined(_AIX)
#define MAKEDLLNAME_W(name) L"lib" name L".a"
#define MAKEDLLNAME_A(name)  "lib" name  ".a"
#elif defined(__hppa__) || defined(_IA64_)
#define MAKEDLLNAME_W(name) L"lib" name L".sl"
#define MAKEDLLNAME_A(name)  "lib" name  ".sl"
#else
#define MAKEDLLNAME_W(name) u"lib" name u".so"
#define MAKEDLLNAME_A(name)  "lib" name  ".so"
#endif

#define interface struct
typedef GUID IID;

#ifdef __cplusplus
#define REFGUID const GUID &
#else
#define REFGUID const GUID *
#endif

typedef GUID *LPGUID;
typedef const GUID FAR *LPCGUID;

#ifdef __cplusplus
extern "C++" {
#if !defined _SYS_GUID_OPERATOR_EQ_ && !defined _NO_SYS_GUID_OPERATOR_EQ_
#define _SYS_GUID_OPERATOR_EQ_
inline int IsEqualGUID(REFGUID rguid1, REFGUID rguid2)
    { return !memcmp(&rguid1, &rguid2, sizeof(GUID)); }
inline int operator==(REFGUID guidOne, REFGUID guidOther)
    { return IsEqualGUID(guidOne,guidOther); }
inline int operator!=(REFGUID guidOne, REFGUID guidOther)
    { return !IsEqualGUID(guidOne,guidOther); }
#endif
};
#endif // __cplusplus

#ifdef __cplusplus
#define REFIID const IID &
#else
#define REFIID const IID *
#endif
#define IsEqualIID(riid1, riid2) IsEqualGUID(riid1, riid2)

#ifndef _DECLSPEC_DEFINED_
#define _DECLSPEC_DEFINED_

#if  defined(_MSC_VER)
#define DECLSPEC_NOVTABLE   __declspec(novtable)
#define DECLSPEC_IMPORT     __declspec(dllimport)
#define DECLSPEC_SELECTANY  __declspec(selectany)
#elif defined(__GNUC__)
#define DECLSPEC_NOVTABLE
#define DECLSPEC_IMPORT     
#define DECLSPEC_SELECTANY  __attribute__((weak))
#else
#define DECLSPEC_NOVTABLE
#define DECLSPEC_IMPORT
#define DECLSPEC_SELECTANY
#endif

#if defined(_MSC_VER) || defined(__llvm__)
#define DECLSPEC_ALIGN(x)   __declspec(align(x))
#else
#define DECLSPEC_ALIGN(x) 
#endif

#endif // !_DECLSPEC_DEFINED_

#define DECLSPEC_UUID(x) __declspec(uuid(x))
#define MIDL_INTERFACE(x) struct DECLSPEC_UUID(x) DECLSPEC_NOVTABLE

#define STDMETHODCALLTYPE    __cdecl
#define STDMETHODVCALLTYPE   __cdecl

#define STDAPICALLTYPE       __cdecl
#define STDAPIVCALLTYPE      __cdecl

#ifdef RC_INVOKED
#define _HRESULT_TYPEDEF_(_sc) _sc
#else // RC_INVOKED
#define _HRESULT_TYPEDEF_(_sc) ((HRESULT)_sc)
#endif // RC_INVOKED

#define S_OK                             _HRESULT_TYPEDEF_(0x00000000L)
#define S_FALSE                          _HRESULT_TYPEDEF_(0x00000001L)

#define E_NOTIMPL                        _HRESULT_TYPEDEF_(0x80004001L)
#define E_NOINTERFACE                    _HRESULT_TYPEDEF_(0x80004002L)
#define E_UNEXPECTED                     _HRESULT_TYPEDEF_(0x8000FFFFL)
#define E_OUTOFMEMORY                    _HRESULT_TYPEDEF_(0x8007000EL)
#define E_INVALIDARG                     _HRESULT_TYPEDEF_(0x80070057L)
#define E_POINTER                        _HRESULT_TYPEDEF_(0x80004003L)
#define E_HANDLE                         _HRESULT_TYPEDEF_(0x80070006L)
#define E_ABORT                          _HRESULT_TYPEDEF_(0x80004004L)
#define E_FAIL                           _HRESULT_TYPEDEF_(0x80004005L)
#define E_ACCESSDENIED                   _HRESULT_TYPEDEF_(0x80070005L)
#define E_PENDING                        _HRESULT_TYPEDEF_(0x8000000AL)

#define EXCEPTION_MAXIMUM_PARAMETERS    15

typedef struct _EXCEPTION_RECORD64 {
    DWORD ExceptionCode;
    ULONG ExceptionFlags;
    ULONG64 ExceptionRecord;
    ULONG64 ExceptionAddress;
    ULONG NumberParameters;
    ULONG __unusedAlignment;
    ULONG64 ExceptionInformation[EXCEPTION_MAXIMUM_PARAMETERS];
} EXCEPTION_RECORD64, *PEXCEPTION_RECORD64;

#include <unknwn.h>

#ifndef FORCEINLINE
#if _MSC_VER < 1200
#define FORCEINLINE inline
#else
#define FORCEINLINE __forceinline
#endif
#endif

FORCEINLINE void PAL_ArmInterlockedOperationBarrier()
{
#ifdef _ARM64_
    // On arm64, most of the __sync* functions generate a code sequence like:
    //   loop:
    //     ldaxr (load acquire exclusive)
    //     ...
    //     stlxr (store release exclusive)
    //     cbnz loop
    //
    // It is possible for a load following the code sequence above to be reordered to occur prior to the store above due to the
    // release barrier, this is substantiated by https://github.com/dotnet/coreclr/pull/17508. Interlocked operations in the PAL
    // require the load to occur after the store. This memory barrier should be used following a call to a __sync* function to
    // prevent that reordering. Code generated for arm32 includes a 'dmb' after 'cbnz', so no issue there at the moment.
    __sync_synchronize();
#endif // _ARM64_
}

/*++
Function:
InterlockedIncrement

The InterlockedIncrement function increments (increases by one) the
value of the specified variable and checks the resulting value. The
function prevents more than one thread from using the same variable
simultaneously.

Parameters

lpAddend 
[in/out] Pointer to the variable to increment. 

Return Values

The return value is the resulting incremented value. 

--*/
EXTERN_C
PALIMPORT
inline
LONG
PALAPI
InterlockedIncrement(
    IN OUT LONG volatile *lpAddend)
{
    LONG result = __sync_add_and_fetch(lpAddend, (LONG)1);
    PAL_ArmInterlockedOperationBarrier();
    return result;
}

/*++
Function:
InterlockedDecrement

The InterlockedDecrement function decrements (decreases by one) the
value of the specified variable and checks the resulting value. The
function prevents more than one thread from using the same variable
simultaneously.

Parameters

lpAddend 
[in/out] Pointer to the variable to decrement. 

Return Values

The return value is the resulting decremented value.

--*/
EXTERN_C
PALIMPORT
inline
LONG
PALAPI
InterlockedDecrement(
    IN OUT LONG volatile *lpAddend)
{
    LONG result = __sync_sub_and_fetch(lpAddend, (LONG)1);
    PAL_ArmInterlockedOperationBarrier();
    return result;
}
