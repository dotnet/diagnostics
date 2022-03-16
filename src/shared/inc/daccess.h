// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//*****************************************************************************
// File: daccess.h
// 

#ifndef __daccess_h__
#define __daccess_h__

#include <stdint.h>

#include "switches.h"
#include "safemath.h"
#include "corerror.h"

#ifdef PAL_STDCPP_COMPAT
#include <type_traits>
#else
#include "clr_std/type_traits"
#include "crosscomp.h"
#endif

//
// This version of things wraps pointer access in
// templates which understand how to retrieve data
// through an access layer.  In this case no assumptions
// can be made that the current compilation processor or
// pointer types match the target's processor or pointer types.
//

// Define TADDR as a non-pointer value so use of it as a pointer
// will not work properly.  Define it as unsigned so
// pointer comparisons aren't affected by sign.
// This requires special casting to ULONG64 to sign-extend if necessary.
typedef ULONG_PTR TADDR;

// TSIZE_T used for counts or ranges that need to span the size of a 
// target pointer.  For cross-plat, this may be different than SIZE_T
// which reflects the host pointer size.
typedef SIZE_T TSIZE_T;
#define VPTR_CLASS_METHODS(name)
// Used for base classes that can be instantiated directly.
// The fake vfn is still used to force a vtable even when
// all the normal vfns are ifdef'ed out.
#define VPTR_BASE_CONCRETE_VTABLE_CLASS(name)                   \
public: name(TADDR addr, TADDR vtAddr) {}                       \
        VPTR_CLASS_METHODS(name)

//
// This version of the macros turns into normal pointers
// for unmodified in-proc compilation.

// *******************************************************
// !!!!!!!!!!!!!!!!!!!!!!!!!NOTE!!!!!!!!!!!!!!!!!!!!!!!!!!
//
// Please search this file for the type name to find the
// DAC versions of these definitions
//
// !!!!!!!!!!!!!!!!!!!!!!!!!NOTE!!!!!!!!!!!!!!!!!!!!!!!!!!
// *******************************************************


// Declare TADDR as a non-pointer type so that arithmetic
// can be done on it directly, as with the DACCESS_COMPILE definition.
// This also helps expose pointer usage that may need to be changed.
typedef ULONG_PTR TADDR;

typedef void* PTR_VOID;
typedef LPVOID* PTR_PTR_VOID;
typedef const void* PTR_CVOID;

#define DPTR(type) type*
#define ArrayDPTR(type) type*
#define SPTR(type) type*
#define VPTR(type) type*
#define S8PTR(type) type*
#define S8PTRMAX(type, maxChars) type*
#define S16PTR(type) type*
#define S16PTRMAX(type, maxChars) type*
#define _SPTR_DECL(acc_type, store_type, var) \
    static store_type var
#define _SPTR_IMPL(acc_type, store_type, cls, var) \
    store_type cls::var

#define GVAL_DECL(type, var) \
    extern type var
#define GVAL_IMPL(type, var) \
    type var
#define GVAL_IMPL_INIT(type, var, init) \
    type var = init

//----------------------------------------------------------------------------
// dac_cast
// Casting utility, to be used for casting one class pointer type to another.
// Use as you would use static_cast
//
// dac_cast is designed to act just as static_cast does when
// dealing with pointers and their DAC abstractions. Specifically,
// it handles these coversions:
//
//      dac_cast<TargetType>(SourceTypeVal)
//
// where TargetType <- SourceTypeVal are
//
//      ?PTR(Tgt) <- TADDR     - Create PTR type (DPtr etc.) from TADDR
//      ?PTR(Tgt) <- ?PTR(Src) - Convert one PTR type to another
//      ?PTR(Tgt) <- Src *     - Create PTR type from dac host object instance
//      TADDR <- ?PTR(Src)     - Get TADDR of PTR object (DPtr etc.)
//      TADDR <- Src *         - Get TADDR of dac host object instance
//
// Note that there is no direct convertion to other host-pointer types (because we don't
// know if you want a DPTR or VPTR etc.).  However, due to the implicit DAC conversions,
// you can just use dac_cast<PTR_Foo> and assign that to a Foo*.
//
// The beauty of this syntax is that it is consistent regardless
// of source and target casting types. You just use dac_cast
// and the partial template specialization will do the right thing.
//
// One important thing to realise is that all "Foo *" types are
// assumed to be pointers to host instances that were marshalled by DAC.  This should
// fail at runtime if it's not the case.
//
// Some examples would be:
//
//   - Host pointer of one type to a related host pointer of another
//     type, i.e., MethodDesc * <-> InstantiatedMethodDesc *
//     Syntax: with MethodDesc *pMD, InstantiatedMethodDesc *pInstMD
//             pInstMd = dac_cast<PTR_InstantiatedMethodDesc>(pMD)
//             pMD = dac_cast<PTR_MethodDesc>(pInstMD)
//
//   - (D|V)PTR of one encapsulated pointer type to a (D|V)PTR of
//     another type, i.e., PTR_AppDomain <-> PTR_BaseDomain
//     Syntax: with PTR_AppDomain pAD, PTR_BaseDomain pBD
//             dac_cast<PTR_AppDomain>(pBD)
//             dac_cast<PTR_BaseDomain>(pAD)
//
// Example comparsions of some old and new syntax, where
//    h is a host pointer, such as "Foo *h;"
//    p is a DPTR, such as "PTR_Foo p;"
//
//      PTR_HOST_TO_TADDR(h)           ==> dac_cast<TADDR>(h)
//      PTR_TO_TADDR(p)                ==> dac_cast<TADDR>(p)
//      PTR_Foo(PTR_HOST_TO_TADDR(h))  ==> dac_cast<PTR_Foo>(h)
//
//----------------------------------------------------------------------------
template <typename Tgt, typename Src>
inline Tgt dac_cast(Src src)
{
    // In non-DAC builds, dac_cast is the same as a C-style cast because we need to support:
    //  - casting away const
    //  - conversions between pointers and TADDR
    // Perhaps we should more precisely restrict it's usage, but we get the precise
    // restrictions in DAC builds, so it wouldn't buy us much.
    return (Tgt)(src);
#define SPTR_DECL(type, var) _SPTR_DECL(type*, PTR_##type, var)
#define SPTR_IMPL(type, cls, var) _SPTR_IMPL(type*, PTR_##type, cls, var)
}

//----------------------------------------------------------------------------
//
// Forward typedefs for system types.  This is a convenient place
// to declare things for system types, plus it gives us a central
// place to look at when deciding what types may cause issues for
// cross-platform compilation.
//
//----------------------------------------------------------------------------

typedef ArrayDPTR(BYTE)    PTR_BYTE;
typedef ArrayDPTR(uint8_t) PTR_uint8_t;
typedef DPTR(PTR_BYTE) PTR_PTR_BYTE;
typedef DPTR(PTR_uint8_t) PTR_PTR_uint8_t;
typedef DPTR(PTR_PTR_BYTE) PTR_PTR_PTR_BYTE;
typedef ArrayDPTR(signed char) PTR_SBYTE;
typedef ArrayDPTR(const BYTE) PTR_CBYTE;
typedef DPTR(INT8)    PTR_INT8;
typedef DPTR(INT16)   PTR_INT16;
typedef DPTR(UINT16)  PTR_UINT16;
typedef DPTR(WORD)    PTR_WORD;
typedef DPTR(USHORT)  PTR_USHORT;
typedef DPTR(DWORD)   PTR_DWORD;
typedef DPTR(uint32_t) PTR_uint32_t;
typedef DPTR(LONG)    PTR_LONG;
typedef DPTR(ULONG)   PTR_ULONG;
typedef DPTR(INT32)   PTR_INT32;
typedef DPTR(UINT32)  PTR_UINT32;
typedef DPTR(ULONG64) PTR_ULONG64;
typedef DPTR(INT64)   PTR_INT64;
typedef DPTR(UINT64)  PTR_UINT64;
typedef DPTR(SIZE_T)  PTR_SIZE_T;
typedef DPTR(size_t)  PTR_size_t;
typedef DPTR(TADDR)   PTR_TADDR;
typedef DPTR(int)     PTR_int;
typedef DPTR(BOOL)    PTR_BOOL;
typedef DPTR(unsigned) PTR_unsigned;

typedef S8PTR(char)           PTR_STR;
typedef S8PTR(const char)     PTR_CSTR;
typedef S8PTR(char)           PTR_UTF8;
typedef S8PTR(const char)     PTR_CUTF8;
typedef S16PTR(WCHAR)         PTR_WSTR;
typedef S16PTR(const WCHAR)   PTR_CWSTR;

typedef DPTR(T_CONTEXT)                  PTR_CONTEXT;
typedef DPTR(PTR_CONTEXT)                PTR_PTR_CONTEXT;
typedef DPTR(struct _EXCEPTION_POINTERS) PTR_EXCEPTION_POINTERS;
typedef DPTR(struct _EXCEPTION_RECORD)   PTR_EXCEPTION_RECORD;

typedef DPTR(struct _EXCEPTION_REGISTRATION_RECORD) PTR_EXCEPTION_REGISTRATION_RECORD;

typedef DPTR(struct IMAGE_COR_VTABLEFIXUP) PTR_IMAGE_COR_VTABLEFIXUP;
typedef DPTR(IMAGE_DATA_DIRECTORY)  PTR_IMAGE_DATA_DIRECTORY;
typedef DPTR(IMAGE_DEBUG_DIRECTORY)  PTR_IMAGE_DEBUG_DIRECTORY;
typedef DPTR(IMAGE_DOS_HEADER)      PTR_IMAGE_DOS_HEADER;
typedef DPTR(IMAGE_NT_HEADERS)      PTR_IMAGE_NT_HEADERS;
typedef DPTR(IMAGE_NT_HEADERS32)    PTR_IMAGE_NT_HEADERS32;
typedef DPTR(IMAGE_NT_HEADERS64)    PTR_IMAGE_NT_HEADERS64;
typedef DPTR(IMAGE_SECTION_HEADER)  PTR_IMAGE_SECTION_HEADER;
typedef DPTR(IMAGE_EXPORT_DIRECTORY)  PTR_IMAGE_EXPORT_DIRECTORY;
typedef DPTR(IMAGE_TLS_DIRECTORY)   PTR_IMAGE_TLS_DIRECTORY;

//----------------------------------------------------------------------------
//
// A PCODE is a valid PC/IP value -- a pointer to an instruction, possibly including some processor mode bits.
// (On ARM, for example, a PCODE value should have the low-order THUMB_CODE bit set if the code should
// be executed in that mode.)
//
typedef TADDR PCODE;
typedef DPTR(PCODE) PTR_PCODE;
typedef DPTR(PTR_PCODE) PTR_PTR_PCODE;

// TARGET_CONSISTENCY_CHECK represents a condition that should not fail unless the DAC target is corrupt.
// This is in contrast to ASSERTs in DAC infrastructure code which shouldn't fail regardless of the memory
// read from the target.  At the moment we treat these the same, but in the future we will want a mechanism
// for disabling just the target consistency checks (eg. for tests that intentionally use corrupted targets).
// @dbgtodo : Separating asserts and target consistency checks is tracked by DevDiv Bugs 31674
#define TARGET_CONSISTENCY_CHECK(expr,msg) _ASSERTE_MSG(expr,msg)

// For cross compilation, controlling type layout is important
// We add a simple macro here which defines DAC_ALIGNAS to the C++11 alignas operator
// This helps force the alignment of the next member
// For most cross compilation cases the layout of types simply works
// There are a few cases (where this macro is helpful) which are not consistent across platforms:
// - Base class whose size is padded to its align size.  On Linux the gcc/clang
//   layouts will reuse this padding in the derived class for the first member
// - Class with an vtable pointer and an alignment greater than the pointer size.
//   The Windows compilers will align the first member to the alignment size of the
//   class.  Linux will align the first member to its natural alignment
#define DAC_ALIGNAS(a) alignas(a)
#endif // #ifndef __daccess_h__
