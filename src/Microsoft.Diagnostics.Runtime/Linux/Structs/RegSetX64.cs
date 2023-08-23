// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct RegSetX64
    {
        public readonly ulong R15;
        public readonly ulong R14;
        public readonly ulong R13;
        public readonly ulong R12;
        public readonly ulong Rbp;
        public readonly ulong Rbx;
        public readonly ulong R11;
        public readonly ulong R10;
        public readonly ulong R8;
        public readonly ulong R9;
        public readonly ulong Rax;
        public readonly ulong Rcx;
        public readonly ulong Rdx;
        public readonly ulong Rsi;
        public readonly ulong Rdi;
        public readonly ulong OrigRax;
        public readonly ulong Rip;
        public readonly ulong CS;
        public readonly ulong EFlags;
        public readonly ulong Rsp;
        public readonly ulong SS;
        public readonly ulong FSBase;
        public readonly ulong GSBase;
        public readonly ulong DS;
        public readonly ulong ES;
        public readonly ulong FS;
        public readonly ulong GS;

        public bool CopyContext(Span<byte> context)
        {
            if (context.Length < AMD64Context.Size)
                return false;

            ref AMD64Context contextRef = ref Unsafe.As<byte, AMD64Context>(ref MemoryMarshal.GetReference(context));

            contextRef.ContextFlags = AMD64Context.ContextControl | AMD64Context.ContextInteger | AMD64Context.ContextSegments;
            contextRef.R15 = R15;
            contextRef.R14 = R14;
            contextRef.R13 = R13;
            contextRef.R12 = R12;
            contextRef.Rbp = Rbp;
            contextRef.Rbx = Rbx;
            contextRef.R11 = R11;
            contextRef.R10 = R10;
            contextRef.R9 = R9;
            contextRef.R8 = R8;
            contextRef.Rax = Rax;
            contextRef.Rcx = Rcx;
            contextRef.Rdx = Rdx;
            contextRef.Rsi = Rsi;
            contextRef.Rdi = Rdi;
            contextRef.Rip = Rip;
            contextRef.Rsp = Rsp;
            contextRef.Cs = (ushort)CS;
            contextRef.Ds = (ushort)DS;
            contextRef.Ss = (ushort)SS;
            contextRef.Fs = (ushort)FS;
            contextRef.Gs = (ushort)GS;

            return true;
        }
    }
}