// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

// The first byte of the index is the count of bytes
typedef unsigned char SYMBOL_INDEX;
#define RUNTIME_INFO_SIGNATURE "DotNetRuntimeInfo"

// Make sure that if you update this structure
//    - You do so in a in a way that it is backwards compatible. For example, only tail append to this.
//    - Rev the version.
//    - Update the logic in ClrDataAccess::EnumMemCLRMainModuleInfo to ensure all needed state is in the dump.
typedef struct _RuntimeInfo
{
    char Signature[18];
    int Version;
    SYMBOL_INDEX RuntimeModuleIndex[24];
    SYMBOL_INDEX DacModuleIndex[24];
    SYMBOL_INDEX DbiModuleIndex[24];
} RuntimeInfo;

extern RuntimeInfo DotNetRuntimeInfo;
