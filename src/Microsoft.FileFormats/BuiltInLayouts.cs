// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.FileFormats.ELF;
using Microsoft.FileFormats.MachO;
using Microsoft.FileFormats.Minidump;
using Microsoft.FileFormats.PDB;
using Microsoft.FileFormats.PE;

namespace Microsoft.FileFormats
{
    internal static class BuiltInLayouts
    {
        internal static LayoutManager RegisterPE(this LayoutManager layouts, IEnumerable<string> defines)
        {
            return layouts
                .RegisterArray<byte>()
                .RegisterArray<char>()
                .RegisterTStruct<ImageOptionalHeaderMagic>()
                .RegisterTStruct<ImageOptionalHeader>(enabledDefines: defines)
                .RegisterTStruct<ImageFileHeader>()
                .RegisterTStruct<ImageSectionHeader>()
                .RegisterTStruct<ImageDataDirectory>()
                .RegisterTStruct<ImageDebugDirectory>()
                .RegisterTStruct<CvInfoPdb70>()
                .RegisterTStruct<PerfMapIdV1>()
                .RegisterTStruct<ImageResourceDirectory>()
                .RegisterTStruct<ImageResourceDirectoryEntry>()
                .RegisterTStruct<ImageResourceDataEntry>()
                .RegisterTStruct<VsFixedFileInfo>()
                .RegisterTStruct<VsVersionInfo>()
                .RegisterTStruct<ImageExportDirectory>();
        }

        internal static LayoutManager RegisterELF(this LayoutManager layouts, IEnumerable<string> defines)
        {
            return layouts
                .RegisterArray<byte>()
                .RegisterPointer<FileOffset, byte, SizeT>()
                .RegisterPointer<VirtualAddress, byte, SizeT>()
                .RegisterTStruct<ELFHeaderIdent>()
                .RegisterTStruct<ELFHeader>()
                .RegisterTStruct<ELFProgramHeader>(enabledDefines: defines)
                .RegisterTStruct<ELFSectionHeader>()
                .RegisterTStruct<ELFNoteHeader>()
                .RegisterTStruct<ELFFileTableHeader>()
                .RegisterTStruct<ELFFileTableEntryPointers>();
        }

        internal static LayoutManager RegisterMachFat(this LayoutManager layouts)
        {
            return layouts
                .RegisterTStruct<MachFatHeaderMagic>()
                .RegisterTStruct<MachFatHeader>()
                .RegisterTStruct<MachFatArch>();
        }

        internal static LayoutManager RegisterMach(this LayoutManager layouts)
        {
            return layouts
                .RegisterArray<byte>()
                .RegisterTStruct<MachHeaderMagic>()
                .RegisterTStruct<MachHeader>()
                .RegisterTStruct<MachLoadCommand>()
                .RegisterTStruct<MachFixedLengthString16>()
                .RegisterTStruct<MachSegmentLoadCommand>()
                .RegisterTStruct<MachSection>()
                .RegisterTStruct<MachSymtabLoadCommand>()
                .RegisterTStruct<MachDySymtabLoadCommand>()
                .RegisterTStruct<MachUuidLoadCommand>()
                .RegisterTStruct<NList>()
                .RegisterTStruct<DyldImageAllInfosVersion>()
                .RegisterTStruct<DyldImageAllInfosV2>()
                .RegisterTStruct<DyldImageInfo>();
        }

        internal static LayoutManager RegisterMinidump(this LayoutManager layouts)
        {
            return layouts
                .RegisterTStruct<MinidumpHeader>()
                .RegisterTStruct<MinidumpDirectory>()
                .RegisterTStruct<MinidumpSystemInfo>()
                .RegisterTStruct<FixedFileInfo>()
                .RegisterTStruct<MinidumpLocationDescriptor>()
                .RegisterTStruct<MinidumpModule>()
                .RegisterTStruct<MinidumpMemoryDescriptor>()
                .RegisterTStruct<MinidumpMemoryDescriptor64>();
        }

        internal static LayoutManager RegisterPDB(this LayoutManager layouts)
        {
            return layouts
                .RegisterArray<byte>()
                .RegisterTStruct<PDBFileHeader>()
                .RegisterTStruct<NameIndexStreamHeader>()
                .RegisterTStruct<DbiStreamHeader>()
                .RegisterTStruct<MSFZFileHeader>();
        }
    }
}
