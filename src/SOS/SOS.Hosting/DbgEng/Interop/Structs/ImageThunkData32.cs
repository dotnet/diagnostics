// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Explicit)]
    public struct IMAGE_THUNK_DATA32
    {
        [FieldOffset(0)]
        public uint ForwarderString; // PBYTE
        [FieldOffset(0)]
        public uint Function; // PDWORD
        [FieldOffset(0)]
        public uint Ordinal;
        [FieldOffset(0)]
        public uint AddressOfData; // PIMAGE_IMPORT_BY_NAME
    }
}