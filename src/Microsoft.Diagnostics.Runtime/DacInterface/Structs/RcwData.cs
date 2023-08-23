// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct RcwData
    {
        public readonly ClrDataAddress IdentityPointer;
        public readonly ClrDataAddress IUnknownPointer;
        public readonly ClrDataAddress ManagedObject;
        public readonly ClrDataAddress JupiterObject;
        public readonly ClrDataAddress VTablePointer;
        public readonly ClrDataAddress CreatorThread;
        public readonly ClrDataAddress CTXCookie;

        public readonly int RefCount;
        public readonly int InterfaceCount;

        public readonly uint IsJupiterObject;
        public readonly uint SupportsIInspectable;
        public readonly uint IsAggregated;
        public readonly uint IsContained;
        public readonly uint IsFreeThreaded;
        public readonly uint IsDisconnected;
    }
}