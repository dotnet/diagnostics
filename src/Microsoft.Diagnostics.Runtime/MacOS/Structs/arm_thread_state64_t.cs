// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct arm_thread_state64_t
    {
        public unsafe fixed ulong __x[29];
        public ulong __fp;
        public ulong __lr;
        public ulong __sp;
        public ulong __pc;
        public uint __cpsr;
        public uint __pad; // or __opaque_flags when ptrauth is enabled

        public unsafe bool CopyContext(Span<byte> context)
        {
            if (context.Length < Arm64Context.Size)
                return false;

            ref Arm64Context contextRef = ref Unsafe.As<byte, Arm64Context>(ref MemoryMarshal.GetReference(context));

            contextRef.ContextFlags = Arm64Context.ContextControl | Arm64Context.ContextInteger;
            contextRef.Cpsr = __cpsr;

            contextRef.X0 = __x[0];
            contextRef.X1 = __x[1];
            contextRef.X2 = __x[2];
            contextRef.X3 = __x[3];
            contextRef.X4 = __x[4];
            contextRef.X5 = __x[5];
            contextRef.X6 = __x[6];
            contextRef.X7 = __x[7];
            contextRef.X8 = __x[8];
            contextRef.X9 = __x[9];
            contextRef.X10 = __x[10];
            contextRef.X11 = __x[11];
            contextRef.X12 = __x[12];
            contextRef.X13 = __x[13];
            contextRef.X14 = __x[14];
            contextRef.X15 = __x[15];
            contextRef.X16 = __x[16];
            contextRef.X17 = __x[17];
            contextRef.X18 = __x[18];
            contextRef.X19 = __x[19];
            contextRef.X20 = __x[20];
            contextRef.X21 = __x[21];
            contextRef.X22 = __x[22];
            contextRef.X23 = __x[23];
            contextRef.X24 = __x[24];
            contextRef.X25 = __x[25];
            contextRef.X26 = __x[26];
            contextRef.X27 = __x[27];
            contextRef.X28 = __x[28];

            // no ptrauth
            contextRef.Fp = __fp;
            contextRef.Lr = __lr;
            contextRef.Sp = __sp;
            contextRef.Pc = __pc;

            return true;
        }
    }
}
