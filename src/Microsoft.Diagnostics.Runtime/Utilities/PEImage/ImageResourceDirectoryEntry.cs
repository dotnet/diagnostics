// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// Each directory contains the 32-bit Name of the entry and an offset,
    /// relative to the beginning of the resource directory of the data associated
    /// with this directory entry.  If the name of the entry is an actual text
    /// string instead of an integer Id, then the high order bit of the name field
    /// is set to one and the low order 31-bits are an offset, relative to the
    /// beginning of the resource directory of the string, which is of type
    /// IMAGE_RESOURCE_DIRECTORY_STRING.  Otherwise the high bit is clear and the
    /// low-order 16-bits are the integer Id that identify this resource directory
    /// entry. If the directory entry is yet another resource directory (i.e. a
    /// subdirectory), then the high order bit of the offset field will be
    /// set to indicate this.  Otherwise the high bit is clear and the offset
    /// field points to a resource data entry.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct ImageResourceDirectoryEntry
    {
        private readonly int _nameOffsetAndFlag;
        private readonly int _dataOffsetAndFlag;

        public bool IsStringName => _nameOffsetAndFlag < 0;
        public int NameOffset => _nameOffsetAndFlag & 0x7FFFFFFF;

        public bool IsLeaf => (0x80000000 & _dataOffsetAndFlag) == 0;
        public int DataOffset => _dataOffsetAndFlag & 0x7FFFFFFF;
        public int Id => 0xFFFF & _nameOffsetAndFlag;

        internal static string GetTypeNameForTypeId(int typeId) => typeId switch
        {
            1 => "Cursor",
            2 => "BitMap",
            3 => "Icon",
            4 => "Menu",
            5 => "Dialog",
            6 => "String",
            7 => "FontDir",
            8 => "Font",
            9 => "Accelerator",
            10 => "RCData",
            11 => "MessageTable",
            12 => "GroupCursor",
            14 => "GroupIcon",
            16 => "Version",
            19 => "PlugPlay",
            20 => "Vxd",
            21 => "Aniicursor",
            22 => "Aniicon",
            23 => "Html",
            24 => "RT_MANIFEST",
            _ => typeId.ToString(),
        };
    }
}