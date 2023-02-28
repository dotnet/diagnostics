// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_BASIC_INFORMATION64
    {
        public ulong BaseAddress;
        public ulong AllocationBase;
        public PAGE AllocationProtect;
        public uint __alignment1;
        public ulong RegionSize;
        public MEM State;
        public PAGE Protect;
        public MEM Type;
        public uint __alignment2;
    }
}
