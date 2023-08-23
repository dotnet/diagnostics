// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct ObjectData
    {
        public readonly ClrDataAddress MethodTable;
        public readonly uint ObjectType;
        public readonly ulong Size;
        public readonly ClrDataAddress ElementTypeHandle;
        public readonly uint ElementType;
        public readonly uint Rank;
        public readonly ulong NumComponents;
        public readonly ulong ComponentSize;
        public readonly ClrDataAddress ArrayDataPointer;
        public readonly ClrDataAddress ArrayBoundsPointer;
        public readonly ClrDataAddress ArrayLowerBoundsPointer;
        public readonly ClrDataAddress RCW;
        public readonly ClrDataAddress CCW;
    }
}
