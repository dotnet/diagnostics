// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.FileFormats.PDB
{
    public class PDBFileHeader : TStruct
    {
        private static byte[] ExpectedMagic
        {
            get
            {
                return new byte[]
                {
                    0x4D, 0x69, 0x63, 0x72, 0x6F, 0x73, 0x6F, 0x66, // "Microsof"
                    0x74, 0x20, 0x43, 0x2F, 0x43, 0x2B, 0x2B, 0x20, // "t C/C++ "
                    0x4D, 0x53, 0x46, 0x20, 0x37, 0x2E, 0x30, 0x30, // "MSF 7.00"
                    0x0D, 0x0A, 0x1A, 0x44, 0x53, 0x00, 0x00, 0x00  // "^^^DS^^^"
                };
            }
        }

        [ArraySize(32)]
        public byte[] Magic;
        public uint PageSize;
        public uint FreePageMap;
        public uint PagesUsed;
        public uint DirectorySize;
        public uint Reserved;

        #region Validation Rules
        public bool IsMagicValid
        {
            get { return Magic.SequenceEqual(ExpectedMagic); }
        }
        #endregion
    }

    public class NameIndexStreamHeader : TStruct
    {
        public uint Version;
        public uint Signature;
        public uint Age;
        [ArraySize(16)]
        public byte[] Guid;
        public uint CountStringBytes;
    }

    public class DbiStreamHeader : TStruct
    {
        public const uint CurrentSignature = uint.MaxValue;
        public const uint CurrentVersion = 19990903;          // DBIImpvV70

        public uint Signature;
        public uint Version;
        public uint Age;

        // This is not the complete DBI header, but it is enough to get the Age.

        #region Validation Rules
        public ValidationRule IsHeaderValid
        {
            get { return new ValidationRule("DBI header is invalid", () => Signature == CurrentSignature && Version == CurrentVersion); }
        }
        #endregion
    }
}
