// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct MethodDescData
    {
        public readonly uint HasNativeCode;
        public readonly uint IsDynamic;
        public readonly ushort SlotNumber;
        public readonly ClrDataAddress NativeCodeAddr;

        // Useful for breaking when a method is jitted.
        public readonly ClrDataAddress AddressOfNativeCodeSlot;

        public readonly ClrDataAddress MethodDesc;
        public readonly ClrDataAddress MethodTable;
        public readonly ClrDataAddress Module;

        public readonly uint MDToken;
        public readonly ClrDataAddress GCInfo;
        public readonly ClrDataAddress GCStressCodeCopy;

        // This is only valid if bIsDynamic is true
        public readonly ClrDataAddress ManagedDynamicMethodObject;

        public readonly ClrDataAddress RequestedIP;

        // Gives info for the single currently active version of a method
        public readonly RejitData RejitDataCurrent;

        // Gives info corresponding to requestedIP (for !ip2md)
        public readonly RejitData RejitDataRequested;

        // Total number of rejit versions that have been jitted
        public readonly uint JittedRejitVersions;
    }
}
