// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

// 2.x version
struct MSLAYOUT DacpTieredVersionData_2x
{
    enum TieredState 
    {
        NON_TIERED,
        TIERED_0,
        TIERED_1,
        TIERED_UNKNOWN
    };
    
    CLRDATA_ADDRESS NativeCodeAddr;
    TieredState     TieredInfo;
    CLRDATA_ADDRESS NativeCodeVersionNodePtr;
};
