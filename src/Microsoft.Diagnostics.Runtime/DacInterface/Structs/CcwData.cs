// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct CcwData
    {
        public readonly ClrDataAddress OuterIUnknown;
        public readonly ClrDataAddress ManagedObject;
        public readonly ClrDataAddress Handle;
        public readonly ClrDataAddress CCWAddress;

        public readonly int RefCount;
        public readonly int InterfaceCount;
        public readonly uint IsNeutered;

        public readonly int JupiterRefCount;
        public readonly uint IsPegged;
        public readonly uint IsGlobalPegged;
        public readonly uint HasStrongRef;
        public readonly uint IsExtendsCOMObject;
        public readonly uint HasWeakReference;
        public readonly uint IsAggregated;
    }
}