// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    internal sealed unsafe class SosMemoryEnum : CallableCOMWrapper, IEnumerable<SosMemoryRegion>
    {
        private ref readonly ISOSMemoryEnumVtable VTable => ref Unsafe.AsRef<ISOSMemoryEnumVtable>(_vtable);
        public static readonly Guid IID_ISOSMemoryEnum = new("E4B860EC-337A-40C0-A591-F09A9680690F");
        public SosMemoryEnum(DacLibrary library, IntPtr pUnk)
            : base(library?.OwningLibrary, IID_ISOSMemoryEnum, pUnk)
        {
        }

        private void Reset() => VTable.Reset(Self);

        public IEnumerator<SosMemoryRegion> GetEnumerator()
        {
            Reset();

            SosMemoryRegion[] regions = new SosMemoryRegion[32];

            int read;
            while ((read = Read(regions)) > 0)
            {
                for (int i = 0; i < read; i++)
                    yield return regions[i];
            }
        }

        private int Read(Span<SosMemoryRegion> regions)
        {
            fixed (SosMemoryRegion* ptr = regions)
            {
                HResult hr = VTable.Next(Self, regions.Length, ptr, out int read);
                return hr ? read : 0;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        [StructLayout(LayoutKind.Sequential)]
        internal readonly unsafe struct ISOSMemoryEnumVtable
        {
            private readonly IntPtr Skip;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, int> Reset;
            private readonly IntPtr GetCount;
            public readonly delegate* unmanaged[Stdcall]<IntPtr, int, SosMemoryRegion*, out int, int> Next;
        }
    }
}
