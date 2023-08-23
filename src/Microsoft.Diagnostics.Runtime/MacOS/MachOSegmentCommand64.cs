// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.MacOS
{
    internal struct MachOSegmentCommand64
    {
        // readonly MachOCommand
        internal unsafe fixed byte SegmentName[16];
        internal readonly long VMAddress;
        internal readonly long VMSize;
        internal readonly long FileOffset;
        internal readonly long FileSize;
        internal readonly int MaximumProtection;
        internal readonly int InitialProtection;
        internal readonly int NumberOfSections;
        internal readonly int Flags;
    }
}