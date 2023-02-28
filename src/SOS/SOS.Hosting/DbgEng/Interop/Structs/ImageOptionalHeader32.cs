// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Explicit)]
    public struct IMAGE_OPTIONAL_HEADER32
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
        public uint SizeOfImage;
        [FieldOffset(60)]
        public uint SizeOfHeaders;
        [FieldOffset(64)]
        public uint CheckSum;
        [FieldOffset(68)]
        public ushort Subsystem;
        [FieldOffset(70)]
        public ushort DllCharacteristics;
        [FieldOffset(72)]
        public uint SizeOfStackReserve;
        [FieldOffset(76)]
        public uint SizeOfStackCommit;
        [FieldOffset(80)]
        public uint SizeOfHeapReserve;
        [FieldOffset(84)]
        public uint SizeOfHeapCommit;
        [FieldOffset(88)]
        public uint LoaderFlags;
        [FieldOffset(92)]
        public uint NumberOfRvaAndSizes;
        [FieldOffset(96)]
        public IMAGE_DATA_DIRECTORY DataDirectory0;
        [FieldOffset(104)]
        public IMAGE_DATA_DIRECTORY DataDirectory1;
        [FieldOffset(112)]
        public IMAGE_DATA_DIRECTORY DataDirectory2;
        [FieldOffset(120)]
        public IMAGE_DATA_DIRECTORY DataDirectory3;
        [FieldOffset(128)]
        public IMAGE_DATA_DIRECTORY DataDirectory4;
        [FieldOffset(136)]
        public IMAGE_DATA_DIRECTORY DataDirectory5;
        [FieldOffset(144)]
        public IMAGE_DATA_DIRECTORY DataDirectory6;
        [FieldOffset(152)]
        public IMAGE_DATA_DIRECTORY DataDirectory7;
        [FieldOffset(160)]
        public IMAGE_DATA_DIRECTORY DataDirectory8;
        [FieldOffset(168)]
        public IMAGE_DATA_DIRECTORY DataDirectory9;
        [FieldOffset(176)]
        public IMAGE_DATA_DIRECTORY DataDirectory10;
        [FieldOffset(284)]
        public IMAGE_DATA_DIRECTORY DataDirectory11;
        [FieldOffset(292)]
        public IMAGE_DATA_DIRECTORY DataDirectory12;
        [FieldOffset(300)]
        public IMAGE_DATA_DIRECTORY DataDirectory13;
        [FieldOffset(308)]
        public IMAGE_DATA_DIRECTORY DataDirectory14;
        [FieldOffset(316)]
        public IMAGE_DATA_DIRECTORY DataDirectory15;

        public static unsafe IMAGE_DATA_DIRECTORY* GetDataDirectory(IMAGE_OPTIONAL_HEADER32* header, int zeroBasedIndex)
        {
            return &header->DataDirectory0 + zeroBasedIndex;
        }
    }
}
