// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct RegSetArm64
    {
        public unsafe fixed ulong regs[31];
        public ulong sp;
        public ulong pc;
        public ulong pstate;

        public unsafe bool CopyContext(Span<byte> context)
        {
            if (context.Length < Arm64Context.Size)
                return false;

            ref Arm64Context contextRef = ref Unsafe.As<byte, Arm64Context>(ref MemoryMarshal.GetReference(context));

            contextRef.ContextFlags = Arm64Context.ContextControl | Arm64Context.ContextInteger;
            contextRef.Cpsr = (uint)pstate;

            contextRef.X0 = regs[0];
            contextRef.X1 = regs[1];
            contextRef.X2 = regs[2];
            contextRef.X3 = regs[3];
            contextRef.X4 = regs[4];
            contextRef.X5 = regs[5];
            contextRef.X6 = regs[6];
            contextRef.X7 = regs[7];
            contextRef.X8 = regs[8];
            contextRef.X9 = regs[9];
            contextRef.X10 = regs[10];
            contextRef.X11 = regs[11];
            contextRef.X12 = regs[12];
            contextRef.X13 = regs[13];
            contextRef.X14 = regs[14];
            contextRef.X15 = regs[15];
            contextRef.X16 = regs[16];
            contextRef.X17 = regs[17];
            contextRef.X18 = regs[18];
            contextRef.X19 = regs[19];
            contextRef.X20 = regs[20];
            contextRef.X21 = regs[21];
            contextRef.X22 = regs[22];
            contextRef.X23 = regs[23];
            contextRef.X24 = regs[24];
            contextRef.X25 = regs[25];
            contextRef.X26 = regs[26];
            contextRef.X27 = regs[27];
            contextRef.X28 = regs[28];

            contextRef.Fp = regs[29];
            contextRef.Lr = regs[30];
            contextRef.Sp = sp;
            contextRef.Pc = pc;

            return true;
        }
    }
}
