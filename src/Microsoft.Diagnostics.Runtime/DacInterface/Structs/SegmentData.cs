// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct SegmentData
    {
        public readonly ClrDataAddress Address;
        public readonly ClrDataAddress Allocated;
        public readonly ClrDataAddress Committed;
        public readonly ClrDataAddress Reserved;
        public readonly ClrDataAddress Used;
        public readonly ClrDataAddress Start;
        public readonly ClrDataAddress Next;
        public readonly ClrDataAddress Heap;
        public readonly ClrDataAddress HighAllocMark;
        public readonly IntPtr Flags;
        public readonly ClrDataAddress BackgroundAllocated;
    }
}
