// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DEBUG_SYMBOL_PARAMETERS
    {
        public ulong Module;
        public uint TypeId;
        public uint ParentSymbol;
        public uint SubElements;
        public DEBUG_SYMBOL Flags;
        public ulong Reserved;
    }
}
