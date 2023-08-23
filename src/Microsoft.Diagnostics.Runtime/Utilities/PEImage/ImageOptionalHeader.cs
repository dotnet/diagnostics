// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [StructLayout(LayoutKind.Explicit)]
    internal struct ImageOptionalHeader
    {
        [FieldOffset(0)]
        public ushort Magic;
        [FieldOffset(2)]
        public byte MajorLinkerVersion;
        [FieldOffset(3)]
        public byte MinorLinkerVersion;
        [FieldOffset(4)]
        public uint SizeOfCode;
        [FieldOffset(8)]
        public uint SizeOfInitializedData;
        [FieldOffset(12)]
        public uint SizeOfUninitializedData;
        [FieldOffset(16)]
        public uint AddressOfEntryPoint;
        [FieldOffset(20)]
        public uint BaseOfCode;
        [FieldOffset(24)]
        public ulong ImageBase64;
        [FieldOffset(24)]
        public uint BaseOfData;
        [FieldOffset(28)]
        public uint ImageBase;
        [FieldOffset(32)]
        public uint SectionAlignment;
        [FieldOffset(36)]
        public uint FileAlignment;
        [FieldOffset(40)]
        public ushort MajorOperatingSystemVersion;
        [FieldOffset(42)]
        public ushort MinorOperatingSystemVersion;
        [FieldOffset(44)]
        public ushort MajorImageVersion;
        [FieldOffset(46)]
        public ushort MinorImageVersion;
        [FieldOffset(48)]
        public ushort MajorSubsystemVersion;
        [FieldOffset(50)]
        public ushort MinorSubsystemVersion;
        [FieldOffset(52)]
        public uint Win32VersionValue;
        [FieldOffset(56)]
        public int SizeOfImage;
        [FieldOffset(60)]
        public uint SizeOfHeaders;
        [FieldOffset(64)]
        public uint CheckSum;
        [FieldOffset(68)]
        public ushort Subsystem;
        [FieldOffset(70)]
        public ushort DllCharacteristics;
    }
}
