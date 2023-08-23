// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct ThreadData
    {
        public readonly uint ManagedThreadId;
        public readonly uint OSThreadId;
        public readonly int State;
        public readonly uint PreemptiveGCDisabled;
        public ClrDataAddress AllocationContextPointer;
        public ClrDataAddress AllocationContextLimit;
        public ClrDataAddress Context;
        public ClrDataAddress Domain;
        public ClrDataAddress Frame;
        public readonly uint LockCount;
        public ClrDataAddress FirstNestedException;
        public ClrDataAddress Teb;
        public ClrDataAddress FiberData;
        public ClrDataAddress LastThrownObjectHandle;
        public ClrDataAddress NextThread;
    }
}