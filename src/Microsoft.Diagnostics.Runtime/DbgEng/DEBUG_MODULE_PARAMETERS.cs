// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DbgEng
{
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct DEBUG_MODULE_PARAMETERS
    {
        public ulong Base;
        public int Size;
        public int TimeDateStamp;
        public uint Checksum;
        public uint Flags;
        public uint SymbolType;
        public uint ImageNameSize;
        public uint ModuleNameSize;
        public uint LoadedImageNameSize;
        public uint SymbolFileNameSize;
        public uint MappedImageNameSize;
        public fixed ulong Reserved[2];
    }
}
