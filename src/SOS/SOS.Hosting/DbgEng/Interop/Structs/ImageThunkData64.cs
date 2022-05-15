// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Explicit)]
    public struct IMAGE_THUNK_DATA64
    {
        [FieldOffset(0)]
        public ulong ForwarderString; // PBYTE
        [FieldOffset(0)]
        public ulong Function; // PDWORD
        [FieldOffset(0)]
        public ulong Ordinal;
        [FieldOffset(0)]
        public ulong AddressOfData; // PIMAGE_IMPORT_BY_NAME
    }
}