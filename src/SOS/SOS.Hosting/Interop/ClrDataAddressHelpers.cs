// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace SOS.Hosting.Interop
{
    public static class ClrDataAddressHelpers
    {
        // CLRDATA_ADDRESS is always 64 bits. Native TO_CDADDR sign-extends 32-bit pointers.
        public static ulong ToClrDataAddress(ulong targetAddress, int pointerSize) =>
            pointerSize == 4 ? unchecked((ulong)(long)(int)targetAddress) : targetAddress;

        public static ulong ToTargetAddress(ulong clrDataAddress, int pointerSize) =>
            pointerSize == 4 ? unchecked((uint)clrDataAddress) : clrDataAddress;
    }
}
