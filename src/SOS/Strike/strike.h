// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ==++==
// 
 
// 
// ==--==
#ifndef __strike_h__
#define __strike_h__

#include "warningcontrol.h"

#define ___in       _SAL1_Source_(__in, (), _In_)
#define ___out      _SAL1_Source_(__out, (), _Out_)

#define _max(a, b) (((a) > (b)) ? (a) : (b))
#define _min(a, b) (((a) < (b)) ? (a) : (b))

#include <winternl.h>
#include <winver.h>
#include <windows.h>
#include <wchar.h>
#include <minipal/utils.h>
#include <dn-u16.h>

#define _wcsrchr    u16_strrchr
#define _wcscmp     u16_strcmp
#define _wcsncmp    u16_strncmp
#define _wcschr     u16_strchr
#define _wcscat     u16_strcat
#define _wcsstr     u16_strstr

inline size_t __cdecl _wcslen(const WCHAR* str)
{
    return u16_strlen(str);
}

#define KDEXT_64BIT
#include <wdbgexts.h>
#undef DECLARE_API
#undef GetContext
#undef SetContext
#undef ReadMemory
#undef WriteMemory
#undef GetFieldValue
#undef StackTrace

#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>
#include "host.h"
#include "hostservices.h"

#ifdef FEATURE_PAL
#ifndef alloca
#define alloca  __builtin_alloca
#endif
#ifndef _alloca
#define _alloca __builtin_alloca
#endif
#endif // FEATURE_PAL

#include <stddef.h>

#ifndef FEATURE_PAL
#include <basetsd.h>  
#endif

#define  CORHANDLE_MASK 0x1

#include "static_assert.h"

// exts.h includes dbgeng.h which has a bunch of IIDs we need instantiated.
#define INITGUID
#include "guiddef.h"

#define SOS_PTR(x) (size_t)(x)

#include "exts.h"

//Alignment constant for allocation
#if defined(_TARGET_X86_) || defined(_TARGET_ARM_)
#define ALIGNCONST 3
#else
#define ALIGNCONST 7
#endif

//The large object heap uses a different alignment
#define ALIGNCONSTLARGE 7

#ifdef _WIN64
#define SIZEOF_OBJHEADER    8
#else // !_WIN64
#define SIZEOF_OBJHEADER    4
#endif // !_WIN64

#define plug_skew           SIZEOF_OBJHEADER
#define min_obj_size        (sizeof(BYTE*)+plug_skew+sizeof(size_t))

#ifndef NT_SUCCESS
#define NT_SUCCESS(Status) (((NTSTATUS)(Status)) >= 0)
#endif

HRESULT SetNGENCompilerFlags(DWORD flags);


#endif // __strike_h__
