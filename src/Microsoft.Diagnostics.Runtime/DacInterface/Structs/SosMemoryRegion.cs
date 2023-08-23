// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct SosMemoryRegion
    {
        public readonly ClrDataAddress Start;
        public readonly ClrDataAddress Length;
        public readonly ClrDataAddress ExtraData;
        public readonly int Heap;
    }
}