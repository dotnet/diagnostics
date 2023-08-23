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
    internal sealed unsafe class SosDac12 : CallableCOMWrapper
    {
        internal static readonly Guid IID_ISOSDac12 = new("1b93bacc-8ca4-432d-943a-3e6e7ec0b0a3");

        public SosDac12(DacLibrary library, IntPtr ptr)
            : base(library?.OwningLibrary, IID_ISOSDac12, ptr)
        {
        }

        private ref readonly ISOSDac12VTable VTable => ref Unsafe.AsRef<ISOSDac12VTable>(_vtable);

        public HResult GetGlobalAllocationContext(out ulong allocPtr, out ulong allocLimit)
        {
            return VTable.GetGlobalAllocationContext(Self, out allocPtr, out allocLimit);
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly unsafe struct ISOSDac12VTable
        {
            public readonly delegate* unmanaged[Stdcall]<IntPtr, out ulong, out ulong, int> GetGlobalAllocationContext;
        }
    }
}
