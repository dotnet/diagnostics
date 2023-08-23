// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal readonly struct x86_thread_state64_t
    {
        public readonly ulong __rax;
        public readonly ulong __rbx;
        public readonly ulong __rcx;
        public readonly ulong __rdx;
        public readonly ulong __rdi;
        public readonly ulong __rsi;
        public readonly ulong __rbp;
        public readonly ulong __rsp;
        public readonly ulong __r8;
        public readonly ulong __r9;
        public readonly ulong __r10;
        public readonly ulong __r11;
        public readonly ulong __r12;
        public readonly ulong __r13;
        public readonly ulong __r14;
        public readonly ulong __r15;
        public readonly ulong __rip;
        public readonly ulong __rflags;
        public readonly ulong __cs;
        public readonly ulong __fs;
        public readonly ulong __gs;

        public bool CopyContext(Span<byte> context)
        {
            if (context.Length < AMD64Context.Size)
                return false;

            ref AMD64Context contextRef = ref Unsafe.As<byte, AMD64Context>(ref MemoryMarshal.GetReference(context));

            contextRef.ContextFlags = AMD64Context.ContextControl | AMD64Context.ContextInteger | AMD64Context.ContextSegments;
            contextRef.Rax = __rax;
            contextRef.Rbx = __rbx;
            contextRef.Rcx = __rcx;
            contextRef.Rdx = __rdx;
            contextRef.Rdi = __rdi;
            contextRef.Rsi = __rsi;
            contextRef.Rbp = __rbp;
            contextRef.Rsp = __rsp;
            contextRef.R8 = __r8;
            contextRef.R9 = __r9;
            contextRef.R10 = __r10;
            contextRef.R11 = __r11;
            contextRef.R12 = __r12;
            contextRef.R13 = __r13;
            contextRef.R14 = __r14;
            contextRef.R15 = __r15;
            contextRef.Rip = __rip;
            contextRef.EFlags = (int)__rflags;
            contextRef.Cs = (ushort)__cs;
            contextRef.Ss = 0;
            contextRef.Ds = 0;
            contextRef.Es = 0;
            contextRef.Fs = (ushort)__fs;
            contextRef.Gs = (ushort)__gs;

            return true;
        }
    }
}