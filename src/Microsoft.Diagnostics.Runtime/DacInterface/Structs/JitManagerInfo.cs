// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct JitManagerInfo
    {
        public readonly ClrDataAddress Address;
        public readonly CodeHeapKind Kind;
        public readonly ClrDataAddress HeapList;
    }
}