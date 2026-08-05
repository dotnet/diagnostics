// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#include "strike.h"
#include "util.h"
#include "gcinfoprovider.h"
#include "disasm.h"
#include <algorithm>
#include <vector>

// ============================================================================
// Create: query ISOSDacInterface18 for GC info
// ============================================================================

HRESULT GCInfoData::Create(CLRDATA_ADDRESS methodIP, GCInfoData& out)
{
    out = GCInfoData();

    if (g_clrData == NULL)
        return E_NOINTERFACE;

    ReleaseHolder<ISOSDacInterface18> pSos18;
    HRESULT hr = g_clrData->QueryInterface(__uuidof(ISOSDacInterface18), (void**)&pSos18);
    if (FAILED(hr) || pSos18 == NULL)
        return E_NOINTERFACE;

    out.Header.SizeOf = sizeof(SOSGCInfoHeader);
    hr = pSos18->GetGCInfoHeader(methodIP, &out.Header);
    if (FAILED(hr))
        return hr;

    ULONG rangeCount = 0;
    hr = pSos18->GetGCInfoInterruptibleRanges(methodIP, 0, nullptr, &rangeCount);
    if (SUCCEEDED(hr) && rangeCount > 0)
    {
        out.InterruptibleRanges.resize(rangeCount);
        ULONG fetched = 0;
        hr = pSos18->GetGCInfoInterruptibleRanges(methodIP, rangeCount, out.InterruptibleRanges.data(), &fetched);
        if (SUCCEEDED(hr))
            out.InterruptibleRanges.resize(fetched);
        else
            out.InterruptibleRanges.clear();
    }

    ULONG slotCount = 0;
    hr = pSos18->GetGCInfoSlotLifetimes(methodIP, 0, nullptr, &slotCount);
    if (SUCCEEDED(hr) && slotCount > 0)
    {
        out.SlotLifetimes.resize(slotCount);
        ULONG fetched = 0;
        hr = pSos18->GetGCInfoSlotLifetimes(methodIP, slotCount, out.SlotLifetimes.data(), &fetched);
        if (SUCCEEDED(hr))
            out.SlotLifetimes.resize(fetched);
        else
            out.SlotLifetimes.clear();
    }

    ULONG safePointCount = 0;
    hr = pSos18->GetGCInfoSafePoints(methodIP, 0, nullptr, &safePointCount);
    if (SUCCEEDED(hr) && safePointCount > 0)
    {
        out.SafePoints.resize(safePointCount);
        ULONG fetched = 0;
        hr = pSos18->GetGCInfoSafePoints(methodIP, safePointCount, out.SafePoints.data(), &fetched);
        if (SUCCEEDED(hr))
            out.SafePoints.resize(fetched);
        else
            out.SafePoints.clear();
    }

    out.IsValid = true;
    return S_OK;
}

// ============================================================================
// Formatting
// ============================================================================

void GCInfoData::DumpToOutput(GCInfoData::printfFtn pfnPrintf) const
{
    if (pfnPrintf == NULL)
        pfnPrintf = ExtOut;

    if (!IsValid)
    {
        pfnPrintf("No GC info available\n");
        return;
    }

    // Map ICorDebugInfo::RegNum to register names.
    // The cDAC returns register numbers using ICorDebugInfo::RegNum ordering,
    // NOT SOS's s_GCRegs ordering. Use the same mapping as GetRegName() in gcdumpnonx86.cpp.
    auto getRegName = [](unsigned int regNum) -> const char* {
#if defined(SOS_TARGET_AMD64)
        static const char* s_regNames[] = {
            "rax", "rcx", "rdx", "rbx", "rsp", "rbp", "rsi", "rdi",
            "r8", "r9", "r10", "r11", "r12", "r13", "r14", "r15"
        };
        if (regNum < ARRAY_SIZE(s_regNames))
            return s_regNames[regNum];
#elif defined(SOS_TARGET_ARM64)
        if (regNum <= 28)
        {
            static char buf[8];
            sprintf_s(buf, ARRAY_SIZE(buf), "x%u", regNum);
            return buf;
        }
        if (regNum == 29) return "fp";
        if (regNum == 30) return "lr";
        if (regNum == 31) return "sp";
#elif defined(SOS_TARGET_ARM)
        static const char* s_regNames[] = {
            "r0", "r1", "r2", "r3", "r4", "r5", "r6", "r7",
            "r8", "r9", "r10", "r11", "r12", "sp", "lr", "pc"
        };
        if (regNum < ARRAY_SIZE(s_regNames))
            return s_regNames[regNum];
#endif
        return "???";
    };

    // Print header fields matching legacy DumpGCTable format
    pfnPrintf("Prolog size: %d\n", Header.PrologSize);

    if (Header.GSCookieIsPresent)
    {
        pfnPrintf("GS cookie: caller.sp%+x\n", Header.GSCookieStackSlot);
        pfnPrintf("GS cookie valid range: [%x;%x)\n", Header.GSCookieValidRangeStart, Header.GSCookieValidRangeEnd);
    }
    else
    {
        pfnPrintf("GS cookie: <none>\n");
    }

    if (Header.PSPSymIsPresent)
        pfnPrintf("PSPSym: caller.sp%+x\n", Header.PSPSymStackSlot);
    else
        pfnPrintf("PSPSym: <none>\n");

    if (Header.GenericsInstContextIsPresent)
        pfnPrintf("Generics inst context: caller.sp%+x\n", Header.GenericsInstContextStackSlot);
    else
        pfnPrintf("Generics inst context: <none>\n");

    if (Header.PSPSymIsPresent)
        pfnPrintf("PSP slot: caller.sp%+x\n", Header.PSPSymStackSlot);
    else
        pfnPrintf("PSP slot: <none>\n");

    if (Header.GenericsInstContextIsPresent)
    {
        const char* kindStr = "GENERIC_PARAM_CONTEXT_THIS";
        if (Header.GenericsInstContextKind == 1) kindStr = "GENERIC_PARAM_CONTEXT_METHODDESC";
        else if (Header.GenericsInstContextKind == 2) kindStr = "GENERIC_PARAM_CONTEXT_METHODHANDLE";
        pfnPrintf("GenericInst slot: caller.sp%+x (%s)\n", Header.GenericsInstContextStackSlot, kindStr);
    }
    else
    {
        pfnPrintf("GenericInst slot: <none>\n");
    }

    pfnPrintf("Varargs: %d\n", Header.IsVarArg ? 1 : 0);
    if (Header.StackBaseRegister != 0xFFFFFFFF)
        pfnPrintf("Frame pointer: %s\n", getRegName(Header.StackBaseRegister));
    else
        pfnPrintf("Frame pointer: <none>\n");
    pfnPrintf("Wants Report Only Leaf: %d\n", Header.WantsReportOnlyLeaf ? 1 : 0);
    pfnPrintf("Size of parameter area: %x\n", Header.SizeOfStackParameterArea);
    pfnPrintf("Code size: %x\n", Header.CodeSize);

    const char* frameRegName = (Header.StackBaseRegister != 0xFFFFFFFF) ? getRegName(Header.StackBaseRegister) : "sp";

    // Helper to format a stack slot offset as hex with sign, matching legacy format
    auto fmtStackSlot = [&](const SOSGCSlotLifetime& s) -> const char* {
        static char buf[64];
        const char* baseReg = frameRegName;
        if (s.BaseRegister == 0) baseReg = "sp";
        else if (s.BaseRegister == 1) baseReg = frameRegName;

        if (s.SpOffset >= 0)
            sprintf_s(buf, ARRAY_SIZE(buf), "%s+%x", baseReg, (unsigned)s.SpOffset);
        else
            sprintf_s(buf, ARRAY_SIZE(buf), "%s-%x", baseReg, (unsigned)(-s.SpOffset));
        return buf;
    };

    // Untracked slots (spanning the entire method)
    for (size_t i = 0; i < SlotLifetimes.size(); i++)
    {
        const SOSGCSlotLifetime& s = SlotLifetimes[i];
        if (s.BeginOffset == 0 && s.EndOffset == Header.CodeSize)
        {
            if (s.IsRegister)
                pfnPrintf("Untracked: %s+%s\n", s.GcFlags & 0x2 ? "pinned " : "", getRegName(s.RegisterNumber));
            else
                pfnPrintf("Untracked: %s+%s\n", s.GcFlags & 0x2 ? "pinned " : "", fmtStackSlot(s));
        }
    }

    // Display format depends on whether the method is fully interruptible or safe-point-only.
    // This matches the legacy GcInfoDumper output exactly.
    bool isInterruptible = !InterruptibleRanges.empty();

    if (!isInterruptible)
    {
        // Non-interruptible: only show safe points with live slots listed on the same line.
        // No +/- transitions shown separately.
        for (size_t sp = 0; sp < SafePoints.size(); sp++)
        {
            unsigned int spOffset = SafePoints[sp];
            pfnPrintf("%08x is a safepoint: ", spOffset);
            for (size_t i = 0; i < SlotLifetimes.size(); i++)
            {
                const SOSGCSlotLifetime& s = SlotLifetimes[i];
                if (s.BeginOffset == 0 && s.EndOffset == Header.CodeSize)
                    continue; // skip untracked
                if (spOffset >= s.BeginOffset && spOffset < s.EndOffset)
                {
                    if (s.IsRegister)
                        pfnPrintf(" +%s", getRegName(s.RegisterNumber));
                    else
                        pfnPrintf(" +%s", fmtStackSlot(s));
                }
            }
            pfnPrintf("\n");
        }
    }
    else
    {
        // Interruptible: show interruptible range boundaries and +/- slot transitions.
        // Group transitions at the same offset on one line.
        struct TimelineEvent
        {
            unsigned int offset;
            int type; // 0=interruptible start, 1=interruptible end, 2=slot live, 3=slot dead
            size_t slotIndex;
        };

        std::vector<TimelineEvent> events;

        for (size_t i = 0; i < InterruptibleRanges.size(); i++)
        {
            events.push_back({InterruptibleRanges[i].BeginOffset, 0, 0});
            events.push_back({InterruptibleRanges[i].EndOffset, 1, 0});
        }

        for (size_t i = 0; i < SlotLifetimes.size(); i++)
        {
            if (SlotLifetimes[i].BeginOffset == 0 && SlotLifetimes[i].EndOffset == Header.CodeSize)
                continue;
            events.push_back({SlotLifetimes[i].BeginOffset, 2, i});
            events.push_back({SlotLifetimes[i].EndOffset, 3, i});
        }

        std::sort(events.begin(), events.end(), [](const TimelineEvent& a, const TimelineEvent& b) {
            if (a.offset != b.offset) return a.offset < b.offset;
            return a.type < b.type;
        });

        unsigned int lastOffset = (unsigned int)-1;
        for (const auto& ev : events)
        {
            switch (ev.type)
            {
            case 0:
                pfnPrintf("%08x interruptible\n", ev.offset);
                lastOffset = (unsigned int)-1;
                break;
            case 1:
                pfnPrintf("%08x not interruptible\n", ev.offset);
                lastOffset = (unsigned int)-1;
                break;
            case 2:
            {
                const SOSGCSlotLifetime& s = SlotLifetimes[ev.slotIndex];
                if (ev.offset != lastOffset)
                {
                    if (lastOffset != (unsigned int)-1)
                        pfnPrintf("\n");
                    pfnPrintf("%08x", ev.offset);
                    lastOffset = ev.offset;
                }
                if (s.IsRegister)
                    pfnPrintf(" +%s", getRegName(s.RegisterNumber));
                else
                    pfnPrintf(" +%s", fmtStackSlot(s));
                break;
            }
            case 3:
            {
                const SOSGCSlotLifetime& s = SlotLifetimes[ev.slotIndex];
                if (ev.offset != lastOffset)
                {
                    if (lastOffset != (unsigned int)-1)
                        pfnPrintf("\n");
                    pfnPrintf("%08x", ev.offset);
                    lastOffset = ev.offset;
                }
                if (s.IsRegister)
                    pfnPrintf(" -%s", getRegName(s.RegisterNumber));
                else
                    pfnPrintf(" -%s", fmtStackSlot(s));
                break;
            }
            }
        }
        if (lastOffset != (unsigned int)-1)
            pfnPrintf("\n");
    }
}
