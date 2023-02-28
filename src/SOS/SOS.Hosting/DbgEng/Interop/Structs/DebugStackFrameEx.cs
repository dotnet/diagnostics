// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace SOS.Hosting.DbgEng.Interop
{
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DEBUG_STACK_FRAME_EX
    {
        /* DEBUG_STACK_FRAME */
        public ulong InstructionOffset;
        public ulong ReturnOffset;
        public ulong FrameOffset;
        public ulong StackOffset;
        public ulong FuncTableEntry;
        public fixed ulong Params[4];
        public fixed ulong Reserved[6];
        public uint Virtual;
        public uint FrameNumber;

        /* DEBUG_STACK_FRAME_EX */
        public uint InlineFrameContext;
        public uint Reserved1;

        public DEBUG_STACK_FRAME_EX(DEBUG_STACK_FRAME dsf)
        {
            InstructionOffset = dsf.InstructionOffset;
            ReturnOffset = dsf.ReturnOffset;
            FrameOffset = dsf.FrameOffset;
            StackOffset = dsf.StackOffset;
            FuncTableEntry = dsf.FuncTableEntry;

            fixed (ulong* pParams = Params)
            {
                for (int i = 0; i < 4; ++i)
                {
                    pParams[i] = dsf.Params[i];
                }
            }

            fixed (ulong* pReserved = Params)
            {
                for (int i = 0; i < 6; ++i)
                {
                    pReserved[i] = dsf.Reserved[i];
                }
            }

            Virtual = dsf.Virtual;
            FrameNumber = dsf.FrameNumber;
            InlineFrameContext = 0xFFFFFFFF;
            Reserved1 = 0;
        }
    }
}