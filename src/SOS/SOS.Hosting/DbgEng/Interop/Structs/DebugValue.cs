// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct DEBUG_VALUE
    {
        [FieldOffset(0)]
        public byte I8;
        [FieldOffset(0)]
        public ushort I16;
        [FieldOffset(0)]
        public uint I32;
        [FieldOffset(0)]
        public ulong I64;
        [FieldOffset(8)]
        public uint Nat;
        [FieldOffset(0)]
        public float F32;
        [FieldOffset(0)]
        public double F64;
        [FieldOffset(0)]
        public fixed byte F80Bytes[10];
        [FieldOffset(0)]
        public fixed byte F82Bytes[11];
        [FieldOffset(0)]
        public fixed byte F128Bytes[16];
        [FieldOffset(0)]
        public fixed byte VI8[16];
        [FieldOffset(0)]
        public fixed ushort VI16[8];
        [FieldOffset(0)]
        public fixed uint VI32[4];
        [FieldOffset(0)]
        public fixed ulong VI64[2];
        [FieldOffset(0)]
        public fixed float VF32[4];
        [FieldOffset(0)]
        public fixed double VF64[2];
        [FieldOffset(0)]
        public I64PARTS32 I64Parts32;
        [FieldOffset(0)]
        public F128PARTS64 F128Parts64;
        [FieldOffset(0)]
        public fixed byte RawBytes[24];
        [FieldOffset(24)]
        public uint TailOfRawBytes;
        [FieldOffset(28)]
        public DEBUG_VALUE_TYPE Type;
    }
}
