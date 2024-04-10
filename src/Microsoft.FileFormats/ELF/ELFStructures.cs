// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.FileFormats.ELF
{
    public class FileOffset<T> : Pointer<T, SizeT> { }
    public class FileOffset : FileOffset<byte> { }
    public class VirtualAddress<T> : Pointer<T, SizeT> { }
    public class VirtualAddress : VirtualAddress<byte> { }

    public static class ELFLayoutManagerExtensions
    {
        public static LayoutManager AddELFTypes(this LayoutManager layouts, bool isBigEndian, bool is64Bit)
        {
            return layouts
                     .AddPrimitives(isBigEndian)
                     .AddEnumTypes()
                     .AddSizeT(is64Bit ? 8 : 4)
                     .AddPointerTypes()
                     .AddNullTerminatedString()
                     .AddTStructTypes(is64Bit ? new string[] { "64BIT" } : new string[] { "32BIT" });
        }
    }

    public enum ELFClass : byte
    {
        None = 0,
        Class32 = 1,
        Class64 = 2
    }

    public enum ELFData : byte
    {
        None = 0,
        LittleEndian = 1,
        BigEndian = 2
    }

    /// <summary>
    /// The leading 16 bytes of the ELF file format
    /// </summary>
    /// <remarks>
    /// Although normally this is described as being part of the ELFHeader, its
    /// useful to parse this independently. The endianess and bitness
    /// described in the identity bytes are needed to calculate the size of and
    /// offset of fields in the remainder of the header
    /// </remarks>
    public class ELFHeaderIdent : TStruct
    {
        [ArraySize(16)]
        public byte[] Ident;

        public ELFClass Class
        {
            get
            {
                return (ELFClass)Ident[4];
            }
        }

        public ELFData Data
        {
            get
            {
                return (ELFData)Ident[5];
            }
        }

        #region Validation Rules
        public ValidationRule IsIdentMagicValid
        {
            get
            {
                return new ValidationRule("Invalid ELFHeader Ident magic", () =>
                {
                    return Ident[0] == 0x7f &&
                       Ident[1] == 0x45 &&
                       Ident[2] == 0x4c &&
                       Ident[3] == 0x46;
                });
            }
        }

        public ValidationRule IsClassValid
        {
            get
            {
                return new ValidationRule("Invalid ELFHeader Ident Class", () =>
                {
                    return Class == ELFClass.Class32 || Class == ELFClass.Class64;
                });
            }
        }

        public ValidationRule IsDataValid
        {
            get
            {
                return new ValidationRule("Invalid ELFHeader Ident Data", () =>
                {
                    return Data == ELFData.BigEndian || Data == ELFData.LittleEndian;
                });
            }
        }
        #endregion
    }

    public enum ELFHeaderType : ushort
    {
        Relocatable = 1,
        Executable = 2,
        Shared = 3,
        Core = 4
    }

    public class ELFHeader : ELFHeaderIdent
    {
        public ELFHeaderType Type;
        public ushort Machine;
        public uint Version;
        public VirtualAddress Entry;
        public FileOffset ProgramHeaderOffset;
        public FileOffset SectionHeaderOffset;
        public uint Flags;
        public ushort EHSize;
        public ushort ProgramHeaderEntrySize;
        public ushort ProgramHeaderCount;
        public ushort SectionHeaderEntrySize;
        public ushort SectionHeaderCount;
        public ushort SectionHeaderStringIndex;

        #region Validation Rules

        public ValidationRule IsProgramHeaderCountReasonable
        {
            get
            {
                return new ValidationRule("Unreasonably large ELFHeader ProgramHeaderCount", () => ProgramHeaderCount <= 30000);
            }
        }

        public ValidationRule IsSectionHeaderCountReasonable
        {
            get
            {
                return new ValidationRule("Unreasonably large ELFHeader SectionHeaderCount", () => SectionHeaderCount <= 30000);
            }
        }

        #endregion
    }

    public class ELFFileTableHeader : TStruct
    {
        public SizeT EntryCount;
        public SizeT PageSize;
    }

    public class ELFFileTableEntryPointers : TStruct
    {
        public VirtualAddress Start;
        public VirtualAddress Stop;
        public SizeT PageOffset;
    }
}
