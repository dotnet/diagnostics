// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DbgEng
{
    internal sealed unsafe class DebugAdvanced : CallableCOMWrapper
    {
        internal static readonly Guid IID_IDebugAdvanced = new("f2df5f53-071f-47bd-9de6-5734c3fed689");

        public DebugAdvanced(RefCountedFreeLibrary library, IntPtr pUnk, DebugSystemObjects sys)
            : base(library, IID_IDebugAdvanced, pUnk)
        {
            _sys = sys;
            SuppressRelease();
        }

        private ref readonly IDebugAdvancedVTable VTable => ref Unsafe.AsRef<IDebugAdvancedVTable>(_vtable);

        public HResult GetThreadContext(Span<byte> context)
        {
            using IDisposable holder = _sys.Enter();
            fixed (byte* ptr = context)
                return VTable.GetThreadContext(Self, ptr, context.Length);
        }

        private readonly DebugSystemObjects _sys;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly unsafe struct IDebugAdvancedVTable
    {
        public readonly delegate* unmanaged[Stdcall]<IntPtr, byte*, int, int> GetThreadContext;
        public readonly IntPtr SetThreadContext;
    }
}