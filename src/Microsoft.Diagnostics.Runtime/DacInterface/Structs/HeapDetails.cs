// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct HeapDetails
    {
        public readonly ClrDataAddress Address; // Only filled in server mode, otherwise NULL
        public readonly ClrDataAddress Allocated;
        public readonly ClrDataAddress MarkArray;
        public readonly ClrDataAddress CurrentGCState;
        public readonly ClrDataAddress NextSweepObj;
        public readonly ClrDataAddress SavedSweepEphemeralSeg;
        public readonly ClrDataAddress SavedSweepEphemeralStart;
        public readonly ClrDataAddress BackgroundSavedLowestAddress;
        public readonly ClrDataAddress BackgroundSavedHighestAddress;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public readonly GenerationData[] GenerationTable;
        public readonly ClrDataAddress EphemeralHeapSegment;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
        public readonly ClrDataAddress[] FinalizationFillPointers;
        public readonly ClrDataAddress LowestAddress;
        public readonly ClrDataAddress HighestAddress;
        public readonly ClrDataAddress CardTable;

        public ulong EphemeralAllocContextPtr => GenerationTable[0].AllocationContextPointer;
        public ulong EphemeralAllocContextLimit => GenerationTable[0].AllocationContextLimit;
    }
}
