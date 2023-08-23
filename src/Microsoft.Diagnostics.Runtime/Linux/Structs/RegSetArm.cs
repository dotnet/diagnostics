// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct RegSetArm
    {
        public uint r0;
        public uint r1;
        public uint r2;
        public uint r3;
        public uint r4;
        public uint r5;
        public uint r6;
        public uint r7;
        public uint r8;
        public uint r9;
        public uint r10;
        public uint fp;
        public uint ip;
        public uint sp;
        public uint lr;
        public uint pc;
        public uint cpsr;
        public uint orig_r0;

        public bool CopyContext(Span<byte> context)
        {
            if (context.Length < ArmContext.Size)
                return false;

            ref ArmContext contextRef = ref Unsafe.As<byte, ArmContext>(ref MemoryMarshal.GetReference(context));

            contextRef.ContextFlags = ArmContext.ContextControl | ArmContext.ContextInteger;
            contextRef.R0 = r0;
            contextRef.R1 = r1;
            contextRef.R2 = r2;
            contextRef.R3 = r3;
            contextRef.R4 = r4;
            contextRef.R5 = r5;
            contextRef.R6 = r6;
            contextRef.R7 = r7;
            contextRef.R8 = r8;
            contextRef.R9 = r9;
            contextRef.R10 = r10;
            contextRef.R11 = fp;
            contextRef.R12 = ip;
            contextRef.Sp = sp;
            contextRef.Lr = lr;
            contextRef.Pc = pc;
            contextRef.Cpsr = cpsr;

            return true;
        }
    }
}
