// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#pragma once

#include <vector>
#include "sospriv.h"

// GCInfoData holds decoded GC info for a method, populated from either
// ISOSDacInterface18 or the legacy GcInfoDecoder/GcInfoDumper.
struct GCInfoData
{
    SOSGCInfoHeader Header;
    std::vector<SOSCodeRange> InterruptibleRanges;
    std::vector<SOSGCSlotLifetime> SlotLifetimes;
    std::vector<unsigned int> SafePoints;
    bool IsValid;

    GCInfoData() : Header{}, IsValid(false) {}

    // Populate GCInfoData for the method at the given IP via ISOSDacInterface18.
    // Returns E_NOINTERFACE if the interface is not available.
    static HRESULT Create(CLRDATA_ADDRESS methodIP, GCInfoData& out);

    // Format and print the GC info to the debugger output.
    // If pfnPrintf is NULL, uses ExtOut.
    typedef void (*printfFtn)(const char* fmt, ...);
    void DumpToOutput(printfFtn pfnPrintf) const;
};
