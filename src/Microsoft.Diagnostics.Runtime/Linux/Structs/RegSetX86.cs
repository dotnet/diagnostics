// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct RegSetX86
    {
        public readonly uint Ebx;
        public readonly uint Ecx;
        public readonly uint Edx;
        public readonly uint Esi;
        public readonly uint Edi;
        public readonly uint Ebp;
        public readonly uint Eax;
        public readonly uint Xds;
        public readonly uint Xes;
        public readonly uint Xfs;
        public readonly uint Xgs;
        public readonly uint OrigEax;
        public readonly uint Eip;
        public readonly uint Xcs;
        public readonly uint EFlags;
        public readonly uint Esp;
        public readonly uint Xss;

        public bool CopyContext(Span<byte> context)
        {
            if (context.Length < X86Context.Size)
                return false;

            ref X86Context contextRef = ref Unsafe.As<byte, X86Context>(ref MemoryMarshal.GetReference(context));

            contextRef.ContextFlags = X86Context.ContextControl | X86Context.ContextInteger;

            contextRef.Ebp = Ebp;
            contextRef.Eip = Eip;
            contextRef.Ecx = Ecx;
            contextRef.EFlags = EFlags;
            contextRef.Esp = Esp;
            contextRef.Ss = Xss;

            contextRef.Edi = Edi;
            contextRef.Esi = Esi;
            contextRef.Ebx = Ebx;
            contextRef.Edx = Edx;
            contextRef.Ecx = Ecx;
            contextRef.Eax = Eax;

            return true;
        }
    }
}