// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Runtime.Utilities;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    internal sealed unsafe class SOSHandleEnum : CallableCOMWrapper
    {
        private readonly List<nint> _data = new();

        private static readonly Guid IID_ISOSHandleEnum = new("3E269830-4A2B-4301-8EE2-D6805B29B2FA");

        public SOSHandleEnum(DacLibrary library, IntPtr pUnk)
            : base(library?.OwningLibrary, IID_ISOSHandleEnum, pUnk)
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

        private Span<HandleData> Read()
        {
            Span<HandleData> span = CreateStorage();
            fixed (HandleData* ptr = span)
            {
                HResult hr = VTable.Next(Self, span.Length, ptr, out int read);
                span = span.Slice(0, hr ? read : 0);
                return span;
            }
        }

        private Span<HandleData> CreateStorage()
        {
            nint storage = Marshal.AllocHGlobal(0x1000 * sizeof(StackRefData));
            _data.Add(storage);
            return new((void*)storage, 0x1000);
        }

        private ref readonly ISOSHandleEnumVTable VTable => ref Unsafe.AsRef<ISOSHandleEnumVTable>(_vtable);


        public IEnumerable<HandleData> ReadHandles()
        {
            List<HandleData> result = new();
            Span<HandleData> span = Read();
            while (span.Length > 0)
            {
                for (int i = 0; i < span.Length; i++)
                    result.Add(span[i]);
                span = Read();
            }

            return result;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly unsafe struct ISOSHandleEnumVTable
    {
        private readonly IntPtr Skip;
        private readonly IntPtr Reset;
        private readonly IntPtr GetCount;
        public readonly delegate* unmanaged[Stdcall]<IntPtr, int, HandleData*, out int, int> Next;
    }
}