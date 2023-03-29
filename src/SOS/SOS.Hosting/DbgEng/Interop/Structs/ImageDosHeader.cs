// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Explicit)]
    public unsafe struct IMAGE_DOS_HEADER
    {
        [FieldOffset(0)]
        public ushort e_magic; // Magic number
        [FieldOffset(2)]
        public ushort e_cblp; // Bytes on last page of file
        [FieldOffset(4)]
        public ushort e_cp; // Pages in file
        [FieldOffset(6)]
        public ushort e_crlc; // Relocations
        [FieldOffset(8)]
        public ushort e_cparhdr; // Size of header in paragraphs
        [FieldOffset(10)]
        public ushort e_minalloc; // Minimum extra paragraphs needed
        [FieldOffset(12)]
        public ushort e_maxalloc; // Maximum extra paragraphs needed
        [FieldOffset(14)]
        public ushort e_ss; // Initial (relative) SS value
        [FieldOffset(16)]
        public ushort e_sp; // Initial SP value
        [FieldOffset(18)]
        public ushort e_csum; // Checksum
        [FieldOffset(20)]
        public ushort e_ip; // Initial IP value
        [FieldOffset(22)]
        public ushort e_cs; // Initial (relative) CS value
        [FieldOffset(24)]
        public ushort e_lfarlc; // File address of relocation table
        [FieldOffset(26)]
        public ushort e_ovno; // Overlay number
        [FieldOffset(28)]
        public fixed ushort e_res[4]; // Reserved words
        [FieldOffset(36)]
        public ushort e_oemid; // OEM identifier (for e_oeminfo)
        [FieldOffset(38)]
        public ushort e_oeminfo; // OEM information; e_oemid specific
        [FieldOffset(40)]
        public fixed ushort e_res2[10]; // Reserved words
        [FieldOffset(60)]
        public uint e_lfanew; // File address of new exe header
    }
}
