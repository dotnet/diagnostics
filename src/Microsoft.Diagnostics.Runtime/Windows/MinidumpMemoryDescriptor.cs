// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.DacInterface;

namespace Microsoft.Diagnostics.Runtime.Windows
{
    [StructLayout(LayoutKind.Explicit)]
    internal readonly struct MinidumpMemoryDescriptor
    {
        [FieldOffset(0)]
        public readonly ClrDataAddress StartAddress;

        // MINIDUMP_MEMORY_DESCRIPTOR64
        [FieldOffset(8)]
        public readonly ClrDataAddress DataSize64;

        // MINIDUMP_MEMORY_DESCRIPTOR
        [FieldOffset(8)]
        public readonly uint DataSize32;
        [FieldOffset(12)]
        public readonly uint Rva;
    }
}