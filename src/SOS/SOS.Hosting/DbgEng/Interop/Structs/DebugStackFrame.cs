// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DEBUG_STACK_FRAME
    {
        public ulong InstructionOffset;
        public ulong ReturnOffset;
        public ulong FrameOffset;
        public ulong StackOffset;
        public ulong FuncTableEntry;
        public fixed ulong Params[4];
        public fixed ulong Reserved[6];
        public uint Virtual;
        public uint FrameNumber;
    }
}
