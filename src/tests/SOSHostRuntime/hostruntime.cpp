// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include <stdio.h>
#include <stdlib.h>

// Fail before starting a runtime so the next candidate can host the real managed extension.
extern "C" int coreclr_initialize(
    const char*, const char*, int, const char**, const char**, void**, unsigned int*)
{
    const char* mode = getenv("PAL_MachExceptionMode");
    fprintf(stderr, "SOS test host: PAL_MachExceptionMode='%s'\n", mode == nullptr ? "<unset>" : mode);
    return static_cast<int>(0x8007000c);
}

extern "C" int coreclr_create_delegate(void*, unsigned int, const char*, const char*, const char*, void**)
{
    abort();
}
