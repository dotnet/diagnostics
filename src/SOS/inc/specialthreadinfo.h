// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ******************************************************************************
// WARNING!!!: This code is also used by createdump in the runtime repo.
// See: https://github.com/dotnet/runtime/blob/main/src/coreclr/debug/createdump/specialthreadinfo.h
// ******************************************************************************

// This defines a workaround to the MacOS dump format not having the OS process
// and thread ids that SOS needs to map thread "indexes" to thread "ids". The MacOS
// createdump adds this special memory region at this specific address that is not
// in the user or kernel address spaces. lldb is fine with it.

#define SPECIAL_THREADINFO_SIGNATURE "THREADINFO"

const uint64_t SpecialThreadInfoAddress = 0x7fffffff00000000;

struct SpecialThreadInfoHeader
{
    char signature[16];
    uint32_t pid;
    uint32_t numThreads;        // The number of SpecialThreadInfoEntry's after this header
};

struct SpecialThreadInfoEntry
{
    uint32_t tid;
    uint64_t sp;
};
