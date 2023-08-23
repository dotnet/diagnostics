// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    internal sealed unsafe class ClrStackWalk : CallableCOMWrapper
    {
        private static readonly Guid IID_IXCLRDataStackWalk = new("E59D8D22-ADA7-49a2-89B5-A415AFCFC95F");

        public ClrStackWalk(DacLibrary library, IntPtr pUnk)
            : base(library?.OwningLibrary, IID_IXCLRDataStackWalk, pUnk)
        {
        }

        private ref readonly IXCLRDataStackWalkVTable VTable => ref Unsafe.AsRef<IXCLRDataStackWalkVTable>(_vtable);

        public ClrDataAddress GetFrameVtable()
        {
            long ptr = 0xcccccccc;

            HResult hr = VTable.Request(Self, 0xf0000000, 0, null, 8u, (byte*)&ptr);
            return hr ? new ClrDataAddress(ptr) : default;
        }

        public HResult Next()
        {
            return VTable.Next(Self);
        }

        public HResult GetContext(uint contextFlags, int contextBufSize, out int contextSize, byte[] buffer)
        {
            fixed (byte* ptr = buffer)
                return VTable.GetContext(Self, contextFlags, contextBufSize, out contextSize, ptr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly unsafe struct IXCLRDataStackWalkVTable
    {
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, int, out int, byte*, int> GetContext;
        private readonly IntPtr GetContext2;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int> Next;
        private readonly IntPtr GetStackSizeSkipped;
        private readonly IntPtr GetFrameType;
        public readonly IntPtr GetFrame;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, uint, uint, byte*, uint, byte*, int> Request;
        private readonly IntPtr SetContext2;
    }
}
