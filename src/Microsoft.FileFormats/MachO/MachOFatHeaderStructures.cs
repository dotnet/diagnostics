// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.FileFormats.MachO
{
    public static class MachFatHeaderLayoutManagerExtensions
    {
        public static LayoutManager AddMachFatHeaderTypes(this LayoutManager layoutManager, bool isBigEndian)
        {
            layoutManager
                .AddPrimitives(isBigEndian)
                .AddEnumTypes()
                .AddTStructTypes();
            return layoutManager;
        }
    }

    public enum MachFatHeaderMagicKind : uint
    {
        LittleEndian = 0xcafebabe,
        BigEndian = 0xbebafeca
    }

    public class MachFatHeaderMagic : TStruct
    {
        public MachFatHeaderMagicKind Magic;

        #region Validation Rules
        public ValidationRule IsMagicValid
        {
            get
            {
                return new ValidationRule("Invalid MachO Fat Header Magic", () =>
                {
                    return Magic == MachFatHeaderMagicKind.BigEndian ||
                           Magic == MachFatHeaderMagicKind.LittleEndian;
                });
            }
        }
        #endregion
    }

    public class MachFatHeader : MachFatHeaderMagic
    {
        public uint CountFatArches;

        #region Validation Rules
        public ValidationRule IsCountFatArchesReasonable
        {
            get
            {
                return new ValidationRule("Unreasonable MachO Fat Header CountFatArches",
                    () => CountFatArches <= 20);
            }
        }
        #endregion
    }

    public class MachFatArch : TStruct
    {
        public uint CpuType;
        public uint CpuSubType;
        public uint Offset;
        public uint Size;
        public uint Align;
    }
}
