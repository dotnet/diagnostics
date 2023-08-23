// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Diagnostics.Runtime.MacOS.Structs
{
    internal readonly struct NList64
    {
        public uint n_strx { get; }
        public byte n_type { get; }
        public byte n_sect { get; }
        public ushort n_desc { get; }
        public ulong n_value { get; }
    }
}
