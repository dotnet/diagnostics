// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.MacOS.Structs
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct SymtabLoadCommand
    {
        public LoadCommandHeader Header { get; }
        public uint SymOff { get; }
        public uint NSyms { get; }
        public uint StrOff { get; }
        public uint StrSize { get; }
    }
}
