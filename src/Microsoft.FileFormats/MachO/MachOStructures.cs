// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Linq;
using System.Text;

namespace Microsoft.FileFormats.MachO
{
    public static class MachLayoutManagerExtensions
    {
        public static LayoutManager AddMachTypes(this LayoutManager layoutManager, bool isBigEndian, bool is64Bit)
        {
            layoutManager
                .AddPrimitives(isBigEndian)
                .AddSizeT(is64Bit ? 8 : 4)
                .AddEnumTypes()
                .AddNullTerminatedString()
                .AddTStructTypes();
            return layoutManager;
        }
    }

    public enum MachHeaderMagicType : uint
    {
        LittleEndian64Bit = 0xfeedfacf,
        LittleEndian32Bit = 0xfeedface,
        BigEndian64Bit = 0xcffaedfe,
        BigEndian32Bit = 0xcefaedfe
    }

    public class MachHeaderMagic : TStruct
    {
        public MachHeaderMagicType Magic;
        #region Validation Rules
        public ValidationRule IsMagicValid
        {
            get
            {
                return new ValidationRule("Invalid MachO Header Magic", () =>
                {
                    return Magic == MachHeaderMagicType.LittleEndian32Bit ||
                           Magic == MachHeaderMagicType.LittleEndian64Bit;
                });
            }
        }
        #endregion
    }

    public enum MachHeaderFileType : uint
    {
        Object = 1,
        Execute = 2,
        FvmLib = 3,
        Core = 4,
        Preload = 5,
        Dylib = 6,
        Dylinker = 7,
        Bundle = 8,
        DylibStub = 9,
        Dsym = 10,
        KextBundle = 11
    }

    public class MachHeader : MachHeaderMagic
    {
        public uint CpuType;
        public uint CpuSubType;
        public MachHeaderFileType FileType;
        public uint NumberCommands;
        public uint SizeOfCommands;
        public uint Flags;

        #region Validation Rules
        public ValidationRule IsFileTypeValid
        {
            get
            {
                return new ValidationRule("Mach Header FileType is invalid",
                                          () => Enum.IsDefined(typeof(MachHeaderFileType), FileType));
            }
        }

        public ValidationRule IsNumberCommandsReasonable
        {
            get
            {
                return new ValidationRule("Mach Header NumberCommands is unreasonable",
                                          () => NumberCommands <= 20000);
            }
        }
        #endregion
    }

    public enum LoadCommandType
    {
        Segment = 1,
        Symtab = 2,
        Thread = 4,
        DySymtab = 11,
        Segment64 = 25,
        Uuid = 27,
    }

    public class MachLoadCommand : TStruct
    {
        public LoadCommandType Command;
        public uint CommandSize;

        public override string ToString()
        {
            return "LoadCommand {" + Command + ", 0x" + CommandSize.ToString("x") + "}";
        }

        #region Validation Rules
        public ValidationRule IsCmdSizeReasonable
        {
            get
            {
                return new ValidationRule("Mach Load Command Size is unreasonable",
                                           () => CommandSize < 0x1000);
            }
        }

        public ValidationRule IsCommandRecognized
        {
            get
            {
                return new ValidationRule("Mach Load Command is not recognized",
                                           () => Enum.IsDefined(typeof(LoadCommandType), Command));
            }
        }
        #endregion
    }

    public class MachFixedLengthString16 : TStruct
    {
        [ArraySize(16)]
        public byte[] Bytes;

        public override string ToString()
        {
            try
            {
                return Encoding.UTF8.GetString(Bytes.TakeWhile(b => b != 0).ToArray());
            }
            catch (FormatException)
            {
                throw new BadInputFormatException("Bytes could not be parsed with UTF8 encoding");
            }
        }
    }

    public class MachSegmentLoadCommand : MachLoadCommand
    {
        public MachFixedLengthString16 SegName;
        public SizeT VMAddress;
        public SizeT VMSize;
        public SizeT FileOffset;
        public SizeT FileSize;
        public uint MaxProt;
        public uint InitProt;
        public uint CountSections;
        public uint Flags;

        #region Validation Rules
        private ValidationRule IsCommandValid
        {
            get
            {
                return new ValidationRule("Mach Segment Command has invalid Command field",
                                          () => Command == LoadCommandType.Segment || Command == LoadCommandType.Segment64);
            }
        }
        #endregion
    }

    public class MachSection : TStruct
    {
        public MachFixedLengthString16 SectionName;
        public MachFixedLengthString16 SegmentName;
        public SizeT Address;
        public SizeT Size;
        public uint Offset;
        public uint Align;
        public uint RelativeOffset;
        public uint CountRelocs;
        public uint Flags;
        public uint Reserved1;
        public uint Reserved2;
    }

    public class MachUuidLoadCommand : MachLoadCommand
    {
        [ArraySize(16)]
        public byte[] Uuid;

        #region Validation Rules
        public ValidationRule IsCommandValid
        {
            get
            {
                return new ValidationRule("Mach UUID LoadCommand has invalid command id",
                                           () => Command == LoadCommandType.Uuid);
            }
        }

        public ValidationRule IsCommandSizeValid
        {
            get
            {
                return new ValidationRule("Mach UUID LoadCommand has invalid size",
                                          () => CommandSize == 24);
            }
        }
        #endregion
    }

    public class MachSymtabLoadCommand : MachLoadCommand
    {
        public uint SymOffset;
        public uint SymCount;
        public uint StringOffset;
        public uint StringSize;

        #region Validation Rules
        public ValidationRule IsCommandValid
        {
            get
            {
                return new ValidationRule("Mach Symtab LoadCommand has invalid command id",
                                           () => Command == LoadCommandType.Symtab);
            }
        }

        public ValidationRule IsCommandSizeValid
        {
            get
            {
                return new ValidationRule("Mach Symtab LoadCommand has invalid size",
                                          () => CommandSize == 24);
            }
        }

        public ValidationRule IsNSymsReasonable
        {
            get
            {
                return new ValidationRule("Mach symtab LoadCommand has unreasonable SymCount",
                                          () => SymCount <= 0x100000);
            }
        }
        #endregion
    }

    public class MachDySymtabLoadCommand : MachLoadCommand
    {
        public uint ILocalSym;
        public uint NLocalSym;
        public uint IExtDefSym;
        public uint NextDefSym;
        public uint IUndefSym;
        public uint NUndefSym;
        public uint ToCoff;
        public uint NToc;
        public uint ModTabOff;
        public uint MModTab;
        public uint ExtrefSymOff;
        public uint NextrefSyms;
        public uint IndirectSymOff;
        public uint NindirectSyms;
        public uint ExtrelOff;
        public uint Nextrel;
        public uint LocrelOff;
        public uint NLocrel;
    }

    public class NList : TStruct
    {
        public uint StringIndex;
        public byte Type;
        public byte Section;
        public ushort Desc;
        public SizeT Value;
    }

    public class DyldImageAllInfosVersion : TStruct
    {
        public uint Version;
    }

    public class DyldImageAllInfosV2 : DyldImageAllInfosVersion
    {
        public uint InfoArrayCount;
        public SizeT InfoArray;
        public SizeT Notification;
        public SizeT Undetermined; // there are some fields here but I haven't determined their size and purpose
        public SizeT ImageLoadAddress;
    }

    public class DyldImageInfo : TStruct
    {
        public SizeT Address;
        public SizeT PathAddress;
        public SizeT ModDate;
    }
}
