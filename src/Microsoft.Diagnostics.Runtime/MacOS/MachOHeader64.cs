// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.MacOS
{
    internal readonly struct MachOHeader64
    {
        internal const int ExpectedMagic = unchecked((int)0xfeedfacf);

        internal readonly int Magic;
        internal readonly MachOCpuType CpuType;
        internal readonly int CpuSubtype;
        internal readonly int FileType;
        internal readonly int NumberOfCommands;
        internal readonly int SizeOfCommands;
        internal readonly int Flags;
        internal readonly int Reserved;
    }
}
