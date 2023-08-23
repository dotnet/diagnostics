// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    /// <summary>
    /// This is an undocumented, untested, and unsupported interface.  Do not use.
    /// </summary>
    internal sealed unsafe class SOSDac6 : CallableCOMWrapper
    {
        internal static readonly Guid IID_ISOSDac6 = new("11206399-4B66-4EDB-98EA-85654E59AD45");

        public SOSDac6(DacLibrary library, IntPtr ptr)
            : base(library?.OwningLibrary, IID_ISOSDac6, ptr)
        {
        }

        private ref readonly ISOSDac6VTable VTable => ref Unsafe.AsRef<ISOSDac6VTable>(_vtable);

        public SOSDac6(CallableCOMWrapper toClone) : base(toClone)
        {
        }

        public HResult GetMethodTableCollectibleData(ulong mt, out MethodTableCollectibleData data)
        {
            return VTable.GetMethodTableCollectibleData(Self, mt, out data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly unsafe struct ISOSDac6VTable
    {
        public readonly delegate* unmanaged[Stdcall]<IntPtr, ClrDataAddress, out MethodTableCollectibleData, int> GetMethodTableCollectibleData;
    }
}