// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    internal sealed unsafe class SOSStackRefEnum : CallableCOMWrapper
    {
        private readonly List<nint> _data = new();
        private static readonly Guid IID_ISOSStackRefEnum = new("8FA642BD-9F10-4799-9AA3-512AE78C77EE");

        public SOSStackRefEnum(DacLibrary library, IntPtr pUnk)
            : base(library?.OwningLibrary, IID_ISOSStackRefEnum, pUnk)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            foreach (nint data in _data)
            {
                Marshal.FreeHGlobal(data);
            }

            _data.Clear();
        }

        private ref readonly ISOSStackRefEnumVTable VTable => ref Unsafe.AsRef<ISOSStackRefEnumVTable>(_vtable);

        public IEnumerable<StackRefData> ReadStackRefs()
        {
            List<StackRefData> result = new();
            Span<StackRefData> span = Read();
            while (span.Length > 0)
            {
                for (int i = 0; i < span.Length; i++)
                    result.Add(span[i]);
                span = Read();
            }

            return result;
        }

        private Span<StackRefData> Read()
        {
            Span<StackRefData> span = CreateStorage();
            fixed (StackRefData* ptr = span)
            {
                HResult hr = VTable.Next(Self, span.Length, ptr, out int read);
                span = span.Slice(0, hr ? read : 0);
                return span;
            }
        }

        private Span<StackRefData> CreateStorage()
        {
            nint storage = Marshal.AllocHGlobal(0x1000 * sizeof(StackRefData));
            _data.Add(storage);
            return new((void*)storage, 0x1000);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly unsafe struct ISOSStackRefEnumVTable
    {
        private readonly IntPtr Skip;
        private readonly IntPtr Reset;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, out int, int> GetCount;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, StackRefData*, out int, int> Next;
    }
}
