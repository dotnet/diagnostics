// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Runtime.Interfaces
{
    public interface IClrSegment : IEquatable<IClrSegment>
    {
        ulong Address { get; }
        MemoryRange CommittedMemory { get; }
        ulong End { get; }
        ulong FirstObjectAddress { get; }
        MemoryRange Generation0 { get; }
        MemoryRange Generation1 { get; }
        MemoryRange Generation2 { get; }
        bool IsPinned { get; }
        GCSegmentKind Kind { get; }
        ClrSegmentFlags Flags { get; }
        ulong Length { get; }
        MemoryRange ObjectRange { get; }
        MemoryRange ReservedMemory { get; }
        ulong Start { get; }
        IClrSubHeap SubHeap { get; }
        IEnumerable<IClrValue> EnumerateObjects(bool carefully = false);
        IEnumerable<IClrValue> EnumerateObjects(MemoryRange range, bool carefully = false);
        Generation GetGeneration(ulong obj);
    }
}
