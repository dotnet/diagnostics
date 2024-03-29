// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Contains some definitions duplicated from pal.h, palrt.h, rpc.h, 
// etc. because they have various conflicits with the linux standard
// runtime h files like wchar_t, memcpy, etc.

#include <pal_mstypes.h>

#define MAX_PATH      260 

#ifndef LONG_MAX      
#define LONG_MAX      2147483647L
#endif

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
