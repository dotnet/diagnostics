// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#ifndef DIAGNOSTICS_PAL_H
#define DIAGNOSTICS_PAL_H

#include <minipal/debugger.h>

#include "../bstr.h"

#ifndef IsDebuggerPresent
#define IsDebuggerPresent minipal_is_native_debugger_present
#endif

#ifndef EMPTY_BASES_DECL
#define EMPTY_BASES_DECL
#endif

#ifndef __annotation
#define __annotation(...)
#endif

#if !defined(_MSC_VER)
#ifndef I64
#define I64_HELPER(x) x ## LL
#define I64(x) I64_HELPER(x)
#endif
#ifndef UI64
#define UI64_HELPER(x) x ## ULL
#define UI64(x) UI64_HELPER(x)
#endif
#endif

#ifndef ERROR_TOO_MANY_SEMAPHORES
#define ERROR_TOO_MANY_SEMAPHORES 100L
#endif

typedef bool (*PPAL_STARTUP_CALLBACK)(const char* modulePath, HMODULE module, PVOID parameter);

typedef struct tagVS_FIXEDFILEINFO
{
    DWORD dwSignature;
    DWORD dwStrucVersion;
    DWORD dwFileVersionMS;
    DWORD dwFileVersionLS;
    DWORD dwProductVersionMS;
    DWORD dwProductVersionLS;
    DWORD dwFileFlagsMask;
    DWORD dwFileFlags;
    DWORD dwFileOS;
    DWORD dwFileType;
    DWORD dwFileSubtype;
    DWORD dwFileDateMS;
    DWORD dwFileDateLS;
} VS_FIXEDFILEINFO;

#ifndef VS_FF_DEBUG
#define VS_FF_DEBUG 0x00000001L
#endif

#ifndef VS_FF_SPECIALBUILD
#define VS_FF_SPECIALBUILD 0x00000020L
#endif

extern "C"
{
    DWORD PALAPI PAL_RegisterForRuntimeStartup(
        DWORD processId,
        LPCWSTR applicationGroupId,
        PPAL_STARTUP_CALLBACK callback,
        PVOID parameter,
        PVOID* unregisterToken);

    DWORD PALAPI PAL_UnregisterForRuntimeStartup(PVOID unregisterToken);

    int __cdecl swprintf_s(WCHAR* string, size_t sizeInWords, const WCHAR* format, ...);
    int __cdecl _snwprintf_s(WCHAR* string, size_t sizeInWords, size_t count, const WCHAR* format, ...);
    int __cdecl vswprintf_s(WCHAR* string, size_t sizeInWords, const WCHAR* format, va_list arglist);
    int __cdecl _vsnwprintf_s(WCHAR* string, size_t sizeInWords, size_t count, const WCHAR* format, va_list arglist);
    int __cdecl DiagnosticsVsnprintf_s(char* string, size_t sizeInBytes, size_t count, const char* format, va_list arglist);
    errno_t __cdecl _itow_s(int value, WCHAR* buffer, size_t bufferSize, int radix);
    errno_t __cdecl _i64tow_s(long long value, WCHAR* buffer, size_t bufferSize, int radix);
}

#endif
