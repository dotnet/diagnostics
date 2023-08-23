// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime
{
    [StructLayout(LayoutKind.Explicit)]
    public struct XmmSaveArea
    {
        public const int HeaderSize = 2;
        public const int LegacySize = 8;

        [FieldOffset(0x0)]
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = HeaderSize)]
        public M128A[] Header;

        [FieldOffset(0x20)]
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = LegacySize)]
        public M128A[] Legacy;

        [FieldOffset(0xa0)]
        public M128A Xmm0;
        [FieldOffset(0xb0)]
        public M128A Xmm1;
        [FieldOffset(0xc0)]
        public M128A Xmm2;
        [FieldOffset(0xd0)]
        public M128A Xmm3;
        [FieldOffset(0xe0)]
        public M128A Xmm4;
        [FieldOffset(0xf0)]
        public M128A Xmm5;
        [FieldOffset(0x100)]
        public M128A Xmm6;
        [FieldOffset(0x110)]
        public M128A Xmm7;
        [FieldOffset(0x120)]
        public M128A Xmm8;
        [FieldOffset(0x130)]
        public M128A Xmm9;
        [FieldOffset(0x140)]
        public M128A Xmm10;
        [FieldOffset(0x150)]
        public M128A Xmm11;
        [FieldOffset(0x160)]
        public M128A Xmm12;
        [FieldOffset(0x170)]
        public M128A Xmm13;
        [FieldOffset(0x180)]
        public M128A Xmm14;
        [FieldOffset(0x190)]
        public M128A Xmm15;
    }
}
