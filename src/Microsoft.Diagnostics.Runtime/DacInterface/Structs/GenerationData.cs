// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct GenerationData
    {
        public readonly ClrDataAddress StartSegment;
        public readonly ClrDataAddress AllocationStart;

        // These are examined only for generation 0, otherwise NULL
        public readonly ClrDataAddress AllocationContextPointer;
        public readonly ClrDataAddress AllocationContextLimit;
    }
}
