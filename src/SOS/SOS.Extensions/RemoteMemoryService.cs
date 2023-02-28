// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace SOS
{
    internal unsafe class RemoteMemoryService : CallableCOMWrapper, IRemoteMemoryService
    {
        private static Guid IID_IRemoteMemoryService = new Guid("CD6A0F22-8BCF-4297-9366-F440C2D1C781");

        private ref readonly IRemoteMemoryServiceVTable VTable => ref Unsafe.AsRef<IRemoteMemoryServiceVTable>(_vtable);

        internal RemoteMemoryService(IntPtr punk)
            : base(new RefCountedFreeLibrary(IntPtr.Zero), IID_IRemoteMemoryService, punk)
        {
        }

        public bool AllocateMemory(ulong address, uint size, uint typeFlags, uint protectFlags, out ulong remoteAddress)
        {
            return VTable.AllocVirtual(Self, address, size, typeFlags, protectFlags, out remoteAddress) == HResult.S_OK;
        }

        public bool FreeMemory(ulong address, uint size, uint typeFlags)
        {
            return VTable.FreeVirtual(Self, address, size, typeFlags) == HResult.S_OK;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly unsafe struct IRemoteMemoryServiceVTable
        {
            public readonly delegate* unmanaged[Stdcall]<IntPtr, ulong, uint, uint, uint, out ulong, int> AllocVirtual;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, ulong, uint, uint, int> FreeVirtual;
        }
    }
}
