// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DEBUG_CACHED_SYMBOL_INFO
    {
        public ulong ModBase;
        public ulong Arg1;
        public ulong Arg2;
        public uint Id;
        public uint Arg3;
    }
}
