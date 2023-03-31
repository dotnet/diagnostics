// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Explicit)]
    public struct IMAGE_IMPORT_DESCRIPTOR
    {
        [FieldOffset(0)]
        public uint Characteristics; // 0 for terminating null import descriptor
        [FieldOffset(0)]
        public uint OriginalFirstThunk; // RVA to original unbound IAT (PIMAGE_THUNK_DATA)
        [FieldOffset(4)]
        public uint TimeDateStamp; // 0 if not bound,
        // -1 if bound, and real date\time stamp
        //     in IMAGE_DIRECTORY_ENTRY_BOUND_IMPORT (new BIND)
        // O.W. date/time stamp of DLL bound to (Old BIND)

        [FieldOffset(8)]
        public uint ForwarderChain; // -1 if no forwarders
        [FieldOffset(12)]
        public uint Name;
        [FieldOffset(16)]
        public uint FirstThunk; // RVA to IAT (if bound this IAT has actual addresses)
    }
}
