// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;

namespace Microsoft.FileFormats.PE
{
    public static class LayoutManagerExtensions
    {
        public static LayoutManager AddPETypes(this LayoutManager layouts, bool is64Bit)
        {
            return layouts
            .AddPrimitives(false)
            .AddEnumTypes()
            .AddSizeT(is64Bit ? 8 : 4)
            .AddNullTerminatedString()
            .AddTStructTypes(is64Bit ? new string[] { "PE32+" } : new string[] { "PE32" });
        }
    }

    /// <summary>
    /// IMAGE_NT_OPTIONAL_HDR32_MAGIC/IMAGE_NT_OPTIONAL_HDR64_MAGIC values
    /// </summary>
    public enum ImageMagic : ushort
    {
        Magic32 = 0x10b,
        Magic64 = 0x20b
    }

    /// <summary>
    /// IMAGE_OPTIONAL_HEADER
    /// </summary>
    public class ImageOptionalHeaderMagic : TStruct
    {
        public ImageMagic Magic;

        #region Validation Rules
        public ValidationRule IsMagicValid
        {
            get
            {
                return new ValidationRule("PE Optional Header has invalid magic field", () => Enum.IsDefined(typeof(ImageMagic), Magic));
            }
        }
        #endregion
    }

    /// <summary>
    /// IMAGE_OPTIONAL_HEADER
    /// </summary>
    public class ImageOptionalHeader : ImageOptionalHeaderMagic
    {
        // Standard fields
        public byte MajorLinkerVersion;
        public byte MinorLinkerVersion;
        public uint SizeOfCode;
        public uint SizeOfInitializedData;
        public uint SizeOfUninitializedData;
        public uint RVAOfEntryPoint;
        public uint BaseOfCode;
        [If("PE32")]
        public uint BaseOfData;

        // NT additional fields
        public SizeT ImageBase;
        public uint SectionAlignment;
        public uint FileAlignment;
        public ushort MajorOperatingSystemVersion;
        public ushort MinorOperatingSystemVersion;
        public ushort MajorImageVersion;
        public ushort MinorImageVersion;
        public ushort MajorSubsystemVersion;
        public ushort MinorSubsystemVersion;
        public uint Win32VersionValue;
        public uint SizeOfImage;
        public uint SizeOfHeaders;
        public uint CheckSum;
        public ushort Subsystem;
        public ushort DllCharacteristics;
        public SizeT SizeOfStackReserve;
        public SizeT SizeOfStackCommit;
        public SizeT SizeOfHeapReserve;
        public SizeT SizeOfHeapCommit;
        public uint LoaderFlags;
        public uint NumberOfRvaAndSizes;
    }

    /// <summary>
    /// IMAGE_FILE_MACHINE_* values for ImageFileHeader.Machine
    /// </summary>
    public enum ImageFileMachine
    {
        Unknown     = 0,
        Amd64       = 0x8664,   // AMD64 (K8)
        I386        = 0x014c,   // Intel 386.
        Arm         = 0x01c0,   // ARM Little-Endian
        Thumb       = 0x01c2,
        ArmNT       = 0x01c4,   // ARM Thumb-2 Little-Endian
        Arm64       = 0xAA64
    }

    /// <summary>
    /// Characteristics (IMAGE_FILE)
    /// </summary>
    [Flags]
    public enum ImageFile : ushort
    {
        RelocsStripped      = 0x0001,
        ExecutableImage     = 0x0002,
        LargeAddressAware   = 0x0020,
        System              = 0x1000,
        Dll                 = 0x2000,
    }

    /// <summary>
    /// IMAGE_FILE_HEADER struct
    /// </summary>
    public class ImageFileHeader : TStruct
    {
        public ushort Machine;
        public ushort NumberOfSections;
        public uint TimeDateStamp;
        public uint PointerToSymbolTable;
        public uint NumberOfSymbols;
        public ushort SizeOfOptionalHeader;
        public ushort Characteristics;
    }

    #region Section Header

    /// <summary>
    /// IMAGE_SECTION_HEADER
    /// </summary>
    public class ImageSectionHeader : TStruct
    {
        [ArraySize(8)]
        public byte[] Name;
        public uint VirtualSize;
        public uint VirtualAddress;
        public uint SizeOfRawData;
        public uint PointerToRawData;
        public uint PointerToRelocations;
        public uint PointerToLinenumbers;
        public ushort NumberOfRelocations;
        public ushort NumberOfLinenumbers;
        public uint Characteristics;
    }

    #endregion

    #region Directories

    /// <summary>
    /// IMAGE_DIRECTORY_ENTRY_* defines
    /// </summary>
    public enum ImageDirectoryEntry
    {
        Export = 0,
        Import = 1,
        Resource = 2,
        Exception = 3,
        Certificates = 4,
        BaseRelocation = 5,
        Debug = 6,
        Architecture = 7,
        GlobalPointers = 8,
        ThreadStorage = 9,
        LoadConfiguration = 10,
        BoundImport = 11,
        ImportAddress = 12,
        DelayImport = 13,
        ComDescriptor = 14
    }

    /// <summary>
    /// IMAGE_DATA_DIRECTORY struct
    /// </summary>
    public class ImageDataDirectory : TStruct
    {
        public uint VirtualAddress;
        public uint Size;
    }

    #endregion

    #region Debug Directory

    /// <summary>
    /// IMAGE_DEBUG_TYPE_* defines
    /// </summary>
    public enum ImageDebugType
    {
        Unknown = 0,
        Coff = 1,
        Codeview = 2,
        Fpo = 3,
        Misc = 4,
        Bbt = 10,
        Reproducible = 16,
        EmbeddedPortablePdb = 17,
        PdbChecksum = 19,
        PerfMap = 21
    };

    /// <summary>
    /// IMAGE_DEBUG_DIRECTORY struct
    /// </summary>
    public class ImageDebugDirectory : TStruct
    {
        public const ushort PortablePDBMinorVersion = 0x504d;

        public uint Characteristics;
        public uint TimeDateStamp;
        public ushort MajorVersion;
        public ushort MinorVersion;
        public ImageDebugType Type;
        public uint SizeOfData;
        public uint AddressOfRawData;
        public uint PointerToRawData;
    };

    public class CvInfoPdb70 : TStruct
    {
        public const int PDB70CvSignature = 0x53445352; // RSDS in ascii

        public int CvSignature;
        [ArraySize(16)]
        public byte[] Signature;
        public int Age;
    }

    public class PerfMapIdV1 : TStruct
    {
        public const int PerfMapEntryMagic = 0x4D523252; // R2RM in ascii

        public int Magic;

        [ArraySize(16)]
        public byte[] Signature;
        public uint Version;
    }

    #endregion

    #region Resource Directory

    /// <summary>
    /// IMAGE_RESOURCE_DIRECTORY struct
    /// </summary>
    public class ImageResourceDirectory : TStruct
    {
        public uint Characteristics;
        public uint TimeDateStamp;
        public ushort MajorVersion;
        public ushort MinorVersion;
        public ushort NumberOfNamedEntries;
        public ushort NumberOfIdEntries;
    };

    /// <summary>
    /// IMAGE_RESOURCE_DIRECTORY_ENTRY for the resources by id
    /// </summary>
    public class ImageResourceDirectoryEntry : TStruct
    {
        // Resource id or name offset. Currently doesn't supported named entries/resources.
        public uint Id;

        // High bit 0. Address of a Resource Data entry (a leaf).
        // High bit 1. The lower 31 bits are the address of another resource directory table (the next level down).
        public uint OffsetToData;
    }

    /// <summary>
    /// IMAGE_RESOURCE_DATA_ENTRY struct
    /// </summary>
    public class ImageResourceDataEntry : TStruct
    {
        public uint OffsetToData;
        public uint Size;
        public uint CodePage;
        public uint Reserved;
    }

    /// <summary>
    /// VS_FIXEDFILEINFO.FileFlags
    /// </summary>
    [Flags]
    public enum FileInfoFlags : uint
    {
        Debug         = 0x00000001,
        SpecialBuild  = 0x00000020,
    }

    /// <summary>
    /// VS_FIXEDFILEINFO struct
    /// </summary>
    public class VsFixedFileInfo : TStruct
    {
        public const uint FixedFileInfoSignature = 0xFEEF04BD;

        public uint Signature;          // e.g. 0xfeef04bd
        public uint StrucVersion;       // e.g. 0x00000042 = "0.42"
        public ushort FileVersionMinor;
        public ushort FileVersionMajor;
        public ushort FileVersionRevision;
        public ushort FileVersionBuild;
        public ushort ProductVersionMinor;
        public ushort ProductVersionMajor;
        public ushort ProductVersionRevision;
        public ushort ProductVersionBuild;
        public uint FileFlagsMask;      // = 0x3F for version "0.42"
        public FileInfoFlags FileFlags;
        public uint FileOS;             // e.g. VOS_DOS_WINDOWS16
        public uint FileType;           // e.g. VFT_DRIVER
        public uint FileSubtype;        // e.g. VFT2_DRV_KEYBOARD
        public uint FileDateMS;         // e.g. 0
        public uint FileDateLS;         // e.g. 0
    }

    /// <summary>
    /// VS_VERSIONINFO struct
    /// </summary>
    public class VsVersionInfo  : TStruct
    {
        public ushort Length;
        public ushort ValueLength;
        public ushort Type;
        [ArraySize(16)]
        public char[] Key;
        public ushort Padding1;
        public VsFixedFileInfo Value;
    }

    #endregion

    #region IMAGE_EXPORT_DIRECTORY

    /// <summary>
    /// IMAGE_EXPORT_DIRECTORY struct
    /// </summary>
    public class ImageExportDirectory : TStruct
    {
        public uint Characteristics;
        public uint TimeDateStamp;
        public ushort MajorVersion;
        public ushort MinorVersion;
        public uint Name;
        public uint Base;
        public uint NumberOfFunctions;
        public uint NumberOfNames;
        public uint AddressOfFunctions;     // RVA from base of image
        public uint AddressOfNames;         // RVA from base of image
        public uint AddressOfNameOrdinals;  // RVA from base of image
    };

    #endregion

    /// <summary>
    /// Pair of a checksum algorithm name (ex: "SHA256") and the bytes of the checksum.
    /// </summary>
    public class PdbChecksum : TStruct
    {
        public PdbChecksum(string algorithmName, byte[] checksum)
        {
            AlgorithmName = algorithmName;
            Checksum = checksum;
        }

        public string AlgorithmName { get; }
        public byte[] Checksum { get; }

        public override string ToString()
        {
            return $"{AlgorithmName}:{ToHexString(Checksum)}";
        }

        /// <summary>
        /// Convert an array of bytes to a lower case hex string.
        /// </summary>
        /// <param name="bytes">array of bytes</param>
        /// <returns>hex string</returns>
        public static string ToHexString(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }
            return string.Concat(bytes.Select(b => b.ToString("x2")));
        }
    }
}
