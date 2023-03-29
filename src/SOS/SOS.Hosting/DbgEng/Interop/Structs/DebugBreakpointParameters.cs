// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct DEBUG_BREAKPOINT_PARAMETERS
    {
        public ulong Offset;
        public uint Id;
        public DEBUG_BREAKPOINT_TYPE BreakType;
        public uint ProcType;
        public DEBUG_BREAKPOINT_FLAG Flags;
        public uint DataSize;
        public DEBUG_BREAKPOINT_ACCESS_TYPE DataAccessType;
        public uint PassCount;
        public uint CurrentPassCount;
        public uint MatchThread;
        public uint CommandSize;
        public uint OffsetExpressionSize;
    }
}
