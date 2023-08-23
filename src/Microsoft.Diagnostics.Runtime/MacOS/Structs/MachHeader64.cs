// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.MacOS
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct MachHeader64
    {
        public static unsafe uint Size => (uint)sizeof(MachHeader64);
        public const uint Magic64 = 0xfeedfacf;

        public uint Magic { get; }
        public MachOCpuType CpuType { get; }
        public int MachOCpuSubType { get; }
        public MachOFileType FileType { get; }
        public uint NumberCommands { get; }
        public uint SizeOfCommands { get; }
        public MachOFlags Flags { get; }
        public uint Reserved { get; }
    }
}
