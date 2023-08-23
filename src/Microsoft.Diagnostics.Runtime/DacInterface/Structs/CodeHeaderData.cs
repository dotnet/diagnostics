// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct CodeHeaderData
    {
        public readonly ClrDataAddress GCInfo;
        public readonly uint JITType;
        public readonly ClrDataAddress MethodDesc;
        public readonly ClrDataAddress MethodStart;
        public readonly uint MethodSize;
        public readonly ClrDataAddress ColdRegionStart;
        public readonly uint ColdRegionSize;
        public readonly uint HotRegionSize;
    }
}
