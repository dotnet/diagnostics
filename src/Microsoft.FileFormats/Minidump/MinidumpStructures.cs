// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.FileFormats.Minidump
{
    public static class CrashDumpLayoutManagerExtensions
    {
        public static LayoutManager AddCrashDumpTypes(this LayoutManager layouts, bool isBigEndian, bool is64Bit)
        {
            return layouts
                     .AddPrimitives(isBigEndian)
                     .AddEnumTypes()
                     .AddSizeT(is64Bit ? 8 : 4)
                     .AddPointerTypes()
                     .AddNullTerminatedString()
                     .AddTStructTypes();
        }
    }

#pragma warning disable 0649
#pragma warning disable 0169

    internal sealed class MinidumpHeader : TStruct
    {
        public const int MinidumpVersion = 0x504d444d;

        public uint Signature;
        public uint Version;
        public uint NumberOfStreams;
        public uint StreamDirectoryRva;
        public uint CheckSum;
        public uint TimeDateStamp;
        public ulong Flags;

        public ValidationRule IsSignatureValid
        {
            get
            {
                return new ValidationRule("Invalid minidump header signature", () =>
                {
                    return Signature == MinidumpVersion;
                });
            }
        }
    }

    internal sealed class MinidumpDirectory : TStruct
    {
        public MinidumpStreamType StreamType;
        public uint DataSize;
        public uint Rva;
    }

    internal enum MinidumpStreamType
    {
        UnusedStream = 0,
        ReservedStream0 = 1,
        ReservedStream1 = 2,
        ThreadListStream = 3,
        ModuleListStream = 4,
        MemoryListStream = 5,
        ExceptionStream = 6,
        SystemInfoStream = 7,
        ThreadExListStream = 8,
        Memory64ListStream = 9,
        CommentStreamA = 10,
        CommentStreamW = 11,
        HandleDataStream = 12,
        FunctionTableStream = 13,
        UnloadedModuleListStream = 14,
        MiscInfoStream = 15,
        MemoryInfoListStream = 16,
        ThreadInfoListStream = 17,
        LastReservedStream = 0xffff,
    }


    internal sealed class MinidumpSystemInfo : TStruct
    {
        public ProcessorArchitecture ProcessorArchitecture;
        public ushort ProcessorLevel;
        public ushort ProcessorRevision;
        public byte NumberOfProcessors;
        public byte ProductType;
        public uint MajorVersion;
        public uint MinorVersion;
        public uint BuildNumber;
        public uint PlatformId;
        public uint CSDVersionRva;
    }

    public enum ProcessorArchitecture : ushort
    {
        Intel = 0,
        Mips = 1,
        Alpha = 2,
        Ppc = 3,
        Shx = 4,
        Arm = 5,
        Ia64 = 6,
        Alpha64 = 7,
        Msil = 8,
        Amd64 = 9,
        Ia32OnWin64 = 10,
    }

    internal sealed class FixedFileInfo : TStruct
    {
        public uint Signature;            /* e.g. 0xfeef04bd */
        public uint StrucVersion;         /* e.g. 0x00000042 = "0.42" */
        public uint FileVersionMS;        /* e.g. 0x00030075 = "3.75" */
        public uint FileVersionLS;        /* e.g. 0x00000031 = "0.31" */
        public uint ProductVersionMS;     /* e.g. 0x00030010 = "3.10" */
        public uint ProductVersionLS;     /* e.g. 0x00000031 = "0.31" */
        public uint FileFlagsMask;        /* = 0x3F for version "0.42" */
        public uint FileFlags;            /* e.g. VFF_DEBUG | VFF_PRERELEASE */
        public uint FileOS;               /* e.g. VOS_DOS_WINDOWS16 */
        public uint FileType;             /* e.g. VFT_DRIVER */
        public uint FileSubtype;          /* e.g. VFT2_DRV_KEYBOARD */

        // Timestamps would be useful, but they're generally missing (0).
        public uint FileDateMS;           /* e.g. 0 */
        public uint FileDateLS;           /* e.g. 0 */
    }


    internal sealed class MinidumpLocationDescriptor : TStruct
    {
        public uint DataSize;
        public uint Rva;
    }

    [Pack(4)]
    internal sealed class MinidumpModule : TStruct
    {
        public ulong Baseofimage;
        public uint SizeOfImage;
        public uint CheckSum;
        public uint TimeDateStamp;
        public uint ModuleNameRva;
        public FixedFileInfo VersionInfo;
        public MinidumpLocationDescriptor CvRecord;
        public MinidumpLocationDescriptor MiscRecord;
#pragma warning disable CA1823 // Avoid unused private fields
        private ulong _reserved0;
        private ulong _reserved1;
#pragma warning restore CA1823 // Avoid unused private fields
    }

    internal sealed class MinidumpMemoryDescriptor : TStruct
    {
        public ulong StartOfMemoryRange;
        public MinidumpLocationDescriptor Memory;

    }

    internal sealed class MinidumpMemoryDescriptor64 : TStruct
    {
        public ulong StartOfMemoryRange;
        public ulong DataSize;
    }
}
