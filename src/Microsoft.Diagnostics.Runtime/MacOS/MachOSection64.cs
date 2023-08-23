// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.MacOS
{
    internal struct MachOSection64
    {
        internal unsafe fixed byte SectionName[16];
        internal unsafe fixed byte SegmentName[16];
        internal readonly long Address;
        internal readonly long Size;
        internal readonly int Offset;
        internal readonly int Alignment;
        internal readonly int RelocOffset;
        internal readonly int NumberOfReloc;
        internal readonly int Flags;
        internal readonly int Reserved1;
        internal readonly int Reserved2;
        internal readonly int Reserved3;
    }
}