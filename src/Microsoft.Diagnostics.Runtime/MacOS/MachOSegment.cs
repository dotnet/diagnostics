// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Diagnostics.Runtime.MacOS.Structs;

namespace Microsoft.Diagnostics.Runtime.MacOS
{
    internal readonly struct MachOSegment
    {
        public ulong Address { get; }
        public ulong Size { get; }

        public ulong FileOffset { get; }
        public ulong FileSize { get; }

        public MachOSegment(in Segment64LoadCommand cmd)
        {
            Address = cmd.VMAddr;
            Size = cmd.VMSize;

            FileOffset = cmd.FileOffset;
            FileSize = cmd.FileSize;
        }
    }
}
